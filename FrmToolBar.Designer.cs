namespace Automation
{
    partial class FrmToolBar
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
            this.ToolBar_Panel = new System.Windows.Forms.Panel();
            this.CleanAllMark = new System.Windows.Forms.Button();
            this.Mark = new System.Windows.Forms.Button();
            this.LastMark = new System.Windows.Forms.Button();
            this.NextMark = new System.Windows.Forms.Button();
            this.btnLocate = new System.Windows.Forms.Button();
            this.btnAlarm = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.btnMonitor = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnTrack = new System.Windows.Forms.Button();
            this.SingleRun = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnSearch = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.ToolBar_Panel.SuspendLayout();
            this.SuspendLayout();
            // 
            // ToolBar_Panel
            // 
            this.ToolBar_Panel.Controls.Add(this.CleanAllMark);
            this.ToolBar_Panel.Controls.Add(this.Mark);
            this.ToolBar_Panel.Controls.Add(this.LastMark);
            this.ToolBar_Panel.Controls.Add(this.NextMark);
            this.ToolBar_Panel.Controls.Add(this.btnLocate);
            this.ToolBar_Panel.Controls.Add(this.btnAlarm);
            this.ToolBar_Panel.Controls.Add(this.button2);
            this.ToolBar_Panel.Controls.Add(this.btnMonitor);
            this.ToolBar_Panel.Controls.Add(this.btnStop);
            this.ToolBar_Panel.Controls.Add(this.btnTrack);
            this.ToolBar_Panel.Controls.Add(this.SingleRun);
            this.ToolBar_Panel.Controls.Add(this.btnPause);
            this.ToolBar_Panel.Controls.Add(this.btnSearch);
            this.ToolBar_Panel.Controls.Add(this.button1);
            this.ToolBar_Panel.Controls.Add(this.btnCancel);
            this.ToolBar_Panel.Controls.Add(this.btnSave);
            this.ToolBar_Panel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ToolBar_Panel.Location = new System.Drawing.Point(0, 0);
            this.ToolBar_Panel.Name = "ToolBar_Panel";
            this.ToolBar_Panel.Size = new System.Drawing.Size(1280, 72);
            this.ToolBar_Panel.TabIndex = 0;
            // 
            // CleanAllMark
            // 
            this.CleanAllMark.Location = new System.Drawing.Point(964, 5);
            this.CleanAllMark.Margin = new System.Windows.Forms.Padding(2);
            this.CleanAllMark.Name = "CleanAllMark";
            this.CleanAllMark.Size = new System.Drawing.Size(35, 30);
            this.CleanAllMark.TabIndex = 15;
            this.CleanAllMark.Text = "×";
            this.CleanAllMark.UseVisualStyleBackColor = true;
            this.CleanAllMark.Visible = false;
            this.CleanAllMark.Click += new System.EventHandler(this.CleanAllMark_Click);
            // 
            // Mark
            // 
            this.Mark.Location = new System.Drawing.Point(815, 5);
            this.Mark.Margin = new System.Windows.Forms.Padding(2);
            this.Mark.Name = "Mark";
            this.Mark.Size = new System.Drawing.Size(67, 30);
            this.Mark.TabIndex = 14;
            this.Mark.Text = "标记";
            this.Mark.UseVisualStyleBackColor = true;
            this.Mark.Visible = false;
            this.Mark.Click += new System.EventHandler(this.Mark_Click);
            // 
            // LastMark
            // 
            this.LastMark.Location = new System.Drawing.Point(886, 5);
            this.LastMark.Margin = new System.Windows.Forms.Padding(2);
            this.LastMark.Name = "LastMark";
            this.LastMark.Size = new System.Drawing.Size(35, 30);
            this.LastMark.TabIndex = 13;
            this.LastMark.Text = "←";
            this.LastMark.UseVisualStyleBackColor = true;
            this.LastMark.Visible = false;
            this.LastMark.Click += new System.EventHandler(this.LastMark_Click);
            // 
            // NextMark
            // 
            this.NextMark.Location = new System.Drawing.Point(925, 5);
            this.NextMark.Margin = new System.Windows.Forms.Padding(2);
            this.NextMark.Name = "NextMark";
            this.NextMark.Size = new System.Drawing.Size(35, 30);
            this.NextMark.TabIndex = 12;
            this.NextMark.Text = "→";
            this.NextMark.UseVisualStyleBackColor = true;
            this.NextMark.Visible = false;
            this.NextMark.Click += new System.EventHandler(this.NextMark_Click);
            // 
            // btnLocate
            // 
            this.btnLocate.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnLocate.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnLocate.FlatAppearance.BorderSize = 2;
            this.btnLocate.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnLocate.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnLocate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLocate.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnLocate.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnLocate.Location = new System.Drawing.Point(378, 1);
            this.btnLocate.Margin = new System.Windows.Forms.Padding(2);
            this.btnLocate.Name = "btnLocate";
            this.btnLocate.Size = new System.Drawing.Size(76, 39);
            this.btnLocate.TabIndex = 11;
            this.btnLocate.Text = "定位";
            this.btnLocate.UseVisualStyleBackColor = false;
            this.btnLocate.Click += new System.EventHandler(this.btnLocate_Click);
            // 
            // btnAlarm
            // 
            this.btnAlarm.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnAlarm.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnAlarm.FlatAppearance.BorderSize = 2;
            this.btnAlarm.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnAlarm.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnAlarm.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAlarm.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAlarm.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnAlarm.Location = new System.Drawing.Point(449, 1);
            this.btnAlarm.Margin = new System.Windows.Forms.Padding(2);
            this.btnAlarm.Name = "btnAlarm";
            this.btnAlarm.Size = new System.Drawing.Size(99, 39);
            this.btnAlarm.TabIndex = 10;
            this.btnAlarm.Text = "报警信息";
            this.btnAlarm.UseVisualStyleBackColor = false;
            this.btnAlarm.Click += new System.EventHandler(this.btnAlarm_Click);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(1047, 5);
            this.button2.Margin = new System.Windows.Forms.Padding(2);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(67, 30);
            this.button2.TabIndex = 9;
            this.button2.Text = "Test";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // btnMonitor
            // 
            this.btnMonitor.Location = new System.Drawing.Point(1118, 5);
            this.btnMonitor.Margin = new System.Windows.Forms.Padding(2);
            this.btnMonitor.Name = "btnMonitor";
            this.btnMonitor.Size = new System.Drawing.Size(67, 30);
            this.btnMonitor.TabIndex = 8;
            this.btnMonitor.Text = "监视器";
            this.btnMonitor.UseVisualStyleBackColor = true;
            this.btnMonitor.Click += new System.EventHandler(this.btnMonitor_Click);
            // 
            // btnStop
            // 
            this.btnStop.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnStop.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnStop.FlatAppearance.BorderSize = 2;
            this.btnStop.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnStop.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStop.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnStop.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnStop.Location = new System.Drawing.Point(236, 1);
            this.btnStop.Margin = new System.Windows.Forms.Padding(2);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(76, 39);
            this.btnStop.TabIndex = 7;
            this.btnStop.Text = "停止";
            this.btnStop.UseVisualStyleBackColor = false;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // btnTrack
            // 
            this.btnTrack.Location = new System.Drawing.Point(1189, 5);
            this.btnTrack.Margin = new System.Windows.Forms.Padding(2);
            this.btnTrack.Name = "btnTrack";
            this.btnTrack.Size = new System.Drawing.Size(67, 30);
            this.btnTrack.TabIndex = 6;
            this.btnTrack.Text = "监控";
            this.btnTrack.UseVisualStyleBackColor = true;
            this.btnTrack.Click += new System.EventHandler(this.btnTrack_Click);
            // 
            // SingleRun
            // 
            this.SingleRun.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.SingleRun.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.SingleRun.FlatAppearance.BorderSize = 2;
            this.SingleRun.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.SingleRun.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.SingleRun.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.SingleRun.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.SingleRun.ForeColor = System.Drawing.SystemColors.ControlText;
            this.SingleRun.Location = new System.Drawing.Point(307, 1);
            this.SingleRun.Margin = new System.Windows.Forms.Padding(2);
            this.SingleRun.Name = "SingleRun";
            this.SingleRun.Size = new System.Drawing.Size(76, 39);
            this.SingleRun.TabIndex = 5;
            this.SingleRun.Text = "单步";
            this.SingleRun.UseVisualStyleBackColor = false;
            this.SingleRun.Click += new System.EventHandler(this.SingleRun_Click);
            // 
            // btnPause
            // 
            this.btnPause.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnPause.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnPause.FlatAppearance.BorderSize = 2;
            this.btnPause.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnPause.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnPause.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPause.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPause.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnPause.Location = new System.Drawing.Point(165, 1);
            this.btnPause.Margin = new System.Windows.Forms.Padding(2);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(76, 39);
            this.btnPause.TabIndex = 4;
            this.btnPause.Text = "暂停";
            this.btnPause.UseVisualStyleBackColor = false;
            this.btnPause.Click += new System.EventHandler(this.btnPause_Click);
            // 
            // btnSearch
            // 
            this.btnSearch.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnSearch.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnSearch.FlatAppearance.BorderSize = 2;
            this.btnSearch.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnSearch.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnSearch.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSearch.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSearch.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnSearch.Location = new System.Drawing.Point(543, 1);
            this.btnSearch.Margin = new System.Windows.Forms.Padding(2);
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.Size = new System.Drawing.Size(76, 39);
            this.btnSearch.TabIndex = 3;
            this.btnSearch.Text = "查找";
            this.btnSearch.UseVisualStyleBackColor = false;
            this.btnSearch.Click += new System.EventHandler(this.btnSearch_Click);
            // 
            // button1
            // 
            this.button1.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.button1.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.button1.FlatAppearance.BorderSize = 2;
            this.button1.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.button1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.White;
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button1.ForeColor = System.Drawing.SystemColors.ControlText;
            this.button1.Location = new System.Drawing.Point(689, 5);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(121, 30);
            this.button1.TabIndex = 2;
            this.button1.Text = "打开程序文件夹";
            this.button1.UseVisualStyleBackColor = false;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnCancel.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnCancel.FlatAppearance.BorderSize = 2;
            this.btnCancel.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnCancel.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCancel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnCancel.Location = new System.Drawing.Point(85, 1);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(84, 39);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnSave
            // 
            this.btnSave.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.btnSave.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnSave.FlatAppearance.BorderSize = 2;
            this.btnSave.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnSave.FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.MenuHighlight;
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSave.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnSave.Location = new System.Drawing.Point(6, 1);
            this.btnSave.Margin = new System.Windows.Forms.Padding(2);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(84, 39);
            this.btnSave.TabIndex = 0;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // FrmToolBar
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1280, 72);
            this.Controls.Add(this.ToolBar_Panel);
            this.Name = "FrmToolBar";
            this.Text = "FrmToolBar";
            this.ToolBar_Panel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ToolBar_Panel;
        private System.Windows.Forms.Button button1;
        public System.Windows.Forms.Button btnSave;
        public System.Windows.Forms.Button btnStop;
        public System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnMonitor;
        public System.Windows.Forms.Button btnCancel;
        public System.Windows.Forms.Button SingleRun;
        public System.Windows.Forms.Button btnTrack;
        private System.Windows.Forms.Button button2;
        public System.Windows.Forms.Button btnAlarm;
        public System.Windows.Forms.Button btnLocate;
        public System.Windows.Forms.Button btnSearch;
        public System.Windows.Forms.Button Mark;
        public System.Windows.Forms.Button LastMark;
        public System.Windows.Forms.Button NextMark;
        public System.Windows.Forms.Button CleanAllMark;
    }
}