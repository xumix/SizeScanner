// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Text;

namespace ScannerUiWinForms;

internal static class UiIcons
{
    private const string IconFontName = "Segoe MDL2 Assets";

    public static Bitmap Up(int size, Color color) =>
        FromGlyph("\uE70E", size, color);

    public static Bitmap Root(int size, Color color) =>
        FromGlyph("\uE80F", size, color);

    public static Bitmap ToggleInaccessiblePane(int size, Color color) =>
        FromGlyph("\uE8BF", size, color);

    public static Bitmap RunAsAdministrator(int size, Color color) =>
        FromGlyph("\uE7EF", size, color);

    private static Bitmap FromGlyph(string glyph, int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var font = new Font(IconFontName, size * 0.7f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }
}
