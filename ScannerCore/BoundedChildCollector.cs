// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace ScannerCore
{
    public sealed class BoundedChildCollector
    {
        private readonly int _maxChildren;
        private readonly PriorityQueue<FsItem, ItemPriority> _kept = new();
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
            return size > worst!.Size
                || size == worst.Size
                && name.CompareTo(
                    worst.Name.AsSpan(), StringComparison.Ordinal) < 0;
        }

        private void Consider(FsItem candidate)
        {
            _kept.Enqueue(candidate, ItemPriority.For(candidate));
            if (_kept.Count > _maxChildren)
                Hide(_kept.Dequeue().Size);
        }

        private void Hide(long size)
        {
            _hiddenSize = checked(_hiddenSize + size);
            _hiddenCount = checked(_hiddenCount + 1);
        }

        public List<FsItem> BuildChildren()
        {
            var children = _kept.UnorderedItems
                .Select(item => item.Element)
                .OrderByDescending(item => item.Size)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToList();

            if (_hiddenCount > 0)
                children.Add(FsItem.CreateAggregate(_hiddenSize));
            return children;
        }

        public bool HasHiddenChildren => _hiddenCount > 0;

        private readonly record struct ItemPriority(long Size, string Name)
            : IComparable<ItemPriority>
        {
            public static ItemPriority For(FsItem item) =>
                new(item.Size, item.Name);

            public int CompareTo(ItemPriority other)
            {
                var bySize = Size.CompareTo(other.Size);
                return bySize != 0
                    ? bySize
                    : -StringComparer.Ordinal.Compare(Name, other.Name);
            }
        }
    }
}
