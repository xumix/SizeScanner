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
    public class DirectoryScanner : IDirectoryEntrySource
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

        internal const int BufferSize = 1024 * 1024;
        private readonly bool PreferAllocatedSize;

        public DirectoryScanner(bool preferAllocatedSize)
        {
            PreferAllocatedSize = preferAllocatedSize;
        }

        internal IDirectoryEntryCursor? Open(string path)
        {
            var handle = NativeMethods.CreateFile(path,
                                                   FileListDirectory,
                                                   FileShare.ReadWrite | FileShare.Delete,
                                                   IntPtr.Zero,
                                                   FileMode.Open,
                                                   FileFlagBackupSemantics,
                                                   IntPtr.Zero);

            return handle.IsInvalid
                ? null
                : new Cursor(handle, PreferAllocatedSize);
        }

        IDirectoryEntryCursor? IDirectoryEntrySource.Open(string path) => Open(path);

        public List<FsItem>? Scan(string dir, ref long processed)
        {
            using var cursor = Open(dir);
            if (cursor is null)
                return null;

            var sink = new LegacyFsItemSink();
            var rented = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                var buffer = rented.AsSpan(0, BufferSize);
                while (cursor.ReadNext(buffer, sink) == DirectoryBatchResult.Entries)
                {
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            processed += sink.Size;
            return sink.Items;
        }

        private static unsafe void ParseBuffer(byte* basePtr, bool preferAllocatedSize, IDirectoryEntrySink sink)
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
                    var name = new ReadOnlySpan<char>(
                        (char*)(ptr + OffsetFileName),
                        checked((int)(nameLengthBytes / 2)));
                    var isDirectory = (attributes & FileAttributeDirectory) != 0;

                    if (!(isDirectory
                        && (name.SequenceEqual(".".AsSpan())
                            || name.SequenceEqual("..".AsSpan()))))
                    {
                        var size = preferAllocatedSize
                            ? Unsafe.ReadUnaligned<long>(ptr + OffsetAllocationSize)
                            : Unsafe.ReadUnaligned<long>(ptr + OffsetEndOfFile);
                        sink.OnEntry(name, size, isDirectory);
                    }
                }

                if (nextEntryOffset == 0)
                    break;
                ptr += nextEntryOffset;
            }
        }

        /// <summary>
        /// Temporary adapter that materializes a <see cref="List{FsItem}"/> from cursor batches.
        /// Removed in Task 8 once callers consume <see cref="IDirectoryEntrySink"/> directly.
        /// </summary>
        private sealed class LegacyFsItemSink : IDirectoryEntrySink
        {
            public List<FsItem> Items { get; } = new();
            public long Size { get; private set; }

            public void OnEntry(ReadOnlySpan<char> name, long size, bool isDirectory)
            {
                Items.Add(new FsItem(new string(name), size, isDirectory));
                Size += size;
            }
        }

        private sealed class Cursor : IDirectoryEntryCursor
        {
            private readonly SafeFileHandle _handle;
            private readonly bool _preferAllocatedSize;
            private readonly IO_STATUS_BLOCK _statusBlock = new();
            private bool _completed;

            internal Cursor(SafeFileHandle handle, bool preferAllocatedSize)
            {
                _handle = handle;
                _preferAllocatedSize = preferAllocatedSize;
            }

            public unsafe DirectoryBatchResult ReadNext(Span<byte> buffer, IDirectoryEntrySink sink)
            {
                if (_completed)
                    return DirectoryBatchResult.Completed;

                uint ntstatus;
                fixed (byte* bufferPtr = buffer)
                {
                    ntstatus = NativeMethods.NtQueryDirectoryFile(
                        _handle,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        _statusBlock,
                        (IntPtr)bufferPtr,
                        (uint)buffer.Length,
                        FileDirectoryInformation,
                        false,
                        IntPtr.Zero,
                        false);

                    if (ntstatus == StatusSuccess)
                    {
                        ParseBuffer(bufferPtr, _preferAllocatedSize, sink);
                        return DirectoryBatchResult.Entries;
                    }
                }

                _completed = true;
                if (ntstatus != StatusNoMoreFiles)
                {
                    Debug.WriteLine($"NtQueryDirectoryFile failed with NTSTATUS 0x{ntstatus:X8}.");
                    return DirectoryBatchResult.Failed;
                }

                return DirectoryBatchResult.Completed;
            }

            public void Dispose() => _handle.Close();
        }
    }
}
