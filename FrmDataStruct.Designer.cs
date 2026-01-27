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
            this.contextMenuRoot = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuAddStruct = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuStruct = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuStructAddItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStructRename = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStructDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuItem = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuItemAddField = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemRename = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuField = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuFieldEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFieldRename = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFieldTypeText = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFieldTypeNumber = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFieldDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuRoot.SuspendLayout();
            this.contextMenuStruct.SuspendLayout();
            this.contextMenuItem.SuspendLayout();
            this.contextMenuField.SuspendLayout();
            this.SuspendLayout();
            // 
            // treeView1
            // 
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.treeView1.FullRowSelect = true;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.ShowLines = true;
            this.treeView1.Size = new System.Drawing.Size(1195, 705);
            this.treeView1.TabIndex = 0;
            this.treeView1.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.treeView1_BeforeExpand);
            this.treeView1.NodeMouseClick += new System.Windows.Forms.TreeNodeMouseClickEventHandler(this.treeView1_NodeMouseClick);
            this.treeView1.NodeMouseDoubleClick += new System.Windows.Forms.TreeNodeMouseClickEventHandler(this.treeView1_NodeMouseDoubleClick);
            this.treeView1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.treeView1_MouseDown);
            // 
            // contextMenuRoot
            // 
            this.contextMenuRoot.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuAddStruct});
            this.contextMenuRoot.Name = "contextMenuRoot";
            this.contextMenuRoot.Size = new System.Drawing.Size(125, 26);
            // 
            // menuAddStruct
            // 
            this.menuAddStruct.Name = "menuAddStruct";
            this.menuAddStruct.Size = new System.Drawing.Size(124, 22);
            this.menuAddStruct.Text = "新建结构体";
            this.menuAddStruct.Click += new System.EventHandler(this.menuAddStruct_Click);
            // 
            // contextMenuStruct
            // 
            this.contextMenuStruct.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuStructAddItem,
            this.menuStructRename,
            this.menuStructDelete});
            this.contextMenuStruct.Name = "contextMenuStruct";
            this.contextMenuStruct.Size = new System.Drawing.Size(137, 70);
            // 
            // menuStructAddItem
            // 
            this.menuStructAddItem.Name = "menuStructAddItem";
            this.menuStructAddItem.Size = new System.Drawing.Size(136, 22);
            this.menuStructAddItem.Text = "新增数据项";
            this.menuStructAddItem.Click += new System.EventHandler(this.menuStructAddItem_Click);
            // 
            // menuStructRename
            // 
            this.menuStructRename.Name = "menuStructRename";
            this.menuStructRename.Size = new System.Drawing.Size(136, 22);
            this.menuStructRename.Text = "重命名结构体";
            this.menuStructRename.Click += new System.EventHandler(this.menuStructRename_Click);
            // 
            // menuStructDelete
            // 
            this.menuStructDelete.Name = "menuStructDelete";
            this.menuStructDelete.Size = new System.Drawing.Size(136, 22);
            this.menuStructDelete.Text = "删除结构体";
            this.menuStructDelete.Click += new System.EventHandler(this.menuStructDelete_Click);
            // 
            // contextMenuItem
            // 
            this.contextMenuItem.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemAddField,
            this.menuItemRename,
            this.menuItemDelete});
            this.contextMenuItem.Name = "contextMenuItem";
            this.contextMenuItem.Size = new System.Drawing.Size(125, 70);
            // 
            // menuItemAddField
            // 
            this.menuItemAddField.Name = "menuItemAddField";
            this.menuItemAddField.Size = new System.Drawing.Size(124, 22);
            this.menuItemAddField.Text = "新增字段";
            this.menuItemAddField.Click += new System.EventHandler(this.menuItemAddField_Click);
            // 
            // menuItemRename
            // 
            this.menuItemRename.Name = "menuItemRename";
            this.menuItemRename.Size = new System.Drawing.Size(124, 22);
            this.menuItemRename.Text = "重命名数据项";
            this.menuItemRename.Click += new System.EventHandler(this.menuItemRename_Click);
            // 
            // menuItemDelete
            // 
            this.menuItemDelete.Name = "menuItemDelete";
            this.menuItemDelete.Size = new System.Drawing.Size(124, 22);
            this.menuItemDelete.Text = "删除数据项";
            this.menuItemDelete.Click += new System.EventHandler(this.menuItemDelete_Click);
            // 
            // contextMenuField
            // 
            this.contextMenuField.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFieldEdit,
            this.menuFieldRename,
            this.menuFieldTypeText,
            this.menuFieldTypeNumber,
            this.menuFieldDelete});
            this.contextMenuField.Name = "contextMenuField";
            this.contextMenuField.Size = new System.Drawing.Size(149, 114);
            // 
            // menuFieldEdit
            // 
            this.menuFieldEdit.Name = "menuFieldEdit";
            this.menuFieldEdit.Size = new System.Drawing.Size(148, 22);
            this.menuFieldEdit.Text = "编辑字段";
            this.menuFieldEdit.Click += new System.EventHandler(this.menuFieldEdit_Click);
            // 
            // menuFieldRename
            // 
            this.menuFieldRename.Name = "menuFieldRename";
            this.menuFieldRename.Size = new System.Drawing.Size(148, 22);
            this.menuFieldRename.Text = "重命名字段";
            this.menuFieldRename.Click += new System.EventHandler(this.menuFieldRename_Click);
            // 
            // menuFieldTypeText
            // 
            this.menuFieldTypeText.Name = "menuFieldTypeText";
            this.menuFieldTypeText.Size = new System.Drawing.Size(148, 22);
            this.menuFieldTypeText.Text = "类型改为 string";
            this.menuFieldTypeText.Click += new System.EventHandler(this.menuFieldTypeText_Click);
            // 
            // menuFieldTypeNumber
            // 
            this.menuFieldTypeNumber.Name = "menuFieldTypeNumber";
            this.menuFieldTypeNumber.Size = new System.Drawing.Size(148, 22);
            this.menuFieldTypeNumber.Text = "类型改为 double";
            this.menuFieldTypeNumber.Click += new System.EventHandler(this.menuFieldTypeNumber_Click);
            // 
            // menuFieldDelete
            // 
            this.menuFieldDelete.Name = "menuFieldDelete";
            this.menuFieldDelete.Size = new System.Drawing.Size(148, 22);
            this.menuFieldDelete.Text = "删除字段";
            this.menuFieldDelete.Click += new System.EventHandler(this.menuFieldDelete_Click);
            // 
            // FrmDataStruct
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1195, 705);
            this.Controls.Add(this.treeView1);
            this.Name = "FrmDataStruct";
            this.Text = "数据结构";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmDataStruct_FormClosing);
            this.Load += new System.EventHandler(this.FrmDataStruct_Load);
            this.contextMenuRoot.ResumeLayout(false);
            this.contextMenuStruct.ResumeLayout(false);
            this.contextMenuItem.ResumeLayout(false);
            this.contextMenuField.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.ContextMenuStrip contextMenuRoot;
        private System.Windows.Forms.ToolStripMenuItem menuAddStruct;
        private System.Windows.Forms.ContextMenuStrip contextMenuStruct;
        private System.Windows.Forms.ToolStripMenuItem menuStructAddItem;
        private System.Windows.Forms.ToolStripMenuItem menuStructRename;
        private System.Windows.Forms.ToolStripMenuItem menuStructDelete;
        private System.Windows.Forms.ContextMenuStrip contextMenuItem;
        private System.Windows.Forms.ToolStripMenuItem menuItemAddField;
        private System.Windows.Forms.ToolStripMenuItem menuItemRename;
        private System.Windows.Forms.ToolStripMenuItem menuItemDelete;
        private System.Windows.Forms.ContextMenuStrip contextMenuField;
        private System.Windows.Forms.ToolStripMenuItem menuFieldEdit;
        private System.Windows.Forms.ToolStripMenuItem menuFieldRename;
        private System.Windows.Forms.ToolStripMenuItem menuFieldTypeText;
        private System.Windows.Forms.ToolStripMenuItem menuFieldTypeNumber;
        private System.Windows.Forms.ToolStripMenuItem menuFieldDelete;
    }
}
