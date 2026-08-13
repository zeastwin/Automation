namespace Automation.Hmi
{
    partial class LegacyVideoPage
    {
        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this.videoLayout = new System.Windows.Forms.TableLayoutPanel();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.channelLayout1 = new System.Windows.Forms.TableLayoutPanel();
            this.previewBox1 = new System.Windows.Forms.PictureBox();
            this.deviceBar1 = new System.Windows.Forms.TableLayoutPanel();
            this.deviceLabel1 = new System.Windows.Forms.Label();
            this.deviceSelector1 = new System.Windows.Forms.ComboBox();
            this.startButton1 = new System.Windows.Forms.Button();
            this.stopButton1 = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.channelLayout2 = new System.Windows.Forms.TableLayoutPanel();
            this.previewBox2 = new System.Windows.Forms.PictureBox();
            this.deviceBar2 = new System.Windows.Forms.TableLayoutPanel();
            this.deviceLabel2 = new System.Windows.Forms.Label();
            this.deviceSelector2 = new System.Windows.Forms.ComboBox();
            this.startButton2 = new System.Windows.Forms.Button();
            this.stopButton2 = new System.Windows.Forms.Button();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.channelLayout3 = new System.Windows.Forms.TableLayoutPanel();
            this.previewBox3 = new System.Windows.Forms.PictureBox();
            this.deviceBar3 = new System.Windows.Forms.TableLayoutPanel();
            this.deviceLabel3 = new System.Windows.Forms.Label();
            this.deviceSelector3 = new System.Windows.Forms.ComboBox();
            this.startButton3 = new System.Windows.Forms.Button();
            this.stopButton3 = new System.Windows.Forms.Button();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.channelLayout4 = new System.Windows.Forms.TableLayoutPanel();
            this.previewBox4 = new System.Windows.Forms.PictureBox();
            this.deviceBar4 = new System.Windows.Forms.TableLayoutPanel();
            this.deviceLabel4 = new System.Windows.Forms.Label();
            this.deviceSelector4 = new System.Windows.Forms.ComboBox();
            this.startButton4 = new System.Windows.Forms.Button();
            this.stopButton4 = new System.Windows.Forms.Button();
            this.videoLayout.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.channelLayout1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.previewBox1)).BeginInit();
            this.deviceBar1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.channelLayout2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.previewBox2)).BeginInit();
            this.deviceBar2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.channelLayout3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.previewBox3)).BeginInit();
            this.deviceBar3.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.channelLayout4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.previewBox4)).BeginInit();
            this.deviceBar4.SuspendLayout();
            this.SuspendLayout();
            //
            // videoLayout
            //
            this.videoLayout.ColumnCount = 2;
            this.videoLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.videoLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.videoLayout.Controls.Add(this.groupBox1, 0, 0);
            this.videoLayout.Controls.Add(this.groupBox2, 1, 0);
            this.videoLayout.Controls.Add(this.groupBox3, 0, 1);
            this.videoLayout.Controls.Add(this.groupBox4, 1, 1);
            this.videoLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.videoLayout.Location = new System.Drawing.Point(0, 0);
            this.videoLayout.Name = "videoLayout";
            this.videoLayout.RowCount = 2;
            this.videoLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.videoLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.videoLayout.Size = new System.Drawing.Size(1028, 604);
            this.videoLayout.TabIndex = 0;
            //
            // groupBox1
            //
            this.groupBox1.Controls.Add(this.channelLayout1);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox1.Location = new System.Drawing.Point(3, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(508, 296);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Video1";
            //
            // channelLayout1
            //
            this.channelLayout1.ColumnCount = 1;
            this.channelLayout1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout1.Controls.Add(this.previewBox1, 0, 0);
            this.channelLayout1.Controls.Add(this.deviceBar1, 0, 1);
            this.channelLayout1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.channelLayout1.Location = new System.Drawing.Point(3, 17);
            this.channelLayout1.Name = "channelLayout1";
            this.channelLayout1.RowCount = 2;
            this.channelLayout1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F));
            this.channelLayout1.Size = new System.Drawing.Size(502, 276);
            this.channelLayout1.TabIndex = 0;
            //
            // previewBox1
            //
            this.previewBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.previewBox1.Location = new System.Drawing.Point(3, 3);
            this.previewBox1.Name = "previewBox1";
            this.previewBox1.Size = new System.Drawing.Size(496, 235);
            this.previewBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.previewBox1.TabIndex = 0;
            this.previewBox1.TabStop = false;
            this.previewBox1.DoubleClick += new System.EventHandler(this.Preview_DoubleClick);
            //
            // deviceBar1
            //
            this.deviceBar1.ColumnCount = 4;
            this.deviceBar1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 61F));
            this.deviceBar1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar1.Controls.Add(this.deviceLabel1, 0, 0);
            this.deviceBar1.Controls.Add(this.deviceSelector1, 1, 0);
            this.deviceBar1.Controls.Add(this.startButton1, 2, 0);
            this.deviceBar1.Controls.Add(this.stopButton1, 3, 0);
            this.deviceBar1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceBar1.Name = "deviceBar1";
            this.deviceBar1.RowCount = 1;
            this.deviceBar1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.deviceLabel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel1.Name = "deviceLabel1";
            this.deviceLabel1.Text = "视频设备";
            this.deviceLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.deviceSelector1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceSelector1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.deviceSelector1.Name = "deviceSelector1";
            this.deviceSelector1.Tag = 1;
            this.deviceSelector1.DropDown += new System.EventHandler(this.DeviceSelector_DropDown);
            this.deviceSelector1.SelectionChangeCommitted += new System.EventHandler(this.DeviceSelector_Commit);
            this.startButton1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startButton1.Name = "startButton1";
            this.startButton1.Tag = 1;
            this.startButton1.Text = "开始";
            this.startButton1.UseVisualStyleBackColor = true;
            this.startButton1.Click += new System.EventHandler(this.StartButton_Click);
            this.stopButton1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stopButton1.Name = "stopButton1";
            this.stopButton1.Tag = 1;
            this.stopButton1.Text = "停止";
            this.stopButton1.UseVisualStyleBackColor = true;
            this.stopButton1.Click += new System.EventHandler(this.StopButton_Click);
            //
            // groupBox2
            //
            this.groupBox2.Controls.Add(this.channelLayout2);
            this.groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox2.Location = new System.Drawing.Point(517, 3);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(508, 296);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Video2";
            this.channelLayout2.ColumnCount = 1;
            this.channelLayout2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout2.Controls.Add(this.previewBox2, 0, 0);
            this.channelLayout2.Controls.Add(this.deviceBar2, 0, 1);
            this.channelLayout2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.channelLayout2.Location = new System.Drawing.Point(3, 17);
            this.channelLayout2.Name = "channelLayout2";
            this.channelLayout2.RowCount = 2;
            this.channelLayout2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F));
            this.channelLayout2.Size = new System.Drawing.Size(502, 276);
            this.previewBox2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.previewBox2.Name = "previewBox2";
            this.previewBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.previewBox2.TabStop = false;
            this.previewBox2.DoubleClick += new System.EventHandler(this.Preview_DoubleClick);
            this.deviceBar2.ColumnCount = 4;
            this.deviceBar2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 61F));
            this.deviceBar2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar2.Controls.Add(this.deviceLabel2, 0, 0);
            this.deviceBar2.Controls.Add(this.deviceSelector2, 1, 0);
            this.deviceBar2.Controls.Add(this.startButton2, 2, 0);
            this.deviceBar2.Controls.Add(this.stopButton2, 3, 0);
            this.deviceBar2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel2.Text = "视频设备";
            this.deviceLabel2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.deviceSelector2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceSelector2.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.deviceSelector2.Tag = 2;
            this.deviceSelector2.DropDown += new System.EventHandler(this.DeviceSelector_DropDown);
            this.deviceSelector2.SelectionChangeCommitted += new System.EventHandler(this.DeviceSelector_Commit);
            this.startButton2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startButton2.Tag = 2;
            this.startButton2.Text = "开始";
            this.startButton2.UseVisualStyleBackColor = true;
            this.startButton2.Click += new System.EventHandler(this.StartButton_Click);
            this.stopButton2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stopButton2.Tag = 2;
            this.stopButton2.Text = "停止";
            this.stopButton2.UseVisualStyleBackColor = true;
            this.stopButton2.Click += new System.EventHandler(this.StopButton_Click);
            //
            // groupBox3
            //
            this.groupBox3.Controls.Add(this.channelLayout3);
            this.groupBox3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox3.Location = new System.Drawing.Point(3, 305);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(508, 296);
            this.groupBox3.TabIndex = 2;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Video3";
            this.channelLayout3.ColumnCount = 1;
            this.channelLayout3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout3.Controls.Add(this.previewBox3, 0, 0);
            this.channelLayout3.Controls.Add(this.deviceBar3, 0, 1);
            this.channelLayout3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.channelLayout3.Location = new System.Drawing.Point(3, 17);
            this.channelLayout3.Name = "channelLayout3";
            this.channelLayout3.RowCount = 2;
            this.channelLayout3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F));
            this.channelLayout3.Size = new System.Drawing.Size(502, 276);
            this.previewBox3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.previewBox3.Name = "previewBox3";
            this.previewBox3.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.previewBox3.TabStop = false;
            this.previewBox3.DoubleClick += new System.EventHandler(this.Preview_DoubleClick);
            this.deviceBar3.ColumnCount = 4;
            this.deviceBar3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 61F));
            this.deviceBar3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar3.Controls.Add(this.deviceLabel3, 0, 0);
            this.deviceBar3.Controls.Add(this.deviceSelector3, 1, 0);
            this.deviceBar3.Controls.Add(this.startButton3, 2, 0);
            this.deviceBar3.Controls.Add(this.stopButton3, 3, 0);
            this.deviceBar3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel3.Text = "视频设备";
            this.deviceLabel3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.deviceSelector3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceSelector3.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.deviceSelector3.Tag = 3;
            this.deviceSelector3.DropDown += new System.EventHandler(this.DeviceSelector_DropDown);
            this.deviceSelector3.SelectionChangeCommitted += new System.EventHandler(this.DeviceSelector_Commit);
            this.startButton3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startButton3.Tag = 3;
            this.startButton3.Text = "开始";
            this.startButton3.UseVisualStyleBackColor = true;
            this.startButton3.Click += new System.EventHandler(this.StartButton_Click);
            this.stopButton3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stopButton3.Tag = 3;
            this.stopButton3.Text = "停止";
            this.stopButton3.UseVisualStyleBackColor = true;
            this.stopButton3.Click += new System.EventHandler(this.StopButton_Click);
            //
            // groupBox4
            //
            this.groupBox4.Controls.Add(this.channelLayout4);
            this.groupBox4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox4.Location = new System.Drawing.Point(517, 305);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(508, 296);
            this.groupBox4.TabIndex = 3;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Video4";
            this.channelLayout4.ColumnCount = 1;
            this.channelLayout4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout4.Controls.Add(this.previewBox4, 0, 0);
            this.channelLayout4.Controls.Add(this.deviceBar4, 0, 1);
            this.channelLayout4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.channelLayout4.Location = new System.Drawing.Point(3, 17);
            this.channelLayout4.Name = "channelLayout4";
            this.channelLayout4.RowCount = 2;
            this.channelLayout4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.channelLayout4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F));
            this.channelLayout4.Size = new System.Drawing.Size(502, 276);
            this.previewBox4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.previewBox4.Name = "previewBox4";
            this.previewBox4.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.previewBox4.TabStop = false;
            this.previewBox4.DoubleClick += new System.EventHandler(this.Preview_DoubleClick);
            this.deviceBar4.ColumnCount = 4;
            this.deviceBar4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 61F));
            this.deviceBar4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 13F));
            this.deviceBar4.Controls.Add(this.deviceLabel4, 0, 0);
            this.deviceBar4.Controls.Add(this.deviceSelector4, 1, 0);
            this.deviceBar4.Controls.Add(this.startButton4, 2, 0);
            this.deviceBar4.Controls.Add(this.stopButton4, 3, 0);
            this.deviceBar4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceLabel4.Text = "视频设备";
            this.deviceLabel4.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.deviceSelector4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceSelector4.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.deviceSelector4.Tag = 4;
            this.deviceSelector4.DropDown += new System.EventHandler(this.DeviceSelector_DropDown);
            this.deviceSelector4.SelectionChangeCommitted += new System.EventHandler(this.DeviceSelector_Commit);
            this.startButton4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.startButton4.Tag = 4;
            this.startButton4.Text = "开始";
            this.startButton4.UseVisualStyleBackColor = true;
            this.startButton4.Click += new System.EventHandler(this.StartButton_Click);
            this.stopButton4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stopButton4.Tag = 4;
            this.stopButton4.Text = "停止";
            this.stopButton4.UseVisualStyleBackColor = true;
            this.stopButton4.Click += new System.EventHandler(this.StopButton_Click);
            //
            // LegacyVideoPage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1028, 604);
            this.Controls.Add(this.videoLayout);
            this.Font = new System.Drawing.Font("宋体", 9F);
            this.Name = "LegacyVideoPage";
            this.Text = "UI_CameraPage";
            this.videoLayout.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.channelLayout1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.previewBox1)).EndInit();
            this.deviceBar1.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.channelLayout2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.previewBox2)).EndInit();
            this.deviceBar2.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.channelLayout3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.previewBox3)).EndInit();
            this.deviceBar3.ResumeLayout(false);
            this.groupBox4.ResumeLayout(false);
            this.channelLayout4.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.previewBox4)).EndInit();
            this.deviceBar4.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel videoLayout;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.TableLayoutPanel channelLayout1;
        private System.Windows.Forms.TableLayoutPanel channelLayout2;
        private System.Windows.Forms.TableLayoutPanel channelLayout3;
        private System.Windows.Forms.TableLayoutPanel channelLayout4;
        private System.Windows.Forms.PictureBox previewBox1;
        private System.Windows.Forms.PictureBox previewBox2;
        private System.Windows.Forms.PictureBox previewBox3;
        private System.Windows.Forms.PictureBox previewBox4;
        private System.Windows.Forms.TableLayoutPanel deviceBar1;
        private System.Windows.Forms.TableLayoutPanel deviceBar2;
        private System.Windows.Forms.TableLayoutPanel deviceBar3;
        private System.Windows.Forms.TableLayoutPanel deviceBar4;
        private System.Windows.Forms.Label deviceLabel1;
        private System.Windows.Forms.Label deviceLabel2;
        private System.Windows.Forms.Label deviceLabel3;
        private System.Windows.Forms.Label deviceLabel4;
        private System.Windows.Forms.ComboBox deviceSelector1;
        private System.Windows.Forms.ComboBox deviceSelector2;
        private System.Windows.Forms.ComboBox deviceSelector3;
        private System.Windows.Forms.ComboBox deviceSelector4;
        private System.Windows.Forms.Button startButton1;
        private System.Windows.Forms.Button startButton2;
        private System.Windows.Forms.Button startButton3;
        private System.Windows.Forms.Button startButton4;
        private System.Windows.Forms.Button stopButton1;
        private System.Windows.Forms.Button stopButton2;
        private System.Windows.Forms.Button stopButton3;
        private System.Windows.Forms.Button stopButton4;
    }
}
