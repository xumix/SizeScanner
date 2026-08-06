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
                sequentialTimes.Add(MeasureScan(sequentialEngine, ScanTreeBudget.Default, out var sequentialTotal));
                parallelTimes.Add(MeasureScan(parallelEngine, ScanTreeBudget.Default, out var parallelTotal));
                output.WriteLine($"Round {round + 1}: sequential total={sequentialTotal:N0}, parallel total={parallelTotal:N0}");
            }
            else
            {
                parallelTimes.Add(MeasureScan(parallelEngine, ScanTreeBudget.Default, out var parallelTotal));
                sequentialTimes.Add(MeasureScan(sequentialEngine, ScanTreeBudget.Default, out var sequentialTotal));
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

    /// <summary>
    /// Collects two samples per config across two global rounds — one pass A→E, one pass
    /// E→A — so every config gets one early-round and one late-round sample instead of the
    /// whole grouped order always measuring the last config against the warmest filesystem
    /// cache. Grouping by config (run both of its samples back-to-back, then move on) was the
    /// order-confound that motivated this shape; per-config medians below are therefore
    /// comparable across configs, not just across rounds.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void Fan_out_configuration_matrix_report()
    {
        Assert.SkipUnless(RunPerfTests,
            "Set SIZESCANNER_RUN_PERF_TESTS=1 to run the C: fan-out configuration matrix.");
        Assert.SkipUnless(Directory.Exists(MeasurementRoot), $"{MeasurementRoot} is not available.");
        Assert.SkipUnless(VolumeParallelismPolicy.ShouldParallelize(MeasurementRoot),
            $"{MeasurementRoot} is not SSD-class.");

        var processors = Math.Min(Environment.ProcessorCount, 16);
        (string Name, int Levels, int Degree)[] configs =
        [
            ("A sequential",      0, 1),
            ("B root-only dop4",  1, 4),
            ("C root-only dopN",  1, processors),
            ("D two-level dopN",  2, processors),
            ("E three-level dopN",3, processors)
        ];

        var samples = new List<TimeSpan>[configs.Length];
        var totals = new long[configs.Length];
        for (var i = 0; i < configs.Length; i++)
            samples[i] = new List<TimeSpan>(capacity: 2);

        for (var round = 0; round < 2; round++)
        {
            var forward = round % 2 == 0;
            output.WriteLine($"Round {round + 1} order: {(forward ? "A->E" : "E->A")}");

            for (var step = 0; step < configs.Length; step++)
            {
                var index = forward ? step : configs.Length - 1 - step;
                var config = configs[index];
                var engine = new DirectoryWalkEngine(_ => config.Levels > 0);
                var budget = new ScanTreeBudget(
                    maxDegreeOfParallelism: config.Degree,
                    parallelFanOutLevels: config.Levels);

                var elapsed = MeasureScan(engine, budget, out var total);
                samples[index].Add(elapsed);
                totals[index] = total;

                output.WriteLine(
                    $"  {config.Name,-20} levels={config.Levels} dop={config.Degree,-2} " +
                    $"elapsed={elapsed.TotalSeconds:F2}s total={total:N0}");
            }
        }

        for (var i = 0; i < configs.Length; i++)
        {
            output.WriteLine(
                $"{configs[i].Name,-20} levels={configs[i].Levels} dop={configs[i].Degree,-2} " +
                $"median={Median(samples[i]).TotalSeconds:F2}s total={totals[i]:N0}");
        }
    }

    private static TimeSpan MeasureScan(
        DirectoryWalkEngine engine, ScanTreeBudget budget, out long total)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var stopwatch = Stopwatch.StartNew();
        var result = engine.Scan(
            MeasurementRoot, isDriveScan: true, CancellationToken.None,
            onProgress: null, budget);
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
