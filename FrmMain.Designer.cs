
namespace Automation
{
    partial class FrmMain
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
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
            this.MenuPanel = new System.Windows.Forms.Panel();
            this.state_panel = new System.Windows.Forms.Panel();
            this.main_panel = new System.Windows.Forms.Panel();
            this.DataGrid_panel = new System.Windows.Forms.Panel();
            this.panel_Info = new System.Windows.Forms.Panel();
            this.propertyGrid_panel = new System.Windows.Forms.Panel();
            this.treeView_panel = new System.Windows.Forms.Panel();
            this.ToolBar_panel = new System.Windows.Forms.Panel();
            this.main_panel.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuPanel
            // 
            this.MenuPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.MenuPanel.Location = new System.Drawing.Point(0, 0);
            this.MenuPanel.Name = "MenuPanel";
            this.MenuPanel.Size = new System.Drawing.Size(1584, 83);
            this.MenuPanel.TabIndex = 0;
            // 
            // state_panel
            // 
            this.state_panel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.state_panel.Location = new System.Drawing.Point(0, 898);
            this.state_panel.Name = "state_panel";
            this.state_panel.Size = new System.Drawing.Size(1584, 23);
            this.state_panel.TabIndex = 2;
            // 
            // main_panel
            // 
            this.main_panel.Controls.Add(this.DataGrid_panel);
            this.main_panel.Controls.Add(this.panel_Info);
            this.main_panel.Controls.Add(this.propertyGrid_panel);
            this.main_panel.Controls.Add(this.treeView_panel);
            this.main_panel.Controls.Add(this.ToolBar_panel);
            this.main_panel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.main_panel.Location = new System.Drawing.Point(0, 83);
            this.main_panel.Name = "main_panel";
            this.main_panel.Size = new System.Drawing.Size(1584, 815);
            this.main_panel.TabIndex = 3;
            // 
            // DataGrid_panel
            // 
            this.DataGrid_panel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataGrid_panel.Location = new System.Drawing.Point(235, 43);
            this.DataGrid_panel.Name = "DataGrid_panel";
            this.DataGrid_panel.Size = new System.Drawing.Size(1011, 504);
            this.DataGrid_panel.TabIndex = 9;
            // 
            // panel_Info
            // 
            this.panel_Info.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel_Info.Location = new System.Drawing.Point(235, 547);
            this.panel_Info.Margin = new System.Windows.Forms.Padding(0);
            this.panel_Info.Name = "panel_Info";
            this.panel_Info.Size = new System.Drawing.Size(1011, 268);
            this.panel_Info.TabIndex = 8;
            // 
            // propertyGrid_panel
            // 
            this.propertyGrid_panel.Dock = System.Windows.Forms.DockStyle.Right;
            this.propertyGrid_panel.Location = new System.Drawing.Point(1246, 43);
            this.propertyGrid_panel.Name = "propertyGrid_panel";
            this.propertyGrid_panel.Size = new System.Drawing.Size(338, 772);
            this.propertyGrid_panel.TabIndex = 6;
            // 
            // treeView_panel
            // 
            this.treeView_panel.Dock = System.Windows.Forms.DockStyle.Left;
            this.treeView_panel.Location = new System.Drawing.Point(0, 43);
            this.treeView_panel.Name = "treeView_panel";
            this.treeView_panel.Size = new System.Drawing.Size(235, 772);
            this.treeView_panel.TabIndex = 4;
            // 
            // ToolBar_panel
            // 
            this.ToolBar_panel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ToolBar_panel.Location = new System.Drawing.Point(0, 0);
            this.ToolBar_panel.Name = "ToolBar_panel";
            this.ToolBar_panel.Size = new System.Drawing.Size(1584, 43);
            this.ToolBar_panel.TabIndex = 3;
            // 
            // FrmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1584, 921);
            this.Controls.Add(this.main_panel);
            this.Controls.Add(this.state_panel);
            this.Controls.Add(this.MenuPanel);
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.Name = "FrmMain";
            this.Text = "Automation";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmMain_FormClosing);
            this.Load += new System.EventHandler(this.FrmMain_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FrmMain_KeyDown);
            this.main_panel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel MenuPanel;
        public System.Windows.Forms.Panel main_panel;
        public System.Windows.Forms.Panel propertyGrid_panel;
        public System.Windows.Forms.Panel treeView_panel;
        public System.Windows.Forms.Panel ToolBar_panel;
        public System.Windows.Forms.Panel state_panel;
        public System.Windows.Forms.Panel DataGrid_panel;
        public System.Windows.Forms.Panel panel_Info;
    }
}

