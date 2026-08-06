// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ScannerCore;

namespace ScannerCore.Tests;

internal sealed class SyntheticNode
{
    public required string Name { get; init; }
    public long Size { get; init; }
    public bool IsDirectory { get; init; }
    public List<SyntheticNode> Children { get; } = [];

    public static SyntheticNode File(string name, long size) =>
        new() { Name = name, Size = size, IsDirectory = false };

    public static SyntheticNode Dir(string name, params SyntheticNode[] children)
    {
        var node = new SyntheticNode { Name = name, Size = 0, IsDirectory = true };
        node.Children.AddRange(children);
        return node;
    }
}

/// <summary>Tracks how many directory reads are in flight across the whole source.</summary>
internal sealed class ConcurrencyProbe
{
    private int _current;
    private int _peak;

    public int Peak => Volatile.Read(ref _peak);

    /// <summary>Number of reads presently in flight, for asserting nothing is still
    /// active after a scan has returned.</summary>
    public int Current => Volatile.Read(ref _current);

    public IDisposable Enter()
    {
        var now = Interlocked.Increment(ref _current);
        RecordPeak(ref _peak, now);
        return new Scope(this);
    }

    internal static void RecordPeak(ref int peak, int candidate)
    {
        var observed = Volatile.Read(ref peak);
        while (candidate > observed)
        {
            var prior = Interlocked.CompareExchange(ref peak, candidate, observed);
            if (prior == observed)
                break;
            observed = prior;
        }
    }

    private sealed class Scope(ConcurrencyProbe owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._current);
    }
}

/// <summary>
/// A meeting point for a specific set of directories. Each participant blocks inside its
/// read until the expected number of participants has arrived or the timeout expires, so
/// "were these two directories walked at the same time?" becomes a deterministic
/// assertion instead of a wall-clock comparison. Unlike <see cref="ConcurrencyProbe"/>
/// this counts only the directories a test opts in, so unrelated overlapping reads (a
/// parent's trailing end-of-directory read, for instance) cannot inflate the result.
/// </summary>
internal sealed class Rendezvous(int participants, TimeSpan timeout)
{
    private int _current;
    private int _peak;

    public int Peak => Volatile.Read(ref _peak);

    public void Arrive()
    {
        var now = Interlocked.Increment(ref _current);
        ConcurrencyProbe.RecordPeak(ref _peak, now);

        var elapsed = Stopwatch.StartNew();
        while (Volatile.Read(ref _current) < participants && elapsed.Elapsed < timeout)
            Thread.Sleep(5);

        Interlocked.Decrement(ref _current);
    }
}

internal sealed class SyntheticTreeSource : IDirectoryEntrySource
{
    private readonly Dictionary<string, SyntheticNode> _byPath;

    public SyntheticTreeSource(string rootPath, SyntheticNode root)
    {
        _byPath = new Dictionary<string, SyntheticNode>(StringComparer.OrdinalIgnoreCase);
        Index(rootPath, root);
    }

    public ConcurrencyProbe Probe { get; } = new();

    /// <summary>Invoked at the start of every read, before entries are produced.</summary>
    public Action<string>? GateRead { get; set; }

    /// <summary>Reads of this directory return <see cref="DirectoryBatchResult.Failed"/>.</summary>
    public string? FailPath { get; set; }

    /// <summary>Returns true for any additional directory reads that should fail.</summary>
    public Func<string, bool>? ShouldFailRead { get; set; }

    /// <summary>Invoked when a synthetic directory cursor is disposed.</summary>
    public Action<string>? CursorDisposed { get; set; }

    public IDirectoryEntryCursor? Open(string path)
    {
        var normalized = Normalize(path);
        return _byPath.TryGetValue(normalized, out var node) && node.IsDirectory
            ? new Cursor(node, normalized, this)
            : null;
    }

    private void Index(string path, SyntheticNode node)
    {
        _byPath[Normalize(path)] = node;
        foreach (var child in node.Children)
            Index(Path.Combine(path, child.Name), child);
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar);

    private sealed class Cursor(
        SyntheticNode node,
        string path,
        SyntheticTreeSource owner) : IDirectoryEntryCursor
    {
        private int _index;

        public DirectoryBatchResult ReadNext(Span<byte> buffer, IDirectoryEntrySink sink)
        {
            using var scope = owner.Probe.Enter();
            owner.GateRead?.Invoke(path);

            if (string.Equals(path, owner.FailPath, StringComparison.OrdinalIgnoreCase) ||
                owner.ShouldFailRead?.Invoke(path) == true)
                return DirectoryBatchResult.Failed;

            if (_index >= node.Children.Count)
                return DirectoryBatchResult.Completed;

            var end = Math.Min(node.Children.Count, _index + 256);
            for (; _index < end; _index++)
            {
                var child = node.Children[_index];
                sink.OnEntry(child.Name.AsSpan(), child.Size, child.IsDirectory);
            }

            return DirectoryBatchResult.Entries;
        }

        public void Dispose() => owner.CursorDisposed?.Invoke(path);
    }
}
