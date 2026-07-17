// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace ScannerCore
{
    internal enum DirectoryBatchResult
    {
        Entries,
        Completed,
        Failed
    }

    internal interface IDirectoryEntrySink
    {
        void OnEntry(ReadOnlySpan<char> name, long size, bool isDirectory);
    }

    internal interface IDirectoryEntryCursor : IDisposable
    {
        DirectoryBatchResult ReadNext(
            Span<byte> buffer,
            IDirectoryEntrySink sink);
    }

    internal interface IDirectoryEntrySource
    {
        IDirectoryEntryCursor? Open(string path);
    }
}
