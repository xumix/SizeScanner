// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;
using Avalonia.Controls;
using SizeScanner.Avalonia;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.ViewModels;
using SizeScanner.Avalonia.Views;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ViewLocatorTests
{
    private readonly ViewLocator _locator = new();

    [Fact]
    public void Match_returns_true_for_view_models() =>
        Assert.True(_locator.Match(new ChartViewModel(new NoopFs(), new NoopDialogs())));

    [Fact]
    public void Match_returns_false_for_non_view_models() =>
        Assert.False(_locator.Match("not a view model"));

    [Fact]
    public void Build_returns_null_for_null() =>
        Assert.Null(_locator.Build(null));

    [Fact]
    public void Build_returns_ChartView_for_ChartViewModel()
    {
        var view = _locator.Build(new ChartViewModel(new NoopFs(), new NoopDialogs()));

        Assert.IsType<ChartView>(view);
    }

    [Fact]
    public void Build_returns_TextBlock_for_unregistered_view_model()
    {
        var view = _locator.Build(new UnregisteredViewModel());

        var textBlock = Assert.IsType<TextBlock>(view);
        Assert.Contains(nameof(UnregisteredViewModel), textBlock.Text);
    }

    private sealed class UnregisteredViewModel : ViewModelBase;

    private sealed class NoopFs : IFileSystemActions
    {
        public void ShowInExplorer(string path) { }
        public bool TryDelete(string path, bool permanent, out string? error) { error = null; return true; }
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}
