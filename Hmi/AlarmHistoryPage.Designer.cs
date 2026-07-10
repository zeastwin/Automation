namespace Automation.Hmi
{
    partial class AlarmHistoryPage
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.toolbarLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblDate = new System.Windows.Forms.Label();
            this.dtpDate = new System.Windows.Forms.DateTimePicker();
            this.lblPath = new System.Windows.Forms.Label();
            this.txtPath = new System.Windows.Forms.TextBox();
            this.btnChoosePath = new System.Windows.Forms.Button();
            this.btnRead = new System.Windows.Forms.Button();
            this.gridAlarmHistory = new System.Windows.Forms.DataGridView();
            this.toolbarLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridAlarmHistory)).BeginInit();
            this.SuspendLayout();
            // 
            // toolbarLayout
            // 
            this.toolbarLayout.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(247)))), ((int)(((byte)(249)))), ((int)(((byte)(251)))));
            this.toolbarLayout.ColumnCount = 6;
            this.toolbarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            this.toolbarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.toolbarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 92F));
            this.toolbarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.toolbarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 118F));
            this.toolbarLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 92F));
            this.toolbarLayout.Controls.Add(this.lblDate, 0, 0);
            this.toolbarLayout.Controls.Add(this.dtpDate, 1, 0);
            this.toolbarLayout.Controls.Add(this.lblPath, 2, 0);
            this.toolbarLayout.Controls.Add(this.txtPath, 3, 0);
            this.toolbarLayout.Controls.Add(this.btnChoosePath, 4, 0);
            this.toolbarLayout.Controls.Add(this.btnRead, 5, 0);
            this.toolbarLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolbarLayout.Location = new System.Drawing.Point(0, 0);
            this.toolbarLayout.Name = "toolbarLayout";
            this.toolbarLayout.Padding = new System.Windows.Forms.Padding(12, 10, 12, 10);
            this.toolbarLayout.RowCount = 1;
            this.toolbarLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.toolbarLayout.Size = new System.Drawing.Size(1176, 72);
            this.toolbarLayout.TabIndex = 0;
            // 
            // lblDate
            // 
            this.lblDate.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDate.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblDate.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(47)))), ((int)(((byte)(66)))), ((int)(((byte)(82)))));
            this.lblDate.Location = new System.Drawing.Point(15, 10);
            this.lblDate.Name = "lblDate";
            this.lblDate.Size = new System.Drawing.Size(46, 52);
            this.lblDate.TabIndex = 0;
            this.lblDate.Text = "日期";
            this.lblDate.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // dtpDate
            // 
            this.dtpDate.CustomFormat = "yyyy-MM-dd";
            this.dtpDate.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dtpDate.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dtpDate.Location = new System.Drawing.Point(64, 17);
            this.dtpDate.Margin = new System.Windows.Forms.Padding(0, 7, 12, 7);
            this.dtpDate.Name = "dtpDate";
            this.dtpDate.Size = new System.Drawing.Size(138, 23);
            this.dtpDate.TabIndex = 1;
            // 
            // lblPath
            // 
            this.lblPath.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPath.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblPath.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(47)))), ((int)(((byte)(66)))), ((int)(((byte)(82)))));
            this.lblPath.Location = new System.Drawing.Point(217, 10);
            this.lblPath.Name = "lblPath";
            this.lblPath.Size = new System.Drawing.Size(86, 52);
            this.lblPath.TabIndex = 2;
            this.lblPath.Text = "读取路径";
            this.lblPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtPath
            // 
            this.txtPath.BackColor = System.Drawing.Color.White;
            this.txtPath.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtPath.Location = new System.Drawing.Point(306, 18);
            this.txtPath.Margin = new System.Windows.Forms.Padding(0, 8, 12, 7);
            this.txtPath.Name = "txtPath";
            this.txtPath.ReadOnly = true;
            this.txtPath.Size = new System.Drawing.Size(636, 23);
            this.txtPath.TabIndex = 3;
            // 
            // btnChoosePath
            // 
            this.btnChoosePath.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(47)))), ((int)(((byte)(85)))), ((int)(((byte)(87)))));
            this.btnChoosePath.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnChoosePath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnChoosePath.ForeColor = System.Drawing.Color.White;
            this.btnChoosePath.Location = new System.Drawing.Point(954, 14);
            this.btnChoosePath.Margin = new System.Windows.Forms.Padding(0, 4, 8, 4);
            this.btnChoosePath.Name = "btnChoosePath";
            this.btnChoosePath.Size = new System.Drawing.Size(110, 44);
            this.btnChoosePath.TabIndex = 4;
            this.btnChoosePath.Text = "选择路径";
            this.btnChoosePath.UseVisualStyleBackColor = false;
            this.btnChoosePath.Click += new System.EventHandler(this.btnChoosePath_Click);
            // 
            // btnRead
            // 
            this.btnRead.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(116)))), ((int)(((byte)(177)))));
            this.btnRead.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnRead.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRead.ForeColor = System.Drawing.Color.White;
            this.btnRead.Location = new System.Drawing.Point(1072, 14);
            this.btnRead.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnRead.Name = "btnRead";
            this.btnRead.Size = new System.Drawing.Size(92, 44);
            this.btnRead.TabIndex = 5;
            this.btnRead.Text = "读取";
            this.btnRead.UseVisualStyleBackColor = false;
            this.btnRead.Click += new System.EventHandler(this.btnRead_Click);
            // 
            // gridAlarmHistory
            // 
            this.gridAlarmHistory.AllowUserToAddRows = false;
            this.gridAlarmHistory.AllowUserToDeleteRows = false;
            this.gridAlarmHistory.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridAlarmHistory.BackgroundColor = System.Drawing.Color.White;
            this.gridAlarmHistory.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(47)))), ((int)(((byte)(85)))), ((int)(((byte)(87)))));
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            dataGridViewCellStyle1.ForeColor = System.Drawing.Color.White;
            this.gridAlarmHistory.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.gridAlarmHistory.ColumnHeadersHeight = 44;
            this.gridAlarmHistory.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.gridAlarmHistory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridAlarmHistory.EnableHeadersVisualStyles = false;
            this.gridAlarmHistory.Location = new System.Drawing.Point(0, 72);
            this.gridAlarmHistory.MultiSelect = false;
            this.gridAlarmHistory.Name = "gridAlarmHistory";
            this.gridAlarmHistory.ReadOnly = true;
            this.gridAlarmHistory.RowHeadersVisible = false;
            this.gridAlarmHistory.RowTemplate.Height = 36;
            this.gridAlarmHistory.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridAlarmHistory.Size = new System.Drawing.Size(1176, 560);
            this.gridAlarmHistory.TabIndex = 0;
            // 
            // AlarmHistoryPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1176, 632);
            this.Controls.Add(this.gridAlarmHistory);
            this.Controls.Add(this.toolbarLayout);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "AlarmHistoryPage";
            this.Text = "报警历史";
            this.toolbarLayout.ResumeLayout(false);
            this.toolbarLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridAlarmHistory)).EndInit();
            this.ResumeLayout(false);

        }

        private System.Windows.Forms.TableLayoutPanel toolbarLayout;
        private System.Windows.Forms.Label lblDate;
        private System.Windows.Forms.DateTimePicker dtpDate;
        private System.Windows.Forms.Label lblPath;
        private System.Windows.Forms.TextBox txtPath;
        private System.Windows.Forms.Button btnChoosePath;
        private System.Windows.Forms.Button btnRead;
        private System.Windows.Forms.DataGridView gridAlarmHistory;
    }
}
