namespace Automation
{
    partial class FrmInfo
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
            if (disposing)
            {
                if (statusTimer != null)
                {
                    statusTimer.Stop();
                    statusTimer.Dispose();
                    statusTimer = null;
                }
                if (infoAutoScrollTimer != null)
                {
                    infoAutoScrollTimer.Stop();
                    infoAutoScrollTimer.Dispose();
                    infoAutoScrollTimer = null;
                }
                if (components != null)
                {
                    components.Dispose();
                }
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
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.tabPageStatus = new System.Windows.Forms.TabPage();
            this.dgvProcStatus = new System.Windows.Forms.DataGridView();
            this.panelStatusTools = new System.Windows.Forms.Panel();
            this.lblStatusTip = new System.Windows.Forms.Label();
            this.ReceiveTextBox = new System.Windows.Forms.RichTextBox();
            this.tabControl1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.tabPageStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvProcStatus)).BeginInit();
            this.panelStatusTools.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Alignment = System.Windows.Forms.TabAlignment.Bottom;
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPageStatus);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Margin = new System.Windows.Forms.Padding(0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(800, 450);
            this.tabControl1.TabIndex = 0;
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.ReceiveTextBox);
            this.tabPage2.Location = new System.Drawing.Point(4, 4);
            this.tabPage2.Margin = new System.Windows.Forms.Padding(0);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(792, 424);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "运行信息";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // tabPageStatus
            // 
            this.tabPageStatus.Controls.Add(this.dgvProcStatus);
            this.tabPageStatus.Controls.Add(this.panelStatusTools);
            this.tabPageStatus.Location = new System.Drawing.Point(4, 4);
            this.tabPageStatus.Margin = new System.Windows.Forms.Padding(0);
            this.tabPageStatus.Name = "tabPageStatus";
            this.tabPageStatus.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageStatus.Size = new System.Drawing.Size(792, 424);
            this.tabPageStatus.TabIndex = 2;
            this.tabPageStatus.Text = "流程状态";
            this.tabPageStatus.UseVisualStyleBackColor = true;
            // 
            // dgvProcStatus
            // 
            this.dgvProcStatus.AllowUserToAddRows = false;
            this.dgvProcStatus.AllowUserToDeleteRows = false;
            this.dgvProcStatus.AllowUserToResizeColumns = false;
            this.dgvProcStatus.AllowUserToResizeRows = false;
            this.dgvProcStatus.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvProcStatus.BackgroundColor = System.Drawing.Color.White;
            this.dgvProcStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.dgvProcStatus.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.Single;
            this.dgvProcStatus.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(245)))), ((int)(((byte)(245)))));
            dataGridViewCellStyle1.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold);
            dataGridViewCellStyle1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(245)))), ((int)(((byte)(245)))));
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dgvProcStatus.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dgvProcStatus.ColumnHeadersHeight = 28;
            this.dgvProcStatus.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.White;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(221)))), ((int)(((byte)(235)))), ((int)(((byte)(247)))));
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dgvProcStatus.DefaultCellStyle = dataGridViewCellStyle2;
            this.dgvProcStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvProcStatus.EnableHeadersVisualStyles = false;
            this.dgvProcStatus.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(220)))));
            this.dgvProcStatus.Location = new System.Drawing.Point(3, 39);
            this.dgvProcStatus.Margin = new System.Windows.Forms.Padding(0);
            this.dgvProcStatus.MultiSelect = false;
            this.dgvProcStatus.Name = "dgvProcStatus";
            this.dgvProcStatus.ReadOnly = true;
            this.dgvProcStatus.RowHeadersVisible = false;
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(250)))), ((int)(((byte)(250)))));
            this.dgvProcStatus.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle3;
            this.dgvProcStatus.RowTemplate.Height = 24;
            this.dgvProcStatus.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.dgvProcStatus.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.dgvProcStatus.Size = new System.Drawing.Size(786, 382);
            this.dgvProcStatus.TabIndex = 2;
            // 
            // panelStatusTools
            // 
            this.panelStatusTools.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.panelStatusTools.Controls.Add(this.lblStatusTip);
            this.panelStatusTools.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelStatusTools.Location = new System.Drawing.Point(3, 3);
            this.panelStatusTools.Margin = new System.Windows.Forms.Padding(0);
            this.panelStatusTools.Name = "panelStatusTools";
            this.panelStatusTools.Size = new System.Drawing.Size(786, 36);
            this.panelStatusTools.TabIndex = 3;
            // 
            // lblStatusTip
            // 
            this.lblStatusTip.AutoSize = true;
            this.lblStatusTip.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblStatusTip.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatusTip.Location = new System.Drawing.Point(8, 10);
            this.lblStatusTip.Name = "lblStatusTip";
            this.lblStatusTip.Size = new System.Drawing.Size(140, 17);
            this.lblStatusTip.TabIndex = 0;
            this.lblStatusTip.Text = "双击当前位置可跳转到指令";
            // 
            // ReceiveTextBox
            // 
            this.ReceiveTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReceiveTextBox.Font = new System.Drawing.Font("Calibri", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ReceiveTextBox.Location = new System.Drawing.Point(3, 3);
            this.ReceiveTextBox.Margin = new System.Windows.Forms.Padding(0);
            this.ReceiveTextBox.Name = "ReceiveTextBox";
            this.ReceiveTextBox.ReadOnly = true;
            this.ReceiveTextBox.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.ReceiveTextBox.Size = new System.Drawing.Size(786, 418);
            this.ReceiveTextBox.TabIndex = 2;
            this.ReceiveTextBox.Text = "";
            // 
            // FrmInfo
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.tabControl1);
            this.DoubleBuffered = true;
            this.Name = "FrmInfo";
            this.Text = "FrmInfo";
            this.Load += new System.EventHandler(this.FrmInfo_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            this.tabPageStatus.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvProcStatus)).EndInit();
            this.panelStatusTools.ResumeLayout(false);
            this.panelStatusTools.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPageStatus;
        public System.Windows.Forms.RichTextBox ReceiveTextBox;
        public System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.DataGridView dgvProcStatus;
        private System.Windows.Forms.Panel panelStatusTools;
        private System.Windows.Forms.Label lblStatusTip;
    }
}
