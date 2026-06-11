// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Microsoft.Win32;
using ScannerCore;

namespace ScannerUiWinForms
{
    public partial class Form1 : Form
    {
        private const int DriveButtonInsertIndex = 3;

        private readonly ScanSession _session = new ScanSession();
        private readonly ChartMapper _chartMapper = new ChartMapper();
        private readonly UserSettings _settings = UserSettings.Load();
        private FsItem? _scanRoot;
        private FsItem? _scopedChartRoot;
        private FsItem? _chartRootWithoutSynthetic;
        private string _displayChartRootPath = string.Empty;
        private long _filterThreshold;
        private CancellationTokenSource? _scanCts;
        private readonly int _cursorSize;
        private readonly Font _chartToolTipFont;

        public Form1()
        {
            InitializeComponent();
            _chartToolTipFont = new Font(FontFamily.GenericMonospace, Font.Size);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            RegistryKey? reg = null;
            try
            {
                reg = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
                _cursorSize = reg != null ? (int)reg.GetValue("CursorBaseSize")! : 48;
            }
            catch
            {
                _cursorSize = 48;
            }
            finally
            {
                reg?.Dispose();
            }

            AssignButtonIcons();
        }

        private void AssignButtonIcons()
        {
            var iconSize = LogicalToDeviceUnits(16);
            var color = SystemColors.ControlText;

            AssignButtonIcon(chartGoUpButton, UiIcons.Up(iconSize, color));
            AssignButtonIcon(chartGoToRootButton, UiIcons.Root(iconSize, color));
            AssignButtonIcon(toggleInaccessiblePaneButton, UiIcons.ToggleInaccessiblePane(iconSize, color));
            AssignButtonIcon(relaunchAsAdminButton, UiIcons.RunAsAdministrator(iconSize, color));
        }

        private static void AssignButtonIcon(Button button, Bitmap image)
        {
            button.Image = image;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        private bool IsScanning => cancelScanButtonHost.Visible;

        private static ToolStripControlHost CreateToolbarButtonHost(Button button, bool autoSize = false)
        {
            button.UseVisualStyleBackColor = true;
            button.AutoSize = autoSize;
            button.Padding = new Padding(10, 4, 10, 4);
            if (!autoSize && button.Width < 60)
                button.Size = new Size(60, 33);

            return new ToolStripControlHost(button)
            {
                Margin = new Padding(4, 2, 4, 2),
                AutoSize = autoSize
            };
        }

        private void SetAppStatus(string text) =>
            toolStripStatusLabelStatus.Text = text;

        private void SetStatusDetails(string text) =>
            toolStripStatusLabelDetails.Text = text;

        private void Form1_Load(object sender, EventArgs e)
        {
            var driveInsertIndex = DriveButtonInsertIndex;
            foreach (var driveInfo in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var driveButton = new Button { Text = driveInfo.Name };
                driveButton.Click += LoadDrive;
                var driveHost = CreateToolbarButtonHost(driveButton, autoSize: true);
                mainToolStrip.Items.Insert(driveInsertIndex++, driveHost);
            }

            if (driveInsertIndex > DriveButtonInsertIndex)
            {
                mainToolStrip.Items.Insert(driveInsertIndex,
                    new ToolStripSeparator { Margin = new Padding(8, 2, 8, 2) });
            }

            freeSpaceComboBox.SelectedIndex = _settings.FreeSpaceIndex;
            filterThresholdComboBox.SelectedIndex = _settings.FilterIndex;
            if (_settings.WindowWidth > 0)
            {
                Width = _settings.WindowWidth;
                Height = _settings.WindowHeight;
            }

            if (_settings.SplitterDistance > 0)
                mainSplitContainer.SplitterDistance = _settings.SplitterDistance;
            else
                mainSplitContainer.SplitterDistance = mainSplitContainer.Width - LogicalToDeviceUnits(mainSplitContainer.Width - mainSplitContainer.SplitterDistance);

            mainSplitContainer.Panel2Collapsed = _settings.InaccessiblePaneCollapsed;
            chartNavPanel.Resize += (_, _) => UpdateChartScopeLabelWidth();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
            chartGoUpButton.Image?.Dispose();
            chartGoToRootButton.Image?.Dispose();
            toggleInaccessiblePaneButton.Image?.Dispose();
            relaunchAsAdminButton.Image?.Dispose();
            _chartToolTipFont.Dispose();
        }

        private void SaveSettings()
        {
            _settings.FilterIndex = filterThresholdComboBox.SelectedIndex;
            _settings.FreeSpaceIndex = freeSpaceComboBox.SelectedIndex;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.SplitterDistance = mainSplitContainer.SplitterDistance;
            _settings.InaccessiblePaneCollapsed = mainSplitContainer.Panel2Collapsed;
            _settings.Save();
        }

        private async void LoadDrive(object? sender, EventArgs e)
        {
            var target = ((Button)sender!).Text.Substring(0, 2);
            await StartScan(target, isDrive: true);
        }

        private async void browseFolderButton_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select a folder to scan",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            await StartScan(dialog.SelectedPath, isDrive: false);
        }

