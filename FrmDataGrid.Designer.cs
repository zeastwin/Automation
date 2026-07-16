namespace Automation
{
    partial class FrmDataGrid
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
            this.components = new System.ComponentModel.Container();
            this.dataGridView1 = new Automation.InstructionListView();
            this.contextMenuStrip2 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Add = new System.Windows.Forms.ToolStripMenuItem();
            this.Modify = new System.Windows.Forms.ToolStripMenuItem();
            this.Delete = new System.Windows.Forms.ToolStripMenuItem();
            this.SetStopPoint = new System.Windows.Forms.ToolStripMenuItem();
            this.Enable = new System.Windows.Forms.ToolStripMenuItem();
            this.copy = new System.Windows.Forms.ToolStripMenuItem();
            this.paste = new System.Windows.Forms.ToolStripMenuItem();
            this.Others = new System.Windows.Forms.ToolStripMenuItem();
            this.CProgramCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.CProgramPaste = new System.Windows.Forms.ToolStripMenuItem();
            this.separatorStartOps = new System.Windows.Forms.ToolStripSeparator();
            this.SetStartOps = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuStrip2.SuspendLayout();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.ContextMenuStrip = this.contextMenuStrip2;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.Size = new System.Drawing.Size(800, 450);
            this.dataGridView1.TabIndex = 1;
            this.dataGridView1.DragDrop += new System.Windows.Forms.DragEventHandler(this.dataGridView1_DragDrop);
            this.dataGridView1.DragOver += new System.Windows.Forms.DragEventHandler(this.dataGridView1_DragOver);
            this.dataGridView1.KeyDown += new System.Windows.Forms.KeyEventHandler(this.dataGridView1_KeyDown);
            this.dataGridView1.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.dataGridView1_MouseDoubleClick);
            this.dataGridView1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.dataGridView1_MouseDown);
            // 
            // contextMenuStrip2
            // 
            this.contextMenuStrip2.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.contextMenuStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Add,
            this.Modify,
            this.Delete,
            this.SetStopPoint,
            this.Enable,
            this.copy,
            this.paste,
            this.Others,
            this.separatorStartOps,
            this.SetStartOps});
            this.contextMenuStrip2.Name = "contextMenuStrip2";
            this.contextMenuStrip2.Size = new System.Drawing.Size(181, 246);
            // 
            // Add
            // 
            this.Add.Name = "Add";
            this.Add.Size = new System.Drawing.Size(180, 22);
            this.Add.Text = "新建";
            this.Add.Click += new System.EventHandler(this.Add_Click);
            // 
            // Modify
            // 
            this.Modify.Name = "Modify";
            this.Modify.Size = new System.Drawing.Size(180, 22);
            this.Modify.Text = "修改";
            this.Modify.Click += new System.EventHandler(this.Modify_Click);
            // 
            // Delete
            // 
            this.Delete.Name = "Delete";
            this.Delete.Size = new System.Drawing.Size(180, 22);
            this.Delete.Text = "删除";
            this.Delete.Click += new System.EventHandler(this.Delete_Click);
            // 
            // SetStopPoint
            // 
            this.SetStopPoint.Name = "SetStopPoint";
            this.SetStopPoint.Size = new System.Drawing.Size(180, 22);
            this.SetStopPoint.Text = "设置断点";
            this.SetStopPoint.Click += new System.EventHandler(this.SetStopPoint_Click);
            // 
            // Enable
            // 
            this.Enable.Name = "Enable";
            this.Enable.Size = new System.Drawing.Size(180, 22);
            this.Enable.Text = "启用/禁用";
            this.Enable.Click += new System.EventHandler(this.Enable_Click);
            // 
            // copy
            // 
            this.copy.Name = "copy";
            this.copy.Size = new System.Drawing.Size(180, 22);
            this.copy.Text = "复制";
            this.copy.Click += new System.EventHandler(this.copy_Click);
            // 
            // paste
            // 
            this.paste.Name = "paste";
            this.paste.Size = new System.Drawing.Size(180, 22);
            this.paste.Text = "粘贴";
            this.paste.Click += new System.EventHandler(this.paste_Click);
            // 
            // Others
            // 
            this.Others.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CProgramCopy,
            this.CProgramPaste});
            this.Others.Name = "Others";
            this.Others.Size = new System.Drawing.Size(180, 22);
            this.Others.Text = "其他";
            // 
            // CProgramCopy
            // 
            this.CProgramCopy.Name = "CProgramCopy";
            this.CProgramCopy.Size = new System.Drawing.Size(136, 22);
            this.CProgramCopy.Text = "跨程序复制";
            this.CProgramCopy.Click += new System.EventHandler(this.CProgramCopy_Click);
            // 
            // CProgramPaste
            // 
            this.CProgramPaste.Name = "CProgramPaste";
            this.CProgramPaste.Size = new System.Drawing.Size(136, 22);
            this.CProgramPaste.Text = "跨程序粘贴";
            this.CProgramPaste.Click += new System.EventHandler(this.CProgramPaste_Click);
            //
            // separatorStartOps
            //
            this.separatorStartOps.Name = "separatorStartOps";
            this.separatorStartOps.Size = new System.Drawing.Size(177, 6);
            //
            // SetStartOps
            //
            this.SetStartOps.Name = "SetStartOps";
            this.SetStartOps.Size = new System.Drawing.Size(180, 22);
            this.SetStartOps.Text = "设为启动点";
            this.SetStartOps.Click += new System.EventHandler(this.SetStartOps_Click);
            // 
            // FrmDataGrid
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.dataGridView1);
            this.DoubleBuffered = true;
            this.Name = "FrmDataGrid";
            this.Text = "FrmDataGrid";
            this.contextMenuStrip2.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        public Automation.InstructionListView dataGridView1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip2;
        private System.Windows.Forms.ToolStripMenuItem Add;
        private System.Windows.Forms.ToolStripMenuItem Delete;
        private System.Windows.Forms.ToolStripMenuItem Modify;
        private System.Windows.Forms.ToolStripMenuItem SetStartOps;
        private System.Windows.Forms.ToolStripMenuItem SetStopPoint;
        private System.Windows.Forms.ToolStripMenuItem copy;
        private System.Windows.Forms.ToolStripMenuItem paste;
        private System.Windows.Forms.ToolStripMenuItem Enable;
        private System.Windows.Forms.ToolStripMenuItem Others;
        private System.Windows.Forms.ToolStripMenuItem CProgramCopy;
        private System.Windows.Forms.ToolStripMenuItem CProgramPaste;
        private System.Windows.Forms.ToolStripSeparator separatorStartOps;
    }
}
