namespace Automation
{
    partial class FrmDataGrid
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.index = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.name = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.operaType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Note = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.contextMenuStrip2 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.OneSetp = new System.Windows.Forms.ToolStripMenuItem();
            this.Add = new System.Windows.Forms.ToolStripMenuItem();
            this.Modify = new System.Windows.Forms.ToolStripMenuItem();
            this.Delete = new System.Windows.Forms.ToolStripMenuItem();
            this.SetStopPoint = new System.Windows.Forms.ToolStripMenuItem();
            this.Enable = new System.Windows.Forms.ToolStripMenuItem();
            this.copy = new System.Windows.Forms.ToolStripMenuItem();
            this.paste = new System.Windows.Forms.ToolStripMenuItem();
            this.SetStartOps = new System.Windows.Forms.ToolStripMenuItem();
            this.Others = new System.Windows.Forms.ToolStripMenuItem();
            this.CProgramCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.CProgramPaste = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.contextMenuStrip2.SuspendLayout();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowDrop = true;
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToResizeRows = false;
            this.dataGridView1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView1.BackgroundColor = System.Drawing.SystemColors.ControlLight;
            this.dataGridView1.ColumnHeadersHeight = 22;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.index,
            this.name,
            this.operaType,
            this.Note});
            this.dataGridView1.ContextMenuStrip = this.contextMenuStrip2;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersVisible = false;
            this.dataGridView1.RowHeadersWidth = 21;
            this.dataGridView1.RowTemplate.Height = 23;
            this.dataGridView1.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridView1.Size = new System.Drawing.Size(800, 450);
            this.dataGridView1.TabIndex = 1;
            this.dataGridView1.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellDoubleClick);
            this.dataGridView1.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.dataGridView1_CellFormatting);
            this.dataGridView1.CellMouseDown += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dataGridView1_CellMouseDown);
            this.dataGridView1.DragDrop += new System.Windows.Forms.DragEventHandler(this.dataGridView1_DragDrop);
            this.dataGridView1.DragOver += new System.Windows.Forms.DragEventHandler(this.dataGridView1_DragOver);
            this.dataGridView1.KeyDown += new System.Windows.Forms.KeyEventHandler(this.dataGridView1_KeyDown);
            this.dataGridView1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.dataGridView1_MouseDown);
            // 
            // index
            // 
            this.index.DataPropertyName = "Num";
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.index.DefaultCellStyle = dataGridViewCellStyle1;
            this.index.FillWeight = 15F;
            this.index.HeaderText = "编号";
            this.index.MinimumWidth = 6;
            this.index.Name = "index";
            this.index.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // name
            // 
            this.name.DataPropertyName = "Name";
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.name.DefaultCellStyle = dataGridViewCellStyle2;
            this.name.FillWeight = 40F;
            this.name.HeaderText = "名称";
            this.name.MinimumWidth = 6;
            this.name.Name = "name";
            this.name.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // operaType
            // 
            this.operaType.DataPropertyName = "OperaType";
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.operaType.DefaultCellStyle = dataGridViewCellStyle3;
            this.operaType.FillWeight = 30F;
            this.operaType.HeaderText = "操作类型";
            this.operaType.MinimumWidth = 6;
            this.operaType.Name = "operaType";
            this.operaType.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Note
            // 
            this.Note.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.Note.DataPropertyName = "Note";
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.Note.DefaultCellStyle = dataGridViewCellStyle4;
            this.Note.FillWeight = 146.5714F;
            this.Note.HeaderText = "备注";
            this.Note.MinimumWidth = 6;
            this.Note.Name = "Note";
            this.Note.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // contextMenuStrip2
            // 
            this.contextMenuStrip2.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.contextMenuStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.OneSetp,
            this.SetStartOps,
            this.Add,
            this.Modify,
            this.Delete,
            this.SetStopPoint,
            this.Enable,
            this.copy,
            this.paste,
            this.Others});
            this.contextMenuStrip2.Name = "contextMenuStrip1";
            this.contextMenuStrip2.Size = new System.Drawing.Size(181, 246);
            // 
            // OneSetp
            // 
            this.OneSetp.Name = "OneSetp";
            this.OneSetp.Size = new System.Drawing.Size(180, 22);
            this.OneSetp.Text = "单步执行";
            this.OneSetp.Click += new System.EventHandler(this.OneSetp_Click);
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
            // SetStartOps
            // 
            this.SetStartOps.Name = "SetStartOps";
            this.SetStartOps.Size = new System.Drawing.Size(180, 22);
            this.SetStartOps.Text = "设为启动点";
            this.SetStartOps.Click += new System.EventHandler(this.SetStartOps_Click);
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
            // FrmDataGrid
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.dataGridView1);
            this.DoubleBuffered = true;
            this.Name = "FrmDataGrid";
            this.Text = "FrmDataGrid";
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.contextMenuStrip2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip2;
        private System.Windows.Forms.ToolStripMenuItem Add;
        private System.Windows.Forms.ToolStripMenuItem Delete;
        private System.Windows.Forms.ToolStripMenuItem Modify;
        private System.Windows.Forms.ToolStripMenuItem SetStartOps;
        private System.Windows.Forms.DataGridViewTextBoxColumn index;
        private System.Windows.Forms.DataGridViewTextBoxColumn name;
        private System.Windows.Forms.DataGridViewTextBoxColumn operaType;
        private System.Windows.Forms.DataGridViewTextBoxColumn Note;
        private System.Windows.Forms.ToolStripMenuItem SetStopPoint;
        private System.Windows.Forms.ToolStripMenuItem copy;
        private System.Windows.Forms.ToolStripMenuItem paste;
        private System.Windows.Forms.ToolStripMenuItem OneSetp;
        private System.Windows.Forms.ToolStripMenuItem Enable;
        private System.Windows.Forms.ToolStripMenuItem Others;
        private System.Windows.Forms.ToolStripMenuItem CProgramCopy;
        private System.Windows.Forms.ToolStripMenuItem CProgramPaste;
    }
}