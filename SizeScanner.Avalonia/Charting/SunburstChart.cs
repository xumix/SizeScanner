// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace SizeScanner.Avalonia.Charting;

public sealed record SunburstChart(
    IReadOnlyList<SunburstSegment> Segments,
    int RingCount,
    long TotalSize);
