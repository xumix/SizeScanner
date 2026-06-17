// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

/// <summary>
/// Opt-in wall-clock comparison of parallel vs sequential directory walks on a real volume.
/// Set <c>SIZESCANNER_RUN_PERF_TESTS=1</c> to execute; default CI/dev test runs skip instantly.
/// </summary>
public sealed class DirectoryWalkEngineParallelSpeedTests(ITestOutputHelper output)
{
    private const string MeasurementRoot = @"C:\";
    private static readonly bool RunPerfTests =
        string.Equals(Environment.GetEnvironmentVariable("SIZESCANNER_RUN_PERF_TESTS"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "Performance")]
    public void Parallel_walk_is_faster_than_sequential_on_c_drive()
    {
        Assert.SkipUnless(RunPerfTests,
            "Set SIZESCANNER_RUN_PERF_TESTS=1 to run the C: parallel vs sequential speed comparison.");

        Assert.SkipUnless(Directory.Exists(MeasurementRoot), $"{MeasurementRoot} is not available.");

        Assert.SkipUnless(VolumeParallelismPolicy.ShouldParallelize(MeasurementRoot),
            $"{MeasurementRoot} is not SSD-class; parallel speedup is only expected on fixed volumes without seek penalty.");

        var sequentialEngine = new DirectoryWalkEngine(_ => false);
        var parallelEngine = new DirectoryWalkEngine(_ => true);

        var sequentialTimes = new List<TimeSpan>(capacity: 2);
        var parallelTimes = new List<TimeSpan>(capacity: 2);

        for (var round = 0; round < 2; round++)
        {
            var sequentialFirst = round % 2 == 0;
            if (sequentialFirst)
            {
                sequentialTimes.Add(MeasureScan(sequentialEngine, out var sequentialTotal));
                parallelTimes.Add(MeasureScan(parallelEngine, out var parallelTotal));
                output.WriteLine($"Round {round + 1}: sequential total={sequentialTotal:N0}, parallel total={parallelTotal:N0}");
            }
            else
            {
                parallelTimes.Add(MeasureScan(parallelEngine, out var parallelTotal));
                sequentialTimes.Add(MeasureScan(sequentialEngine, out var sequentialTotal));
                output.WriteLine($"Round {round + 1}: parallel total={parallelTotal:N0}, sequential total={sequentialTotal:N0}");
            }
        }

        var sequentialMedian = Median(sequentialTimes);
        var parallelMedian = Median(parallelTimes);

        output.WriteLine($"Sequential median: {sequentialMedian.TotalSeconds:F2}s");
        output.WriteLine($"Parallel median:   {parallelMedian.TotalSeconds:F2}s");
        output.WriteLine($"Speedup:           {sequentialMedian.TotalSeconds / parallelMedian.TotalSeconds:F2}x");

        Assert.True(
            parallelMedian < sequentialMedian,
            $"Parallel walk ({parallelMedian.TotalSeconds:F1}s) should be faster than sequential ({sequentialMedian.TotalSeconds:F1}s) on {MeasurementRoot}.");
    }

    private static TimeSpan MeasureScan(DirectoryWalkEngine engine, out long total)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var stopwatch = Stopwatch.StartNew();
        var result = engine.Scan(MeasurementRoot, isDriveScan: true, CancellationToken.None, onProgress: null);
        stopwatch.Stop();

        total = result.Total;
        return stopwatch.Elapsed;
    }

    private static TimeSpan Median(IReadOnlyList<TimeSpan> samples)
    {
        var sorted = new TimeSpan[samples.Count];
        for (var i = 0; i < samples.Count; i++)
            sorted[i] = samples[i];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
