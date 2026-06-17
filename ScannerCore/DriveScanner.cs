// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ScannerCore
{
    public class DriveScanner
    {
        private long _total, _occupied;
        private IReadOnlyList<string> _problematic = Array.Empty<string>();
        private IProgress<ScanProgress>? _progress;
        private Stopwatch? _progressStopwatch;
        private bool _isDriveScan;
        private readonly object _progressLock = new();
        private readonly IScanEngine _engine;

        public DriveScanner()
            : this(new ScanEngineSelector(new IScanEngine[] { new DirectoryWalkEngine() }, DetectElevation())) { }

        private static bool DetectElevation()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public DriveScanner(IScanEngine engine) => _engine = engine;

        public float Progress =>
            _occupied == 0 ? 0 : _total * (float)100 / _occupied;

        public string[] Inaccessible
        {
            get
            {
                var array = new string[_problematic.Count];
                for (var i = 0; i < _problematic.Count; i++) array[i] = _problematic[i];
                return array;
            }
        }

        public string? CurrentTarget { get; private set; }

        public string? CurrentScanned { get; private set; }

        public long GetDisplayThreshold(float percent, bool includeFreeSpace) =>
            (long)(percent * (includeFreeSpace ? _total : _occupied));

        public FsItem ScanDrive(string driveName, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
        {
            var drive = new DriveInfo(driveName);
            _occupied = drive.TotalSize - drive.TotalFreeSpace;

            var root = ScanUnitInternal(driveName, isDriveScan: true, cancellationToken, progress);
            var freeSpace = new FsItem(DriveScanMetadata.FreeSpaceName, drive.TotalFreeSpace, false);
            var inaccessible = new FsItem(DriveScanMetadata.InaccessibleName, Math.Max(0, _occupied - _total), false);
            freeSpace.Parent = root;
            inaccessible.Parent = root;
            root.Items!.InsertRange(0, new[] { freeSpace, inaccessible });
            return root;
        }

        public FsItem ScanDirectory(string path, CancellationToken cancellationToken, IProgress<ScanProgress>? progress = null) =>
            ScanUnitInternal(path, isDriveScan: false, cancellationToken, progress);

        private FsItem ScanUnitInternal(string location, bool isDriveScan, CancellationToken token, IProgress<ScanProgress>? progress)
        {
            _total = 0;
            _problematic = Array.Empty<string>();
            CurrentTarget = location;
            CurrentScanned = null;
            _progress = progress;
            _progressStopwatch = null;
            _isDriveScan = isDriveScan;

            var result = _engine.Scan(location, isDriveScan, token, OnEngineProgress);
            _total = result.Total;
            _problematic = result.Inaccessible;
            ReportProgress(force: true);
            return result.Root;
        }

        private void OnEngineProgress(string currentPath, long total)
        {
            lock (_progressLock)
            {
                CurrentScanned = currentPath;
                _total = total;
                ReportProgress(force: false);
            }
        }

        private void ReportProgress(bool force)
        {
            if (_progress == null)
                return;

            if (!force)
            {
                if (_progressStopwatch == null)
                    _progressStopwatch = Stopwatch.StartNew();
                else if (_progressStopwatch.ElapsedMilliseconds < 300)
                    return;
                else
                    _progressStopwatch.Restart();
            }

            float? percent = _occupied == 0 ? null : Progress;
            _progress.Report(new ScanProgress(CurrentScanned ?? CurrentTarget ?? string.Empty, _total, percent, _isDriveScan));
        }
    }
}
