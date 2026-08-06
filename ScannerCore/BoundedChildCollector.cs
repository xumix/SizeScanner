// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace ScannerCore
{
    public sealed class BoundedChildCollector
    {
        private readonly int _maxChildren;
        private readonly PriorityQueue<FsItem, FsItem> _kept = new(RetentionOrder.Instance);
        private long _hiddenSize;
        private int _hiddenCount;

        public BoundedChildCollector(int maxChildren) =>
            _maxChildren = maxChildren;

        public void ConsiderFile(ReadOnlySpan<char> name, long size)
        {
            if (!WouldRetain(size, name))
            {
                Hide(size);
                return;
            }

            var ownedName = name.ToString();
            Consider(new FsItem(ownedName, size, isDir: false));
        }

        public void ConsiderDirectory(FsItem directory) => Consider(directory);

        private bool WouldRetain(long size, ReadOnlySpan<char> name)
        {
            if (_maxChildren == 0)
                return false;
            if (_kept.Count < _maxChildren)
                return true;

            _kept.TryPeek(out var worst, out _);
            return RetentionOrder.Compare(size, name, worst!) > 0;
        }

        private void Consider(FsItem candidate)
        {
            if (_kept.Count < _maxChildren)
            {
                _kept.Enqueue(candidate, candidate);
                return;
            }

            // The window is full, so admitting the candidate evicts the worst of the
            // window plus the candidate itself in a single sift.
            Hide(_kept.EnqueueDequeue(candidate, candidate).Size);
        }

        private void Hide(long size)
        {
            _hiddenSize = checked(_hiddenSize + size);
            _hiddenCount = checked(_hiddenCount + 1);
        }

        public List<FsItem> BuildChildren()
        {
            var children = new List<FsItem>(_kept.Count + 1);
            foreach (var (element, _) in _kept.UnorderedItems)
                children.Add(element);

            children.Sort(static (a, b) => RetentionOrder.Instance.Compare(b, a));

            if (_hiddenCount > 0)
                children.Add(FsItem.CreateAggregate(_hiddenSize));
            return children;
        }

        public bool HasHiddenChildren => _hiddenCount > 0;

        /// <summary>
        /// The single retention order: greater means "keep in preference to". Larger size
        /// wins, and equal sizes are broken by the ordinally smaller name. The queue head is
        /// therefore the worst retained child, and <see cref="BuildChildren"/> reverses this
        /// order for display.
        /// </summary>
        private sealed class RetentionOrder : IComparer<FsItem>
        {
            public static readonly RetentionOrder Instance = new();

            public int Compare(FsItem? x, FsItem? y) =>
                Compare(x!.Size, x.Name.AsSpan(), y!);

            /// <summary>
            /// Span overload so a candidate can be ranked before its name is allocated
            /// as a managed string.
            /// </summary>
            public static int Compare(long size, ReadOnlySpan<char> name, FsItem other)
            {
                var bySize = size.CompareTo(other.Size);
                return bySize != 0
                    ? bySize
                    : -name.CompareTo(other.Name.AsSpan(), StringComparison.Ordinal);
            }
        }
    }
}
