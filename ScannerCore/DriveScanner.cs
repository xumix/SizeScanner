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
        private readonly List<string> _problematic = new List<string>();
        private DirectoryScanner _scanner = null!;
        private IProgress<ScanProgress>? _progress;
        private Stopwatch? _progressStopwatch;
        private bool _isDriveScan;

        public float Progress =>
            _occupied == 0 ? 0 : _total * (float)100 / _occupied;

        public string[] Inaccessible => _problematic.ToArray();

        public string? CurrentTarget { get; private set; }

        public string? CurrentScanned { get; private set; }

        public long GetDisplayThreshold(float percent, bool includeFreeSpace) =>
            (long)(percent * (includeFreeSpace ? _total : _occupied));

        public FsItem ScanDrive(string driveName, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
        {
            var drive = new DriveInfo(driveName);
            _occupied = drive.TotalSize - drive.TotalFreeSpace;
            _isDriveScan = true;

            var root = ScanUnitInternal(driveName, true, cancellationToken, progress);
            root.Items!.InsertRange(0, new[]
            {
                new FsItem(DriveScanMetadata.FreeSpaceName, drive.TotalFreeSpace, false),
                new FsItem(DriveScanMetadata.InaccessibleName, Math.Max(0, _occupied - _total), false)
            });
            return root;
        }

        public FsItem ScanDirectory(string path, CancellationToken cancellationToken, IProgress<ScanProgress>? progress = null) =>
            ScanUnitInternal(path, false, cancellationToken, progress);

        private FsItem ScanUnitInternal(string location, bool useAllocationSize, CancellationToken run, IProgress<ScanProgress>? progress)
        {
            _total = 0;
            _problematic.Clear();
            CurrentTarget = location;
            _progress = progress;
            _progressStopwatch = null;
            _isDriveScan = useAllocationSize;

            var root = new FsItem(location, 0, true);
            _scanner = new DirectoryScanner(useAllocationSize);
            ScanChildren(root, null, run);
            ReportProgress(force: true);
            return root;
        }

        private void ScanChildren(FsItem item, string? parentPath, CancellationToken run)
        {
            if (run.IsCancellationRequested) return;

            var scanObject = parentPath + item.Name;
            if (scanObject[scanObject.Length - 1] != Path.DirectorySeparatorChar)
                scanObject += Path.DirectorySeparatorChar;

            CurrentScanned = scanObject;
            ReportProgress(force: false);
            item.Items = _scanner.Scan(scanObject, ref _total);
            if (item.Items == null)
            {
                _problematic.Add(scanObject);
                return;
            }

            for (var i = item.Items.Count - 1; i >= 0; i--)
            {
                var child = item.Items[i];
                if (child.IsDir)
                    ScanChildren(child, scanObject, run);
                item.Size += child.Size;
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
