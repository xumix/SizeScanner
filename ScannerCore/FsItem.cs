// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ScannerCore
{
    [DebuggerDisplay("Dir:{IsDir}, {Name}, {Size} bytes")]
    public sealed class FsItem
    {
        public FsItem(string name, long size, bool isDir)
        {
            Name = name;
            Size = size;
            IsDir = isDir;
        }

        public string Name { get; }
        public long Size { get; set; }
        public bool IsDir { get; }
        public FsItem? Parent { get; internal set; }

        public List<FsItem>? Items { get; set; }

        internal void AttachChildren(List<FsItem> children)
        {
            Items = children;
            foreach (var child in children)
                child.Parent = this;
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

            path = segments[0];
            for (int i = 1; i < segments.Count; i++)
                path = Path.Combine(path, segments[i]);
            return true;
        }
    }
}
