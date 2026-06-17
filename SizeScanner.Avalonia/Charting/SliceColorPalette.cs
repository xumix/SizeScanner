// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia.Media;

namespace SizeScanner.Avalonia.Charting;

public readonly record struct SliceHsb(float Hue, float Saturation, float Brightness)
{
    public Color ToColor()
    {
        if (Saturation <= 0)
        {
            var gray = (byte)Math.Round(Brightness * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var hue = (Hue % 360 + 360) % 360;
        var h = hue / 60f;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var p = Brightness * (1 - Saturation);
        var q = Brightness * (1 - Saturation * f);
        var t = Brightness * (1 - Saturation * (1 - f));

        float r, g, b;
        switch (i % 6)
        {
            case 0: r = Brightness; g = t; b = p; break;
            case 1: r = q; g = Brightness; b = p; break;
            case 2: r = p; g = Brightness; b = t; break;
            case 3: r = p; g = q; b = Brightness; break;
            case 4: r = t; g = p; b = Brightness; break;
            default: r = Brightness; g = p; b = q; break;
        }

        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }
}

public static class SliceColorPalette
{
    public static SliceHsb LevelBaseColor(int siblingIndex, int siblingCount)
    {
        var hue = siblingCount <= 1 ? 0f : 360f * siblingIndex / siblingCount;
        return new SliceHsb(hue, 0.95f, 0.68f);
    }

    public static SliceHsb ChildShade(SliceHsb parent, int siblingIndex, int siblingCount)
    {
        if (siblingCount <= 1)
            return new SliceHsb(parent.Hue, parent.Saturation, parent.Brightness);

        const float minBrightness = 0.38f;
        const float maxBrightness = 0.92f;
        var brightness = minBrightness + (maxBrightness - minBrightness) * siblingIndex / (siblingCount - 1);
        return new SliceHsb(parent.Hue, parent.Saturation, brightness);
    }
}
