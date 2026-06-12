// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace SizeScanner.Avalonia.Converters;

/// <summary>True (pane visible) => 360px; False => zero width.</summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new GridLength(360) : new GridLength(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True (pane visible) => 360; False => 0 (pane column MinWidth).</summary>
public sealed class BoolToPaneMinWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 360.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
