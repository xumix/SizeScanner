// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ScannerCore
{
    public enum FsItemKind
    {
        File,
        Directory,
        Aggregate
    }

    [DebuggerDisplay("Dir:{IsDir}, {Name}, {Size} bytes")]
    public sealed class FsItem
    {
        public FsItem(string name, long size, bool isDir)
            : this(name, size, isDir ? FsItemKind.Directory : FsItemKind.File)
        {
        }

        private FsItem(string name, long size, FsItemKind kind)
        {
            Name = name;
            Size = size;
            Kind = kind;
        }

        public string Name { get; }
        public long Size { get; set; }
        public FsItemKind Kind { get; }
        public bool IsDir => Kind == FsItemKind.Directory;
        public bool IsAggregate => Kind == FsItemKind.Aggregate;
        public bool HasUnretainedChildren { get; internal set; }
        public FsItem? Parent { get; internal set; }

        public List<FsItem>? Items { get; set; }

        public static FsItem CreateAggregate(long size) =>
            new(string.Empty, size, FsItemKind.Aggregate);

        internal void AttachChildren(List<FsItem> children)
        {
            Items = children;
            foreach (var child in children)
                child.Parent = this;
        }

        public int CountRetainedNodes()
        {
            var count = 1;
            if (Items is null)
                return count;
            foreach (var child in Items)
                count = checked(count + child.CountRetainedNodes());
            return count;
        }

        public bool TryGetPathFrom(FsItem root, out string path)
        {
            if (ReferenceEquals(this, root))
            {
                path = root.Name;
                return true;
            }

            var segments = new List<string>();
            for (var current = this; !ReferenceEquals(current, root); current = current.Parent!)
            {
                segments.Add(current.Name);
                if (current.Parent == null)
                {
                    path = string.Empty;
                    return false;
                }
            }

            segments.Add(root.Name);
            segments.Reverse();

            path = Path.Join(segments.ToArray());
            return true;
        }
    }
}
