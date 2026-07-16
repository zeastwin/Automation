namespace MachineApp.Hmi
{
    partial class AlarmHistoryPage
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
            this.SuspendLayout();
            //
            // AlarmHistoryPage
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 656);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.Name = "AlarmHistoryPage";
            this.Text = "报警历史";
            this.ResumeLayout(false);
        }
    }
}
