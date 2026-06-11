// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace ScannerCore;

public sealed record ScanProgress(
    string CurrentPath,
    long BytesScanned,
    float? PercentComplete,
    bool IsDriveScan);
