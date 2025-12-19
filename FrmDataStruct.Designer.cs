namespace Automation
{
    partial class FrmDataStruct
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
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.AddDataStruct = new System.Windows.Forms.ToolStripMenuItem();
            this.ModifyDataStruct = new System.Windows.Forms.ToolStripMenuItem();
            this.RemoveDataStruct = new System.Windows.Forms.ToolStripMenuItem();
            this.panel2 = new System.Windows.Forms.Panel();
            this.btnTrack = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.contextMenuStrip2 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.NewItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ModifyItem = new System.Windows.Forms.ToolStripMenuItem();
            this.RemoveItem = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuStrip1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.contextMenuStrip2.SuspendLayout();
            this.SuspendLayout();
            // 
            // treeView1
            // 
            this.treeView1.ContextMenuStrip = this.contextMenuStrip1;
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Left;
            this.treeView1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeView1.FullRowSelect = true;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.ShowLines = false;
            this.treeView1.Size = new System.Drawing.Size(197, 705);
            this.treeView1.TabIndex = 3;
            this.treeView1.BeforeSelect += new System.Windows.Forms.TreeViewCancelEventHandler(this.treeView1_BeforeSelect);
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            this.treeView1.NodeMouseDoubleClick += new System.Windows.Forms.TreeNodeMouseClickEventHandler(this.treeView1_NodeMouseDoubleClick);
            this.treeView1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.treeView1_MouseDown);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddDataStruct,
            this.ModifyDataStruct,
            this.RemoveDataStruct});
            this.contextMenuStrip1.Name = "contextMenuStrip2";
            this.contextMenuStrip1.Size = new System.Drawing.Size(101, 70);
            // 
            // AddDataStruct
            // 
            this.AddDataStruct.Name = "AddDataStruct";
            this.AddDataStruct.Size = new System.Drawing.Size(100, 22);
            this.AddDataStruct.Text = "新建";
            this.AddDataStruct.Click += new System.EventHandler(this.AddDataStruct_Click);
            // 
            // ModifyDataStruct
            // 
            this.ModifyDataStruct.Name = "ModifyDataStruct";
            this.ModifyDataStruct.Size = new System.Drawing.Size(100, 22);
            this.ModifyDataStruct.Text = "修改";
            this.ModifyDataStruct.Click += new System.EventHandler(this.ModifyDataStruct_Click);
            // 
            // RemoveDataStruct
            // 
            this.RemoveDataStruct.Name = "RemoveDataStruct";
            this.RemoveDataStruct.Size = new System.Drawing.Size(100, 22);
            this.RemoveDataStruct.Text = "删除";
            this.RemoveDataStruct.Click += new System.EventHandler(this.RemoveDataStruct_Click);
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.panel2.Controls.Add(this.btnTrack);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel2.Location = new System.Drawing.Point(197, 0);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(998, 45);
            this.panel2.TabIndex = 5;
            // 
            // btnTrack
            // 
            this.btnTrack.BackColor = System.Drawing.Color.WhiteSmoke;
            this.btnTrack.FlatAppearance.BorderColor = System.Drawing.Color.White;
            this.btnTrack.FlatAppearance.BorderSize = 2;
            this.btnTrack.FlatAppearance.MouseDownBackColor = System.Drawing.Color.White;
            this.btnTrack.FlatAppearance.MouseOverBackColor = System.Drawing.Color.White;
            this.btnTrack.Font = new System.Drawing.Font("黑体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnTrack.Location = new System.Drawing.Point(6, 7);
            this.btnTrack.Name = "btnTrack";
            this.btnTrack.Size = new System.Drawing.Size(78, 35);
            this.btnTrack.TabIndex = 96;
            this.btnTrack.Text = "监控";
            this.btnTrack.UseVisualStyleBackColor = false;
            this.btnTrack.Click += new System.EventHandler(this.btnTrack_Click);
            // 
            // panel1
            // 
            this.panel1.AutoScroll = true;
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(197, 45);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(998, 660);
            this.panel1.TabIndex = 6;
            // 
            // contextMenuStrip2
            // 
            this.contextMenuStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.NewItem,
            this.ModifyItem,
            this.RemoveItem});
            this.contextMenuStrip2.Name = "contextMenuStrip2";
            this.contextMenuStrip2.Size = new System.Drawing.Size(101, 70);
            // 
            // NewItem
            // 
            this.NewItem.Name = "NewItem";
            this.NewItem.Size = new System.Drawing.Size(180, 22);
            this.NewItem.Text = "新建";
            this.NewItem.Click += new System.EventHandler(this.NewItem_Click);
            // 
            // ModifyItem
            // 
            this.ModifyItem.Name = "ModifyItem";
            this.ModifyItem.Size = new System.Drawing.Size(180, 22);
            this.ModifyItem.Text = "修改";
            this.ModifyItem.Click += new System.EventHandler(this.ModifyItem_Click);
            // 
            // RemoveItem
            // 
            this.RemoveItem.Name = "RemoveItem";
            this.RemoveItem.Size = new System.Drawing.Size(180, 22);
            this.RemoveItem.Text = "删除";
            this.RemoveItem.Click += new System.EventHandler(this.RemoveItem_Click);
            // 
            // FrmDataStruct
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1195, 705);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.treeView1);
            this.Name = "FrmDataStruct";
            this.Text = "数据结构";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmDataStruct_FormClosing);
            this.Load += new System.EventHandler(this.FrmDataStruct_Load);
            this.contextMenuStrip1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.contextMenuStrip2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem AddDataStruct;
        private System.Windows.Forms.ToolStripMenuItem ModifyDataStruct;
        private System.Windows.Forms.ToolStripMenuItem RemoveDataStruct;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Panel panel1;
        public System.Windows.Forms.Button btnTrack;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip2;
        private System.Windows.Forms.ToolStripMenuItem NewItem;
        private System.Windows.Forms.ToolStripMenuItem ModifyItem;
        private System.Windows.Forms.ToolStripMenuItem RemoveItem;
    }
}