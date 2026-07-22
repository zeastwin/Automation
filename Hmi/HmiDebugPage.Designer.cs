namespace Automation.Hmi
{
    partial class HmiDebugPage
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
            this.debugRoot = new System.Windows.Forms.Panel();
            this.clockPanel = new System.Windows.Forms.Panel();
            this.clockControl = new Automation.Hmi.ClockControl();
            this.debugRoot.SuspendLayout();
            this.clockPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // debugRoot
            // 
            this.debugRoot.BackColor = global::Automation.UiPalette.HmiBackground;
            this.debugRoot.Controls.Add(this.clockPanel);
            this.debugRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugRoot.Location = new System.Drawing.Point(0, 0);
            this.debugRoot.Name = "debugRoot";
            this.debugRoot.Size = new System.Drawing.Size(1200, 656);
            this.debugRoot.TabIndex = 0;
            // 
            // clockPanel
            // 
            this.clockPanel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.clockPanel.BackColor = global::Automation.UiPalette.SurfaceStrong;
            this.clockPanel.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.clockPanel.Controls.Add(this.clockControl);
            this.clockPanel.Location = new System.Drawing.Point(1036, 16);
            this.clockPanel.Name = "clockPanel";
            this.clockPanel.Padding = new System.Windows.Forms.Padding(8);
            this.clockPanel.Size = new System.Drawing.Size(148, 148);
            this.clockPanel.TabIndex = 0;
            // 
            // clockControl
            // 
            this.clockControl.BackColor = global::Automation.UiPalette.SurfaceStrong;
            this.clockControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.clockControl.Location = new System.Drawing.Point(8, 8);
            this.clockControl.Name = "clockControl";
            this.clockControl.Size = new System.Drawing.Size(132, 132);
            this.clockControl.TabIndex = 0;
            // 
            // HmiDebugPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = global::Automation.UiPalette.SurfaceStrong;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Controls.Add(this.debugRoot);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "HmiDebugPage";
            this.Text = "调试";
            this.debugRoot.ResumeLayout(false);
            this.clockPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel debugRoot;
        private System.Windows.Forms.Panel clockPanel;
        private Automation.Hmi.ClockControl clockControl;
    }
}
