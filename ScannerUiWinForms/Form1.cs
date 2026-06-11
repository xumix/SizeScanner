using System;
using System.Collections.Generic;
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
        public Form1()
        {
            InitializeComponent();
            _chartToolTipFont = new Font(FontFamily.GenericMonospace, Font.Size);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            RegistryKey reg = null;
            try
            {
                reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors");
                _cursorSize = reg !=null ? (int)reg.GetValue("CursorBaseSize") : 48;
            }
            catch
            {
                _cursorSize = 48;
            }
            finally
            {
                reg?.Dispose();
            }
            
        }

        private DriveScanner _scanner;
        private FsItem _scanRoot;
        private long _filterThreshold;
        private CancellationTokenSource _scanCts;
        private readonly int _cursorSize;
        private readonly Font _chartToolTipFont;

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
            var staticItems = mainToolStrip.Items.Count;
            var driveInsertIndex = mainToolStrip.Items.Count - staticItems;
            foreach (var driveInfo in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var driveButton = new Button { Text = driveInfo.Name };
                driveButton.Click += LoadDrive;
                var driveHost = CreateToolbarButtonHost(driveButton, autoSize: true);
                mainToolStrip.Items.Insert(driveInsertIndex++, driveHost);
            }

            if (driveInsertIndex > mainToolStrip.Items.Count - staticItems)
            {
                mainToolStrip.Items.Insert(driveInsertIndex,
                    new ToolStripSeparator { Margin = new Padding(8, 2, 8, 2) });
            }
            freeSpaceComboBox.SelectedIndex = 1;
            filterThresholdComboBox.SelectedIndex = 4;
            mainSplitContainer.SplitterDistance = mainSplitContainer.Width - LogicalToDeviceUnits(mainSplitContainer.Width - mainSplitContainer.SplitterDistance);
        }

        

        private async void LoadDrive(object sender, EventArgs e)
        {
            var target = ((Button)sender).Text.Substring(0, 2);
            _scanner = new DriveScanner();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();

            SetScanningState(true);
            SetAppStatus($"Scanning {target}...");
            SetStatusDetails(string.Empty);
            scanProgressTimer.Start();

            var token = _scanCts.Token;
            FsItem root;
            try
            {
                root = await Task.Run(() => _scanner.ScanDrive(target, token), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                FinishCancelledScan();
                return;
            }

            scanProgressTimer.Stop();
            scanProgressBar.Value = 0;

            if (token.IsCancellationRequested)
            {
                FinishCancelledScan();
                return;
            }

            inaccessibleListBox.Items.Clear();
            inaccessibleListBox.Items.AddRange(_scanner.Inaccessible.Cast<object>().ToArray());
            UpdateRelaunchAsAdminButtonVisibility();

            _scanRoot = root;
            inaccessibleTotalSizeLabel.Text = Humanize.Size(root.Items[1].Size);
            RefreshChart();

            SetAppStatus("Ready");
            SetStatusDetails(string.Empty);
            SetScanningState(false);
            _scanCts.Dispose();
            _scanCts = null;
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
            scanProgressTimer.Stop();
            scanProgressBar.Value = 0;
            SetAppStatus("Scan cancelled");
            SetStatusDetails(string.Empty);
            SetScanningState(false);
            _scanCts?.Dispose();
            _scanCts = null;
        }

        private void scanProgressTimer_Tick(object sender, EventArgs e)
        {
            scanProgressBar.Value = Math.Min((int) (_scanner.Progress*10), scanProgressBar.Maximum);
            SetStatusDetails(_scanner.CurrentScanned ?? string.Empty);
        }

        private void DisplayOptionsChanged(object sender, EventArgs e)
        {
            if (IsScanning || _scanRoot == null)
                return;
            RefreshChart();
        }

        private void RefreshChart()
        {
            if (_scanRoot == null || _scanner == null)
                return;

            _totals.Clear();
            _lastObjects = null;

            usageChart.BeginInit();
            usageChart.ChartAreas.Clear();
            usageChart.Series.Clear();

            var percent = 0.0025f * filterThresholdComboBox.SelectedIndex;
            var includeFreeSpace = freeSpaceComboBox.SelectedIndex == 0;
            _filterThreshold = _scanner.GetDisplayThreshold(percent, includeFreeSpace);

            var chartRoot = includeFreeSpace ? _scanRoot : GetChartRootWithoutSyntheticEntries();
            LoadChartDataCollection(0, chartRoot, 0);
            AlignDoughnuts();
            usageChart.EndInit();
        }

        private FsItem GetChartRootWithoutSyntheticEntries()
        {
            return new FsItem(_scanRoot.Name, _scanRoot.Size, _scanRoot.IsDir)
            {
                Items = _scanRoot.Items.Skip(2).ToList()
            };
        }

        private readonly Dictionary<Series, long> _totals = new Dictionary<Series, long>();
        private static readonly Color SliceBorderColor = Color.FromArgb(64, 0, 0, 0);

        private void LoadChartDataCollection(int dataLevel, FsItem dataPoint, long precedingObjectSize, Color? parentColor = null)
        {
            Series ser;
            if (!TryGetDataSeries(dataLevel, dataPoint, out ser)) return;

            if (precedingObjectSize > 0)
            {
                var delta = precedingObjectSize - _totals[ser];
                if (delta > 0)
                {
                    AddOrExtendPlaceHolder(delta, ser);
                }
            }

            var siblingCount = dataPoint.Items.Count;
            for (var siblingIndex = 0; siblingIndex < siblingCount; siblingIndex++)
            {
                var point = dataPoint.Items[siblingIndex];
                var itemColor = parentColor == null
                    ? GetLevelBaseColor(siblingIndex, siblingCount)
                    : GetChildShade(parentColor.Value, siblingIndex, siblingCount);

                if (point.Size > _filterThreshold)
                {
                    ApplySliceStyle(AddPoint(ser, point.Size, point), itemColor);
                }
                else
                {
                    AddOrExtendPlaceHolder(point.Size, ser);
                }
                if (point.Items != null && point.Items.Count > 0)
                {
                    LoadChartDataCollection(dataLevel + 1, point, precedingObjectSize, itemColor);
                }
                precedingObjectSize += point.Size;
            }
            LoadChartDataCollection(dataLevel + 1, Empty, precedingObjectSize);
        }

        private void AddOrExtendPlaceHolder(long size, Series series)
        {
            if (series.Points.Count > 0 && series.Points[series.Points.Count - 1].Tag.Equals(PlaceholderTag))
            {
                series.Points[series.Points.Count - 1].YValues[0] += size;
                _totals[series] += size;
            }
            else
            {
                var point = AddPoint(series, size, PlaceholderTag);
                point.Color = Color.FloralWhite;
                point.BorderWidth = 0;
            }
        }

        private static void ApplySliceStyle(DataPoint point, Color fillColor)
        {
            point.Color = fillColor;
            point.BorderWidth = 1;
            point.BorderColor = SliceBorderColor;
        }

        private static Color GetLevelBaseColor(int siblingIndex, int siblingCount)
        {
            var hue = siblingCount <= 1 ? 0f : 360f * siblingIndex / siblingCount;
            return ColorFromHsb(hue, 0.95f, 0.68f);
        }

        private static Color GetChildShade(Color parentColor, int siblingIndex, int siblingCount)
        {
            var hue = parentColor.GetHue();
            var saturation = parentColor.GetSaturation();
            if (siblingCount <= 1)
                return ColorFromHsb(hue, saturation, parentColor.GetBrightness());

            const float minBrightness = 0.38f;
            const float maxBrightness = 0.92f;
            var brightness = minBrightness + (maxBrightness - minBrightness) * siblingIndex / (siblingCount - 1);
            return ColorFromHsb(hue, saturation, brightness);
        }

        private static Color ColorFromHsb(float hue, float saturation, float brightness)
        {
            if (saturation <= 0)
            {
                var gray = (int)Math.Round(brightness * 255);
                return Color.FromArgb(gray, gray, gray);
            }

            hue = (hue % 360 + 360) % 360;
            var h = hue / 60f;
            var i = (int)Math.Floor(h);
            var f = h - i;
            var p = brightness * (1 - saturation);
            var q = brightness * (1 - saturation * f);
            var t = brightness * (1 - saturation * (1 - f));

            float r, g, b;
            switch (i % 6)
            {
                case 0: r = brightness; g = t; b = p; break;
                case 1: r = q; g = brightness; b = p; break;
                case 2: r = p; g = brightness; b = t; break;
                case 3: r = p; g = q; b = brightness; break;
                case 4: r = t; g = p; b = brightness; break;
                default: r = brightness; g = p; b = q; break;
            }

            return Color.FromArgb(
                (int)Math.Round(r * 255),
                (int)Math.Round(g * 255),
                (int)Math.Round(b * 255));
        }

        private DataPoint AddPoint(Series series, long size, object tag)
        {
            var point = new DataPoint
            {
                YValues = new[] {(double) size},
                Tag = tag
            };
            series.Points.Add(point);
            _totals[series] += size;
            return point;
        }

        private bool TryGetDataSeries(int dataLevel, FsItem dataPoint, out Series ser)
        {
            if (usageChart.ChartAreas.Count == dataLevel)
            {
                if (dataPoint == Empty)
                {
                    ser = null;
                    return false;
                }

                //create chart area and series
                var ca = new ChartArea("chartAreaLevel" + dataLevel)
                {
                    Position =
                    {
                        Auto = false,
                        X = 0,
                        Y = 0,
                        Height = 100,
                        Width = 100
                    }
                };
                if (dataLevel > 0)
                {
                    ca.BackColor = Color.Transparent;
                }
                usageChart.ChartAreas.Add(ca);

                ser = new Series("seriesLevel" + dataLevel)
                {
                    ChartArea = ca.Name,
                    ChartType = SeriesChartType.Doughnut,
                    IsXValueIndexed = true,
                    BorderWidth = 1,
                    BorderColor = SliceBorderColor
                };
                usageChart.Series.Add(ser);
                _totals.Add(ser, 0);
            }
            else
            {
                ser = usageChart.Series[dataLevel];
            }
            return true;
        }

        private static readonly FsItem Empty = new FsItem(null, 0, false) {Items = new List<FsItem>()};
        private const string PlaceholderTag = "Placeholder";

        private void AlignDoughnuts()
        {
            for (int i = usageChart.Series.Count - 1; i >= 0; i--)
            {
                var totalVisible = usageChart.Series[i].Points.Sum(p => p.Tag.Equals(PlaceholderTag) ? 0 : p.YValues[0]);
                if (totalVisible <= _filterThreshold)
                {
                    usageChart.Series.RemoveAt(i);
                }
            }
            var singleWidth = 85.0/usageChart.Series.Count;
            for (int i = 0; i < usageChart.Series.Count; i++)
            {
                usageChart.Series[i].CustomProperties = "PieStartAngle=270, DoughnutRadius=" + (int) (85 - singleWidth*i);
            }
        }

        private Point _last;
        private IList<HitTestResult> _lastObjects;
        private string _lastTip;
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
                    BuildToolTipText();
                    CancelChartToolTip();
                }
                if (!IsScanning)
                {
                    var fsItems = GetFsItemsArray();
                    SetStatusDetails(BuildFullPath(fsItems));
                }
                var offset = LogicalToDeviceUnits(_cursorSize/2);
                ScheduleChartToolTip(_lastTip, (int)(e.X + offset*0.75), e.Y + offset);
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

        private void BuildToolTipText()
        {
            var fsItems = GetFsItemsArray().Reverse().ToArray();
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

            return new string(' ', (depth - 1)*4) + "` " + name;
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

        private void ShowChartToolTip(string text, Control control, int x, int y)
        {
            chartToolTipTimer.Stop();
            _chartToolTipText = text ?? string.Empty;
            chartToolTip.Show(_chartToolTipText, control, x, y);
            _chartToolTipVisible = true;
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
            var lines = text.Split(new[] {Environment.NewLine}, StringSplitOptions.None);
            var padding = LogicalToDeviceUnits(6);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var lineHeight = TextRenderer.MeasureText("M", _chartToolTipFont, Size.Empty, flags).Height;
            var width = lines
                .Select(line => TextRenderer.MeasureText(string.IsNullOrEmpty(line) ? " " : line, _chartToolTipFont, Size.Empty, flags).Width)
                .DefaultIfEmpty(0)
                .Max();

            return new Size(width + padding*2, lineHeight*lines.Length + padding*2);
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

            foreach (var line in text.Split(new[] {Environment.NewLine}, StringSplitOptions.None))
            {
                TextRenderer.DrawText(e.Graphics, line, _chartToolTipFont, new Point(x, y), SystemColors.InfoText, flags);
                y += lineHeight;
            }
        }

        private FsItem[] GetFsItemsArray()
        {
            return _lastObjects.Where(o => o.Object != null && o.ChartElementType == ChartElementType.DataPoint)
                               .Select(o => ((DataPoint) o.Object).Tag as FsItem)
                               .Where(t => t != null)
                               .ToArray();
        }

        private string BuildFullPath(FsItem[] fsItems)
        {
            if (_scanner == null || fsItems.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(_scanner.CurrentTarget);
            for (int i = fsItems.Length - 1; i >= 0; i--)
            {
                builder.Append(Path.DirectorySeparatorChar);
                builder.Append(fsItems[i].Name);
            }
            return builder.ToString();
        }

        private bool CompareCollections(IList<HitTestResult> result)
        {
            if ((_lastObjects == null ^ result == null) || result == null)
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
        }

        private void UpdateRelaunchAsAdminButtonVisibility()
        {
            relaunchAsAdminButton.Visible = _scanner != null
                && _scanner.Inaccessible.Length > 0
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
                             usageChart.PointToClient(new Point(chartContextMenu.Left, chartContextMenu.Top - LogicalToDeviceUnits(52 + (int)Math.Ceiling(DeviceDpi/96.0)))));
        }

        private void showInExplorerMenuItem_Click(object sender, EventArgs e)
        {
            StartExplorerSelect((string)chartContextMenu.Tag);
        }

        public static void StartExplorerSelect(string objectToSelect)
        {
            StartExplorer("/select,\"" + objectToSelect + "\"");
        }

        public static void StartExplorer(string command = null)
        {
            const string explorerString = "explorer.exe";
            Process.Start(explorerString, command);
        }

        private void deleteMenuItem_Click(object sender, EventArgs e)
        {
            var path = (string) chartContextMenu.Tag;
            int i = 0;
            if (File.Exists(path))
            {
                i = 1;
            }
            else if (Directory.Exists(path))
            {
                i = 2;
            }
            if (i == 0)
            {
                MessageBox.Show("Object is already unavailable.");
                return;
            }
            if (
                MessageBox.Show("Are you sure you want to delete " + path, "Confirm operation", MessageBoxButtons.YesNo) ==
                DialogResult.No)
            {
                return;
            }
            try
            {
                if (i == 1)
                {
                    File.Delete(path);
                }
                else
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occurred: " + ex);
            }
        }
    }
}
