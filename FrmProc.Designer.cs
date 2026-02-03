namespace Automation
{
    partial class FrmProc
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.proc_treeView = new System.Windows.Forms.TreeView();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.AddProc = new System.Windows.Forms.ToolStripMenuItem();
            this.AddStep = new System.Windows.Forms.ToolStripMenuItem();
            this.Remove = new System.Windows.Forms.ToolStripMenuItem();
            this.Modify = new System.Windows.Forms.ToolStripMenuItem();
            this.ToggleDisable = new System.Windows.Forms.ToolStripMenuItem();
            this.startProc = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // proc_treeView
            // 
            this.proc_treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.proc_treeView.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.proc_treeView.FullRowSelect = true;
            this.proc_treeView.Location = new System.Drawing.Point(0, 0);
            this.proc_treeView.Name = "proc_treeView";
            this.proc_treeView.ShowLines = false;
            this.proc_treeView.Size = new System.Drawing.Size(465, 585);
            this.proc_treeView.TabIndex = 0;
            this.proc_treeView.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.proc_treeView_AfterSelect);
            this.proc_treeView.MouseDown += new System.Windows.Forms.MouseEventHandler(this.proc_treeView_MouseDown);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddProc,
            this.AddStep,
            this.Modify,
            this.ToggleDisable,
            this.startProc,
            this.Remove});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(125, 136);
            this.contextMenuStrip1.Opening += new System.ComponentModel.CancelEventHandler(this.contextMenuStrip1_Opening);
            // 
            // AddProc
            // 
            this.AddProc.Name = "AddProc";
            this.AddProc.Size = new System.Drawing.Size(124, 22);
            this.AddProc.Text = "添加流程";
            this.AddProc.Click += new System.EventHandler(this.AddProc_Click);
            // 
            // AddStep
            // 
            this.AddStep.Name = "AddStep";
            this.AddStep.Size = new System.Drawing.Size(124, 22);
            this.AddStep.Text = "添加步骤";
            this.AddStep.Click += new System.EventHandler(this.AddStep_Click);
            // 
            // Remove
            // 
            this.Remove.Name = "Remove";
            this.Remove.Size = new System.Drawing.Size(124, 22);
            this.Remove.Text = "删除";
            this.Remove.Click += new System.EventHandler(this.Remove_Click);
            // 
            // Modify
            // 
            this.Modify.Name = "Modify";
            this.Modify.Size = new System.Drawing.Size(124, 22);
            this.Modify.Text = "修改";
            this.Modify.Click += new System.EventHandler(this.Modify_Click);
            // 
            // ToggleDisable
            // 
            this.ToggleDisable.Name = "ToggleDisable";
            this.ToggleDisable.Size = new System.Drawing.Size(124, 22);
            this.ToggleDisable.Text = "禁用";
            this.ToggleDisable.Click += new System.EventHandler(this.ToggleDisable_Click);
            // 
            // startProc
            // 
            this.startProc.Name = "startProc";
            this.startProc.Size = new System.Drawing.Size(124, 22);
            this.startProc.Text = "启动流程";
            this.startProc.Click += new System.EventHandler(this.startProc_Click);
            // 
            // FrmProc
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(465, 585);
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.proc_treeView);
            this.Name = "FrmProc";
            this.Text = "FrmProc";
            this.Load += new System.EventHandler(this.FrmProc_Load);
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem AddProc;
        private System.Windows.Forms.ToolStripMenuItem AddStep;
        private System.Windows.Forms.ToolStripMenuItem Remove;
        private System.Windows.Forms.ToolStripMenuItem Modify;
        private System.Windows.Forms.ToolStripMenuItem ToggleDisable;
        public System.Windows.Forms.TreeView proc_treeView;
        private System.Windows.Forms.ToolStripMenuItem startProc;
    }
}
