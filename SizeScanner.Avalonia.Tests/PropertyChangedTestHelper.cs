// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

internal static class PropertyChangedTestHelper
{
    public static Task WaitForAsync(INotifyPropertyChanged source, string propertyName)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (e.PropertyName != propertyName)
                return;

            source.PropertyChanged -= handler;
            completion.TrySetResult();
        };

        source.PropertyChanged += handler;
        return WaitWithCleanupAsync();

        async Task WaitWithCleanupAsync()
        {
            try
            {
                await completion.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
            }
            finally
            {
                source.PropertyChanged -= handler;
            }
        }
    }
}
