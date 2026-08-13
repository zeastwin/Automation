namespace Automation.Hmi
{
    partial class LegacyProductDataControl
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
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.todayChart = new Automation.Hmi.LegacyBarChartControl();
            this.weekChart = new Automation.Hmi.LegacyBarChartControl();
            this.yieldChart = new Automation.Hmi.LegacyBarChartControl();
            this.functionLayout = new System.Windows.Forms.TableLayoutPanel();
            this.tossingLayout = new System.Windows.Forms.TableLayoutPanel();
            this.tossingTotal = new System.Windows.Forms.Label();
            this.stationALayout = new System.Windows.Forms.TableLayoutPanel();
            this.stationALabel = new System.Windows.Forms.Label();
            this.stationATotal = new System.Windows.Forms.TextBox();
            this.stationBLayout = new System.Windows.Forms.TableLayoutPanel();
            this.stationBLabel = new System.Windows.Forms.Label();
            this.stationBTotal = new System.Windows.Forms.TextBox();
            this.queryLayout = new System.Windows.Forms.TableLayoutPanel();
            this.dateLayout = new System.Windows.Forms.TableLayoutPanel();
            this.startLabel = new System.Windows.Forms.Label();
            this.endLabel = new System.Windows.Forms.Label();
            this.startPicker = new System.Windows.Forms.DateTimePicker();
            this.endPicker = new System.Windows.Forms.DateTimePicker();
            this.queryButtonLayout = new System.Windows.Forms.TableLayoutPanel();
            this.queryButton = new System.Windows.Forms.Button();
            this.exportButton = new System.Windows.Forms.Button();
            this.rootLayout.SuspendLayout();
            this.functionLayout.SuspendLayout();
            this.tossingLayout.SuspendLayout();
            this.stationALayout.SuspendLayout();
            this.stationBLayout.SuspendLayout();
            this.queryLayout.SuspendLayout();
            this.dateLayout.SuspendLayout();
            this.queryButtonLayout.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 2;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 71.3F));
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 28.7F));
            this.rootLayout.Controls.Add(this.todayChart, 0, 0);
            this.rootLayout.Controls.Add(this.weekChart, 0, 1);
            this.rootLayout.Controls.Add(this.yieldChart, 1, 0);
            this.rootLayout.Controls.Add(this.functionLayout, 1, 1);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.Padding = new System.Windows.Forms.Padding(3);
            this.rootLayout.RowCount = 2;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            //
            // charts
            //
            this.todayChart.BackColor = System.Drawing.Color.White;
            this.todayChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.todayChart.Margin = new System.Windows.Forms.Padding(4);
            this.todayChart.Name = "todayChart";
            this.weekChart.BackColor = System.Drawing.Color.White;
            this.weekChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.weekChart.Margin = new System.Windows.Forms.Padding(4);
            this.weekChart.Name = "weekChart";
            this.yieldChart.BackColor = System.Drawing.Color.White;
            this.yieldChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.yieldChart.Margin = new System.Windows.Forms.Padding(4);
            this.yieldChart.Name = "yieldChart";
            //
            // functionLayout
            //
            this.functionLayout.ColumnCount = 1;
            this.functionLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.functionLayout.Controls.Add(this.tossingLayout, 0, 0);
            this.functionLayout.Controls.Add(this.queryLayout, 0, 1);
            this.functionLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.functionLayout.Name = "functionLayout";
            this.functionLayout.RowCount = 2;
            this.functionLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.functionLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            //
            // tossingLayout
            //
            this.tossingLayout.ColumnCount = 2;
            this.tossingLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tossingLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tossingLayout.Controls.Add(this.tossingTotal, 0, 0);
            this.tossingLayout.Controls.Add(this.stationALayout, 0, 1);
            this.tossingLayout.Controls.Add(this.stationBLayout, 1, 1);
            this.tossingLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tossingLayout.Name = "tossingLayout";
            this.tossingLayout.Padding = new System.Windows.Forms.Padding(5);
            this.tossingLayout.RowCount = 2;
            this.tossingLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            this.tossingLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tossingLayout.SetColumnSpan(this.tossingTotal, 2);
            this.tossingTotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tossingTotal.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.tossingTotal.Name = "tossingTotal";
            this.tossingTotal.Text = "TossingSum";
            this.tossingTotal.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // station A/B
            //
            this.stationALayout.ColumnCount = 1;
            this.stationALayout.Controls.Add(this.stationALabel, 0, 0);
            this.stationALayout.Controls.Add(this.stationATotal, 0, 1);
            this.stationALayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stationALayout.RowCount = 2;
            this.stationALayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.stationALayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.stationALabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stationALabel.Font = new System.Drawing.Font("微软雅黑", 10.5F);
            this.stationALabel.Text = "工站A";
            this.stationALabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.stationATotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stationATotal.Font = new System.Drawing.Font("微软雅黑", 18F, System.Drawing.FontStyle.Bold);
            this.stationATotal.ReadOnly = true;
            this.stationATotal.Text = "0";
            this.stationATotal.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.stationBLayout.ColumnCount = 1;
            this.stationBLayout.Controls.Add(this.stationBLabel, 0, 0);
            this.stationBLayout.Controls.Add(this.stationBTotal, 0, 1);
            this.stationBLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stationBLayout.RowCount = 2;
            this.stationBLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.stationBLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.stationBLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stationBLabel.Font = new System.Drawing.Font("微软雅黑", 10.5F);
            this.stationBLabel.Text = "工站B";
            this.stationBLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.stationBTotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stationBTotal.Font = new System.Drawing.Font("微软雅黑", 18F, System.Drawing.FontStyle.Bold);
            this.stationBTotal.ReadOnly = true;
            this.stationBTotal.Text = "0";
            this.stationBTotal.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            //
            // queryLayout
            //
            this.queryLayout.ColumnCount = 1;
            this.queryLayout.Controls.Add(this.dateLayout, 0, 0);
            this.queryLayout.Controls.Add(this.queryButtonLayout, 0, 1);
            this.queryLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryLayout.Padding = new System.Windows.Forms.Padding(6);
            this.queryLayout.RowCount = 2;
            this.queryLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this.queryLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.dateLayout.ColumnCount = 2;
            this.dateLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 78F));
            this.dateLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.dateLayout.Controls.Add(this.startLabel, 0, 0);
            this.dateLayout.Controls.Add(this.startPicker, 1, 0);
            this.dateLayout.Controls.Add(this.endLabel, 0, 1);
            this.dateLayout.Controls.Add(this.endPicker, 1, 1);
            this.dateLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startLabel.Font = new System.Drawing.Font("微软雅黑", 10.5F);
            this.startLabel.Text = "开始时间";
            this.startLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.endLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.endLabel.Font = new System.Drawing.Font("微软雅黑", 10.5F);
            this.endLabel.Text = "结束时间";
            this.endLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.startPicker.CustomFormat = "yyyy-MM-dd";
            this.startPicker.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startPicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.endPicker.CustomFormat = "yyyy-MM-dd";
            this.endPicker.Dock = System.Windows.Forms.DockStyle.Fill;
            this.endPicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.queryButtonLayout.ColumnCount = 2;
            this.queryButtonLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.queryButtonLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.queryButtonLayout.Controls.Add(this.queryButton, 0, 0);
            this.queryButtonLayout.Controls.Add(this.exportButton, 1, 0);
            this.queryButtonLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryButton.BackColor = System.Drawing.Color.Gainsboro;
            this.queryButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.queryButton.Font = new System.Drawing.Font("微软雅黑", 11F);
            this.queryButton.Text = "查询";
            this.queryButton.UseVisualStyleBackColor = false;
            this.queryButton.Click += new System.EventHandler(this.QueryButton_Click);
            this.exportButton.BackColor = System.Drawing.Color.Gainsboro;
            this.exportButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.exportButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.exportButton.Font = new System.Drawing.Font("微软雅黑", 11F);
            this.exportButton.Text = "导出";
            this.exportButton.UseVisualStyleBackColor = false;
            this.exportButton.Click += new System.EventHandler(this.ExportButton_Click);
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this.rootLayout);
            this.Name = "LegacyProductDataControl";
            this.Size = new System.Drawing.Size(1170, 665);
            this.rootLayout.ResumeLayout(false);
            this.functionLayout.ResumeLayout(false);
            this.tossingLayout.ResumeLayout(false);
            this.stationALayout.ResumeLayout(false);
            this.stationALayout.PerformLayout();
            this.stationBLayout.ResumeLayout(false);
            this.stationBLayout.PerformLayout();
            this.queryLayout.ResumeLayout(false);
            this.dateLayout.ResumeLayout(false);
            this.queryButtonLayout.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private Automation.Hmi.LegacyBarChartControl todayChart;
        private Automation.Hmi.LegacyBarChartControl weekChart;
        private Automation.Hmi.LegacyBarChartControl yieldChart;
        private System.Windows.Forms.TableLayoutPanel functionLayout;
        private System.Windows.Forms.TableLayoutPanel tossingLayout;
        private System.Windows.Forms.Label tossingTotal;
        private System.Windows.Forms.TableLayoutPanel stationALayout;
        private System.Windows.Forms.Label stationALabel;
        private System.Windows.Forms.TextBox stationATotal;
        private System.Windows.Forms.TableLayoutPanel stationBLayout;
        private System.Windows.Forms.Label stationBLabel;
        private System.Windows.Forms.TextBox stationBTotal;
        private System.Windows.Forms.TableLayoutPanel queryLayout;
        private System.Windows.Forms.TableLayoutPanel dateLayout;
        private System.Windows.Forms.Label startLabel;
        private System.Windows.Forms.Label endLabel;
        private System.Windows.Forms.DateTimePicker startPicker;
        private System.Windows.Forms.DateTimePicker endPicker;
        private System.Windows.Forms.TableLayoutPanel queryButtonLayout;
        private System.Windows.Forms.Button queryButton;
        private System.Windows.Forms.Button exportButton;
    }
}
