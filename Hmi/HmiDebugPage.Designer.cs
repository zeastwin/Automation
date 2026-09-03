namespace Automation.Hmi
{
    partial class HmiDebugPage
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
            this.debugRoot = new System.Windows.Forms.TableLayoutPanel();
            this.contentGroup = new System.Windows.Forms.GroupBox();
            this.contentLayout = new System.Windows.Forms.TableLayoutPanel();
            this.buttonBar = new System.Windows.Forms.TableLayoutPanel();
            this.buttonMes = new System.Windows.Forms.Button();
            this.buttonPdca = new System.Windows.Forms.Button();
            this.buttonHive = new System.Windows.Forms.Button();
            this.buttonPlc = new System.Windows.Forms.Button();
            this.buttonFingerprint = new System.Windows.Forms.Button();
            this.buttonTools = new System.Windows.Forms.Button();
            this.buttonSet = new System.Windows.Forms.Button();
            this.buttonDatabase = new System.Windows.Forms.Button();
            this.pageHost = new System.Windows.Forms.Panel();
            this.debugRoot.SuspendLayout();
            this.contentGroup.SuspendLayout();
            this.contentLayout.SuspendLayout();
            this.buttonBar.SuspendLayout();
            this.SuspendLayout();
            //
            // debugRoot
            //
            this.debugRoot.BackColor = System.Drawing.Color.White;
            this.debugRoot.ColumnCount = 3;
            this.debugRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.debugRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.debugRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.debugRoot.Controls.Add(this.contentGroup, 1, 1);
            this.debugRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugRoot.Location = new System.Drawing.Point(0, 0);
            this.debugRoot.Name = "debugRoot";
            this.debugRoot.RowCount = 3;
            this.debugRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.debugRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.debugRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.debugRoot.Size = new System.Drawing.Size(1200, 656);
            this.debugRoot.TabIndex = 0;
            //
            // contentGroup
            //
            this.contentGroup.Controls.Add(this.contentLayout);
            this.contentGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentGroup.Location = new System.Drawing.Point(23, 23);
            this.contentGroup.Name = "contentGroup";
            this.contentGroup.Size = new System.Drawing.Size(1154, 610);
            this.contentGroup.TabIndex = 0;
            this.contentGroup.TabStop = false;
            //
            // contentLayout
            //
            this.contentLayout.ColumnCount = 1;
            this.contentLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentLayout.Controls.Add(this.buttonBar, 0, 0);
            this.contentLayout.Controls.Add(this.pageHost, 0, 1);
            this.contentLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentLayout.Location = new System.Drawing.Point(3, 17);
            this.contentLayout.Name = "contentLayout";
            this.contentLayout.RowCount = 2;
            this.contentLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 50F));
            this.contentLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentLayout.Size = new System.Drawing.Size(1148, 590);
            this.contentLayout.TabIndex = 0;
            //
            // buttonBar
            //
            this.buttonBar.ColumnCount = 8;
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 12.5F));
            this.buttonBar.Controls.Add(this.buttonMes, 0, 0);
            this.buttonBar.Controls.Add(this.buttonPdca, 1, 0);
            this.buttonBar.Controls.Add(this.buttonHive, 2, 0);
            this.buttonBar.Controls.Add(this.buttonPlc, 3, 0);
            this.buttonBar.Controls.Add(this.buttonFingerprint, 4, 0);
            this.buttonBar.Controls.Add(this.buttonTools, 5, 0);
            this.buttonBar.Controls.Add(this.buttonSet, 6, 0);
            this.buttonBar.Controls.Add(this.buttonDatabase, 7, 0);
            this.buttonBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonBar.Location = new System.Drawing.Point(3, 3);
            this.buttonBar.Name = "buttonBar";
            this.buttonBar.RowCount = 1;
            this.buttonBar.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.buttonBar.Size = new System.Drawing.Size(1142, 44);
            this.buttonBar.TabIndex = 0;
            //
            // debug buttons
            //
            this.buttonMes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonMes.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonMes.Name = "buttonMes";
            this.buttonMes.Text = "MES";
            this.buttonMes.UseVisualStyleBackColor = false;
            this.buttonPdca.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonPdca.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonPdca.Name = "buttonPdca";
            this.buttonPdca.Text = "PDCA";
            this.buttonPdca.UseVisualStyleBackColor = false;
            this.buttonHive.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonHive.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonHive.Name = "buttonHive";
            this.buttonHive.Text = "Hive";
            this.buttonHive.UseVisualStyleBackColor = false;
            this.buttonPlc.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonPlc.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonPlc.Name = "buttonPlc";
            this.buttonPlc.Text = "PLC";
            this.buttonPlc.UseVisualStyleBackColor = false;
            this.buttonFingerprint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonFingerprint.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonFingerprint.Name = "buttonFingerprint";
            this.buttonFingerprint.Text = "账户登录";
            this.buttonFingerprint.UseVisualStyleBackColor = false;
            this.buttonTools.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonTools.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonTools.Name = "buttonTools";
            this.buttonTools.Text = "Tools";
            this.buttonTools.UseVisualStyleBackColor = false;
            this.buttonSet.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonSet.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonSet.Name = "buttonSet";
            this.buttonSet.Text = "Set";
            this.buttonSet.UseVisualStyleBackColor = false;
            this.buttonDatabase.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonDatabase.Font = new System.Drawing.Font("宋体", 10.5F);
            this.buttonDatabase.Name = "buttonDatabase";
            this.buttonDatabase.Text = "Database";
            this.buttonDatabase.UseVisualStyleBackColor = false;
            //
            // pageHost
            //
            this.pageHost.BackColor = System.Drawing.Color.White;
            this.pageHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageHost.Location = new System.Drawing.Point(3, 53);
            this.pageHost.Name = "pageHost";
            this.pageHost.Size = new System.Drawing.Size(1142, 534);
            this.pageHost.TabIndex = 1;
            //
            // HmiDebugPage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Controls.Add(this.debugRoot);
            this.Font = new System.Drawing.Font("宋体", 9F);
            this.Name = "HmiDebugPage";
            this.Text = "DebugApp";
            this.debugRoot.ResumeLayout(false);
            this.contentGroup.ResumeLayout(false);
            this.contentLayout.ResumeLayout(false);
            this.buttonBar.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel debugRoot;
        private System.Windows.Forms.GroupBox contentGroup;
        private System.Windows.Forms.TableLayoutPanel contentLayout;
        private System.Windows.Forms.TableLayoutPanel buttonBar;
        private System.Windows.Forms.Button buttonMes;
        private System.Windows.Forms.Button buttonPdca;
        private System.Windows.Forms.Button buttonHive;
        private System.Windows.Forms.Button buttonPlc;
        private System.Windows.Forms.Button buttonFingerprint;
        private System.Windows.Forms.Button buttonTools;
        private System.Windows.Forms.Button buttonSet;
        private System.Windows.Forms.Button buttonDatabase;
        private System.Windows.Forms.Panel pageHost;
    }
}
