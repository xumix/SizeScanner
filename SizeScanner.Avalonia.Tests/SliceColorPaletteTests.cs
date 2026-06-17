// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SliceColorPaletteTests
{
    [Fact]
    public void LevelBaseColor_single_child_uses_hue_zero()
    {
        var hsb = SliceColorPalette.LevelBaseColor(0, 1);
        Assert.Equal(0f, hsb.Hue);
        Assert.Equal(0.95f, hsb.Saturation, 3);
        Assert.Equal(0.68f, hsb.Brightness, 3);
    }

    [Fact]
    public void LevelBaseColor_distributes_hue_across_siblings()
    {
        var second = SliceColorPalette.LevelBaseColor(1, 4);
        Assert.Equal(90f, second.Hue, 3);
    }

    [Fact]
    public void ChildShade_keeps_parent_hue_and_varies_brightness()
    {
        var parent = SliceColorPalette.LevelBaseColor(1, 4); // hue 90
        var shade = SliceColorPalette.ChildShade(parent, 0, 3);
        Assert.Equal(parent.Hue, shade.Hue, 3);
        Assert.Equal(0.38f, shade.Brightness, 3); // minBrightness for first of many
    }

    [Fact]
    public void ToColor_grayscale_when_saturation_zero()
    {
        var hsb = new SliceHsb(0f, 0f, 0.5f);
        var color = hsb.ToColor();
        Assert.Equal(color.R, color.G);
        Assert.Equal(color.G, color.B);
    }
}
