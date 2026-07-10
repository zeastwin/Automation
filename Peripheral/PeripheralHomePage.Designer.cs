namespace Automation.Peripheral
{
    partial class PeripheralHomePage
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
            this.layoutMain = new System.Windows.Forms.TableLayoutPanel();
            this.grpEquipment = new System.Windows.Forms.GroupBox();
            this.lblEquipmentHint = new System.Windows.Forms.Label();
            this.grpOperation = new System.Windows.Forms.GroupBox();
            this.lblOperationHint = new System.Windows.Forms.Label();
            this.grpNotice = new System.Windows.Forms.GroupBox();
            this.lblNotice = new System.Windows.Forms.Label();
            this.layoutMain.SuspendLayout();
            this.grpEquipment.SuspendLayout();
            this.grpOperation.SuspendLayout();
            this.grpNotice.SuspendLayout();
            this.SuspendLayout();
            // 
            // layoutMain
            // 
            this.layoutMain.ColumnCount = 2;
            this.layoutMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65F));
            this.layoutMain.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
            this.layoutMain.Controls.Add(this.grpEquipment, 0, 0);
            this.layoutMain.Controls.Add(this.grpOperation, 1, 0);
            this.layoutMain.Controls.Add(this.grpNotice, 0, 1);
            this.layoutMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutMain.Location = new System.Drawing.Point(0, 0);
            this.layoutMain.Name = "layoutMain";
            this.layoutMain.RowCount = 2;
            this.layoutMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 68F));
            this.layoutMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 32F));
            this.layoutMain.Size = new System.Drawing.Size(1176, 632);
            this.layoutMain.TabIndex = 0;
            this.layoutMain.SetColumnSpan(this.grpNotice, 2);
            // 
            // grpEquipment
            // 
            this.grpEquipment.Controls.Add(this.lblEquipmentHint);
            this.grpEquipment.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpEquipment.Margin = new System.Windows.Forms.Padding(0, 0, 6, 6);
            this.grpEquipment.Name = "grpEquipment";
            this.grpEquipment.Padding = new System.Windows.Forms.Padding(12);
            this.grpEquipment.TabIndex = 0;
            this.grpEquipment.TabStop = false;
            this.grpEquipment.Text = "设备首页";
            this.lblEquipmentHint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblEquipmentHint.ForeColor = System.Drawing.Color.FromArgb(96, 110, 125);
            this.lblEquipmentHint.Text = "在此区域配置设备总览、工站示意图和实时状态控件。";
            this.lblEquipmentHint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // grpOperation
            // 
            this.grpOperation.Controls.Add(this.lblOperationHint);
            this.grpOperation.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpOperation.Margin = new System.Windows.Forms.Padding(6, 0, 0, 6);
            this.grpOperation.Name = "grpOperation";
            this.grpOperation.Padding = new System.Windows.Forms.Padding(12);
            this.grpOperation.TabIndex = 1;
            this.grpOperation.TabStop = false;
            this.grpOperation.Text = "操作提示";
            this.lblOperationHint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOperationHint.ForeColor = System.Drawing.Color.FromArgb(96, 110, 125);
            this.lblOperationHint.Text = "平台级流程控制位于“调试”页面。\r\n\r\n业务按钮应通过 AutomationPlatformHost 调用平台能力。";
            this.lblOperationHint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // grpNotice
            // 
            this.grpNotice.Controls.Add(this.lblNotice);
            this.grpNotice.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpNotice.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.grpNotice.Name = "grpNotice";
            this.grpNotice.Padding = new System.Windows.Forms.Padding(12);
            this.grpNotice.TabIndex = 2;
            this.grpNotice.TabStop = false;
            this.grpNotice.Text = "运行说明";
            this.lblNotice.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNotice.ForeColor = System.Drawing.Color.DimGray;
            this.lblNotice.Text = "该页面的控件均由 WinForms 设计器维护，可直接在 Visual Studio 中拖拽和调整。";
            this.lblNotice.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // PeripheralHomePage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1176, 632);
            this.Controls.Add(this.layoutMain);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "PeripheralHomePage";
            this.Text = "主页";
            this.layoutMain.ResumeLayout(false);
            this.grpEquipment.ResumeLayout(false);
            this.grpOperation.ResumeLayout(false);
            this.grpNotice.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel layoutMain;
        private System.Windows.Forms.GroupBox grpEquipment;
        private System.Windows.Forms.Label lblEquipmentHint;
        private System.Windows.Forms.GroupBox grpOperation;
        private System.Windows.Forms.Label lblOperationHint;
        private System.Windows.Forms.GroupBox grpNotice;
        private System.Windows.Forms.Label lblNotice;
    }
}
