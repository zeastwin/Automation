namespace Automation.Peripheral
{
    partial class PeripheralCapacityPage
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
            this.grpCapacity = new System.Windows.Forms.GroupBox();
            this.capacityLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblInputTitle = new System.Windows.Forms.Label();
            this.lblOutputTitle = new System.Windows.Forms.Label();
            this.lblNgTitle = new System.Windows.Forms.Label();
            this.lblOkTitle = new System.Windows.Forms.Label();
            this.lblYieldTitle = new System.Windows.Forms.Label();
            this.lblCycleTitle = new System.Windows.Forms.Label();
            this.lblInputValue = new System.Windows.Forms.Label();
            this.lblOutputValue = new System.Windows.Forms.Label();
            this.lblNgValue = new System.Windows.Forms.Label();
            this.lblOkValue = new System.Windows.Forms.Label();
            this.lblYieldValue = new System.Windows.Forms.Label();
            this.lblCycleValue = new System.Windows.Forms.Label();
            this.btnResetCounter = new System.Windows.Forms.Button();
            this.grpCapacity.SuspendLayout();
            this.capacityLayout.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpCapacity
            // 
            this.grpCapacity.Controls.Add(this.capacityLayout);
            this.grpCapacity.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpCapacity.Location = new System.Drawing.Point(0, 0);
            this.grpCapacity.Margin = new System.Windows.Forms.Padding(18);
            this.grpCapacity.Name = "grpCapacity";
            this.grpCapacity.Padding = new System.Windows.Forms.Padding(16);
            this.grpCapacity.Size = new System.Drawing.Size(1176, 330);
            this.grpCapacity.TabIndex = 0;
            this.grpCapacity.TabStop = false;
            this.grpCapacity.Text = "产能统计";
            // 
            // capacityLayout
            // 
            this.capacityLayout.ColumnCount = 2;
            this.capacityLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.capacityLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.capacityLayout.Controls.Add(this.lblInputTitle, 0, 0);
            this.capacityLayout.Controls.Add(this.lblInputValue, 1, 0);
            this.capacityLayout.Controls.Add(this.lblOutputTitle, 0, 1);
            this.capacityLayout.Controls.Add(this.lblOutputValue, 1, 1);
            this.capacityLayout.Controls.Add(this.lblNgTitle, 0, 2);
            this.capacityLayout.Controls.Add(this.lblNgValue, 1, 2);
            this.capacityLayout.Controls.Add(this.lblOkTitle, 0, 3);
            this.capacityLayout.Controls.Add(this.lblOkValue, 1, 3);
            this.capacityLayout.Controls.Add(this.lblYieldTitle, 0, 4);
            this.capacityLayout.Controls.Add(this.lblYieldValue, 1, 4);
            this.capacityLayout.Controls.Add(this.lblCycleTitle, 0, 5);
            this.capacityLayout.Controls.Add(this.lblCycleValue, 1, 5);
            this.capacityLayout.Controls.Add(this.btnResetCounter, 1, 6);
            this.capacityLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.capacityLayout.Location = new System.Drawing.Point(16, 34);
            this.capacityLayout.Name = "capacityLayout";
            this.capacityLayout.RowCount = 7;
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.capacityLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.capacityLayout.Size = new System.Drawing.Size(1144, 280);
            this.capacityLayout.TabIndex = 0;
            // 
            // capacity labels
            // 
            this.lblInputTitle.Text = "投入总数";
            this.lblOutputTitle.Text = "产出总数";
            this.lblNgTitle.Text = "次品总数";
            this.lblOkTitle.Text = "良品总数";
            this.lblYieldTitle.Text = "良品率";
            this.lblCycleTitle.Text = "周期";
            this.lblInputValue.Text = "--";
            this.lblOutputValue.Text = "--";
            this.lblNgValue.Text = "--";
            this.lblOkValue.Text = "--";
            this.lblYieldValue.Text = "--";
            this.lblCycleValue.Text = "-- s";
            this.lblInputTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblInputTitle.ForeColor = System.Drawing.Color.FromArgb(84, 98, 112);
            this.lblInputTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblOutputTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOutputTitle.ForeColor = System.Drawing.Color.FromArgb(84, 98, 112);
            this.lblOutputTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblNgTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNgTitle.ForeColor = System.Drawing.Color.FromArgb(84, 98, 112);
            this.lblNgTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblOkTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOkTitle.ForeColor = System.Drawing.Color.FromArgb(84, 98, 112);
            this.lblOkTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblYieldTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldTitle.ForeColor = System.Drawing.Color.FromArgb(84, 98, 112);
            this.lblYieldTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblCycleTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleTitle.ForeColor = System.Drawing.Color.FromArgb(84, 98, 112);
            this.lblCycleTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblInputValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblInputValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblInputValue.ForeColor = System.Drawing.Color.FromArgb(31, 52, 74);
            this.lblInputValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblOutputValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOutputValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblOutputValue.ForeColor = System.Drawing.Color.FromArgb(31, 52, 74);
            this.lblOutputValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblNgValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNgValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblNgValue.ForeColor = System.Drawing.Color.FromArgb(31, 52, 74);
            this.lblNgValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblOkValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblOkValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblOkValue.ForeColor = System.Drawing.Color.FromArgb(31, 52, 74);
            this.lblOkValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblYieldValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblYieldValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblYieldValue.ForeColor = System.Drawing.Color.FromArgb(31, 52, 74);
            this.lblYieldValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblCycleValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCycleValue.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblCycleValue.ForeColor = System.Drawing.Color.FromArgb(31, 52, 74);
            this.lblCycleValue.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.btnResetCounter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.btnResetCounter.BackColor = System.Drawing.Color.White;
            this.btnResetCounter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnResetCounter.ForeColor = System.Drawing.Color.FromArgb(52, 70, 86);
            this.btnResetCounter.Size = new System.Drawing.Size(100, 30);
            this.btnResetCounter.Text = "重新计数";
            this.btnResetCounter.UseVisualStyleBackColor = false;
            // 
            // PeripheralCapacityPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1176, 632);
            this.Controls.Add(this.grpCapacity);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "PeripheralCapacityPage";
            this.Text = "产能";
            this.grpCapacity.ResumeLayout(false);
            this.capacityLayout.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.GroupBox grpCapacity;
        private System.Windows.Forms.TableLayoutPanel capacityLayout;
        private System.Windows.Forms.Label lblInputTitle;
        private System.Windows.Forms.Label lblOutputTitle;
        private System.Windows.Forms.Label lblNgTitle;
        private System.Windows.Forms.Label lblOkTitle;
        private System.Windows.Forms.Label lblYieldTitle;
        private System.Windows.Forms.Label lblCycleTitle;
        private System.Windows.Forms.Label lblInputValue;
        private System.Windows.Forms.Label lblOutputValue;
        private System.Windows.Forms.Label lblNgValue;
        private System.Windows.Forms.Label lblOkValue;
        private System.Windows.Forms.Label lblYieldValue;
        private System.Windows.Forms.Label lblCycleValue;
        private System.Windows.Forms.Button btnResetCounter;
    }
}
