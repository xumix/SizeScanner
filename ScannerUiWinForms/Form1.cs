using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Principal;
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
        private readonly int _cursorSize;

        private bool IsScanning => !mainToolStrip.Enabled;

        private void SetAppStatus(string text) =>
            toolStripStatusLabelStatus.Text = text;

        private void SetStatusDetails(string text) =>
            toolStripStatusLabelDetails.Text = text;

        private void Form1_Load(object sender, EventArgs e)
        {
            var staticItems = mainToolStrip.Items.Count;
            foreach (var driveInfo in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                mainToolStrip.Items.Insert(mainToolStrip.Items.Count - staticItems,
                                        new ToolStripButton(driveInfo.Name, null,
                                                            (o, ea) => LoadDrive((ToolStripItem) o)));
            }
            freeSpaceComboBox.SelectedIndex = 1;
            filterThresholdComboBox.SelectedIndex = 4;
            mainSplitContainer.SplitterDistance = mainSplitContainer.Width - LogicalToDeviceUnits(mainSplitContainer.Width - mainSplitContainer.SplitterDistance);
        }

        

        private async void LoadDrive(ToolStripItem sender)
        {
            var target = sender.Text.Substring(0, 2);
            _scanner = new DriveScanner();

            mainToolStrip.Enabled = false;
            SetAppStatus($"Scanning {target}...");
            SetStatusDetails(string.Empty);
            scanProgressTimer.Start();

            var root = await Task.Run(() => _scanner.ScanDrive(target));

            inaccessibleListBox.Items.Clear();
            inaccessibleListBox.Items.AddRange(_scanner.Inaccessible.Cast<object>().ToArray());
            UpdateRelaunchAsAdminButtonVisibility();

            _scanRoot = root;
            inaccessibleTotalSizeLabel.Text = Humanize.Size(root.Items[1].Size);
            RefreshChart();

            scanProgressTimer.Stop();
            scanProgressBar.Value = 0;
            SetAppStatus("Ready");
            SetStatusDetails(string.Empty);
            mainToolStrip.Enabled = true;
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

        private void LoadChartDataCollection(int dataLevel, FsItem dataPoint, long precedingObjectSize)
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

            foreach (var point in dataPoint.Items)
            {
                if (point.Size > _filterThreshold)
                {
                    AddPoint(ser, point.Size, point);
                }
                else
                {
                    AddOrExtendPlaceHolder(point.Size, ser);
                }
                if (point.Items != null && point.Items.Count > 0)
                {
                    LoadChartDataCollection(dataLevel + 1, point, precedingObjectSize);
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
            }
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
                    IsXValueIndexed = true
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

        private void usageChart_MouseMove(object sender, MouseEventArgs e)
        {
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
                }
                if (!IsScanning)
                {
                    var fsItems = GetFsItemsArray();
                    SetStatusDetails(BuildFullPath(fsItems));
                }
                var offset = LogicalToDeviceUnits(_cursorSize/2);
                chartToolTip.Show(_lastTip, usageChart, (int)(e.X + offset*0.75), e.Y + offset);
            }
            else
            {
                chartToolTip.Hide(usageChart);
                if (!IsScanning)
                    SetStatusDetails(string.Empty);
            }
        }

        private void usageChart_MouseLeave(object sender, EventArgs e)
        {
            if (!IsScanning)
                SetStatusDetails(string.Empty);
        }

        private void BuildToolTipText()
        {
            var fsItems = GetFsItemsArray();
            var list = new List<string>();
            var builder = new StringBuilder();
            for (int j = 0; j < fsItems.Length; j++)
            {
                for (int i = j; i < fsItems.Length - 1; i++)
                {
                    builder.Append(">");
                }
                var fsItem = fsItems[j];
                builder.AppendFormat("{0}: {1}", fsItem.Name, Humanize.FsItem(fsItem));
                list.Add(builder.ToString());
                builder.Clear();
            }
            _lastTip = string.Join("\r\n", list);
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
            chartToolTip.Show(builder.ToString(),
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
