// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScannerCore;

/// <summary>
/// Decides whether directory walks may fan out in parallel. Parallel I/O helps SSDs and
/// NVMe; it hurts traditional spinning disks, so those stay sequential.
/// </summary>
internal static class VolumeParallelismPolicy
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint StorageDeviceSeekPenaltyProperty = 7;
    private const uint PropertyStandardQuery = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public uint PropertyId;
        public uint QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
    }

    /// <summary>
    /// Returns true when the target volume is SSD-class (no seek penalty). Unknown or
    /// inaccessible volumes default to false (sequential walk).
    /// </summary>
    public static bool ShouldParallelize(string target)
    {
        try
        {
            var volumePath = TryGetVolumeDevicePath(target);
            if (volumePath is null)
                return false;

            using var handle = NativeMethods.CreateFile(
                volumePath,
                0,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                0,
                IntPtr.Zero);
            if (handle.IsInvalid)
                return false;

            return !QueryIncursSeekPenalty(handle);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetVolumeDevicePath(string target)
    {
        var root = Path.GetPathRoot(target);
        if (string.IsNullOrEmpty(root))
            return null;

        if (root.StartsWith(@"\\", StringComparison.Ordinal))
            return null;

        var driveRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (driveRoot.Length < 2 || driveRoot[1] != ':')
            return null;

        try
        {
            if (new DriveInfo(driveRoot).DriveType != DriveType.Fixed)
                return null;
        }
        catch
        {
            return null;
        }

        return @"\\.\" + driveRoot;
    }

    private static bool QueryIncursSeekPenalty(SafeFileHandle handle)
    {
        var query = new StoragePropertyQuery
        {
            PropertyId = StorageDeviceSeekPenaltyProperty,
            QueryType = PropertyStandardQuery
        };

        var querySize = Marshal.SizeOf<StoragePropertyQuery>();
        var queryPtr = Marshal.AllocHGlobal(querySize);
        try
        {
            Marshal.StructureToPtr(query, queryPtr, false);
            var outSize = Marshal.SizeOf<DeviceSeekPenaltyDescriptor>();
            var outPtr = Marshal.AllocHGlobal(outSize);
            try
            {
                if (!NativeMethods.DeviceIoControl(
                        handle,
                        IoctlStorageQueryProperty,
                        queryPtr,
                        (uint)querySize,
                        outPtr,
                        (uint)outSize,
                        out var bytesReturned,
                        IntPtr.Zero)
                    || bytesReturned < Marshal.SizeOf<DeviceSeekPenaltyDescriptor>())
                {
                    return true;
                }

                var descriptor = Marshal.PtrToStructure<DeviceSeekPenaltyDescriptor>(outPtr);
                return descriptor.IncursSeekPenalty;
            }
            finally
            {
                Marshal.FreeHGlobal(outPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(queryPtr);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string filename,
            uint access,
            FileShare share,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }
}