        private async void rescanButton_Click(object sender, EventArgs e)
        {
            if (_session.LastTarget == null) return;
            await StartScan(_session.LastTarget, _session.IsDriveScan);
        }

        private async Task StartScan(string target, bool isDrive)
        {
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();

            SetScanningState(true);
            SetAppStatus($"Scanning {target}...");
            SetStatusDetails(string.Empty);
            scanProgressBar.Value = 0;

            var token = _scanCts.Token;
            var progress = new Progress<ScanProgress>(OnScanProgress);
            FsItem root;
            try
            {
                root = await _session.RunAsync(target, isDrive, token, progress);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                FinishCancelledScan();
                return;
            }

            scanProgressBar.Value = 0;

            if (token.IsCancellationRequested)
            {
                FinishCancelledScan();
                return;
            }

            var inaccessiblePaths = _session.Scanner.Inaccessible;
            inaccessibleListBox.Items.Clear();
            inaccessibleListBox.Items.AddRange(inaccessiblePaths);
            UpdateRelaunchAsAdminButtonVisibility(inaccessiblePaths.Length);

            _scanRoot = root;
            _chartRootWithoutSynthetic = BuildChartRootWithoutSyntheticEntries(root, isDrive);
            ResetChartScope();
            if (isDrive)
            {
                inaccessibleTotalSizeLabel.Text = Humanize.Size(
                    DriveScanMetadata.GetInaccessibleEntry(_scanRoot).Size);
            }
            else
            {
                inaccessibleTotalSizeLabel.Text = Humanize.Size(0);
            }

            RefreshChart();
            rescanButton.Enabled = true;

            SetAppStatus("Ready");
            SetStatusDetails(string.Empty);
            SetScanningState(false);
            _scanCts.Dispose();
            _scanCts = null;
        }

        private void OnScanProgress(ScanProgress p)
        {
            if (p.PercentComplete.HasValue)
                scanProgressBar.Value = Math.Min((int)(p.PercentComplete.Value * 10), scanProgressBar.Maximum);
            SetStatusDetails(p.CurrentPath);
        }

        private void SetScanningState(bool scanning)
        {
            cancelScanButtonHost.Visible = scanning;
            cancelScanButton.Enabled = true;
            foreach (ToolStripItem item in mainToolStrip.Items)
            {
                if (item != cancelScanButtonHost)
                    item.Enabled = !scanning;
            }
        }

        private void cancelScanButton_Click(object sender, EventArgs e)
        {
            cancelScanButton.Enabled = false;
            _scanCts?.Cancel();
        }

        private void FinishCancelledScan()
        {
            scanProgressBar.Value = 0;
            SetAppStatus("Scan cancelled");
            SetStatusDetails(string.Empty);
            SetScanningState(false);
            _scanCts?.Dispose();
            _scanCts = null;
        }

        private void DisplayOptionsChanged(object sender, EventArgs e)
        {
            if (IsScanning || _scanRoot == null)
                return;
            RefreshChart();
            SaveSettings();
        }

        private void RefreshChart()
        {
            if (_scanRoot == null)
                return;

            var percent = 0.0025f * filterThresholdComboBox.SelectedIndex;
            var includeFreeSpace = freeSpaceComboBox.SelectedIndex == 0;
            _filterThreshold = _session.Scanner.GetDisplayThreshold(percent, includeFreeSpace);
            _chartMapper.RefreshChart(usageChart, GetDisplayChartRoot(), _filterThreshold, includeFreeSpace);
        }

        private FsItem GetBaseChartRoot()
        {
            var includeFreeSpace = freeSpaceComboBox.SelectedIndex == 0;
            return includeFreeSpace && _session.IsDriveScan
                ? _scanRoot!
                : GetChartRootWithoutSyntheticEntries();
        }

        private FsItem GetDisplayChartRoot() =>
            _scopedChartRoot ?? GetBaseChartRoot();

