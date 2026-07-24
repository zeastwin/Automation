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
            this.pageHost = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // debugRoot
            // 
            this.debugRoot.BackColor = System.Drawing.Color.White;
            this.debugRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugRoot.Location = new System.Drawing.Point(0, 0);
            this.debugRoot.Name = "debugRoot";
            this.debugRoot.Size = new System.Drawing.Size(1200, 656);
            this.debugRoot.TabIndex = 0;
            // 
            // pageHost
            // 
            this.pageHost.BackColor = System.Drawing.Color.White;
            this.pageHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageHost.Name = "pageHost";
            this.pageHost.TabIndex = 0;
            // 
            // HmiDebugPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Controls.Add(this.debugRoot);
            this.Font = new System.Drawing.Font("宋体", 9F);
            this.Name = "HmiDebugPage";
            this.Text = "DebugApp";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel debugRoot;
        private System.Windows.Forms.Panel pageHost;
    }
}
