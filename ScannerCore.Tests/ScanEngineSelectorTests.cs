// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class ScanEngineSelectorTests
{
    private sealed class FakeEngine : IScanEngine
    {
        private readonly bool _canHandle;
        private readonly FsItem? _result;
        public int ScanCalls;

        public FakeEngine(bool canHandle, FsItem? result)
        {
            _canHandle = canHandle;
            _result = result;
        }

        public bool CanHandle(string target, bool isDriveScan, bool isElevated) => _canHandle;

        public ScanResult Scan(string target, bool isDriveScan, CancellationToken token, Action<string, long>? onProgress)
        {
            ScanCalls++;
            if (_result is null) throw new IOException("engine failure");
            return new ScanResult { Root = _result, Total = _result.Size, Inaccessible = Array.Empty<string>() };
        }
    }

    [Fact]
    public void Uses_first_capable_engine()
    {
        var preferred = new FakeEngine(canHandle: true, new FsItem("preferred", 10, isDir: true));
        var fallback = new FakeEngine(canHandle: true, new FsItem("fallback", 20, isDir: true));
        var selector = new ScanEngineSelector(new IScanEngine[] { preferred, fallback }, isElevated: true);

        var result = selector.Scan("C:", isDriveScan: true, CancellationToken.None, null);

        Assert.Equal("preferred", result.Root.Name);
        Assert.Equal(0, fallback.ScanCalls);
    }

    [Fact]
    public void Skips_engine_that_cannot_handle()
    {
        var incapable = new FakeEngine(canHandle: false, new FsItem("incapable", 10, isDir: true));
        var capable = new FakeEngine(canHandle: true, new FsItem("capable", 20, isDir: true));
        var selector = new ScanEngineSelector(new IScanEngine[] { incapable, capable }, isElevated: true);

        var result = selector.Scan("C:", isDriveScan: true, CancellationToken.None, null);

        Assert.Equal("capable", result.Root.Name);
        Assert.Equal(0, incapable.ScanCalls);
    }

    [Fact]
    public void Falls_back_when_preferred_engine_throws()
    {
        var failing = new FakeEngine(canHandle: true, result: null);
        var fallback = new FakeEngine(canHandle: true, new FsItem("fallback", 20, isDir: true));
        var selector = new ScanEngineSelector(new IScanEngine[] { failing, fallback }, isElevated: true);

        var result = selector.Scan("C:", isDriveScan: true, CancellationToken.None, null);

        Assert.Equal("fallback", result.Root.Name);
        Assert.Equal(1, failing.ScanCalls);
    }
}
