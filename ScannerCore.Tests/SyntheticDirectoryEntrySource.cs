// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using ScannerCore;

namespace ScannerCore.Tests;

internal static class SyntheticSources
{
    internal static IDirectoryEntrySource WideDirectory(
        string root,
        int entryCount,
        long size,
        bool directories = false) =>
        new SyntheticDirectoryEntrySource(
            root, entryCount, size, directories);
}

internal sealed class SyntheticDirectoryEntrySource(
    string root,
    int entryCount,
    long size,
    bool directories) : IDirectoryEntrySource
{
    public IDirectoryEntryCursor? Open(string path) =>
        Normalize(path).Equals(
            Normalize(root), StringComparison.OrdinalIgnoreCase)
            ? new Cursor(entryCount, size, directories)
            : null;

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar);

    private sealed class Cursor(
        int entryCount,
        long size,
        bool directories) : IDirectoryEntryCursor
    {
        private int _index;

        public DirectoryBatchResult ReadNext(
            Span<byte> buffer,
            IDirectoryEntrySink sink)
        {
            if (_index >= entryCount)
                return DirectoryBatchResult.Completed;

            var end = Math.Min(entryCount, _index + 256);
            for (; _index < end; _index++)
            {
                Span<char> name = stackalloc char[32];
                "entry-".AsSpan().CopyTo(name);
                _index.TryFormat(
                    name[6..], out var written, "D8");
                sink.OnEntry(
                    name[..(6 + written)], size, directories);
            }

            return DirectoryBatchResult.Entries;
        }

        public void Dispose() { }
    }
}
