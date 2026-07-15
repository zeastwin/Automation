namespace Automation
{
    partial class Message
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
            this.panelAccent = new System.Windows.Forms.Panel();
            this.panelBtn = new System.Windows.Forms.Panel();
            this.flowButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btn3 = new System.Windows.Forms.Button();
            this.btn2 = new System.Windows.Forms.Button();
            this.btn1 = new System.Windows.Forms.Button();
            this.panelMessage = new System.Windows.Forms.Panel();
            this.contentLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblCaption = new System.Windows.Forms.Label();
            this.txtMsg = new System.Windows.Forms.RichTextBox();
            this.panelBtn.SuspendLayout();
            this.flowButtons.SuspendLayout();
            this.panelMessage.SuspendLayout();
            this.contentLayout.SuspendLayout();
            this.SuspendLayout();
            //
            // panelAccent
            //
            this.panelAccent.BackColor = System.Drawing.Color.FromArgb(34, 111, 183);
            this.panelAccent.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelAccent.Location = new System.Drawing.Point(0, 0);
            this.panelAccent.Name = "panelAccent";
            this.panelAccent.Size = new System.Drawing.Size(680, 5);
            this.panelAccent.TabIndex = 0;
            //
            // panelBtn
            //
            this.panelBtn.BackColor = System.Drawing.Color.FromArgb(246, 248, 251);
            this.panelBtn.Controls.Add(this.flowButtons);
            this.panelBtn.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBtn.Location = new System.Drawing.Point(0, 264);
            this.panelBtn.Name = "panelBtn";
            this.panelBtn.Padding = new System.Windows.Forms.Padding(24, 14, 24, 14);
            this.panelBtn.Size = new System.Drawing.Size(680, 72);
            this.panelBtn.TabIndex = 2;
            //
            // flowButtons
            //
            this.flowButtons.Controls.Add(this.btn3);
            this.flowButtons.Controls.Add(this.btn2);
            this.flowButtons.Controls.Add(this.btn1);
            this.flowButtons.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowButtons.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.flowButtons.Location = new System.Drawing.Point(24, 14);
            this.flowButtons.Name = "flowButtons";
            this.flowButtons.Size = new System.Drawing.Size(632, 44);
            this.flowButtons.TabIndex = 0;
            this.flowButtons.WrapContents = false;
            //
            // btn3
            //
            this.btn3.AutoSize = true;
            this.btn3.BackColor = System.Drawing.Color.White;
            this.btn3.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(190, 199, 210);
            this.btn3.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(225, 230, 236);
            this.btn3.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(237, 240, 244);
            this.btn3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn3.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.btn3.ForeColor = System.Drawing.Color.FromArgb(48, 59, 72);
            this.btn3.Location = new System.Drawing.Point(522, 0);
            this.btn3.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.btn3.MinimumSize = new System.Drawing.Size(110, 42);
            this.btn3.Name = "btn3";
            this.btn3.Padding = new System.Windows.Forms.Padding(14, 0, 14, 0);
            this.btn3.Size = new System.Drawing.Size(110, 42);
            this.btn3.TabIndex = 2;
            this.btn3.UseVisualStyleBackColor = false;
            this.btn3.Click += new System.EventHandler(this.btn3_Click);
            // 
            // btn2
            // 
            this.btn2.AutoSize = true;
            this.btn2.BackColor = System.Drawing.Color.FromArgb(34, 111, 183);
            this.btn2.FlatAppearance.BorderSize = 0;
            this.btn2.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(22, 83, 139);
            this.btn2.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(43, 126, 201);
            this.btn2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn2.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F, System.Drawing.FontStyle.Bold);
            this.btn2.ForeColor = System.Drawing.Color.White;
            this.btn2.Location = new System.Drawing.Point(400, 0);
            this.btn2.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.btn2.MinimumSize = new System.Drawing.Size(110, 42);
            this.btn2.Name = "btn2";
            this.btn2.Padding = new System.Windows.Forms.Padding(14, 0, 14, 0);
            this.btn2.Size = new System.Drawing.Size(110, 42);
            this.btn2.TabIndex = 1;
            this.btn2.UseVisualStyleBackColor = false;
            this.btn2.Click += new System.EventHandler(this.btn2_Click);
            // 
            // btn1
            // 
            this.btn1.AutoSize = true;
            this.btn1.BackColor = System.Drawing.Color.FromArgb(34, 111, 183);
            this.btn1.FlatAppearance.BorderSize = 0;
            this.btn1.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(22, 83, 139);
            this.btn1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(43, 126, 201);
            this.btn1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn1.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F, System.Drawing.FontStyle.Bold);
            this.btn1.ForeColor = System.Drawing.Color.White;
            this.btn1.Location = new System.Drawing.Point(278, 0);
            this.btn1.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.btn1.MinimumSize = new System.Drawing.Size(110, 42);
            this.btn1.Name = "btn1";
            this.btn1.Padding = new System.Windows.Forms.Padding(14, 0, 14, 0);
            this.btn1.Size = new System.Drawing.Size(110, 42);
            this.btn1.TabIndex = 0;
            this.btn1.UseVisualStyleBackColor = false;
            this.btn1.Click += new System.EventHandler(this.btn1_Click);
            //
            // panelMessage
            //
            this.panelMessage.BackColor = System.Drawing.Color.White;
            this.panelMessage.Controls.Add(this.contentLayout);
            this.panelMessage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelMessage.Location = new System.Drawing.Point(0, 5);
            this.panelMessage.Name = "panelMessage";
            this.panelMessage.Padding = new System.Windows.Forms.Padding(28, 22, 28, 18);
            this.panelMessage.Size = new System.Drawing.Size(680, 259);
            this.panelMessage.TabIndex = 1;
            //
            // contentLayout
            //
            this.contentLayout.ColumnCount = 1;
            this.contentLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentLayout.Controls.Add(this.lblCaption, 0, 0);
            this.contentLayout.Controls.Add(this.txtMsg, 0, 1);
            this.contentLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentLayout.Location = new System.Drawing.Point(28, 22);
            this.contentLayout.Name = "contentLayout";
            this.contentLayout.RowCount = 2;
            this.contentLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            this.contentLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentLayout.Size = new System.Drawing.Size(624, 219);
            this.contentLayout.TabIndex = 0;
            //
            // lblCaption
            //
            this.lblCaption.AutoEllipsis = true;
            this.lblCaption.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCaption.Font = new System.Drawing.Font("Microsoft YaHei UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblCaption.ForeColor = System.Drawing.Color.FromArgb(28, 39, 52);
            this.lblCaption.Location = new System.Drawing.Point(0, 0);
            this.lblCaption.Margin = new System.Windows.Forms.Padding(0);
            this.lblCaption.Name = "lblCaption";
            this.lblCaption.Size = new System.Drawing.Size(624, 42);
            this.lblCaption.TabIndex = 0;
            this.lblCaption.Text = "提示";
            this.lblCaption.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtMsg
            // 
            this.txtMsg.BackColor = System.Drawing.Color.White;
            this.txtMsg.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtMsg.DetectUrls = false;
            this.txtMsg.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtMsg.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F);
            this.txtMsg.ForeColor = System.Drawing.Color.FromArgb(72, 84, 98);
            this.txtMsg.Location = new System.Drawing.Point(0, 48);
            this.txtMsg.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.txtMsg.Name = "txtMsg";
            this.txtMsg.ReadOnly = true;
            this.txtMsg.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.txtMsg.ShortcutsEnabled = true;
            this.txtMsg.Size = new System.Drawing.Size(624, 171);
            this.txtMsg.TabIndex = 1;
            this.txtMsg.TabStop = false;
            this.txtMsg.Text = "";
            this.txtMsg.FontChanged += new System.EventHandler(this.MessageContentChanged);
            this.txtMsg.TextChanged += new System.EventHandler(this.MessageContentChanged);
            // 
            // Message
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(680, 336);
            this.Controls.Add(this.panelMessage);
            this.Controls.Add(this.panelBtn);
            this.Controls.Add(this.panelAccent);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(520, 260);
            this.Name = "Message";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "提示";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.FrmMessage_FormClosed);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Message_KeyDown);
            this.panelBtn.ResumeLayout(false);
            this.flowButtons.ResumeLayout(false);
            this.flowButtons.PerformLayout();
            this.panelMessage.ResumeLayout(false);
            this.contentLayout.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel panelAccent;
        private System.Windows.Forms.Panel panelBtn;
        private System.Windows.Forms.FlowLayoutPanel flowButtons;
        private System.Windows.Forms.Button btn1;
        private System.Windows.Forms.Button btn2;
        private System.Windows.Forms.Button btn3;
        private System.Windows.Forms.Panel panelMessage;
        private System.Windows.Forms.TableLayoutPanel contentLayout;
        private System.Windows.Forms.Label lblCaption;
        public System.Windows.Forms.RichTextBox txtMsg;
    }
}