        private void ResetChartScope()
        {
            _scopedChartRoot = null;
            UpdateDisplayChartRootPath();
            UpdateChartNavButtons();
        }

        private void UpdateChartNavButtons()
        {
            chartNavPanel.Visible = _scopedChartRoot != null;
            if (_scopedChartRoot == null)
                return;

            chartScopeLabel.Text = $"{_displayChartRootPath}  |  {Humanize.FsItem(_scopedChartRoot)}";
            UpdateChartScopeLabelWidth();
        }

        private void UpdateDisplayChartRootPath()
        {
            if (_scopedChartRoot == null || _scanRoot == null)
            {
                _displayChartRootPath = _session.Scanner.CurrentTarget ?? string.Empty;
                return;
            }

            _displayChartRootPath = GetPathForNode(_scopedChartRoot);
        }

        private string GetPathForNode(FsItem node) =>
            _scanRoot != null && node.TryGetPathFrom(_scanRoot, out var path)
                ? path
                : _session.Scanner.CurrentTarget ?? string.Empty;

        private static FsItem? BuildChartRootWithoutSyntheticEntries(FsItem scanRoot, bool isDrive)
        {
            if (!isDrive || scanRoot.Items == null)
                return null;

            return new FsItem(scanRoot.Name, scanRoot.Size, scanRoot.IsDir)
            {
                Items = scanRoot.Items.Skip(DriveScanMetadata.SyntheticEntryCount).ToList()
            };
        }

        private void UpdateChartScopeLabelWidth()
        {
            if (!chartNavPanel.Visible)
                return;

            var available = chartNavPanel.ClientSize.Width - chartScopeLabel.Left - chartNavPanel.Padding.Right;
            if (available > 0)
                chartScopeLabel.MaximumSize = new Size(available, 0);
        }

        private static bool CanScopeTo(FsItem? item)
        {
            if (item == null || !item.IsDir)
                return false;
            if (item.Items == null || item.Items.Count == 0)
                return false;
            if (item.Name == DriveScanMetadata.FreeSpaceName || item.Name == DriveScanMetadata.InaccessibleName)
                return false;
            return true;
        }

        private void ScopeChartTo(FsItem directory)
        {
            _scopedChartRoot = directory;
            UpdateDisplayChartRootPath();
            UpdateChartNavButtons();
            RefreshChart();
        }

        private FsItem GetChartRootWithoutSyntheticEntries() =>
            _chartRootWithoutSynthetic ?? _scanRoot!;

        private Point _last;
        private IList<HitTestResult>? _lastObjects;
        private FsItem[] _lastFsItems = Array.Empty<FsItem>();
        private string _lastStatusPath = string.Empty;
        private string _lastTip = string.Empty;
        private string _chartToolTipText = string.Empty;
        private Point _pendingToolTipLocation;
        private bool _chartToolTipVisible;

        private void usageChart_MouseMove(object sender, MouseEventArgs e)
        {
            if (chartContextMenu.Visible)
                return;

            if (_last == e.Location) return;
            _last = e.Location;

            chartContextMenu.Hide();
            var objectUnder = usageChart.HitTest(e.X, e.Y, true, ChartElementType.DataPoint);
            if (objectUnder.Count > 0)
            {
                if (!CompareCollections(objectUnder))
                {
                    _lastObjects = objectUnder;
                    _lastFsItems = ExtractFsItems(objectUnder);
                    _lastStatusPath = BuildFullPath(_lastFsItems);
                    BuildToolTipText();
                    CancelChartToolTip();
                }
                if (!IsScanning)
                    SetStatusDetails(_lastStatusPath);
                var offset = LogicalToDeviceUnits(_cursorSize / 2);
                ScheduleChartToolTip(_lastTip, (int)(e.X + offset * 0.75), e.Y + offset);
            }
            else
            {
                CancelChartToolTip();
                if (!IsScanning)
                    SetStatusDetails(string.Empty);
            }
        }

        private void usageChart_MouseLeave(object sender, EventArgs e)
        {
            CancelChartToolTip();
            if (!IsScanning)
                SetStatusDetails(string.Empty);
        }

        private void usageChart_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || IsScanning || _scanRoot == null)
                return;

