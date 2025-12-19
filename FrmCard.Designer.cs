namespace Automation
{
    partial class FrmCard
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
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.AddCard = new System.Windows.Forms.ToolStripMenuItem();
            this.Modify = new System.Windows.Forms.ToolStripMenuItem();
            this.Remove = new System.Windows.Forms.ToolStripMenuItem();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.treeView2 = new System.Windows.Forms.TreeView();
            this.contextMenuStrip2 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.AddStation = new System.Windows.Forms.ToolStripMenuItem();
            this.ModifyStation = new System.Windows.Forms.ToolStripMenuItem();
            this.RemoveStation = new System.Windows.Forms.ToolStripMenuItem();
            this.groupBox1.SuspendLayout();
            this.contextMenuStrip1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.contextMenuStrip2.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.treeView1);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(366, 264);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "卡配置";
            // 
            // treeView1
            // 
            this.treeView1.ContextMenuStrip = this.contextMenuStrip1;
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeView1.FullRowSelect = true;
            this.treeView1.Location = new System.Drawing.Point(3, 17);
            this.treeView1.Name = "treeView1";
            this.treeView1.ShowLines = false;
            this.treeView1.Size = new System.Drawing.Size(360, 244);
            this.treeView1.TabIndex = 1;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            this.treeView1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.treeView1_MouseDown);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddCard,
            this.Modify,
            this.Remove});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(101, 70);
            // 
            // AddCard
            // 
            this.AddCard.Name = "AddCard";
            this.AddCard.Size = new System.Drawing.Size(100, 22);
            this.AddCard.Text = "新建";
            this.AddCard.Click += new System.EventHandler(this.AddCard_Click);
            // 
            // Modify
            // 
            this.Modify.Name = "Modify";
            this.Modify.Size = new System.Drawing.Size(100, 22);
            this.Modify.Text = "修改";
            this.Modify.Click += new System.EventHandler(this.Modify_Click);
            // 
            // Remove
            // 
            this.Remove.Name = "Remove";
            this.Remove.Size = new System.Drawing.Size(100, 22);
            this.Remove.Text = "删除";
            this.Remove.Click += new System.EventHandler(this.Remove_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.treeView2);
            this.groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox2.Location = new System.Drawing.Point(0, 264);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(366, 345);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "工站配置";
            // 
            // treeView2
            // 
            this.treeView2.ContextMenuStrip = this.contextMenuStrip2;
            this.treeView2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView2.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeView2.FullRowSelect = true;
            this.treeView2.Location = new System.Drawing.Point(3, 17);
            this.treeView2.Name = "treeView2";
            this.treeView2.ShowLines = false;
            this.treeView2.Size = new System.Drawing.Size(360, 325);
            this.treeView2.TabIndex = 1;
            this.treeView2.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView2_AfterSelect);
            this.treeView2.MouseDown += new System.Windows.Forms.MouseEventHandler(this.treeView2_MouseDown);
            // 
            // contextMenuStrip2
            // 
            this.contextMenuStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddStation,
            this.ModifyStation,
            this.RemoveStation});
            this.contextMenuStrip2.Name = "contextMenuStrip2";
            this.contextMenuStrip2.Size = new System.Drawing.Size(101, 70);
            // 
            // AddStation
            // 
            this.AddStation.Name = "AddStation";
            this.AddStation.Size = new System.Drawing.Size(100, 22);
            this.AddStation.Text = "新建";
            this.AddStation.Click += new System.EventHandler(this.AddStation_Click);
            // 
            // ModifyStation
            // 
            this.ModifyStation.Name = "ModifyStation";
            this.ModifyStation.Size = new System.Drawing.Size(100, 22);
            this.ModifyStation.Text = "修改";
            this.ModifyStation.Click += new System.EventHandler(this.ModifyStation_Click);
            // 
            // RemoveStation
            // 
            this.RemoveStation.Name = "RemoveStation";
            this.RemoveStation.Size = new System.Drawing.Size(100, 22);
            this.RemoveStation.Text = "删除";
            this.RemoveStation.Click += new System.EventHandler(this.RemoveStation_Click);
            // 
            // FrmCard
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(366, 609);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Name = "FrmCard";
            this.Text = "FrmCard";
            this.Load += new System.EventHandler(this.FrmCard_Load);
            this.groupBox1.ResumeLayout(false);
            this.contextMenuStrip1.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.contextMenuStrip2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        public System.Windows.Forms.TreeView treeView1;
        public System.Windows.Forms.TreeView treeView2;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem AddCard;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip2;
        private System.Windows.Forms.ToolStripMenuItem Modify;
        private System.Windows.Forms.ToolStripMenuItem Remove;
        private System.Windows.Forms.ToolStripMenuItem AddStation;
        private System.Windows.Forms.ToolStripMenuItem ModifyStation;
        private System.Windows.Forms.ToolStripMenuItem RemoveStation;
    }
}