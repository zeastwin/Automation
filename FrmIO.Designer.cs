namespace Automation
{
    partial class FrmIO
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle7 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle8 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle9 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle10 = new System.Windows.Forms.DataGridViewCellStyle();
            this.dgvIO = new System.Windows.Forms.DataGridView();
            this.index = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.status = new System.Windows.Forms.DataGridViewImageColumn();
            this.name = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.cardNum = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.moduleNum = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IOIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.value = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UsedType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EffectLevel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Debug = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Note = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Modify = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.dgvIO)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // dgvIO
            // 
            this.dgvIO.AllowUserToAddRows = false;
            this.dgvIO.AllowUserToResizeRows = false;
            this.dgvIO.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvIO.BackgroundColor = System.Drawing.SystemColors.ControlLight;
            this.dgvIO.ColumnHeadersHeight = 22;
            this.dgvIO.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvIO.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.index,
            this.status,
            this.name,
            this.cardNum,
            this.moduleNum,
            this.IOIndex,
            this.value,
            this.UsedType,
            this.EffectLevel,
            this.Debug,
            this.Note});
            this.dgvIO.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvIO.Location = new System.Drawing.Point(0, 0);
            this.dgvIO.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.dgvIO.Name = "dgvIO";
            this.dgvIO.RowHeadersVisible = false;
            this.dgvIO.RowHeadersWidth = 20;
            this.dgvIO.RowTemplate.Height = 23;
            this.dgvIO.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvIO.Size = new System.Drawing.Size(1067, 562);
            this.dgvIO.TabIndex = 3;
            this.dgvIO.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.dgvIO_CellFormatting);
            this.dgvIO.CellMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dgvIO_CellMouseClick);
            this.dgvIO.CellMouseDown += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dgvIO_CellMouseDown);
            // 
            // index
            // 
            this.index.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.index.DefaultCellStyle = dataGridViewCellStyle1;
            this.index.FillWeight = 1F;
            this.index.HeaderText = "编号";
            this.index.MinimumWidth = 6;
            this.index.Name = "index";
            this.index.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.index.Width = 40;
            // 
            // status
            // 
            this.status.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.status.FillWeight = 1F;
            this.status.HeaderText = "状态";
            this.status.ImageLayout = System.Windows.Forms.DataGridViewImageCellLayout.Zoom;
            this.status.MinimumWidth = 6;
            this.status.Name = "status";
            this.status.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.status.Width = 40;
            // 
            // name
            // 
            this.name.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.name.DefaultCellStyle = dataGridViewCellStyle2;
            this.name.FillWeight = 3F;
            this.name.HeaderText = "IO名称";
            this.name.MinimumWidth = 6;
            this.name.Name = "name";
            this.name.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.name.Width = 80;
            // 
            // cardNum
            // 
            this.cardNum.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.cardNum.DefaultCellStyle = dataGridViewCellStyle3;
            this.cardNum.FillWeight = 1F;
            this.cardNum.HeaderText = "卡号";
            this.cardNum.MinimumWidth = 6;
            this.cardNum.Name = "cardNum";
            this.cardNum.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.cardNum.Width = 40;
            // 
            // moduleNum
            // 
            this.moduleNum.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.moduleNum.DefaultCellStyle = dataGridViewCellStyle5;
            this.moduleNum.FillWeight = 1F;
            this.moduleNum.HeaderText = "模块";
            this.moduleNum.MinimumWidth = 6;
            this.moduleNum.Name = "moduleNum";
            this.moduleNum.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.moduleNum.Width = 40;
            // 
            // IOIndex
            // 
            this.IOIndex.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.IOIndex.DefaultCellStyle = dataGridViewCellStyle6;
            this.IOIndex.FillWeight = 1F;
            this.IOIndex.HeaderText = "索引";
            this.IOIndex.MinimumWidth = 6;
            this.IOIndex.Name = "IOIndex";
            this.IOIndex.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.IOIndex.Width = 40;
            // 
            // value
            // 
            this.value.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle7.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.value.DefaultCellStyle = dataGridViewCellStyle7;
            this.value.FillWeight = 3F;
            this.value.HeaderText = "IO类型";
            this.value.MinimumWidth = 6;
            this.value.Name = "value";
            this.value.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.value.Width = 60;
            // 
            // UsedType
            // 
            this.UsedType.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle8.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.UsedType.DefaultCellStyle = dataGridViewCellStyle8;
            this.UsedType.FillWeight = 3F;
            this.UsedType.HeaderText = "应用类型";
            this.UsedType.MinimumWidth = 6;
            this.UsedType.Name = "UsedType";
            this.UsedType.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.UsedType.Width = 60;
            // 
            // EffectLevel
            // 
            this.EffectLevel.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle9.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.EffectLevel.DefaultCellStyle = dataGridViewCellStyle9;
            this.EffectLevel.FillWeight = 3F;
            this.EffectLevel.HeaderText = "有效电平";
            this.EffectLevel.MinimumWidth = 6;
            this.EffectLevel.Name = "EffectLevel";
            this.EffectLevel.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.EffectLevel.Width = 60;
            // 
            // Debug
            // 
            this.Debug.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Debug.FillWeight = 1F;
            this.Debug.HeaderText = "调试";
            this.Debug.MinimumWidth = 6;
            this.Debug.Name = "Debug";
            this.Debug.Width = 40;
            // 
            // Note
            // 
            this.Note.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewCellStyle10.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.Note.DefaultCellStyle = dataGridViewCellStyle10;
            this.Note.FillWeight = 3F;
            this.Note.HeaderText = "备注";
            this.Note.MinimumWidth = 6;
            this.Note.Name = "Note";
            this.Note.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Modify});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(109, 28);
            // 
            // Modify
            // 
            this.Modify.Name = "Modify";
            this.Modify.Size = new System.Drawing.Size(108, 24);
            this.Modify.Text = "修改";
            this.Modify.Click += new System.EventHandler(this.Modify_Click);
            // 
            // FrmIO
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1067, 562);
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.dgvIO);
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "FrmIO";
            this.Text = "IO表";
            ((System.ComponentModel.ISupportInitialize)(this.dgvIO)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.DataGridView dgvIO;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem Modify;
        private System.Windows.Forms.DataGridViewTextBoxColumn index;
        private System.Windows.Forms.DataGridViewImageColumn status;
        private System.Windows.Forms.DataGridViewTextBoxColumn name;
        private System.Windows.Forms.DataGridViewTextBoxColumn cardNum;
        private System.Windows.Forms.DataGridViewTextBoxColumn moduleNum;
        private System.Windows.Forms.DataGridViewTextBoxColumn IOIndex;
        private System.Windows.Forms.DataGridViewTextBoxColumn value;
        private System.Windows.Forms.DataGridViewTextBoxColumn UsedType;
        private System.Windows.Forms.DataGridViewTextBoxColumn EffectLevel;
        private System.Windows.Forms.DataGridViewCheckBoxColumn Debug;
        private System.Windows.Forms.DataGridViewTextBoxColumn Note;
    }
}