            CancelChartToolTip();
            var fsItems = GetFsItemsAt(e.X, e.Y);
            for (int i = 0; i < fsItems.Length; i++)
            {
                if (CanScopeTo(fsItems[i]))
                {
                    ScopeChartTo(fsItems[i]);
                    return;
                }
            }
        }

        private FsItem[] GetFsItemsAt(int x, int y)
        {
            var hits = usageChart.HitTest(x, y, true, ChartElementType.DataPoint);
            return ExtractFsItems(hits);
        }

        private void chartGoUpButton_Click(object sender, EventArgs e)
        {
            if (_scopedChartRoot == null || _scanRoot == null)
                return;

            var parent = _scopedChartRoot.Parent;
            if (parent == null || ReferenceEquals(parent, _scanRoot))
                ResetChartScope();
            else
            {
                _scopedChartRoot = parent;
                UpdateDisplayChartRootPath();
            }

            UpdateChartNavButtons();
            RefreshChart();
        }

        private void chartGoToRootButton_Click(object sender, EventArgs e)
        {
            if (_scopedChartRoot == null)
                return;

            ResetChartScope();
            RefreshChart();
        }

        private void BuildToolTipText()
        {
            var fsItems = _lastFsItems.Reverse().ToArray();
            if (fsItems.Length == 0)
            {
                _lastTip = string.Empty;
                return;
            }

            var names = fsItems
                .Select((fsItem, depth) => BuildToolTipTreeName(fsItem.Name, depth))
                .ToArray();
            var nameColumnWidth = names.Max(n => n.Length);
            var lines = names
                .Select((name, index) => $"{name.PadRight(nameColumnWidth)}  |  {Humanize.FsItem(fsItems[index])}");

            _lastTip = string.Join(Environment.NewLine, lines);
        }

        private static string BuildToolTipTreeName(string name, int depth)
        {
            if (depth == 0)
                return name;

            return new string(' ', (depth - 1) * 4) + "` " + name;
        }

        private void chartToolTip_Popup(object sender, PopupEventArgs e)
        {
            e.ToolTipSize = MeasureChartToolTip(_chartToolTipText);
        }

        private void ScheduleChartToolTip(string text, int x, int y)
        {
            if (_chartToolTipVisible)
                return;

            _chartToolTipText = text ?? string.Empty;
            _pendingToolTipLocation = new Point(x, y);
            chartToolTipTimer.Stop();
            chartToolTipTimer.Start();
        }

        private void chartToolTipTimer_Tick(object sender, EventArgs e)
        {
            chartToolTipTimer.Stop();
            ShowChartToolTip(_chartToolTipText, usageChart, _pendingToolTipLocation);
        }

        private void CancelChartToolTip()
        {
            chartToolTipTimer.Stop();
            chartToolTip.Hide(usageChart);
            _chartToolTipVisible = false;
        }

        private void ShowChartToolTip(string text, Control control, Point point)
        {
            chartToolTipTimer.Stop();
            _chartToolTipText = text ?? string.Empty;
            chartToolTip.Show(_chartToolTipText, control, point);
            _chartToolTipVisible = true;
        }

        private Size MeasureChartToolTip(string text)
        {
            var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var padding = LogicalToDeviceUnits(6);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var lineHeight = TextRenderer.MeasureText("M", _chartToolTipFont, Size.Empty, flags).Height;
            var width = lines
                .Select(line => TextRenderer.MeasureText(string.IsNullOrEmpty(line) ? " " : line, _chartToolTipFont, Size.Empty, flags).Width)
                .DefaultIfEmpty(0)
                .Max();

            return new Size(width + padding * 2, lineHeight * lines.Length + padding * 2);
        }

        private void chartToolTip_Draw(object sender, DrawToolTipEventArgs e)
        {
            e.DrawBackground();
            e.DrawBorder();

            var text = e.ToolTipText ?? _chartToolTipText;
            var padding = LogicalToDeviceUnits(6);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var lineHeight = TextRenderer.MeasureText("M", _chartToolTipFont, Size.Empty, flags).Height;
            var x = e.Bounds.Left + padding;
            var y = e.Bounds.Top + padding;

            foreach (var line in text.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
                TextRenderer.DrawText(e.Graphics, line, _chartToolTipFont, new Point(x, y), SystemColors.InfoText, flags);
                y += lineHeight;
            }
        }

        private FsItem[] GetFsItemsArray() => _lastFsItems;

        private static FsItem[] ExtractFsItems(IList<HitTestResult> hits)
        {
            var count = 0;
            foreach (var hit in hits)
            {
                if (hit.Object is DataPoint { Tag: FsItem } && hit.ChartElementType == ChartElementType.DataPoint)
                    count++;
            }

            if (count == 0)
                return Array.Empty<FsItem>();

            var items = new FsItem[count];
            var index = 0;
            foreach (var hit in hits)
            {
                if (hit.Object is DataPoint { Tag: FsItem item } && hit.ChartElementType == ChartElementType.DataPoint)
                    items[index++] = item;
            }

            return items;
        }

        private string BuildFullPath(FsItem[] fsItems)
        {
            if (fsItems.Length == 0)
                return _displayChartRootPath;

            var path = _displayChartRootPath;
            for (int i = fsItems.Length - 1; i >= 0; i--)
                path = Path.Combine(path, fsItems[i].Name);
            return path;
        }

        private bool CompareCollections(IList<HitTestResult> result)
        {
            if (_lastObjects == null)
                return false;
            if (_lastObjects.Count != result.Count)
                return false;
            for (int i = 0; i < result.Count; i++)
            {
                if (_lastObjects[i].Object != result[i].Object)
                    return false;
            }
            return true;
        }

        private void toggleInaccessiblePaneButton_Click(object sender, EventArgs e)
        {
            mainSplitContainer.Panel2Collapsed ^= true;
            SaveSettings();
        }

        private void UpdateRelaunchAsAdminButtonVisibility(int inaccessibleCount = -1)
        {
            if (inaccessibleCount < 0)
                inaccessibleCount = inaccessibleListBox.Items.Count;

            relaunchAsAdminButton.Visible = inaccessibleCount > 0
                && !IsRunningAsAdministrator();
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void relaunchAsAdminButton_Click(object sender, EventArgs e)
        {
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                MessageBox.Show("Could not determine the application path.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled the UAC prompt.
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occurred: " + ex);
            }
        }

        private void chartContextMenu_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            _last = Point.Empty;
        }

        private void chartContextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (IsFreeSpaceUnderCursor())
                e.Cancel = true;
        }

        private bool IsFreeSpaceUnderCursor()
        {
            var clientPoint = usageChart.PointToClient(System.Windows.Forms.Cursor.Position);
            var hits = usageChart.HitTest(clientPoint.X, clientPoint.Y, true, ChartElementType.DataPoint);
            return hits.Any(h =>
                h.Object is DataPoint dp &&
                dp.Tag is FsItem item &&
                item.Name == DriveScanMetadata.FreeSpaceName);
        }

        private void chartContextMenu_Opened(object sender, EventArgs e)
        {
            var cached = GetFsItemsArray();
            if (cached.Length == 0)
            {
                chartContextMenu.Enabled = false;
                return;
            }

            chartContextMenu.Enabled = true;
            var fullPath = BuildFullPath(cached);
            chartContextMenu.Tag = fullPath;
            var builder = new StringBuilder(fullPath);
            builder.AppendFormat("{0}{1}; {2}{0}(hover this tooltip to return back to search mode)",
                                 Environment.NewLine,
                                 cached[0].IsDir ? "Folder" : "File",
                                 Humanize.FsItem(cached[0]));
            ShowChartToolTip(builder.ToString(),
                             usageChart,
                             usageChart.PointToClient(new Point(chartContextMenu.Left, chartContextMenu.Top - LogicalToDeviceUnits(52 + (int)Math.Ceiling(DeviceDpi / 96.0)))));
        }

        private void showInExplorerMenuItem_Click(object sender, EventArgs e)
        {
            FileSystemActions.ShowInExplorer((string)chartContextMenu.Tag!);
        }

        private void deleteMenuItem_Click(object sender, EventArgs e)
        {
            TryDeleteSelectedItem(permanent: false);
        }

        private void deletePermanentlyMenuItem_Click(object sender, EventArgs e)
        {
            TryDeleteSelectedItem(permanent: true);
        }

        private void TryDeleteSelectedItem(bool permanent)
        {
            var path = (string)chartContextMenu.Tag!;
            var prompt = permanent
                ? "Are you sure you want to permanently delete " + path + "? This cannot be undone."
                : "Move to Recycle Bin?\n\n" + path;
            var title = permanent ? "Permanently delete" : "Move to Recycle Bin";

            if (MessageBox.Show(prompt, title, MessageBoxButtons.YesNo) == DialogResult.No)
                return;

            if (!FileSystemActions.TryDelete(path, permanent, out var error))
                MessageBox.Show(error ?? "Delete failed.");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { rescanButton.PerformClick(); return true; }
            if (keyData == Keys.Escape && IsScanning) { cancelScanButton.PerformClick(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { browseFolderButton.PerformClick(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
