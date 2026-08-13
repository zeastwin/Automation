namespace Automation.Hmi
{
    partial class LegacyDataViewControl
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
            this.modeLayout = new System.Windows.Forms.TableLayoutPanel();
            this.uphPanel = new System.Windows.Forms.Panel();
            this.uphButton = new System.Windows.Forms.Button();
            this.ngPanel = new System.Windows.Forms.Panel();
            this.ngButton = new System.Windows.Forms.Button();
            this.contentLayout = new System.Windows.Forms.TableLayoutPanel();
            this.grid = new System.Windows.Forms.DataGridView();
            this.summaryChart = new Automation.Hmi.LegacyBarChartControl();
            this.rootLayout.SuspendLayout();
            this.modeLayout.SuspendLayout();
            this.uphPanel.SuspendLayout();
            this.ngPanel.SuspendLayout();
            this.contentLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grid)).BeginInit();
            this.SuspendLayout();
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.modeLayout, 0, 0);
            this.rootLayout.Controls.Add(this.contentLayout, 0, 1);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 2;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 22.22222F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 77.77778F));
            this.modeLayout.ColumnCount = 2;
            this.modeLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.modeLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.modeLayout.Controls.Add(this.uphPanel, 0, 0);
            this.modeLayout.Controls.Add(this.ngPanel, 1, 0);
            this.modeLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.modeLayout.Name = "modeLayout";
            this.uphPanel.Controls.Add(this.uphButton);
            this.uphPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.uphButton.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.uphButton.BackColor = System.Drawing.Color.Gainsboro;
            this.uphButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.uphButton.Font = new System.Drawing.Font("微软雅黑", 15.75F);
            this.uphButton.Location = new System.Drawing.Point(195, 34);
            this.uphButton.Name = "uphButton";
            this.uphButton.Size = new System.Drawing.Size(200, 70);
            this.uphButton.Text = "UPH/CT";
            this.uphButton.UseVisualStyleBackColor = false;
            this.uphButton.Click += new System.EventHandler(this.UphButton_Click);
            this.ngPanel.Controls.Add(this.ngButton);
            this.ngPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ngButton.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.ngButton.BackColor = System.Drawing.Color.Gainsboro;
            this.ngButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ngButton.Font = new System.Drawing.Font("微软雅黑", 15.75F);
            this.ngButton.Location = new System.Drawing.Point(195, 34);
            this.ngButton.Name = "ngButton";
            this.ngButton.Size = new System.Drawing.Size(200, 70);
            this.ngButton.Text = "Tossing/NG";
            this.ngButton.UseVisualStyleBackColor = false;
            this.ngButton.Click += new System.EventHandler(this.NgButton_Click);
            this.contentLayout.ColumnCount = 2;
            this.contentLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65F));
            this.contentLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
            this.contentLayout.Controls.Add(this.grid, 0, 0);
            this.contentLayout.Controls.Add(this.summaryChart, 1, 0);
            this.contentLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentLayout.Name = "contentLayout";
            this.contentLayout.Padding = new System.Windows.Forms.Padding(8, 0, 8, 8);
            this.grid.AllowUserToAddRows = false;
            this.grid.AllowUserToDeleteRows = false;
            this.grid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.grid.BackgroundColor = System.Drawing.Color.White;
            this.grid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grid.Name = "grid";
            this.grid.ReadOnly = true;
            this.grid.RowHeadersVisible = false;
            this.grid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.summaryChart.BackColor = System.Drawing.Color.White;
            this.summaryChart.Dock = System.Windows.Forms.DockStyle.Fill;
            this.summaryChart.Margin = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.summaryChart.Name = "summaryChart";
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this.rootLayout);
            this.Name = "LegacyDataViewControl";
            this.Size = new System.Drawing.Size(1185, 702);
            this.rootLayout.ResumeLayout(false);
            this.modeLayout.ResumeLayout(false);
            this.uphPanel.ResumeLayout(false);
            this.ngPanel.ResumeLayout(false);
            this.contentLayout.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.grid)).EndInit();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.TableLayoutPanel modeLayout;
        private System.Windows.Forms.Panel uphPanel;
        private System.Windows.Forms.Panel ngPanel;
        private System.Windows.Forms.Button uphButton;
        private System.Windows.Forms.Button ngButton;
        private System.Windows.Forms.TableLayoutPanel contentLayout;
        private System.Windows.Forms.DataGridView grid;
        private Automation.Hmi.LegacyBarChartControl summaryChart;
    }
}
