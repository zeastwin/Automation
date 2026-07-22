namespace Automation
{
    partial class FrmMenu
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
            this.Io_Page = new System.Windows.Forms.Button();
            this.process_Page = new System.Windows.Forms.Button();
            this.station_Page = new System.Windows.Forms.Button();
            this.communication_Page = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.Card_Page = new System.Windows.Forms.Button();
            this.aiAssistant_Page = new System.Windows.Forms.Button();
            this.valueDebug_Page = new System.Windows.Forms.Button();
            this.value_Page = new System.Windows.Forms.Button();
            this.Plc_Page = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // Io_Page
            // 
            this.Io_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.Io_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.Io_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.Io_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.Io_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.Io_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Io_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.Io_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.Io_Page.Location = new System.Drawing.Point(286, 0);
            this.Io_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 3);
            this.Io_Page.Name = "Io_Page";
            this.Io_Page.Size = new System.Drawing.Size(143, 96);
            this.Io_Page.TabIndex = 9;
            this.Io_Page.Text = "IO调试";
            this.Io_Page.UseVisualStyleBackColor = false;
            this.Io_Page.Click += new System.EventHandler(this.Io_Page_Click);
            // 
            // process_Page
            // 
            this.process_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.process_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.process_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.process_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.process_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.process_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.process_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.process_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.process_Page.Location = new System.Drawing.Point(0, 0);
            this.process_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 100);
            this.process_Page.Name = "process_Page";
            this.process_Page.Size = new System.Drawing.Size(143, 96);
            this.process_Page.TabIndex = 6;
            this.process_Page.Text = "流程";
            this.process_Page.UseVisualStyleBackColor = false;
            this.process_Page.Click += new System.EventHandler(this.process_Page_Click);
            // 
            // station_Page
            // 
            this.station_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.station_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.station_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.station_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.station_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.station_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.station_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.station_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.station_Page.Location = new System.Drawing.Point(143, 0);
            this.station_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 3);
            this.station_Page.Name = "station_Page";
            this.station_Page.Size = new System.Drawing.Size(143, 96);
            this.station_Page.TabIndex = 10;
            this.station_Page.Text = "工站";
            this.station_Page.UseVisualStyleBackColor = false;
            this.station_Page.Click += new System.EventHandler(this.station_Page_Click);
            // 
            // communication_Page
            // 
            this.communication_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.communication_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.communication_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.communication_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.communication_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.communication_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.communication_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.communication_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.communication_Page.Location = new System.Drawing.Point(429, 0);
            this.communication_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 3);
            this.communication_Page.Name = "communication_Page";
            this.communication_Page.Size = new System.Drawing.Size(143, 96);
            this.communication_Page.TabIndex = 11;
            this.communication_Page.Text = "通讯";
            this.communication_Page.UseVisualStyleBackColor = false;
            this.communication_Page.Click += new System.EventHandler(this.communication_Page_Click);
            // 
            // Plc_Page
            // 
            this.Plc_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.Plc_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.Plc_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.Plc_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.Plc_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.Plc_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Plc_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.Plc_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.Plc_Page.Location = new System.Drawing.Point(572, 0);
            this.Plc_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 3);
            this.Plc_Page.Name = "Plc_Page";
            this.Plc_Page.Size = new System.Drawing.Size(143, 96);
            this.Plc_Page.TabIndex = 16;
            this.Plc_Page.Text = "PLC";
            this.Plc_Page.UseVisualStyleBackColor = false;
            this.Plc_Page.Click += new System.EventHandler(this.Plc_Page_Click);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.Card_Page);
            this.panel1.Controls.Add(this.aiAssistant_Page);
            this.panel1.Controls.Add(this.valueDebug_Page);
            this.panel1.Controls.Add(this.Plc_Page);
            this.panel1.Controls.Add(this.communication_Page);
            this.panel1.Controls.Add(this.Io_Page);
            this.panel1.Controls.Add(this.value_Page);
            this.panel1.Controls.Add(this.station_Page);
            this.panel1.Controls.Add(this.process_Page);
            this.panel1.AutoScroll = true;
            this.panel1.AutoScrollMinSize = new System.Drawing.Size(1287, 0);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1205, 96);
            this.panel1.TabIndex = 12;
            // 
            // Card_Page
            // 
            this.Card_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.Card_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.Card_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.Card_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.Card_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.Card_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Card_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.Card_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.Card_Page.Location = new System.Drawing.Point(715, 0);
            this.Card_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 100);
            this.Card_Page.Name = "Card_Page";
            this.Card_Page.Size = new System.Drawing.Size(143, 96);
            this.Card_Page.TabIndex = 12;
            this.Card_Page.Text = "控制卡配置";
            this.Card_Page.UseVisualStyleBackColor = false;
            this.Card_Page.Click += new System.EventHandler(this.Card_Page_Click);
            // 
            // aiAssistant_Page
            // 
            this.aiAssistant_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.aiAssistant_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.aiAssistant_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.aiAssistant_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.aiAssistant_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.aiAssistant_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.aiAssistant_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.aiAssistant_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.aiAssistant_Page.Location = new System.Drawing.Point(858, 0);
            this.aiAssistant_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 100);
            this.aiAssistant_Page.Name = "aiAssistant_Page";
            this.aiAssistant_Page.Size = new System.Drawing.Size(143, 96);
            this.aiAssistant_Page.TabIndex = 17;
            this.aiAssistant_Page.Text = "AI助手";
            this.aiAssistant_Page.UseVisualStyleBackColor = false;
            this.aiAssistant_Page.Click += new System.EventHandler(this.aiAssistant_Page_Click);
            // 
            // valueDebug_Page
            // 
            this.valueDebug_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.valueDebug_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.valueDebug_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.valueDebug_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.valueDebug_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.valueDebug_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.valueDebug_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.valueDebug_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.valueDebug_Page.Location = new System.Drawing.Point(715, 0);
            this.valueDebug_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 100);
            this.valueDebug_Page.Name = "valueDebug_Page";
            this.valueDebug_Page.Size = new System.Drawing.Size(143, 96);
            this.valueDebug_Page.TabIndex = 14;
            this.valueDebug_Page.Text = "变量调试";
            this.valueDebug_Page.UseVisualStyleBackColor = false;
            this.valueDebug_Page.Click += new System.EventHandler(this.valueDebug_Page_Click);
            // 
            // value_Page
            // 
            this.value_Page.BackColor = global::Automation.UiPalette.HmiSection;
            this.value_Page.Dock = System.Windows.Forms.DockStyle.Left;
            this.value_Page.FlatAppearance.BorderColor = global::Automation.UiPalette.SurfaceStrong;
            this.value_Page.FlatAppearance.MouseDownBackColor = global::Automation.UiPalette.SurfaceStrong;
            this.value_Page.FlatAppearance.MouseOverBackColor = global::Automation.UiPalette.NavigationHover;
            this.value_Page.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.value_Page.Font = new System.Drawing.Font("黑体", 14.75F);
            this.value_Page.ForeColor = global::Automation.UiPalette.NavigationText;
            this.value_Page.Location = new System.Drawing.Point(572, 0);
            this.value_Page.Margin = new System.Windows.Forms.Padding(3, 100, 3, 100);
            this.value_Page.Name = "value_Page";
            this.value_Page.Size = new System.Drawing.Size(143, 96);
            this.value_Page.TabIndex = 7;
            this.value_Page.Text = "变量";
            this.value_Page.UseVisualStyleBackColor = false;
            this.value_Page.Click += new System.EventHandler(this.value_Page_Click);
            // 
            // FrmMenu
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1205, 96);
            this.Controls.Add(this.panel1);
            this.Name = "FrmMenu";
            this.Text = "FrmMenu";
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Io_Page;
        private System.Windows.Forms.Button station_Page;
        private System.Windows.Forms.Button communication_Page;
        private System.Windows.Forms.Button process_Page;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button Card_Page;
        private System.Windows.Forms.Button aiAssistant_Page;
        private System.Windows.Forms.Button valueDebug_Page;
        private System.Windows.Forms.Button value_Page;
        private System.Windows.Forms.Button Plc_Page;
    }
}
