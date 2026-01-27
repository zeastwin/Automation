namespace Automation
{
    partial class FrmValue
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnSearch = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnPrevious = new System.Windows.Forms.Button();
            this.BtnNext = new System.Windows.Forms.Button();
            this.btnSet = new System.Windows.Forms.Button();
            this.btnMonitor = new System.Windows.Forms.Button();
            this.btnMonitorAdd = new System.Windows.Forms.Button();
            this.btnMonitorRemove = new System.Windows.Forms.Button();
            this.btnCopy = new System.Windows.Forms.Button();
            this.btnPaste = new System.Windows.Forms.Button();
            this.btnClearData = new System.Windows.Forms.Button();
            this.splitContainerMain = new System.Windows.Forms.SplitContainer();
            this.panelCommon = new System.Windows.Forms.Panel();
            this.listCommon = new System.Windows.Forms.ListBox();
            this.labelCommon = new System.Windows.Forms.Label();
            this.dgvValue = new System.Windows.Forms.DataGridView();
            this.index = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.name = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.type = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.value = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Note = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.panelStructHost = new System.Windows.Forms.Panel();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).BeginInit();
            this.splitContainerMain.Panel1.SuspendLayout();
            this.splitContainerMain.Panel2.SuspendLayout();
            this.splitContainerMain.SuspendLayout();
            this.panelCommon.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvValue)).BeginInit();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.SystemColors.HighlightText;
            this.panel1.Controls.Add(this.btnSearch);
            this.panel1.Controls.Add(this.btnMonitor);
            this.panel1.Controls.Add(this.btnMonitorAdd);
            this.panel1.Controls.Add(this.btnMonitorRemove);
            this.panel1.Controls.Add(this.btnCopy);
            this.panel1.Controls.Add(this.btnPaste);
            this.panel1.Controls.Add(this.btnClearData);
            this.panel1.Controls.Add(this.btnCancel);
            this.panel1.Controls.Add(this.btnPrevious);
            this.panel1.Controls.Add(this.BtnNext);
            this.panel1.Controls.Add(this.btnSet);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1760, 48);
            this.panel1.TabIndex = 3;
            // 
            // btnSearch
            // 
            this.btnSearch.BackColor = System.Drawing.Color.WhiteSmoke;
            this.btnSearch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSearch.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnSearch.FlatAppearance.BorderSize = 2;
            this.btnSearch.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnSearch.FlatAppearance.MouseOverBackColor = System.Drawing.Color.White;
            this.btnSearch.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSearch.Location = new System.Drawing.Point(1596, 6);
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.Size = new System.Drawing.Size(72, 36);
            this.btnSearch.TabIndex = 101;
            this.btnSearch.Text = "查找";
            this.btnSearch.UseVisualStyleBackColor = false;
            this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnCancel.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnCancel.FlatAppearance.BorderSize = 2;
            this.btnCancel.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnCancel.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCancel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnCancel.Location = new System.Drawing.Point(290, 6);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(110, 36);
            this.btnCancel.TabIndex = 15;
            this.btnCancel.Text = "清除常用";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnPrevious
            // 
            this.btnPrevious.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnPrevious.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnPrevious.FlatAppearance.BorderSize = 2;
            this.btnPrevious.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnPrevious.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnPrevious.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPrevious.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPrevious.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnPrevious.Location = new System.Drawing.Point(130, 6);
            this.btnPrevious.Margin = new System.Windows.Forms.Padding(2);
            this.btnPrevious.Name = "btnPrevious";
            this.btnPrevious.Size = new System.Drawing.Size(76, 36);
            this.btnPrevious.TabIndex = 14;
            this.btnPrevious.Text = "←";
            this.btnPrevious.UseVisualStyleBackColor = false;
            this.btnPrevious.Click += new System.EventHandler(this.btnPrevious_Click);
            // 
            // BtnNext
            // 
            this.BtnNext.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.BtnNext.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.BtnNext.FlatAppearance.BorderSize = 2;
            this.BtnNext.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.BtnNext.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.BtnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.BtnNext.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.BtnNext.ForeColor = System.Drawing.SystemColors.ControlText;
            this.BtnNext.Location = new System.Drawing.Point(208, 6);
            this.BtnNext.Margin = new System.Windows.Forms.Padding(2);
            this.BtnNext.Name = "BtnNext";
            this.BtnNext.Size = new System.Drawing.Size(76, 36);
            this.BtnNext.TabIndex = 13;
            this.BtnNext.Text = "→";
            this.BtnNext.UseVisualStyleBackColor = false;
            this.BtnNext.Click += new System.EventHandler(this.BtnNext_Click);
            // 
            // btnSet
            // 
            this.btnSet.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnSet.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnSet.FlatAppearance.BorderSize = 2;
            this.btnSet.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnSet.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnSet.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSet.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSet.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnSet.Location = new System.Drawing.Point(10, 6);
            this.btnSet.Margin = new System.Windows.Forms.Padding(2);
            this.btnSet.Name = "btnSet";
            this.btnSet.Size = new System.Drawing.Size(110, 36);
            this.btnSet.TabIndex = 12;
            this.btnSet.Text = "设常用";
            this.btnSet.UseVisualStyleBackColor = false;
            this.btnSet.Click += new System.EventHandler(this.btnSet_Click);
            // 
            // btnMonitor
            // 
            this.btnMonitor.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnMonitor.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnMonitor.FlatAppearance.BorderSize = 2;
            this.btnMonitor.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnMonitor.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnMonitor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMonitor.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnMonitor.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnMonitor.Location = new System.Drawing.Point(410, 6);
            this.btnMonitor.Margin = new System.Windows.Forms.Padding(2);
            this.btnMonitor.Name = "btnMonitor";
            this.btnMonitor.Size = new System.Drawing.Size(110, 36);
            this.btnMonitor.TabIndex = 16;
            this.btnMonitor.Text = "监控窗口";
            this.btnMonitor.UseVisualStyleBackColor = false;
            this.btnMonitor.Click += new System.EventHandler(this.btnMonitor_Click);
            // 
            // btnMonitorAdd
            // 
            this.btnMonitorAdd.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnMonitorAdd.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnMonitorAdd.FlatAppearance.BorderSize = 2;
            this.btnMonitorAdd.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnMonitorAdd.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnMonitorAdd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMonitorAdd.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnMonitorAdd.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnMonitorAdd.Location = new System.Drawing.Point(522, 6);
            this.btnMonitorAdd.Margin = new System.Windows.Forms.Padding(2);
            this.btnMonitorAdd.Name = "btnMonitorAdd";
            this.btnMonitorAdd.Size = new System.Drawing.Size(110, 36);
            this.btnMonitorAdd.TabIndex = 17;
            this.btnMonitorAdd.Text = "加入监控";
            this.btnMonitorAdd.UseVisualStyleBackColor = false;
            this.btnMonitorAdd.Click += new System.EventHandler(this.btnMonitorAdd_Click);
            // 
            // btnMonitorRemove
            // 
            this.btnMonitorRemove.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnMonitorRemove.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnMonitorRemove.FlatAppearance.BorderSize = 2;
            this.btnMonitorRemove.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnMonitorRemove.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnMonitorRemove.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMonitorRemove.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnMonitorRemove.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnMonitorRemove.Location = new System.Drawing.Point(634, 6);
            this.btnMonitorRemove.Margin = new System.Windows.Forms.Padding(2);
            this.btnMonitorRemove.Name = "btnMonitorRemove";
            this.btnMonitorRemove.Size = new System.Drawing.Size(110, 36);
            this.btnMonitorRemove.TabIndex = 18;
            this.btnMonitorRemove.Text = "取消监控";
            this.btnMonitorRemove.UseVisualStyleBackColor = false;
            this.btnMonitorRemove.Click += new System.EventHandler(this.btnMonitorRemove_Click);
            // 
            // btnCopy
            // 
            this.btnCopy.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnCopy.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnCopy.FlatAppearance.BorderSize = 2;
            this.btnCopy.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnCopy.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnCopy.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCopy.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCopy.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnCopy.Location = new System.Drawing.Point(746, 6);
            this.btnCopy.Margin = new System.Windows.Forms.Padding(2);
            this.btnCopy.Name = "btnCopy";
            this.btnCopy.Size = new System.Drawing.Size(100, 36);
            this.btnCopy.TabIndex = 19;
            this.btnCopy.Text = "复制";
            this.btnCopy.UseVisualStyleBackColor = false;
            this.btnCopy.Click += new System.EventHandler(this.btnCopy_Click);
            // 
            // btnPaste
            // 
            this.btnPaste.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnPaste.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnPaste.FlatAppearance.BorderSize = 2;
            this.btnPaste.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnPaste.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnPaste.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPaste.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPaste.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnPaste.Location = new System.Drawing.Point(850, 6);
            this.btnPaste.Margin = new System.Windows.Forms.Padding(2);
            this.btnPaste.Name = "btnPaste";
            this.btnPaste.Size = new System.Drawing.Size(100, 36);
            this.btnPaste.TabIndex = 20;
            this.btnPaste.Text = "粘贴";
            this.btnPaste.UseVisualStyleBackColor = false;
            this.btnPaste.Click += new System.EventHandler(this.btnPaste_Click);
            // 
            // btnClearData
            // 
            this.btnClearData.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnClearData.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnClearData.FlatAppearance.BorderSize = 2;
            this.btnClearData.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnClearData.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnClearData.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClearData.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnClearData.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnClearData.Location = new System.Drawing.Point(954, 6);
            this.btnClearData.Margin = new System.Windows.Forms.Padding(2);
            this.btnClearData.Name = "btnClearData";
            this.btnClearData.Size = new System.Drawing.Size(110, 36);
            this.btnClearData.TabIndex = 21;
            this.btnClearData.Text = "清除数据";
            this.btnClearData.UseVisualStyleBackColor = false;
            this.btnClearData.Click += new System.EventHandler(this.btnClearData_Click);
            // 
            // splitContainerMain
            // 
            this.splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainerMain.Location = new System.Drawing.Point(0, 48);
            this.splitContainerMain.Name = "splitContainerMain";
            this.splitContainerMain.Orientation = System.Windows.Forms.Orientation.Vertical;
            // 
            // splitContainerMain.Panel1
            // 
            this.splitContainerMain.Panel1.Controls.Add(this.dgvValue);
            this.splitContainerMain.Panel1.Controls.Add(this.panelCommon);
            this.splitContainerMain.Panel1MinSize = 600;
            // 
            // splitContainerMain.Panel2
            // 
            this.splitContainerMain.Panel2.Controls.Add(this.panelStructHost);
            this.splitContainerMain.Panel2MinSize = 240;
            this.splitContainerMain.Size = new System.Drawing.Size(1760, 812);
            this.splitContainerMain.SplitterDistance = 1320;
            this.splitContainerMain.SplitterWidth = 6;
            this.splitContainerMain.TabIndex = 6;
            // 
            // panelCommon
            // 
            this.panelCommon.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.panelCommon.Controls.Add(this.listCommon);
            this.panelCommon.Controls.Add(this.labelCommon);
            this.panelCommon.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelCommon.Location = new System.Drawing.Point(0, 0);
            this.panelCommon.Name = "panelCommon";
            this.panelCommon.Size = new System.Drawing.Size(240, 812);
            this.panelCommon.TabIndex = 5;
            // 
            // listCommon
            // 
            this.listCommon.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listCommon.Font = new System.Drawing.Font("黑体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.listCommon.FormattingEnabled = true;
            this.listCommon.IntegralHeight = false;
            this.listCommon.ItemHeight = 16;
            this.listCommon.Location = new System.Drawing.Point(0, 36);
            this.listCommon.Name = "listCommon";
            this.listCommon.Size = new System.Drawing.Size(240, 776);
            this.listCommon.TabIndex = 1;
            this.listCommon.SelectedIndexChanged += new System.EventHandler(this.listCommon_SelectedIndexChanged);
            // 
            // labelCommon
            // 
            this.labelCommon.BackColor = System.Drawing.SystemColors.ControlLight;
            this.labelCommon.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelCommon.Font = new System.Drawing.Font("黑体", 12.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.labelCommon.Location = new System.Drawing.Point(0, 0);
            this.labelCommon.Name = "labelCommon";
            this.labelCommon.Size = new System.Drawing.Size(240, 36);
            this.labelCommon.TabIndex = 0;
            this.labelCommon.Text = "常用变量";
            this.labelCommon.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // dgvValue
            // 
            this.dgvValue.AllowUserToAddRows = false;
            this.dgvValue.AllowUserToResizeRows = false;
            this.dgvValue.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.None;
            this.dgvValue.BackgroundColor = System.Drawing.SystemColors.ControlLight;
            this.dgvValue.ColumnHeadersHeight = 32;
            this.dgvValue.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvValue.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.index,
            this.name,
            this.type,
            this.value,
            this.Note});
            this.dgvValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvValue.Location = new System.Drawing.Point(240, 0);
            this.dgvValue.Name = "dgvValue";
            this.dgvValue.RowHeadersVisible = false;
            this.dgvValue.RowHeadersWidth = 20;
            this.dgvValue.RowTemplate.Height = 28;
            this.dgvValue.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.dgvValue.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvValue.Size = new System.Drawing.Size(1080, 812);
            this.dgvValue.TabIndex = 4;
            this.dgvValue.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvValue_CellEndEdit);
            this.dgvValue.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.dgvValue_CellFormatting);
            this.dgvValue.CellMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dgvValue_CellMouseClick);
            this.dgvValue.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvValue_CellValueChanged);
            this.dgvValue.KeyDown += new System.Windows.Forms.KeyEventHandler(this.dgvValue_KeyDown);
            // 
            // index
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.index.DefaultCellStyle = dataGridViewCellStyle1;
            this.index.FillWeight = 9F;
            this.index.HeaderText = "编号";
            this.index.MinimumWidth = 50;
            this.index.Name = "index";
            this.index.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.index.Width = 50;
            // 
            // name
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.name.DefaultCellStyle = dataGridViewCellStyle2;
            this.name.FillWeight = 59.88338F;
            this.name.HeaderText = "名称";
            this.name.MinimumWidth = 6;
            this.name.Name = "name";
            this.name.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.name.Width = 200;
            // 
            // type
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.type.DefaultCellStyle = dataGridViewCellStyle3;
            this.type.DisplayStyle = System.Windows.Forms.DataGridViewComboBoxDisplayStyle.ComboBox;
            this.type.FillWeight = 25F;
            this.type.HeaderText = "类型";
            this.type.Items.AddRange(new object[] {
            "double",
            "string"});
            this.type.MinimumWidth = 6;
            this.type.Name = "type";
            this.type.Width = 100;
            // 
            // value
            // 
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.value.DefaultCellStyle = dataGridViewCellStyle4;
            this.value.FillWeight = 86.33793F;
            this.value.HeaderText = "值";
            this.value.MinimumWidth = 6;
            this.value.Name = "value";
            this.value.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.value.Width = 200;
            // 
            // Note
            // 
            this.Note.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.Note.DefaultCellStyle = dataGridViewCellStyle5;
            this.Note.FillWeight = 450F;
            this.Note.HeaderText = "备注";
            this.Note.MinimumWidth = 6;
            this.Note.Name = "Note";
            this.Note.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.Note.HeaderCell.Style.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.Note.Width = 1000;
            // 
            // panelStructHost
            // 
            this.panelStructHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelStructHost.Location = new System.Drawing.Point(0, 0);
            this.panelStructHost.Name = "panelStructHost";
            this.panelStructHost.Size = new System.Drawing.Size(434, 812);
            this.panelStructHost.TabIndex = 0;
            // 
            // FrmValue
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1760, 860);
            this.Controls.Add(this.splitContainerMain);
            this.Controls.Add(this.panel1);
            this.Name = "FrmValue";
            this.Text = "变量表";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmValue_FormClosing);
            this.Load += new System.EventHandler(this.FrmValue_Load);
            this.panel1.ResumeLayout(false);
            this.splitContainerMain.Panel1.ResumeLayout(false);
            this.splitContainerMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).EndInit();
            this.splitContainerMain.ResumeLayout(false);
            this.panelCommon.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvValue)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Panel panel1;
        public System.Windows.Forms.Button btnCancel;
        public System.Windows.Forms.Button btnPrevious;
        public System.Windows.Forms.Button BtnNext;
        public System.Windows.Forms.Button btnSet;
        public System.Windows.Forms.Button btnMonitor;
        public System.Windows.Forms.Button btnMonitorAdd;
        public System.Windows.Forms.Button btnMonitorRemove;
        public System.Windows.Forms.Button btnCopy;
        public System.Windows.Forms.Button btnPaste;
        public System.Windows.Forms.Button btnClearData;
        public System.Windows.Forms.Button btnSearch;
        public System.Windows.Forms.DataGridView dgvValue;
        private System.Windows.Forms.SplitContainer splitContainerMain;
        private System.Windows.Forms.Panel panelCommon;
        private System.Windows.Forms.ListBox listCommon;
        private System.Windows.Forms.Label labelCommon;
        private System.Windows.Forms.Panel panelStructHost;
        private System.Windows.Forms.DataGridViewTextBoxColumn index;
        private System.Windows.Forms.DataGridViewTextBoxColumn name;
        private System.Windows.Forms.DataGridViewComboBoxColumn type;
        private System.Windows.Forms.DataGridViewTextBoxColumn value;
        private System.Windows.Forms.DataGridViewTextBoxColumn Note;
    }
}
