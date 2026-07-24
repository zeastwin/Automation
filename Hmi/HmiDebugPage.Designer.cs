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
            this.btnTest = new System.Windows.Forms.Button();
            this.debugRoot.SuspendLayout();
            this.SuspendLayout();
            // 
            // debugRoot
            // 
            this.debugRoot.BackColor = global::Automation.UiPalette.HmiBackground;
            this.debugRoot.Controls.Add(this.btnTest);
            this.debugRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugRoot.Location = new System.Drawing.Point(0, 0);
            this.debugRoot.Name = "debugRoot";
            this.debugRoot.Size = new System.Drawing.Size(1200, 656);
            this.debugRoot.TabIndex = 0;
            // 
            // btnTest
            // 
            this.btnTest.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.btnTest.BackColor = global::Automation.UiPalette.HmiSection;
            this.btnTest.FlatAppearance.BorderSize = 0;
            this.btnTest.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTest.Font = new System.Drawing.Font("Microsoft YaHei UI", 13F, System.Drawing.FontStyle.Bold);
            this.btnTest.ForeColor = global::Automation.UiPalette.NavigationText;
            this.btnTest.Location = new System.Drawing.Point(525, 300);
            this.btnTest.Name = "btnTest";
            this.btnTest.Size = new System.Drawing.Size(150, 50);
            this.btnTest.TabIndex = 0;
            this.btnTest.Text = "测试";
            this.btnTest.UseVisualStyleBackColor = false;
            this.btnTest.Click += new System.EventHandler(this.BtnTest_Click);
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
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel debugRoot;
        private System.Windows.Forms.Button btnTest;
    }
}
