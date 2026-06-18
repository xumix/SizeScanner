// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class HumanizeTests
{
    [Fact]
    public void Size_does_not_throw_for_max_long()
    {
        var text = Humanize.Size(long.MaxValue);
        Assert.Contains("B", text);
    }

    [Fact]
    public void Size_clamps_to_largest_suffix_for_exabyte_scale()
    {
        // 1 EB = 1024^6 bytes. Largest defined suffix is "P"; value must not overflow Suffixes.
        var text = Humanize.Size(1L << 60);
        Assert.Contains("PB", text);
    }

    [Fact]
    public void Size_formats_kilobytes()
    {
        Assert.Equal("2.00 KB", Humanize.Size(2048));
    }
}
