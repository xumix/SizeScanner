namespace ScannerUiWinForms
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                _chartToolTipFont?.Dispose();
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea1 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            System.Windows.Forms.DataVisualization.Charting.Series series1 = new System.Windows.Forms.DataVisualization.Charting.Series();
            this.mainToolStrip = new System.Windows.Forms.ToolStrip();
            this.scanProgressBar = new System.Windows.Forms.ToolStripProgressBar();
            this.freeSpaceComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.filterLabel = new System.Windows.Forms.ToolStripLabel();
            this.filterThresholdComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.toggleInaccessiblePaneButton = new System.Windows.Forms.ToolStripButton();
            this.scanProgressTimer = new System.Windows.Forms.Timer(this.components);
            this.mainSplitContainer = new System.Windows.Forms.SplitContainer();
            this.usageChart = new System.Windows.Forms.DataVisualization.Charting.Chart();
            this.chartContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.showInExplorerMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.deleteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.inaccessibleListBox = new System.Windows.Forms.ListBox();
            this.inaccessibleHeaderPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.inaccessibleHeaderPrefixLabel = new System.Windows.Forms.Label();
            this.inaccessibleTotalSizeLabel = new System.Windows.Forms.Label();
            this.inaccessibleHeaderSuffixLabel = new System.Windows.Forms.Label();
            this.relaunchAsAdminButton = new System.Windows.Forms.Button();
            this.chartToolTip = new System.Windows.Forms.ToolTip(this.components);
            this.mainStatusStrip = new System.Windows.Forms.StatusStrip();
            this.toolStripStatusLabelStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabelDetails = new System.Windows.Forms.ToolStripStatusLabel();
            this.mainToolStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).BeginInit();
            this.mainSplitContainer.Panel1.SuspendLayout();
            this.mainSplitContainer.Panel2.SuspendLayout();
            this.mainSplitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.usageChart)).BeginInit();
            this.chartContextMenu.SuspendLayout();
            this.inaccessibleHeaderPanel.SuspendLayout();
            this.mainStatusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // mainToolStrip
            // 
            this.mainToolStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.mainToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.scanProgressBar,
            this.freeSpaceComboBox,
            this.filterLabel,
            this.filterThresholdComboBox,
            this.toggleInaccessiblePaneButton});
            this.mainToolStrip.Location = new System.Drawing.Point(0, 0);
            this.mainToolStrip.Name = "mainToolStrip";
            this.mainToolStrip.Padding = new System.Windows.Forms.Padding(0, 0, 3, 0);
            this.mainToolStrip.Size = new System.Drawing.Size(1054, 38);
            this.mainToolStrip.TabIndex = 0;
            this.mainToolStrip.Text = "mainToolStrip";
            // 
            // scanProgressBar
            // 
            this.scanProgressBar.Maximum = 1000;
            this.scanProgressBar.Name = "scanProgressBar";
            this.scanProgressBar.Size = new System.Drawing.Size(450, 33);
            // 
            // freeSpaceComboBox
            // 
            this.freeSpaceComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.freeSpaceComboBox.Items.AddRange(new object[] {
            "Show free space",
            "Hide free space"});
            this.freeSpaceComboBox.Name = "freeSpaceComboBox";
            this.freeSpaceComboBox.Size = new System.Drawing.Size(163, 38);
            this.freeSpaceComboBox.SelectedIndexChanged += new System.EventHandler(this.DisplayOptionsChanged);
            // 
            // filterLabel
            // 
            this.filterLabel.Name = "filterLabel";
            this.filterLabel.Size = new System.Drawing.Size(54, 33);
            this.filterLabel.Text = "Filter:";
            // 
            // filterThresholdComboBox
            // 
            this.filterThresholdComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.filterThresholdComboBox.DropDownWidth = 120;
            this.filterThresholdComboBox.Items.AddRange(new object[] {
            "No threshold (SLOW!)",
            "0.25% (Slower)",
            "0.5%",
            "0.75%",
            "1%",
            "1.25%",
            "1.5%",
            "1.75% (Rougher)",
            "2% (ROUGH!)"});
            this.filterThresholdComboBox.Name = "filterThresholdComboBox";
            this.filterThresholdComboBox.Size = new System.Drawing.Size(110, 38);
            this.filterThresholdComboBox.SelectedIndexChanged += new System.EventHandler(this.DisplayOptionsChanged);
            // 
            // toggleInaccessiblePaneButton
            // 
            this.toggleInaccessiblePaneButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.toggleInaccessiblePaneButton.Image = ((System.Drawing.Image)(resources.GetObject("toggleInaccessiblePaneButton.Image")));
            this.toggleInaccessiblePaneButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toggleInaccessiblePaneButton.Name = "toggleInaccessiblePaneButton";
            this.toggleInaccessiblePaneButton.Size = new System.Drawing.Size(34, 33);
            this.toggleInaccessiblePaneButton.Text = "[]||";
            this.toggleInaccessiblePaneButton.ToolTipText = "Show/Hide Inaccessible Objects Pane";
            this.toggleInaccessiblePaneButton.Click += new System.EventHandler(this.toggleInaccessiblePaneButton_Click);
            // 
            // scanProgressTimer
            // 
            this.scanProgressTimer.Interval = 300;
            this.scanProgressTimer.Tick += new System.EventHandler(this.scanProgressTimer_Tick);
            // 
            // mainSplitContainer
            // 
            this.mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainSplitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.mainSplitContainer.Location = new System.Drawing.Point(0, 38);
            this.mainSplitContainer.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.mainSplitContainer.Name = "mainSplitContainer";
            // 
            // mainSplitContainer.Panel1
            // 
            this.mainSplitContainer.Panel1.Controls.Add(this.usageChart);
            // 
            // mainSplitContainer.Panel2
            // 
            this.mainSplitContainer.Panel2.Controls.Add(this.inaccessibleListBox);
            this.mainSplitContainer.Panel2.Controls.Add(this.inaccessibleHeaderPanel);
            this.mainSplitContainer.Panel2MinSize = 0;
            this.mainSplitContainer.Size = new System.Drawing.Size(1054, 570);
            this.mainSplitContainer.SplitterDistance = 769;
            this.mainSplitContainer.SplitterWidth = 6;
            this.mainSplitContainer.TabIndex = 1;
            // 
            // usageChart
            // 
            chartArea1.Name = "ChartArea1";
            this.usageChart.ChartAreas.Add(chartArea1);
            this.usageChart.ContextMenuStrip = this.chartContextMenu;
            this.usageChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.usageChart.Location = new System.Drawing.Point(0, 0);
            this.usageChart.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.usageChart.Name = "usageChart";
            series1.ChartArea = "ChartArea1";
            series1.Name = "Series1";
            this.usageChart.Series.Add(series1);
            this.usageChart.Size = new System.Drawing.Size(769, 570);
            this.usageChart.TabIndex = 0;
            this.usageChart.Text = "usageChart";
            this.usageChart.MouseMove += new System.Windows.Forms.MouseEventHandler(this.usageChart_MouseMove);
            this.usageChart.MouseLeave += new System.EventHandler(this.usageChart_MouseLeave);
            // 
            // chartContextMenu
            // 
            this.chartContextMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.chartContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.showInExplorerMenuItem,
            this.deleteMenuItem});
            this.chartContextMenu.Name = "chartContextMenu";
            this.chartContextMenu.Size = new System.Drawing.Size(135, 68);
            this.chartContextMenu.Opened += new System.EventHandler(this.chartContextMenu_Opened);
            // 
            // showInExplorerMenuItem
            // 
            this.showInExplorerMenuItem.Name = "showInExplorerMenuItem";
            this.showInExplorerMenuItem.Size = new System.Drawing.Size(134, 32);
            this.showInExplorerMenuItem.Text = "Show";
            this.showInExplorerMenuItem.Click += new System.EventHandler(this.showInExplorerMenuItem_Click);
            // 
            // deleteMenuItem
            // 
            this.deleteMenuItem.Name = "deleteMenuItem";
            this.deleteMenuItem.Size = new System.Drawing.Size(134, 32);
            this.deleteMenuItem.Text = "Delete";
            this.deleteMenuItem.Click += new System.EventHandler(this.deleteMenuItem_Click);
            // 
            // inaccessibleListBox
            // 
            this.inaccessibleListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.inaccessibleListBox.FormattingEnabled = true;
            this.inaccessibleListBox.ItemHeight = 20;
            this.inaccessibleListBox.Location = new System.Drawing.Point(0, 20);
            this.inaccessibleListBox.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.inaccessibleListBox.Name = "inaccessibleListBox";
            this.inaccessibleListBox.Size = new System.Drawing.Size(279, 550);
            this.inaccessibleListBox.TabIndex = 0;
            // 
            // inaccessibleHeaderPanel
            // 
            this.inaccessibleHeaderPanel.AutoSize = true;
            this.inaccessibleHeaderPanel.Controls.Add(this.inaccessibleHeaderPrefixLabel);
            this.inaccessibleHeaderPanel.Controls.Add(this.inaccessibleTotalSizeLabel);
            this.inaccessibleHeaderPanel.Controls.Add(this.inaccessibleHeaderSuffixLabel);
            this.inaccessibleHeaderPanel.Controls.Add(this.relaunchAsAdminButton);
            this.inaccessibleHeaderPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.inaccessibleHeaderPanel.Location = new System.Drawing.Point(0, 0);
            this.inaccessibleHeaderPanel.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.inaccessibleHeaderPanel.Name = "inaccessibleHeaderPanel";
            this.inaccessibleHeaderPanel.Size = new System.Drawing.Size(279, 20);
            this.inaccessibleHeaderPanel.TabIndex = 3;
            // 
            // inaccessibleHeaderPrefixLabel
            // 
            this.inaccessibleHeaderPrefixLabel.AutoSize = true;
            this.inaccessibleHeaderPrefixLabel.Location = new System.Drawing.Point(0, 0);
            this.inaccessibleHeaderPrefixLabel.Margin = new System.Windows.Forms.Padding(0);
            this.inaccessibleHeaderPrefixLabel.Name = "inaccessibleHeaderPrefixLabel";
            this.inaccessibleHeaderPrefixLabel.Size = new System.Drawing.Size(196, 20);
            this.inaccessibleHeaderPrefixLabel.TabIndex = 1;
            this.inaccessibleHeaderPrefixLabel.Text = "Inaccessible objects ( total";
            // 
            // inaccessibleTotalSizeLabel
            // 
            this.inaccessibleTotalSizeLabel.AutoSize = true;
            this.inaccessibleTotalSizeLabel.Location = new System.Drawing.Point(196, 0);
            this.inaccessibleTotalSizeLabel.Margin = new System.Windows.Forms.Padding(0);
            this.inaccessibleTotalSizeLabel.Name = "inaccessibleTotalSizeLabel";
            this.inaccessibleTotalSizeLabel.Size = new System.Drawing.Size(62, 20);
            this.inaccessibleTotalSizeLabel.TabIndex = 4;
            this.inaccessibleTotalSizeLabel.Text = "0 Bytes";
            // 
            // inaccessibleHeaderSuffixLabel
            // 
            this.inaccessibleHeaderSuffixLabel.AutoSize = true;
            this.inaccessibleHeaderSuffixLabel.Location = new System.Drawing.Point(258, 0);
            this.inaccessibleHeaderSuffixLabel.Margin = new System.Windows.Forms.Padding(0);
            this.inaccessibleHeaderSuffixLabel.Name = "inaccessibleHeaderSuffixLabel";
            this.inaccessibleHeaderSuffixLabel.Size = new System.Drawing.Size(18, 20);
            this.inaccessibleHeaderSuffixLabel.TabIndex = 5;
            this.inaccessibleHeaderSuffixLabel.Text = "):";
            // 
            // relaunchAsAdminButton
            // 
            this.relaunchAsAdminButton.AutoSize = true;
            this.relaunchAsAdminButton.Margin = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.relaunchAsAdminButton.Name = "relaunchAsAdminButton";
            this.relaunchAsAdminButton.Size = new System.Drawing.Size(198, 30);
            this.relaunchAsAdminButton.TabIndex = 6;
            this.relaunchAsAdminButton.Text = "Relaunch as administrator";
            this.relaunchAsAdminButton.UseVisualStyleBackColor = true;
            this.relaunchAsAdminButton.Visible = false;
            this.relaunchAsAdminButton.Click += new System.EventHandler(this.relaunchAsAdminButton_Click);
            // 
            // chartToolTip
            // 
            this.chartToolTip.OwnerDraw = true;
            this.chartToolTip.Popup += new System.Windows.Forms.PopupEventHandler(this.chartToolTip_Popup);
            this.chartToolTip.Draw += new System.Windows.Forms.DrawToolTipEventHandler(this.chartToolTip_Draw);
            // 
            // mainStatusStrip
            // 
            this.mainStatusStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.mainStatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabelStatus,
            this.toolStripStatusLabelDetails});
            this.mainStatusStrip.Location = new System.Drawing.Point(0, 582);
            this.mainStatusStrip.Name = "mainStatusStrip";
            this.mainStatusStrip.Padding = new System.Windows.Forms.Padding(2, 0, 21, 0);
            this.mainStatusStrip.Size = new System.Drawing.Size(1054, 26);
            this.mainStatusStrip.SizingGrip = false;
            this.mainStatusStrip.TabIndex = 2;
            this.mainStatusStrip.Text = "mainStatusStrip";
            // 
            // toolStripStatusLabelStatus
            // 
            this.toolStripStatusLabelStatus.Name = "toolStripStatusLabelStatus";
            this.toolStripStatusLabelStatus.Size = new System.Drawing.Size(52, 19);
            this.toolStripStatusLabelStatus.Text = "Ready";
            // 
            // toolStripStatusLabelDetails
            // 
            this.toolStripStatusLabelDetails.Name = "toolStripStatusLabelDetails";
            this.toolStripStatusLabelDetails.Size = new System.Drawing.Size(977, 19);
            this.toolStripStatusLabelDetails.Spring = true;
            this.toolStripStatusLabelDetails.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1054, 608);
            this.Controls.Add(this.mainSplitContainer);
            this.Controls.Add(this.mainStatusStrip);
            this.Controls.Add(this.mainToolStrip);
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.Name = "Form1";
            this.Text = "SizeScanner";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.mainToolStrip.ResumeLayout(false);
            this.mainToolStrip.PerformLayout();
            this.mainSplitContainer.Panel1.ResumeLayout(false);
            this.mainSplitContainer.Panel2.ResumeLayout(false);
            this.mainSplitContainer.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).EndInit();
            this.mainSplitContainer.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.usageChart)).EndInit();
            this.chartContextMenu.ResumeLayout(false);
            this.inaccessibleHeaderPanel.ResumeLayout(false);
            this.inaccessibleHeaderPanel.PerformLayout();
            this.mainStatusStrip.ResumeLayout(false);
            this.mainStatusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip mainToolStrip;
        private System.Windows.Forms.ToolStripProgressBar scanProgressBar;
        private System.Windows.Forms.Timer scanProgressTimer;
        private System.Windows.Forms.SplitContainer mainSplitContainer;
        private System.Windows.Forms.ListBox inaccessibleListBox;
        private System.Windows.Forms.DataVisualization.Charting.Chart usageChart;
        private System.Windows.Forms.ToolTip chartToolTip;
        private System.Windows.Forms.ToolStripComboBox freeSpaceComboBox;
        private System.Windows.Forms.Label inaccessibleHeaderPrefixLabel;
        private System.Windows.Forms.ToolStripComboBox filterThresholdComboBox;
        private System.Windows.Forms.FlowLayoutPanel inaccessibleHeaderPanel;
        private System.Windows.Forms.Label inaccessibleTotalSizeLabel;
        private System.Windows.Forms.Label inaccessibleHeaderSuffixLabel;
        private System.Windows.Forms.ToolStripButton toggleInaccessiblePaneButton;
        private System.Windows.Forms.ToolStripLabel filterLabel;
        private System.Windows.Forms.ContextMenuStrip chartContextMenu;
        private System.Windows.Forms.ToolStripMenuItem showInExplorerMenuItem;
        private System.Windows.Forms.ToolStripMenuItem deleteMenuItem;
        private System.Windows.Forms.StatusStrip mainStatusStrip;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabelStatus;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabelDetails;
        private System.Windows.Forms.Button relaunchAsAdminButton;
    }
}
