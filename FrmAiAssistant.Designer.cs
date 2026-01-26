namespace Automation
{
    partial class FrmAiAssistant
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabPropose = new System.Windows.Forms.TabPage();
            this.lblPropose = new System.Windows.Forms.Label();
            this.tabVerify = new System.Windows.Forms.TabPage();
            this.lblVerify = new System.Windows.Forms.Label();
            this.tabSim = new System.Windows.Forms.TabPage();
            this.lblSim = new System.Windows.Forms.Label();
            this.tabDiff = new System.Windows.Forms.TabPage();
            this.lblDiff = new System.Windows.Forms.Label();
            this.tabMain.SuspendLayout();
            this.tabPropose.SuspendLayout();
            this.tabVerify.SuspendLayout();
            this.tabSim.SuspendLayout();
            this.tabDiff.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabMain
            // 
            this.tabMain.Controls.Add(this.tabPropose);
            this.tabMain.Controls.Add(this.tabVerify);
            this.tabMain.Controls.Add(this.tabSim);
            this.tabMain.Controls.Add(this.tabDiff);
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabMain.Location = new System.Drawing.Point(0, 0);
            this.tabMain.Name = "tabMain";
            this.tabMain.SelectedIndex = 0;
            this.tabMain.Size = new System.Drawing.Size(900, 600);
            this.tabMain.TabIndex = 0;
            // 
            // tabPropose
            // 
            this.tabPropose.Controls.Add(this.lblPropose);
            this.tabPropose.Location = new System.Drawing.Point(4, 22);
            this.tabPropose.Name = "tabPropose";
            this.tabPropose.Padding = new System.Windows.Forms.Padding(3);
            this.tabPropose.Size = new System.Drawing.Size(892, 574);
            this.tabPropose.TabIndex = 0;
            this.tabPropose.Text = "提案";
            this.tabPropose.UseVisualStyleBackColor = true;
            // 
            // lblPropose
            // 
            this.lblPropose.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPropose.Font = new System.Drawing.Font("黑体", 12F);
            this.lblPropose.Location = new System.Drawing.Point(3, 3);
            this.lblPropose.Name = "lblPropose";
            this.lblPropose.Size = new System.Drawing.Size(886, 568);
            this.lblPropose.TabIndex = 0;
            this.lblPropose.Text = "在此接入 AI 生成/增量提案（FlowDelta/Core）";
            this.lblPropose.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // tabVerify
            // 
            this.tabVerify.Controls.Add(this.lblVerify);
            this.tabVerify.Location = new System.Drawing.Point(4, 22);
            this.tabVerify.Name = "tabVerify";
            this.tabVerify.Padding = new System.Windows.Forms.Padding(3);
            this.tabVerify.Size = new System.Drawing.Size(892, 574);
            this.tabVerify.TabIndex = 1;
            this.tabVerify.Text = "验证";
            this.tabVerify.UseVisualStyleBackColor = true;
            // 
            // lblVerify
            // 
            this.lblVerify.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblVerify.Font = new System.Drawing.Font("黑体", 12F);
            this.lblVerify.Location = new System.Drawing.Point(3, 3);
            this.lblVerify.Name = "lblVerify";
            this.lblVerify.Size = new System.Drawing.Size(886, 568);
            this.lblVerify.TabIndex = 0;
            this.lblVerify.Text = "在此展示 Schema/语义/CFG/协作验证结果";
            this.lblVerify.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // tabSim
            // 
            this.tabSim.Controls.Add(this.lblSim);
            this.tabSim.Location = new System.Drawing.Point(4, 22);
            this.tabSim.Name = "tabSim";
            this.tabSim.Padding = new System.Windows.Forms.Padding(3);
            this.tabSim.Size = new System.Drawing.Size(892, 574);
            this.tabSim.TabIndex = 2;
            this.tabSim.Text = "仿真";
            this.tabSim.UseVisualStyleBackColor = true;
            // 
            // lblSim
            // 
            this.lblSim.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSim.Font = new System.Drawing.Font("黑体", 12F);
            this.lblSim.Location = new System.Drawing.Point(3, 3);
            this.lblSim.Name = "lblSim";
            this.lblSim.Size = new System.Drawing.Size(886, 568);
            this.lblSim.TabIndex = 0;
            this.lblSim.Text = "在此展示仿真 Trace 与场景结果";
            this.lblSim.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // tabDiff
            // 
            this.tabDiff.Controls.Add(this.lblDiff);
            this.tabDiff.Location = new System.Drawing.Point(4, 22);
            this.tabDiff.Name = "tabDiff";
            this.tabDiff.Padding = new System.Windows.Forms.Padding(3);
            this.tabDiff.Size = new System.Drawing.Size(892, 574);
            this.tabDiff.TabIndex = 3;
            this.tabDiff.Text = "Diff/回滚";
            this.tabDiff.UseVisualStyleBackColor = true;
            // 
            // lblDiff
            // 
            this.lblDiff.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDiff.Font = new System.Drawing.Font("黑体", 12F);
            this.lblDiff.Location = new System.Drawing.Point(3, 3);
            this.lblDiff.Name = "lblDiff";
            this.lblDiff.Size = new System.Drawing.Size(886, 568);
            this.lblDiff.TabIndex = 0;
            this.lblDiff.Text = "在此展示差异预览与回滚操作";
            this.lblDiff.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // FrmAiAssistant
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 600);
            this.Controls.Add(this.tabMain);
            this.Name = "FrmAiAssistant";
            this.Text = "AI流程助手";
            this.tabMain.ResumeLayout(false);
            this.tabPropose.ResumeLayout(false);
            this.tabVerify.ResumeLayout(false);
            this.tabSim.ResumeLayout(false);
            this.tabDiff.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPropose;
        private System.Windows.Forms.Label lblPropose;
        private System.Windows.Forms.TabPage tabVerify;
        private System.Windows.Forms.Label lblVerify;
        private System.Windows.Forms.TabPage tabSim;
        private System.Windows.Forms.Label lblSim;
        private System.Windows.Forms.TabPage tabDiff;
        private System.Windows.Forms.Label lblDiff;
    }
}
