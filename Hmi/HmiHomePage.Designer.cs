namespace Automation.Hmi
{
    partial class HmiHomePage
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
            this.homeRoot = new System.Windows.Forms.Panel();
            this.workspacePanel = new System.Windows.Forms.Panel();
            this.sidebarPanel = new System.Windows.Forms.Panel();
            this.sidebarLayout = new System.Windows.Forms.TableLayoutPanel();
            this.statisticsSection = new System.Windows.Forms.TableLayoutPanel();
            this.statisticsHeader = new System.Windows.Forms.Panel();
            this.lblStatisticsTitle = new System.Windows.Forms.Label();
            this.statisticsLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblInputTotal = new System.Windows.Forms.Label();
            this.lblInputValue = new System.Windows.Forms.Label();
            this.lblOutputTotal = new System.Windows.Forms.Label();
            this.lblOutputValue = new System.Windows.Forms.Label();
            this.lblDefectTotal = new System.Windows.Forms.Label();
            this.lblDefectValue = new System.Windows.Forms.Label();
            this.lblGoodTotal = new System.Windows.Forms.Label();
            this.lblGoodValue = new System.Windows.Forms.Label();
            this.lblYield = new System.Windows.Forms.Label();
            this.lblYieldValue = new System.Windows.Forms.Label();
            this.lblCycle = new System.Windows.Forms.Label();
            this.lblCycleValue = new System.Windows.Forms.Label();
            this.btnResetCounter = new System.Windows.Forms.Button();
            this.systemSection = new System.Windows.Forms.TableLayoutPanel();
            this.systemHeader = new System.Windows.Forms.Panel();
            this.lblSystemTitle = new System.Windows.Forms.Label();
            this.lblSystemStatus = new System.Windows.Forms.Label();
            this.homeRoot.SuspendLayout();
            this.sidebarPanel.SuspendLayout();
            this.sidebarLayout.SuspendLayout();
            this.statisticsSection.SuspendLayout();
            this.statisticsHeader.SuspendLayout();
            this.statisticsLayout.SuspendLayout();
            this.systemSection.SuspendLayout();
            this.systemHeader.SuspendLayout();
            this.SuspendLayout();
            //
            // homeRoot
            //
            this.homeRoot.BackColor = System.Drawing.Color.FromArgb(226, 234, 240);
            this.homeRoot.Controls.Add(this.workspacePanel);
            this.homeRoot.Controls.Add(this.sidebarPanel);
            this.homeRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.homeRoot.Location = new System.Drawing.Point(0, 0);
            this.homeRoot.Name = "homeRoot";
            this.homeRoot.Size = new System.Drawing.Size(1200, 656);
            this.homeRoot.TabIndex = 0;
            //
            // workspacePanel
            //
            this.workspacePanel.BackColor = System.Drawing.Color.White;
            this.workspacePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.workspacePanel.Location = new System.Drawing.Point(370, 0);
            this.workspacePanel.Name = "workspacePanel";
            this.workspacePanel.Size = new System.Drawing.Size(830, 656);
            this.workspacePanel.TabIndex = 1;
            //
            // sidebarPanel
            //
            this.sidebarPanel.Controls.Add(this.sidebarLayout);
            this.sidebarPanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.sidebarPanel.Location = new System.Drawing.Point(0, 0);
            this.sidebarPanel.Name = "sidebarPanel";
            this.sidebarPanel.Padding = new System.Windows.Forms.Padding(6, 6, 6, 0);
            this.sidebarPanel.Size = new System.Drawing.Size(370, 656);
            this.sidebarPanel.TabIndex = 0;
            //
            // sidebarLayout
            //
            this.sidebarLayout.ColumnCount = 1;
            this.sidebarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.sidebarLayout.Controls.Add(this.statisticsSection, 0, 0);
            this.sidebarLayout.Controls.Add(this.systemSection, 0, 2);
            this.sidebarLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sidebarLayout.Location = new System.Drawing.Point(6, 6);
            this.sidebarLayout.Name = "sidebarLayout";
            this.sidebarLayout.RowCount = 3;
            this.sidebarLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 62F));
            this.sidebarLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 6F));
            this.sidebarLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 38F));
            this.sidebarLayout.Size = new System.Drawing.Size(358, 650);
            this.sidebarLayout.TabIndex = 0;
            //
            // statisticsSection
            //
            this.statisticsSection.BackColor = System.Drawing.Color.White;
            this.statisticsSection.ColumnCount = 1;
            this.statisticsSection.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.statisticsSection.Controls.Add(this.statisticsHeader, 0, 0);
            this.statisticsSection.Controls.Add(this.statisticsLayout, 0, 1);
            this.statisticsSection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statisticsSection.Margin = new System.Windows.Forms.Padding(0);
            this.statisticsSection.Name = "statisticsSection";
            this.statisticsSection.RowCount = 2;
            this.statisticsSection.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 56F));
            this.statisticsSection.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.statisticsSection.TabIndex = 0;
            //
            // statisticsHeader
            //
            this.statisticsHeader.BackColor = System.Drawing.Color.DarkSlateGray;
            this.statisticsHeader.Controls.Add(this.lblStatisticsTitle);
            this.statisticsHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statisticsHeader.Margin = new System.Windows.Forms.Padding(1);
            this.statisticsHeader.Name = "statisticsHeader";
            this.statisticsHeader.TabIndex = 0;
            //
            // lblStatisticsTitle
            //
            this.lblStatisticsTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatisticsTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 13F, System.Drawing.FontStyle.Bold);
            this.lblStatisticsTitle.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.lblStatisticsTitle.Text = "统计数据";
            this.lblStatisticsTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // statisticsLayout
            //
            this.statisticsLayout.ColumnCount = 2;
            this.statisticsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 55F));
            this.statisticsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 45F));
            this.statisticsLayout.Controls.Add(this.lblInputTotal, 0, 0);
            this.statisticsLayout.Controls.Add(this.lblInputValue, 1, 0);
            this.statisticsLayout.Controls.Add(this.lblOutputTotal, 0, 1);
            this.statisticsLayout.Controls.Add(this.lblOutputValue, 1, 1);
            this.statisticsLayout.Controls.Add(this.lblDefectTotal, 0, 2);
            this.statisticsLayout.Controls.Add(this.lblDefectValue, 1, 2);
            this.statisticsLayout.Controls.Add(this.lblGoodTotal, 0, 3);
            this.statisticsLayout.Controls.Add(this.lblGoodValue, 1, 3);
            this.statisticsLayout.Controls.Add(this.lblYield, 0, 4);
            this.statisticsLayout.Controls.Add(this.lblYieldValue, 1, 4);
            this.statisticsLayout.Controls.Add(this.lblCycle, 0, 5);
            this.statisticsLayout.Controls.Add(this.lblCycleValue, 1, 5);
            this.statisticsLayout.Controls.Add(this.btnResetCounter, 0, 6);
            this.statisticsLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statisticsLayout.Padding = new System.Windows.Forms.Padding(24, 12, 24, 14);
            this.statisticsLayout.Name = "statisticsLayout";
            this.statisticsLayout.RowCount = 7;
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14F));
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14F));
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14F));
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14F));
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14F));
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14F));
            this.statisticsLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 16F));
            this.statisticsLayout.SetColumnSpan(this.btnResetCounter, 2);
            //
            this.lblInputTotal.Text = "投入总数";
            this.lblOutputTotal.Text = "产出总数";
            this.lblDefectTotal.Text = "次品总数";
            this.lblGoodTotal.Text = "良品总数";
            this.lblYield.Text = "良品率";
            this.lblCycle.Text = "周期";
            this.lblInputValue.Text = "--";
            this.lblOutputValue.Text = "--";
            this.lblDefectValue.Text = "--";
            this.lblGoodValue.Text = "--";
            this.lblYieldValue.Text = "--";
            this.lblCycleValue.Text = "-- s";
            this.lblInputTotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblInputTotal.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.lblInputTotal.ForeColor = System.Drawing.Color.FromArgb(47, 66, 82);
            this.lblInputTotal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblOutputTotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOutputTotal.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.lblOutputTotal.ForeColor = System.Drawing.Color.FromArgb(47, 66, 82);
            this.lblOutputTotal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblDefectTotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDefectTotal.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.lblDefectTotal.ForeColor = System.Drawing.Color.FromArgb(47, 66, 82);
            this.lblDefectTotal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblGoodTotal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblGoodTotal.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.lblGoodTotal.ForeColor = System.Drawing.Color.FromArgb(47, 66, 82);
            this.lblGoodTotal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblYield.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYield.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.lblYield.ForeColor = System.Drawing.Color.FromArgb(47, 66, 82);
            this.lblYield.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblCycle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycle.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.lblCycle.ForeColor = System.Drawing.Color.FromArgb(47, 66, 82);
            this.lblCycle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblInputValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblInputValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblInputValue.ForeColor = System.Drawing.Color.FromArgb(20, 56, 82);
            this.lblInputValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblOutputValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOutputValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblOutputValue.ForeColor = System.Drawing.Color.FromArgb(20, 56, 82);
            this.lblOutputValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblDefectValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDefectValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblDefectValue.ForeColor = System.Drawing.Color.FromArgb(20, 56, 82);
            this.lblDefectValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblGoodValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblGoodValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblGoodValue.ForeColor = System.Drawing.Color.FromArgb(20, 56, 82);
            this.lblGoodValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblYieldValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblYieldValue.ForeColor = System.Drawing.Color.FromArgb(20, 56, 82);
            this.lblYieldValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblCycleValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblCycleValue.ForeColor = System.Drawing.Color.FromArgb(20, 56, 82);
            this.lblCycleValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // btnResetCounter
            //
            this.btnResetCounter.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.btnResetCounter.BackColor = System.Drawing.Color.DarkSlateGray;
            this.btnResetCounter.FlatAppearance.BorderSize = 0;
            this.btnResetCounter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnResetCounter.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.btnResetCounter.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnResetCounter.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.btnResetCounter.Name = "btnResetCounter";
            this.btnResetCounter.Size = new System.Drawing.Size(132, 36);
            this.btnResetCounter.Text = "重新计数";
            this.btnResetCounter.UseVisualStyleBackColor = false;
            //
            // systemSection
            //
            this.systemSection.BackColor = System.Drawing.Color.White;
            this.systemSection.ColumnCount = 1;
            this.systemSection.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.systemSection.Controls.Add(this.systemHeader, 0, 0);
            this.systemSection.Controls.Add(this.lblSystemStatus, 0, 1);
            this.systemSection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.systemSection.Margin = new System.Windows.Forms.Padding(0);
            this.systemSection.Name = "systemSection";
            this.systemSection.RowCount = 2;
            this.systemSection.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 56F));
            this.systemSection.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.systemSection.TabIndex = 1;
            //
            // systemHeader
            //
            this.systemHeader.BackColor = System.Drawing.Color.DarkSlateGray;
            this.systemHeader.Controls.Add(this.lblSystemTitle);
            this.systemHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.systemHeader.Margin = new System.Windows.Forms.Padding(1);
            this.systemHeader.Name = "systemHeader";
            this.systemHeader.TabIndex = 0;
            //
            // lblSystemTitle
            //
            this.lblSystemTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSystemTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 13F, System.Drawing.FontStyle.Bold);
            this.lblSystemTitle.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.lblSystemTitle.Text = "系统状态栏";
            this.lblSystemTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblSystemStatus
            //
            this.lblSystemStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSystemStatus.Font = new System.Drawing.Font("Microsoft YaHei UI", 38F);
            this.lblSystemStatus.ForeColor = System.Drawing.Color.FromArgb(213, 55, 57);
            this.lblSystemStatus.Text = "未复位";
            this.lblSystemStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // HmiHomePage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Controls.Add(this.homeRoot);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "HmiHomePage";
            this.Text = "主页";
            this.homeRoot.ResumeLayout(false);
            this.sidebarPanel.ResumeLayout(false);
            this.sidebarLayout.ResumeLayout(false);
            this.statisticsSection.ResumeLayout(false);
            this.statisticsHeader.ResumeLayout(false);
            this.statisticsLayout.ResumeLayout(false);
            this.systemSection.ResumeLayout(false);
            this.systemHeader.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel homeRoot;
        private System.Windows.Forms.Panel workspacePanel;
        private System.Windows.Forms.Panel sidebarPanel;
        private System.Windows.Forms.TableLayoutPanel sidebarLayout;
        private System.Windows.Forms.TableLayoutPanel statisticsSection;
        private System.Windows.Forms.Panel statisticsHeader;
        private System.Windows.Forms.Label lblStatisticsTitle;
        private System.Windows.Forms.TableLayoutPanel statisticsLayout;
        private System.Windows.Forms.Label lblInputTotal;
        private System.Windows.Forms.Label lblInputValue;
        private System.Windows.Forms.Label lblOutputTotal;
        private System.Windows.Forms.Label lblOutputValue;
        private System.Windows.Forms.Label lblDefectTotal;
        private System.Windows.Forms.Label lblDefectValue;
        private System.Windows.Forms.Label lblGoodTotal;
        private System.Windows.Forms.Label lblGoodValue;
        private System.Windows.Forms.Label lblYield;
        private System.Windows.Forms.Label lblYieldValue;
        private System.Windows.Forms.Label lblCycle;
        private System.Windows.Forms.Label lblCycleValue;
        private System.Windows.Forms.Button btnResetCounter;
        private System.Windows.Forms.TableLayoutPanel systemSection;
        private System.Windows.Forms.Panel systemHeader;
        private System.Windows.Forms.Label lblSystemTitle;
        private System.Windows.Forms.Label lblSystemStatus;
    }
}
