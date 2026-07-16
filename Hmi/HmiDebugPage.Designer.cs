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
            this.debugRoot.SuspendLayout();
            this.SuspendLayout();
            // 
            // debugRoot
            // 
            this.debugRoot.BackColor = System.Drawing.Color.FromArgb(226, 234, 240);
            this.debugRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugRoot.Location = new System.Drawing.Point(0, 0);
            this.debugRoot.Name = "debugRoot";
            this.debugRoot.Size = new System.Drawing.Size(1200, 656);
            this.debugRoot.TabIndex = 0;
            // 
            // HmiDebugPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Controls.Add(this.debugRoot);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "HmiDebugPage";
            this.Text = "调试";
            this.debugRoot.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel debugRoot;
    }
}
