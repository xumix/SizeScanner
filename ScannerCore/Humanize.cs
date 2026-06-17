// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;

namespace ScannerCore
{
    public static class Humanize
    {
        public static string Size(long size)
        {
            Single output = size;
            int sufIdx = 0;
            while (Math.Abs(output) > 1024 && sufIdx < Suffixes.Length - 1)
            {
                sufIdx++;
                output /= 1024;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:F} {1}Byte(s)", output, Suffixes[sufIdx]);
        }

        public static string FsItem(FsItem source)
        {
            if (source.IsDir && source.Items == null)
            {
                return "<Access Denied>";
            }
            return source.Size == 0 ? "<Empty>" : Size(source.Size);
        }

        private static readonly string[] Suffixes =
        {
            "", "K", "M", "G", "T", "P"
        };

    }
}
