// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace SizeScanner.Avalonia.Tests;

/// <summary>
/// Runs Avalonia object construction and property access for tests on a single, dedicated
/// background thread.
/// </summary>
/// <remarks>
/// Avalonia's <c>Dispatcher.UIThread</c> pins itself, on first use, to whichever thread
/// touches it first (for example while wiring up a <c>ContextMenu</c>'s <c>ItemCollection</c>
/// during <c>InitializeComponent</c>, or while a <c>DispatcherTimer</c> registers itself).
/// This test host has no windowing platform and does not depend on Avalonia.Headless, so
/// there is no "designated" UI thread; xUnit's Task-based executor can run tests that
/// construct Avalonia objects on different pooled threads even with test parallelization
/// disabled, since disabling parallelization only limits *concurrency*, not which physical
/// thread each sequential test lands on. Routing every such test through this single,
/// permanently running thread guarantees the Dispatcher is always touched from the same
/// physical thread, regardless of which xUnit worker thread invoked the test.
/// </remarks>
internal static class AvaloniaUiThread
{
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(30);
    private static readonly BlockingCollection<Action> Queue = new();

    static AvaloniaUiThread()
    {
        var thread = new Thread(() =>
        {
            foreach (var action in Queue.GetConsumingEnumerable())
                action();
        })
        {
            IsBackground = true,
            Name = "SizeScanner.Avalonia.Tests.AvaloniaUiThread",
        };
        thread.Start();
    }

    public static void Invoke(Action action) => Invoke<object?>(() =>
    {
        action();
        return null;
    });

    public static T Invoke<T>(Func<T> func)
    {
        var result = default(T);
        ExceptionDispatchInfo? error = null;
        var completed = new ManualResetEventSlim(false);

        Queue.Add(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                completed.Set();
            }
        });

        if (!completed.Wait(InvokeTimeout))
        {
            throw new TimeoutException(
                "Timed out after 30 seconds waiting for the AvaloniaUiThread pump to execute the queued action.");
        }

        completed.Dispose();
        error?.Throw();
        return result!;
    }
}
