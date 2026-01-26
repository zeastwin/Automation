namespace Automation
{
    partial class FrmValueDebug
    {
        /// <summary>
        /// 设计器所需的变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.groupCheck = new System.Windows.Forms.GroupBox();
            this.dgvCheck = new System.Windows.Forms.DataGridView();
            this.colCheckNote = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colCheckIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colCheckName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colCheckType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colCheckValue = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.panelCheckBottom = new System.Windows.Forms.Panel();
            this.btnCheckRefresh = new System.Windows.Forms.Button();
            this.btnCheckRemove = new System.Windows.Forms.Button();
            this.btnCheckAdd = new System.Windows.Forms.Button();
            this.cboCheckVar = new System.Windows.Forms.ComboBox();
            this.lblCheckIndex = new System.Windows.Forms.Label();
            this.lblCheckStatus = new System.Windows.Forms.Label();
            this.groupEdit = new System.Windows.Forms.GroupBox();
            this.dgvEdit = new System.Windows.Forms.DataGridView();
            this.colEditNote = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colEditIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colEditName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colEditType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colEditValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.panelEditBottom = new System.Windows.Forms.Panel();
            this.btnEditRefresh = new System.Windows.Forms.Button();
            this.btnEditApply = new System.Windows.Forms.Button();
            this.btnEditRemove = new System.Windows.Forms.Button();
            this.btnEditAdd = new System.Windows.Forms.Button();
            this.txtEditValue = new System.Windows.Forms.TextBox();
            this.cboEditVar = new System.Windows.Forms.ComboBox();
            this.lblEditValue = new System.Windows.Forms.Label();
            this.lblEditIndex = new System.Windows.Forms.Label();
            this.lblEditStatus = new System.Windows.Forms.Label();
            this.tableLayoutPanel1.SuspendLayout();
            this.groupCheck.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvCheck)).BeginInit();
            this.panelCheckBottom.SuspendLayout();
            this.groupEdit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvEdit)).BeginInit();
            this.panelEditBottom.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.Controls.Add(this.groupCheck, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.groupEdit, 1, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 1;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(1280, 720);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // groupCheck
            // 
            this.groupCheck.Controls.Add(this.dgvCheck);
            this.groupCheck.Controls.Add(this.panelCheckBottom);
            this.groupCheck.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupCheck.Location = new System.Drawing.Point(6, 6);
            this.groupCheck.Margin = new System.Windows.Forms.Padding(6);
            this.groupCheck.Name = "groupCheck";
            this.groupCheck.Padding = new System.Windows.Forms.Padding(6);
            this.groupCheck.Size = new System.Drawing.Size(628, 708);
            this.groupCheck.TabIndex = 0;
            this.groupCheck.TabStop = false;
            this.groupCheck.Text = "复选框调试";
            // 
            // dgvCheck
            // 
            this.dgvCheck.AllowUserToAddRows = false;
            this.dgvCheck.AllowUserToDeleteRows = false;
            this.dgvCheck.AllowUserToResizeRows = false;
            this.dgvCheck.ColumnHeadersHeight = 32;
            this.dgvCheck.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvCheck.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colCheckNote,
            this.colCheckIndex,
            this.colCheckName,
            this.colCheckType,
            this.colCheckValue});
            this.dgvCheck.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvCheck.Location = new System.Drawing.Point(6, 24);
            this.dgvCheck.MultiSelect = false;
            this.dgvCheck.Name = "dgvCheck";
            this.dgvCheck.RowHeadersVisible = false;
            this.dgvCheck.RowTemplate.Height = 28;
            this.dgvCheck.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvCheck.Size = new System.Drawing.Size(616, 574);
            this.dgvCheck.TabIndex = 0;
            this.dgvCheck.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvCheck_CellValueChanged);
            this.dgvCheck.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvCheck_CellEndEdit);
            this.dgvCheck.CurrentCellDirtyStateChanged += new System.EventHandler(this.dgvCheck_CurrentCellDirtyStateChanged);
            // 
            // colCheckNote
            // 
            this.colCheckNote.HeaderText = "备注";
            this.colCheckNote.MinimumWidth = 120;
            this.colCheckNote.Name = "colCheckNote";
            // 
            // colCheckIndex
            // 
            this.colCheckIndex.HeaderText = "索引";
            this.colCheckIndex.MinimumWidth = 60;
            this.colCheckIndex.Name = "colCheckIndex";
            this.colCheckIndex.ReadOnly = true;
            this.colCheckIndex.Visible = false;
            // 
            // colCheckName
            // 
            this.colCheckName.HeaderText = "名称";
            this.colCheckName.MinimumWidth = 120;
            this.colCheckName.Name = "colCheckName";
            this.colCheckName.ReadOnly = true;
            // 
            // colCheckType
            // 
            this.colCheckType.HeaderText = "类型";
            this.colCheckType.MinimumWidth = 80;
            this.colCheckType.Name = "colCheckType";
            this.colCheckType.ReadOnly = true;
            // 
            // colCheckValue
            // 
            this.colCheckValue.HeaderText = "置1";
            this.colCheckValue.MinimumWidth = 60;
            this.colCheckValue.Name = "colCheckValue";
            // 
            // panelCheckBottom
            // 
            this.panelCheckBottom.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panelCheckBottom.Controls.Add(this.btnCheckRefresh);
            this.panelCheckBottom.Controls.Add(this.btnCheckRemove);
            this.panelCheckBottom.Controls.Add(this.btnCheckAdd);
            this.panelCheckBottom.Controls.Add(this.cboCheckVar);
            this.panelCheckBottom.Controls.Add(this.lblCheckIndex);
            this.panelCheckBottom.Controls.Add(this.lblCheckStatus);
            this.panelCheckBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelCheckBottom.Location = new System.Drawing.Point(6, 598);
            this.panelCheckBottom.Name = "panelCheckBottom";
            this.panelCheckBottom.Size = new System.Drawing.Size(616, 104);
            this.panelCheckBottom.TabIndex = 1;
            // 
            // btnCheckRefresh
            // 
            this.btnCheckRefresh.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnCheckRefresh.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnCheckRefresh.FlatAppearance.BorderSize = 2;
            this.btnCheckRefresh.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnCheckRefresh.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnCheckRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckRefresh.Location = new System.Drawing.Point(492, 14);
            this.btnCheckRefresh.Name = "btnCheckRefresh";
            this.btnCheckRefresh.Size = new System.Drawing.Size(90, 32);
            this.btnCheckRefresh.TabIndex = 4;
            this.btnCheckRefresh.Text = "刷新";
            this.btnCheckRefresh.UseVisualStyleBackColor = false;
            this.btnCheckRefresh.Click += new System.EventHandler(this.btnCheckRefresh_Click);
            // 
            // btnCheckRemove
            // 
            this.btnCheckRemove.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnCheckRemove.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnCheckRemove.FlatAppearance.BorderSize = 2;
            this.btnCheckRemove.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnCheckRemove.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnCheckRemove.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckRemove.Location = new System.Drawing.Point(396, 14);
            this.btnCheckRemove.Name = "btnCheckRemove";
            this.btnCheckRemove.Size = new System.Drawing.Size(90, 32);
            this.btnCheckRemove.TabIndex = 3;
            this.btnCheckRemove.Text = "移除";
            this.btnCheckRemove.UseVisualStyleBackColor = false;
            this.btnCheckRemove.Click += new System.EventHandler(this.btnCheckRemove_Click);
            // 
            // btnCheckAdd
            // 
            this.btnCheckAdd.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnCheckAdd.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnCheckAdd.FlatAppearance.BorderSize = 2;
            this.btnCheckAdd.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnCheckAdd.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnCheckAdd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckAdd.Location = new System.Drawing.Point(300, 14);
            this.btnCheckAdd.Name = "btnCheckAdd";
            this.btnCheckAdd.Size = new System.Drawing.Size(90, 32);
            this.btnCheckAdd.TabIndex = 2;
            this.btnCheckAdd.Text = "添加";
            this.btnCheckAdd.UseVisualStyleBackColor = false;
            this.btnCheckAdd.Click += new System.EventHandler(this.btnCheckAdd_Click);
            // 
            // cboCheckVar
            // 
            this.cboCheckVar.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboCheckVar.FormattingEnabled = true;
            this.cboCheckVar.Location = new System.Drawing.Point(64, 18);
            this.cboCheckVar.Name = "cboCheckVar";
            this.cboCheckVar.Size = new System.Drawing.Size(220, 23);
            this.cboCheckVar.TabIndex = 1;
            // 
            // lblCheckIndex
            // 
            this.lblCheckIndex.AutoSize = true;
            this.lblCheckIndex.Location = new System.Drawing.Point(14, 22);
            this.lblCheckIndex.Name = "lblCheckIndex";
            this.lblCheckIndex.Size = new System.Drawing.Size(35, 15);
            this.lblCheckIndex.TabIndex = 0;
            this.lblCheckIndex.Text = "变量";
            // 
            // lblCheckStatus
            // 
            this.lblCheckStatus.AutoSize = true;
            this.lblCheckStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblCheckStatus.Location = new System.Drawing.Point(14, 64);
            this.lblCheckStatus.Name = "lblCheckStatus";
            this.lblCheckStatus.Size = new System.Drawing.Size(67, 15);
            this.lblCheckStatus.TabIndex = 5;
            this.lblCheckStatus.Text = "未选择变量";
            // 
            // groupEdit
            // 
            this.groupEdit.Controls.Add(this.dgvEdit);
            this.groupEdit.Controls.Add(this.panelEditBottom);
            this.groupEdit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupEdit.Location = new System.Drawing.Point(646, 6);
            this.groupEdit.Margin = new System.Windows.Forms.Padding(6);
            this.groupEdit.Name = "groupEdit";
            this.groupEdit.Padding = new System.Windows.Forms.Padding(6);
            this.groupEdit.Size = new System.Drawing.Size(628, 708);
            this.groupEdit.TabIndex = 1;
            this.groupEdit.TabStop = false;
            this.groupEdit.Text = "编辑框调试";
            // 
            // dgvEdit
            // 
            this.dgvEdit.AllowUserToAddRows = false;
            this.dgvEdit.AllowUserToDeleteRows = false;
            this.dgvEdit.AllowUserToResizeRows = false;
            this.dgvEdit.ColumnHeadersHeight = 32;
            this.dgvEdit.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvEdit.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colEditNote,
            this.colEditIndex,
            this.colEditName,
            this.colEditType,
            this.colEditValue});
            this.dgvEdit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvEdit.Location = new System.Drawing.Point(6, 24);
            this.dgvEdit.MultiSelect = false;
            this.dgvEdit.Name = "dgvEdit";
            this.dgvEdit.ReadOnly = false;
            this.dgvEdit.RowHeadersVisible = false;
            this.dgvEdit.RowTemplate.Height = 28;
            this.dgvEdit.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvEdit.Size = new System.Drawing.Size(616, 548);
            this.dgvEdit.TabIndex = 0;
            this.dgvEdit.SelectionChanged += new System.EventHandler(this.dgvEdit_SelectionChanged);
            this.dgvEdit.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvEdit_CellEndEdit);
            // 
            // colEditNote
            // 
            this.colEditNote.HeaderText = "备注";
            this.colEditNote.MinimumWidth = 120;
            this.colEditNote.Name = "colEditNote";
            // 
            // colEditIndex
            // 
            this.colEditIndex.HeaderText = "索引";
            this.colEditIndex.MinimumWidth = 60;
            this.colEditIndex.Name = "colEditIndex";
            this.colEditIndex.ReadOnly = true;
            this.colEditIndex.Visible = false;
            // 
            // colEditName
            // 
            this.colEditName.HeaderText = "名称";
            this.colEditName.MinimumWidth = 120;
            this.colEditName.Name = "colEditName";
            this.colEditName.ReadOnly = true;
            // 
            // colEditType
            // 
            this.colEditType.HeaderText = "类型";
            this.colEditType.MinimumWidth = 80;
            this.colEditType.Name = "colEditType";
            this.colEditType.ReadOnly = true;
            // 
            // colEditValue
            // 
            this.colEditValue.HeaderText = "当前值";
            this.colEditValue.MinimumWidth = 120;
            this.colEditValue.Name = "colEditValue";
            this.colEditValue.ReadOnly = true;
            // 
            // panelEditBottom
            // 
            this.panelEditBottom.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panelEditBottom.Controls.Add(this.btnEditRefresh);
            this.panelEditBottom.Controls.Add(this.btnEditApply);
            this.panelEditBottom.Controls.Add(this.btnEditRemove);
            this.panelEditBottom.Controls.Add(this.btnEditAdd);
            this.panelEditBottom.Controls.Add(this.txtEditValue);
            this.panelEditBottom.Controls.Add(this.cboEditVar);
            this.panelEditBottom.Controls.Add(this.lblEditValue);
            this.panelEditBottom.Controls.Add(this.lblEditIndex);
            this.panelEditBottom.Controls.Add(this.lblEditStatus);
            this.panelEditBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelEditBottom.Location = new System.Drawing.Point(6, 572);
            this.panelEditBottom.Name = "panelEditBottom";
            this.panelEditBottom.Size = new System.Drawing.Size(616, 130);
            this.panelEditBottom.TabIndex = 1;
            // 
            // btnEditRefresh
            // 
            this.btnEditRefresh.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnEditRefresh.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnEditRefresh.FlatAppearance.BorderSize = 2;
            this.btnEditRefresh.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnEditRefresh.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnEditRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditRefresh.Location = new System.Drawing.Point(496, 16);
            this.btnEditRefresh.Name = "btnEditRefresh";
            this.btnEditRefresh.Size = new System.Drawing.Size(90, 32);
            this.btnEditRefresh.TabIndex = 7;
            this.btnEditRefresh.Text = "刷新";
            this.btnEditRefresh.UseVisualStyleBackColor = false;
            this.btnEditRefresh.Click += new System.EventHandler(this.btnEditRefresh_Click);
            // 
            // btnEditApply
            // 
            this.btnEditApply.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnEditApply.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnEditApply.FlatAppearance.BorderSize = 2;
            this.btnEditApply.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnEditApply.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnEditApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditApply.Location = new System.Drawing.Point(496, 56);
            this.btnEditApply.Name = "btnEditApply";
            this.btnEditApply.Size = new System.Drawing.Size(90, 32);
            this.btnEditApply.TabIndex = 8;
            this.btnEditApply.Text = "应用";
            this.btnEditApply.UseVisualStyleBackColor = false;
            this.btnEditApply.Click += new System.EventHandler(this.btnEditApply_Click);
            // 
            // btnEditRemove
            // 
            this.btnEditRemove.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnEditRemove.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnEditRemove.FlatAppearance.BorderSize = 2;
            this.btnEditRemove.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnEditRemove.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnEditRemove.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditRemove.Location = new System.Drawing.Point(390, 16);
            this.btnEditRemove.Name = "btnEditRemove";
            this.btnEditRemove.Size = new System.Drawing.Size(90, 32);
            this.btnEditRemove.TabIndex = 6;
            this.btnEditRemove.Text = "移除";
            this.btnEditRemove.UseVisualStyleBackColor = false;
            this.btnEditRemove.Click += new System.EventHandler(this.btnEditRemove_Click);
            // 
            // btnEditAdd
            // 
            this.btnEditAdd.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnEditAdd.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnEditAdd.FlatAppearance.BorderSize = 2;
            this.btnEditAdd.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnEditAdd.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnEditAdd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditAdd.Location = new System.Drawing.Point(284, 16);
            this.btnEditAdd.Name = "btnEditAdd";
            this.btnEditAdd.Size = new System.Drawing.Size(90, 32);
            this.btnEditAdd.TabIndex = 5;
            this.btnEditAdd.Text = "添加";
            this.btnEditAdd.UseVisualStyleBackColor = false;
            this.btnEditAdd.Click += new System.EventHandler(this.btnEditAdd_Click);
            // 
            // txtEditValue
            // 
            this.txtEditValue.Location = new System.Drawing.Point(68, 60);
            this.txtEditValue.Name = "txtEditValue";
            this.txtEditValue.Size = new System.Drawing.Size(396, 25);
            this.txtEditValue.TabIndex = 4;
            this.txtEditValue.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtEditValue_KeyDown);
            // 
            // cboEditVar
            // 
            this.cboEditVar.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboEditVar.FormattingEnabled = true;
            this.cboEditVar.Location = new System.Drawing.Point(68, 20);
            this.cboEditVar.Name = "cboEditVar";
            this.cboEditVar.Size = new System.Drawing.Size(200, 23);
            this.cboEditVar.TabIndex = 3;
            // 
            // lblEditValue
            // 
            this.lblEditValue.AutoSize = true;
            this.lblEditValue.Location = new System.Drawing.Point(20, 64);
            this.lblEditValue.Name = "lblEditValue";
            this.lblEditValue.Size = new System.Drawing.Size(35, 15);
            this.lblEditValue.TabIndex = 2;
            this.lblEditValue.Text = "值";
            // 
            // lblEditIndex
            // 
            this.lblEditIndex.AutoSize = true;
            this.lblEditIndex.Location = new System.Drawing.Point(20, 24);
            this.lblEditIndex.Name = "lblEditIndex";
            this.lblEditIndex.Size = new System.Drawing.Size(35, 15);
            this.lblEditIndex.TabIndex = 1;
            this.lblEditIndex.Text = "变量";
            // 
            // lblEditStatus
            // 
            this.lblEditStatus.AutoSize = true;
            this.lblEditStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblEditStatus.Location = new System.Drawing.Point(20, 100);
            this.lblEditStatus.Name = "lblEditStatus";
            this.lblEditStatus.Size = new System.Drawing.Size(67, 15);
            this.lblEditStatus.TabIndex = 9;
            this.lblEditStatus.Text = "未选择变量";
            // 
            // FrmValueDebug
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1280, 720);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Name = "FrmValueDebug";
            this.Text = "变量调试";
            this.Load += new System.EventHandler(this.FrmValueDebug_Load);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.groupCheck.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvCheck)).EndInit();
            this.panelCheckBottom.ResumeLayout(false);
            this.panelCheckBottom.PerformLayout();
            this.groupEdit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvEdit)).EndInit();
            this.panelEditBottom.ResumeLayout(false);
            this.panelEditBottom.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.GroupBox groupCheck;
        private System.Windows.Forms.GroupBox groupEdit;
        private System.Windows.Forms.DataGridView dgvCheck;
        private System.Windows.Forms.DataGridView dgvEdit;
        private System.Windows.Forms.Panel panelCheckBottom;
        private System.Windows.Forms.Panel panelEditBottom;
        private System.Windows.Forms.Button btnCheckAdd;
        private System.Windows.Forms.Button btnCheckRemove;
        private System.Windows.Forms.Button btnCheckRefresh;
        private System.Windows.Forms.Button btnEditAdd;
        private System.Windows.Forms.Button btnEditRemove;
        private System.Windows.Forms.Button btnEditApply;
        private System.Windows.Forms.Button btnEditRefresh;
        private System.Windows.Forms.ComboBox cboCheckVar;
        private System.Windows.Forms.ComboBox cboEditVar;
        private System.Windows.Forms.TextBox txtEditValue;
        private System.Windows.Forms.Label lblCheckIndex;
        private System.Windows.Forms.Label lblEditIndex;
        private System.Windows.Forms.Label lblEditValue;
        private System.Windows.Forms.Label lblCheckStatus;
        private System.Windows.Forms.Label lblEditStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCheckNote;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCheckIndex;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCheckName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCheckType;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colCheckValue;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEditNote;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEditIndex;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEditName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEditType;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEditValue;
    }
}
