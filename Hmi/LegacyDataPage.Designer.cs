namespace Automation.Hmi
{
    partial class LegacyDataPage
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
            this.dataTabs = new System.Windows.Forms.TabControl();
            this.dataViewTab = new System.Windows.Forms.TabPage();
            this.dataView = new Automation.Hmi.LegacyDataViewControl();
            this.productDataTab = new System.Windows.Forms.TabPage();
            this.productData = new Automation.Hmi.LegacyProductDataControl();
            this.dataTabs.SuspendLayout();
            this.dataViewTab.SuspendLayout();
            this.productDataTab.SuspendLayout();
            this.SuspendLayout();
            //
            // dataTabs
            //
            this.dataTabs.Controls.Add(this.dataViewTab);
            this.dataTabs.Controls.Add(this.productDataTab);
            this.dataTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataTabs.Font = new System.Drawing.Font("宋体", 12F);
            this.dataTabs.Location = new System.Drawing.Point(0, 0);
            this.dataTabs.Name = "dataTabs";
            this.dataTabs.SelectedIndex = 0;
            this.dataTabs.Size = new System.Drawing.Size(1185, 702);
            this.dataTabs.TabIndex = 0;
            this.dataTabs.SelectedIndexChanged += new System.EventHandler(this.DataTabs_SelectedIndexChanged);
            //
            // dataViewTab
            //
            this.dataViewTab.Controls.Add(this.dataView);
            this.dataViewTab.Location = new System.Drawing.Point(4, 26);
            this.dataViewTab.Name = "dataViewTab";
            this.dataViewTab.Padding = new System.Windows.Forms.Padding(3);
            this.dataViewTab.Size = new System.Drawing.Size(1177, 672);
            this.dataViewTab.Text = "DataView";
            this.dataViewTab.UseVisualStyleBackColor = true;
            this.dataView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataView.Location = new System.Drawing.Point(3, 3);
            this.dataView.Name = "dataView";
            this.dataView.Size = new System.Drawing.Size(1171, 666);
            //
            // productDataTab
            //
            this.productDataTab.Controls.Add(this.productData);
            this.productDataTab.Location = new System.Drawing.Point(4, 26);
            this.productDataTab.Name = "productDataTab";
            this.productDataTab.Padding = new System.Windows.Forms.Padding(3);
            this.productDataTab.Size = new System.Drawing.Size(1177, 672);
            this.productDataTab.Text = "ProductData";
            this.productDataTab.UseVisualStyleBackColor = true;
            this.productData.Dock = System.Windows.Forms.DockStyle.Fill;
            this.productData.Location = new System.Drawing.Point(3, 3);
            this.productData.Name = "productData";
            this.productData.Size = new System.Drawing.Size(1171, 666);
            //
            // LegacyDataPage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1185, 702);
            this.Controls.Add(this.dataTabs);
            this.Name = "LegacyDataPage";
            this.Text = "DataPage";
            this.dataTabs.ResumeLayout(false);
            this.dataViewTab.ResumeLayout(false);
            this.productDataTab.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TabControl dataTabs;
        private System.Windows.Forms.TabPage dataViewTab;
        private System.Windows.Forms.TabPage productDataTab;
        private Automation.Hmi.LegacyDataViewControl dataView;
        private Automation.Hmi.LegacyProductDataControl productData;
    }
}
