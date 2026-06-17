// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScannerCore
{
    public class DirectoryScanner
    {
        private const int FileDirectoryInformation = 1;
        private const uint StatusSuccess = 0x00000000;
        private const uint StatusNoMoreFiles = 0x80000006;

        #region Native

        [StructLayout(LayoutKind.Explicit)]
        internal struct IO_STATUS_BLOCK_UNION
        {
            [FieldOffset(0)]
            internal UInt32 Status;
            [FieldOffset(0)]
            internal IntPtr Pointer;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal class IO_STATUS_BLOCK
        {
            internal IO_STATUS_BLOCK_UNION Union;
            internal UIntPtr Information;
        }

        private const uint FileListDirectory = 0x00000001;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileAttributeDirectory = 0x00000010;
        private const uint FileAttributeOffline = 0x00001000;
        private const uint FileAttributeReparsePoint = 0x00000400;

        internal static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern SafeFileHandle CreateFile(
                [MarshalAs(UnmanagedType.LPTStr)] string filename,
                uint access,
                [MarshalAs(UnmanagedType.U4)] FileShare share,
                IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
                [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
                uint flagsAndAttributes,
                IntPtr templateFile);

            [DllImport("ntdll.dll")]
            internal static extern uint NtQueryDirectoryFile(
                SafeFileHandle FileHandle,
                IntPtr Event,
                IntPtr ApcRoutine,
                IntPtr ApcContext,
                [Out] IO_STATUS_BLOCK IoStatusBlock,
                [Out] IntPtr FileInformation,
                UInt32 Length,
                UInt32 FileInformationClass,
                [MarshalAs(UnmanagedType.Bool)] Boolean ReturnSingleEntry,
                IntPtr FileName,
                [MarshalAs(UnmanagedType.Bool)] Boolean RestartScan
                );
        }

        #endregion

        private const int BufferSize = 1024 * 1024;
        private readonly bool PreferAllocatedSize;

        public DirectoryScanner(bool preferAllocatedSize)
        {
            PreferAllocatedSize = preferAllocatedSize;
        }

        public unsafe List<FsItem>? Scan(string dir, ref long processed)
        {
            var hFolder = NativeMethods.CreateFile(dir,
                                                   FileListDirectory,
                                                   FileShare.ReadWrite | FileShare.Delete,
                                                   IntPtr.Zero,
                                                   FileMode.Open,
                                                   FileFlagBackupSemantics,
                                                   IntPtr.Zero);
            if (hFolder.IsInvalid)
                return null;

            var res = new List<FsItem>();
            var rented = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                fixed (byte* bufferPtr = rented)
                {
                    var statusBlock = new IO_STATUS_BLOCK();
                    while (true)
                    {
                        var ntstatus = NativeMethods.NtQueryDirectoryFile(
                            hFolder,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            statusBlock,
                            (IntPtr)bufferPtr,
                            BufferSize,
                            FileDirectoryInformation,
                            false,
                            IntPtr.Zero,
                            false);

                        if (ntstatus == StatusSuccess)
                        {
                            ParseBuffer(bufferPtr, res, ref processed);
                            continue;
                        }

                        if (ntstatus != StatusNoMoreFiles)
                            Debug.WriteLine($"NtQueryDirectoryFile failed with NTSTATUS 0x{ntstatus:X8}.");
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
                hFolder.Close();
            }

            return res;
        }

        private unsafe void ParseBuffer(byte* basePtr, List<FsItem> items, ref long processed)
        {
            const int OffsetNextEntry = 0;
            const int OffsetEndOfFile = 40;
            const int OffsetAllocationSize = 48;
            const int OffsetFileAttributes = 56;
            const int OffsetFileNameLength = 60;
            const int OffsetFileName = 64;

            var ptr = basePtr;
            while (true)
            {
                var nextEntryOffset = Unsafe.ReadUnaligned<uint>(ptr + OffsetNextEntry);
                var attributes = Unsafe.ReadUnaligned<uint>(ptr + OffsetFileAttributes);

                var isReparse = (attributes & FileAttributeReparsePoint) != 0;
                var isOffline = (attributes & FileAttributeOffline) != 0;
                if (!isReparse || isOffline)
                {
                    var nameLengthBytes = Unsafe.ReadUnaligned<uint>(ptr + OffsetFileNameLength);
                    var name = new string((char*)(ptr + OffsetFileName), 0, (int)(nameLengthBytes / 2));
                    var isDir = (attributes & FileAttributeDirectory) != 0;
                    var isDotDir = isDir && name is "." or "..";
                    if (!isDotDir)
                    {
                        var size = PreferAllocatedSize
                            ? Unsafe.ReadUnaligned<long>(ptr + OffsetAllocationSize)
                            : Unsafe.ReadUnaligned<long>(ptr + OffsetEndOfFile);
                        items.Add(new FsItem(name, size, isDir));
                        processed += size;
                    }
                }

                if (nextEntryOffset == 0)
                    break;
                ptr += nextEntryOffset;
            }
        }
    }
}
