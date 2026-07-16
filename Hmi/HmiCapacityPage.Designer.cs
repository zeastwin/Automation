namespace Automation.Hmi
{
    partial class HmiCapacityPage
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
            this.capacityRoot = new System.Windows.Forms.Panel();
            this.capacityRoot.SuspendLayout();
            this.SuspendLayout();
            //
            // capacityRoot
            //
            this.capacityRoot.BackColor = System.Drawing.Color.FromArgb(226, 234, 240);
            this.capacityRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.capacityRoot.Location = new System.Drawing.Point(0, 0);
            this.capacityRoot.Name = "capacityRoot";
            this.capacityRoot.Size = new System.Drawing.Size(1200, 656);
            this.capacityRoot.TabIndex = 0;
            //
            // HmiCapacityPage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Controls.Add(this.capacityRoot);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "HmiCapacityPage";
            this.Text = "产能";
            this.capacityRoot.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel capacityRoot;
    }
}
