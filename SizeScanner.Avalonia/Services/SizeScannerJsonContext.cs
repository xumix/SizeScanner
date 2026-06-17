// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Services;

[JsonSerializable(typeof(UserSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SizeScannerJsonContext : JsonSerializerContext;
