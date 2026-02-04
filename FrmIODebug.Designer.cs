namespace Automation
{
    partial class FrmIODebug
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmIODebug));
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.tabPage5 = new System.Windows.Forms.TabPage();
            this.tabPage6 = new System.Windows.Forms.TabPage();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.listView4 = new System.Windows.Forms.ListView();
            this.listView3 = new System.Windows.Forms.ListView();
            this.listView5 = new System.Windows.Forms.ListView();
            this.listView2 = new System.Windows.Forms.ListView();
            this.listView1 = new System.Windows.Forms.ListView();
            this.tabControl1.SuspendLayout();
            this.tabPage4.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Controls.Add(this.tabPage5);
            this.tabControl1.Controls.Add(this.tabPage6);
            this.tabControl1.Controls.Add(this.tabPage4);
            resources.ApplyResources(this.tabControl1, "tabControl1");
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.SelectedIndexChanged += new System.EventHandler(this.tabControl1_SelectedIndexChanged);
            // 
            // tabPage1
            // 
            resources.ApplyResources(this.tabPage1, "tabPage1");
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            resources.ApplyResources(this.tabPage2, "tabPage2");
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // tabPage3
            // 
            resources.ApplyResources(this.tabPage3, "tabPage3");
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // tabPage5
            // 
            resources.ApplyResources(this.tabPage5, "tabPage5");
            this.tabPage5.Name = "tabPage5";
            this.tabPage5.UseVisualStyleBackColor = true;
            // 
            // tabPage6
            // 
            resources.ApplyResources(this.tabPage6, "tabPage6");
            this.tabPage6.Name = "tabPage6";
            this.tabPage6.UseVisualStyleBackColor = true;
            // 
            // tabPage4
            // 
            this.tabPage4.Controls.Add(this.groupBox1);
            this.tabPage4.Controls.Add(this.listView2);
            this.tabPage4.Controls.Add(this.listView1);
            resources.ApplyResources(this.tabPage4, "tabPage4");
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.UseVisualStyleBackColor = true;
            // 
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.listView4);
            this.groupBox1.Controls.Add(this.listView3);
            this.groupBox1.Controls.Add(this.listView5);
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // listView4
            // 
            this.listView4.AllowDrop = true;
            this.listView4.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.listView4, "listView4");
            this.listView4.HideSelection = false;
            this.listView4.Name = "listView4";
            this.listView4.UseCompatibleStateImageBehavior = false;
            // 
            // listView3
            // 
            this.listView3.AllowDrop = true;
            this.listView3.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.listView3, "listView3");
            this.listView3.HideSelection = false;
            this.listView3.Name = "listView3";
            this.listView3.UseCompatibleStateImageBehavior = false;
            this.listView3.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.listView3_ItemDrag);
            this.listView3.SelectedIndexChanged += new System.EventHandler(this.listView3_SelectedIndexChanged);
            this.listView3.DragDrop += new System.Windows.Forms.DragEventHandler(this.listView3_DragDrop);
            this.listView3.DragEnter += new System.Windows.Forms.DragEventHandler(this.listView3_DragEnter);
            this.listView3.DragOver += new System.Windows.Forms.DragEventHandler(this.listView3_DragOver);
            // 
            // listView5
            // 
            this.listView5.AllowDrop = true;
            this.listView5.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.listView5, "listView5");
            this.listView5.HideSelection = false;
            this.listView5.Name = "listView5";
            this.listView5.UseCompatibleStateImageBehavior = false;
            this.listView5.ItemChecked += new System.Windows.Forms.ItemCheckedEventHandler(this.listView5_ItemChecked);
            // 
            // listView2
            // 
            this.listView2.AllowDrop = true;
            this.listView2.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.listView2, "listView2");
            this.listView2.HideSelection = false;
            this.listView2.Name = "listView2";
            this.listView2.UseCompatibleStateImageBehavior = false;
            this.listView2.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.listView2_ItemDrag);
            this.listView2.DragDrop += new System.Windows.Forms.DragEventHandler(this.listView2_DragDrop);
            this.listView2.DragEnter += new System.Windows.Forms.DragEventHandler(this.listView2_DragEnter);
            this.listView2.DragOver += new System.Windows.Forms.DragEventHandler(this.listView2_DragOver);
            // 
            // listView1
            // 
            this.listView1.AllowDrop = true;
            this.listView1.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.listView1, "listView1");
            this.listView1.HideSelection = false;
            this.listView1.Name = "listView1";
            this.listView1.UseCompatibleStateImageBehavior = false;
            this.listView1.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.listView1_ItemDrag);
            this.listView1.DragDrop += new System.Windows.Forms.DragEventHandler(this.listView1_DragDrop);
            this.listView1.DragEnter += new System.Windows.Forms.DragEventHandler(this.listView1_DragEnter);
            this.listView1.DragOver += new System.Windows.Forms.DragEventHandler(this.listView1_DragOver);
            // 
            // FrmIODebug
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tabControl1);
            this.Name = "FrmIODebug";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmIODebug_FormClosing);
            this.Load += new System.EventHandler(this.FrmIODebug_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabPage4.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.TabPage tabPage4;
        private System.Windows.Forms.ListView listView1;
        private System.Windows.Forms.ListView listView2;
        private System.Windows.Forms.ListView listView5;
        private System.Windows.Forms.ListView listView3;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ListView listView4;
        private System.Windows.Forms.TabPage tabPage5;
        private System.Windows.Forms.TabPage tabPage6;
    }
}
