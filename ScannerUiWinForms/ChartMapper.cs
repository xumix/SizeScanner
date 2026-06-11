// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms.DataVisualization.Charting;
using ScannerCore;

namespace ScannerUiWinForms;

internal sealed class ChartMapper
{
    private readonly Dictionary<Series, long> _totals = new Dictionary<Series, long>();
    private static readonly Color SliceBorderColor = Color.FromArgb(64, 0, 0, 0);
    private static readonly FsItem Empty = new FsItem(string.Empty, 0, false) { Items = new List<FsItem>() };
    public const string PlaceholderTag = "Placeholder";

    private long _filterThreshold;

    public void RefreshChart(Chart chart, FsItem root, long filterThreshold, bool includeFreeSpace)
    {
        _filterThreshold = filterThreshold;
        _totals.Clear();

        chart.BeginInit();
        chart.ChartAreas.Clear();
        chart.Series.Clear();

        LoadChartDataCollection(chart, 0, root, 0);
        AlignDoughnuts(chart);
        chart.EndInit();
    }

    private void LoadChartDataCollection(Chart chart, int dataLevel, FsItem dataPoint, long precedingObjectSize, Color? parentColor = null)
    {
        Series ser;
        if (!TryGetDataSeries(chart, dataLevel, dataPoint, out ser)) return;

        if (precedingObjectSize > 0)
        {
            var delta = precedingObjectSize - _totals[ser];
            if (delta > 0)
                AddOrExtendPlaceHolder(delta, ser);
        }

        var siblingCount = dataPoint.Items!.Count;
        for (var siblingIndex = 0; siblingIndex < siblingCount; siblingIndex++)
        {
            var point = dataPoint.Items[siblingIndex];
            var itemColor = parentColor == null
                ? GetLevelBaseColor(siblingIndex, siblingCount)
                : GetChildShade(parentColor.Value, siblingIndex, siblingCount);

            if (point.Size > _filterThreshold)
                ApplySliceStyle(AddPoint(ser, point.Size, point), itemColor);
            else
                AddOrExtendPlaceHolder(point.Size, ser);

            if (point.Items != null && point.Items.Count > 0)
                LoadChartDataCollection(chart, dataLevel + 1, point, precedingObjectSize, itemColor);
            precedingObjectSize += point.Size;
        }

        LoadChartDataCollection(chart, dataLevel + 1, Empty, precedingObjectSize);
    }

    private void AddOrExtendPlaceHolder(long size, Series series)
    {
        if (series.Points.Count > 0 && series.Points[series.Points.Count - 1].Tag!.Equals(PlaceholderTag))
        {
            series.Points[series.Points.Count - 1].YValues[0] += size;
            _totals[series] += size;
        }
        else
        {
            var point = AddPoint(series, size, PlaceholderTag);
            point.Color = Color.FloralWhite;
            point.BorderWidth = 0;
        }
    }

    private static void ApplySliceStyle(DataPoint point, Color fillColor)
    {
        point.Color = fillColor;
        point.BorderWidth = 1;
        point.BorderColor = SliceBorderColor;
    }

    private static Color GetLevelBaseColor(int siblingIndex, int siblingCount)
    {
        var hue = siblingCount <= 1 ? 0f : 360f * siblingIndex / siblingCount;
        return ColorFromHsb(hue, 0.95f, 0.68f);
    }

    private static Color GetChildShade(Color parentColor, int siblingIndex, int siblingCount)
    {
        var hue = parentColor.GetHue();
        var saturation = parentColor.GetSaturation();
        if (siblingCount <= 1)
            return ColorFromHsb(hue, saturation, parentColor.GetBrightness());

        const float minBrightness = 0.38f;
        const float maxBrightness = 0.92f;
        var brightness = minBrightness + (maxBrightness - minBrightness) * siblingIndex / (siblingCount - 1);
        return ColorFromHsb(hue, saturation, brightness);
    }

    private static Color ColorFromHsb(float hue, float saturation, float brightness)
    {
        if (saturation <= 0)
        {
            var gray = (int)Math.Round(brightness * 255);
            return Color.FromArgb(gray, gray, gray);
        }

        hue = (hue % 360 + 360) % 360;
        var h = hue / 60f;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var p = brightness * (1 - saturation);
        var q = brightness * (1 - saturation * f);
        var t = brightness * (1 - saturation * (1 - f));

        float r, g, b;
        switch (i % 6)
        {
            case 0: r = brightness; g = t; b = p; break;
            case 1: r = q; g = brightness; b = p; break;
            case 2: r = p; g = brightness; b = t; break;
            case 3: r = p; g = q; b = brightness; break;
            case 4: r = t; g = p; b = brightness; break;
            default: r = brightness; g = p; b = q; break;
        }

        return Color.FromArgb(
            (int)Math.Round(r * 255),
            (int)Math.Round(g * 255),
            (int)Math.Round(b * 255));
    }

    private DataPoint AddPoint(Series series, long size, object tag)
    {
        var point = new DataPoint
        {
            YValues = new[] { (double)size },
            Tag = tag
        };
        series.Points.Add(point);
        _totals[series] += size;
        return point;
    }

    private bool TryGetDataSeries(Chart chart, int dataLevel, FsItem dataPoint, out Series ser)
    {
        if (chart.ChartAreas.Count == dataLevel)
        {
            if (dataPoint == Empty)
            {
                ser = null!;
                return false;
            }

            var ca = new ChartArea("chartAreaLevel" + dataLevel)
            {
                Position =
                {
                    Auto = false,
                    X = 0,
                    Y = 0,
                    Height = 100,
                    Width = 100
                }
            };
            if (dataLevel > 0)
                ca.BackColor = Color.Transparent;
            chart.ChartAreas.Add(ca);

            ser = new Series("seriesLevel" + dataLevel)
            {
                ChartArea = ca.Name,
                ChartType = SeriesChartType.Doughnut,
                IsXValueIndexed = true,
                BorderWidth = 1,
                BorderColor = SliceBorderColor
            };
            chart.Series.Add(ser);
            _totals.Add(ser, 0);
        }
        else
        {
            ser = chart.Series[dataLevel];
        }

        return true;
    }

    private void AlignDoughnuts(Chart chart)
    {
        for (int i = chart.Series.Count - 1; i >= 0; i--)
        {
            var totalVisible = chart.Series[i].Points.Sum(p => p.Tag!.Equals(PlaceholderTag) ? 0 : p.YValues[0]);
            if (totalVisible <= _filterThreshold)
                chart.Series.RemoveAt(i);
        }

        if (chart.Series.Count == 0)
            return;

        var singleWidth = 85.0 / chart.Series.Count;
        for (int i = 0; i < chart.Series.Count; i++)
            chart.Series[i].CustomProperties = "PieStartAngle=270, DoughnutRadius=" + (int)(85 - singleWidth * i);
    }
}
