namespace Automation.Hmi
{
    partial class AlarmHistoryPage
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.startPicker = new System.Windows.Forms.DateTimePicker();
            this.endPicker = new System.Windows.Forms.DateTimePicker();
            this.queryButton = new System.Windows.Forms.Button();
            this.statisticsMode = new System.Windows.Forms.ComboBox();
            this.deviceFilter = new System.Windows.Forms.ComboBox();
            this.downtimeList = new System.Windows.Forms.ListView();
            this.rankColumn = new System.Windows.Forms.ColumnHeader();
            this.errorColumn = new System.Windows.Forms.ColumnHeader();
            this.totalTimeColumn = new System.Windows.Forms.ColumnHeader();
            this.countColumn = new System.Windows.Forms.ColumnHeader();
            this.pieChart = new Automation.Hmi.LegacyPieChartControl();
            this.durationChart = new Automation.Hmi.LegacyBarChartControl();
            this.detailGrid = new System.Windows.Forms.DataGridView();
            ((System.ComponentModel.ISupportInitialize)(this.detailGrid)).BeginInit();
            this.SuspendLayout();
            //
            // startPicker
            //
            this.startPicker.CustomFormat = "yyyy-MM-dd";
            this.startPicker.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startPicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.startPicker.Name = "startPicker";
            this.startPicker.Value = System.DateTime.Today;
            //
            // endPicker
            //
            this.endPicker.CustomFormat = "yyyy-MM-dd";
            this.endPicker.Dock = System.Windows.Forms.DockStyle.Fill;
            this.endPicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.endPicker.Name = "endPicker";
            this.endPicker.Value = System.DateTime.Today;
            //
            // queryButton
            //
            this.queryButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryButton.Name = "queryButton";
            this.queryButton.Text = "Query";
            this.queryButton.Click += new System.EventHandler((sender, args) => this.Query());
            //
            // statisticsMode
            //
            this.statisticsMode.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statisticsMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.statisticsMode.Items.AddRange(new object[] { "类别统计", "详细统计" });
            this.statisticsMode.Name = "statisticsMode";
            this.statisticsMode.SelectedIndex = 0;
            //
            // deviceFilter
            //
            this.deviceFilter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.deviceFilter.Items.AddRange(new object[] { "全部设备" });
            this.deviceFilter.Name = "deviceFilter";
            this.deviceFilter.SelectedIndex = 0;
            this.deviceFilter.SelectedIndexChanged += new System.EventHandler((sender, args) =>
            {
                if (!this.updatingDeviceFilter && this.loadedRecords.Count > 0)
                {
                    System.Collections.Generic.IReadOnlyList<LegacyAlarmHistoryRecord> filtered =
                        this.FilterByDevice(this.loadedRecords);
                    this.detailGrid.DataSource = new System.ComponentModel.BindingList<LegacyAlarmHistoryRecord>(
                        new System.Collections.Generic.List<LegacyAlarmHistoryRecord>(filtered));
                    this.RenderSummary(filtered);
                }
            });
            //
            // downtimeList
            //
            this.downtimeList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.rankColumn,
                this.errorColumn,
                this.totalTimeColumn,
                this.countColumn});
            this.downtimeList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.downtimeList.FullRowSelect = true;
            this.downtimeList.GridLines = true;
            this.downtimeList.HideSelection = false;
            this.downtimeList.Name = "downtimeList";
            this.downtimeList.View = System.Windows.Forms.View.Details;
            //
            // columns
            //
            this.rankColumn.Text = "";
            this.rankColumn.Width = 34;
            this.errorColumn.Text = "Error";
            this.errorColumn.Width = 210;
            this.totalTimeColumn.Text = "Total Time";
            this.totalTimeColumn.Width = 92;
            this.countColumn.Text = "Count Total";
            this.countColumn.Width = 90;
            //
            // pieChart
            //
            this.pieChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pieChart.Name = "pieChart";
            //
            // durationChart
            //
            this.durationChart.BackColor = System.Drawing.Color.White;
            this.durationChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.durationChart.Name = "durationChart";
            //
            // detailGrid
            //
            this.detailGrid.AllowUserToAddRows = false;
            this.detailGrid.AllowUserToDeleteRows = false;
            this.detailGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.detailGrid.BackgroundColor = System.Drawing.Color.White;
            this.detailGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.detailGrid.ReadOnly = true;
            this.detailGrid.RowHeadersVisible = false;
            this.detailGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            //
            // AlarmHistoryPage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Font = new System.Drawing.Font("宋体", 9F);
            this.Name = "AlarmHistoryPage";
            this.Text = "UI_Alarm_View";
            ((System.ComponentModel.ISupportInitialize)(this.detailGrid)).EndInit();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.DateTimePicker startPicker;
        private System.Windows.Forms.DateTimePicker endPicker;
        private System.Windows.Forms.Button queryButton;
        private System.Windows.Forms.ComboBox statisticsMode;
        private System.Windows.Forms.ComboBox deviceFilter;
        private System.Windows.Forms.ListView downtimeList;
        private System.Windows.Forms.ColumnHeader rankColumn;
        private System.Windows.Forms.ColumnHeader errorColumn;
        private System.Windows.Forms.ColumnHeader totalTimeColumn;
        private System.Windows.Forms.ColumnHeader countColumn;
        private Automation.Hmi.LegacyPieChartControl pieChart;
        private Automation.Hmi.LegacyBarChartControl durationChart;
        private System.Windows.Forms.DataGridView detailGrid;
    }
}
