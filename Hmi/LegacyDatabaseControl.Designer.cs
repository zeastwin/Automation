namespace Automation.Hmi
{
    partial class LegacyDatabaseControl
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
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.leftLayout = new System.Windows.Forms.TableLayoutPanel();
            this.leftToolbar = new System.Windows.Forms.Panel();
            this.tableLabel = new System.Windows.Forms.Label();
            this.tableSelector = new System.Windows.Forms.ComboBox();
            this.dataSource = new System.Windows.Forms.ComboBox();
            this.searchButton = new System.Windows.Forms.Button();
            this.filterLabel = new System.Windows.Forms.Label();
            this.filterGrid = new System.Windows.Forms.DataGridView();
            this.rightLayout = new System.Windows.Forms.TableLayoutPanel();
            this.actionLayout = new System.Windows.Forms.TableLayoutPanel();
            this.applyButton = new System.Windows.Forms.Button();
            this.rejectButton = new System.Windows.Forms.Button();
            this.queryLabel = new System.Windows.Forms.Label();
            this.queryField = new System.Windows.Forms.ComboBox();
            this.queryValue = new System.Windows.Forms.TextBox();
            this.dataGrid = new System.Windows.Forms.DataGridView();
            this.status = new System.Windows.Forms.Label();
            this.rootLayout.SuspendLayout();
            this.leftLayout.SuspendLayout();
            this.leftToolbar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.filterGrid)).BeginInit();
            this.rightLayout.SuspendLayout();
            this.actionLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGrid)).BeginInit();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 2;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 30F));
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 70F));
            this.rootLayout.Controls.Add(this.leftLayout, 0, 0);
            this.rootLayout.Controls.Add(this.rightLayout, 1, 0);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(0, 0);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 1;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Size = new System.Drawing.Size(1180, 520);
            this.rootLayout.TabIndex = 0;
            //
            // leftLayout
            //
            this.leftLayout.ColumnCount = 1;
            this.leftLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.leftLayout.Controls.Add(this.leftToolbar, 0, 0);
            this.leftLayout.Controls.Add(this.filterGrid, 0, 1);
            this.leftLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftLayout.Name = "leftLayout";
            this.leftLayout.RowCount = 2;
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 50F));
            this.leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // leftToolbar
            //
            this.leftToolbar.Controls.Add(this.tableLabel);
            this.leftToolbar.Controls.Add(this.tableSelector);
            this.leftToolbar.Controls.Add(this.dataSource);
            this.leftToolbar.Controls.Add(this.searchButton);
            this.leftToolbar.Controls.Add(this.filterLabel);
            this.leftToolbar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftToolbar.Name = "leftToolbar";
            this.tableLabel.AutoSize = true;
            this.tableLabel.Location = new System.Drawing.Point(10, 12);
            this.tableLabel.Name = "tableLabel";
            this.tableLabel.Text = "TableName";
            this.tableSelector.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.tableSelector.Location = new System.Drawing.Point(75, 8);
            this.tableSelector.Name = "tableSelector";
            this.tableSelector.Size = new System.Drawing.Size(115, 20);
            this.tableSelector.SelectedIndexChanged += new System.EventHandler(this.TableSelector_SelectedIndexChanged);
            this.dataSource.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.dataSource.Location = new System.Drawing.Point(196, 8);
            this.dataSource.Name = "dataSource";
            this.dataSource.Size = new System.Drawing.Size(92, 20);
            this.dataSource.SelectedIndexChanged += new System.EventHandler(this.DataSource_SelectedIndexChanged);
            this.searchButton.Location = new System.Drawing.Point(294, 7);
            this.searchButton.Name = "searchButton";
            this.searchButton.Size = new System.Drawing.Size(55, 23);
            this.searchButton.Text = "筛选";
            this.searchButton.UseVisualStyleBackColor = true;
            this.searchButton.Click += new System.EventHandler(this.SearchButton_Click);
            this.filterLabel.AutoSize = true;
            this.filterLabel.Location = new System.Drawing.Point(10, 33);
            this.filterLabel.Name = "filterLabel";
            this.filterLabel.Text = "Filter";
            //
            // filterGrid
            //
            this.filterGrid.AllowUserToAddRows = false;
            this.filterGrid.AllowUserToDeleteRows = false;
            this.filterGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.filterGrid.BackgroundColor = System.Drawing.Color.White;
            this.filterGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.filterGrid.Name = "filterGrid";
            this.filterGrid.ReadOnly = true;
            this.filterGrid.RowHeadersVisible = false;
            this.filterGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.filterGrid.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.FilterGrid_CellDoubleClick);
            //
            // rightLayout
            //
            this.rightLayout.ColumnCount = 1;
            this.rightLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rightLayout.Controls.Add(this.actionLayout, 0, 0);
            this.rightLayout.Controls.Add(this.dataGrid, 0, 1);
            this.rightLayout.Controls.Add(this.status, 0, 2);
            this.rightLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightLayout.Name = "rightLayout";
            this.rightLayout.RowCount = 3;
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 50F));
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 26F));
            //
            // actionLayout
            //
            this.actionLayout.ColumnCount = 5;
            this.actionLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18F));
            this.actionLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 18F));
            this.actionLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 16F));
            this.actionLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 22F));
            this.actionLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 26F));
            this.actionLayout.Controls.Add(this.applyButton, 0, 0);
            this.actionLayout.Controls.Add(this.rejectButton, 1, 0);
            this.actionLayout.Controls.Add(this.queryLabel, 2, 0);
            this.actionLayout.Controls.Add(this.queryField, 3, 0);
            this.actionLayout.Controls.Add(this.queryValue, 4, 0);
            this.actionLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.actionLayout.Name = "actionLayout";
            this.actionLayout.RowCount = 1;
            this.actionLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.applyButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.applyButton.Name = "applyButton";
            this.applyButton.Text = "应用修改";
            this.applyButton.UseVisualStyleBackColor = true;
            this.applyButton.Click += new System.EventHandler(this.ApplyButton_Click);
            this.rejectButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rejectButton.Name = "rejectButton";
            this.rejectButton.Text = "取消修改";
            this.rejectButton.UseVisualStyleBackColor = true;
            this.rejectButton.Click += new System.EventHandler(this.RejectButton_Click);
            this.queryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryLabel.Name = "queryLabel";
            this.queryLabel.Text = "查询项：";
            this.queryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.queryField.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryField.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.queryField.Margin = new System.Windows.Forms.Padding(3, 14, 3, 3);
            this.queryField.Name = "queryField";
            this.queryValue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queryValue.Margin = new System.Windows.Forms.Padding(3, 14, 3, 3);
            this.queryValue.Name = "queryValue";
            //
            // dataGrid
            //
            this.dataGrid.AllowUserToAddRows = true;
            this.dataGrid.AllowUserToDeleteRows = true;
            this.dataGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGrid.BackgroundColor = System.Drawing.Color.White;
            this.dataGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGrid.Name = "dataGrid";
            this.dataGrid.RowHeadersVisible = false;
            this.dataGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            //
            // status
            //
            this.status.Dock = System.Windows.Forms.DockStyle.Fill;
            this.status.ForeColor = System.Drawing.Color.DimGray;
            this.status.Name = "status";
            this.status.Text = "数据库服务未连接";
            this.status.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // LegacyDatabaseControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this.rootLayout);
            this.Name = "LegacyDatabaseControl";
            this.Size = new System.Drawing.Size(1180, 520);
            this.rootLayout.ResumeLayout(false);
            this.leftLayout.ResumeLayout(false);
            this.leftToolbar.ResumeLayout(false);
            this.leftToolbar.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.filterGrid)).EndInit();
            this.rightLayout.ResumeLayout(false);
            this.actionLayout.ResumeLayout(false);
            this.actionLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGrid)).EndInit();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.TableLayoutPanel leftLayout;
        private System.Windows.Forms.Panel leftToolbar;
        private System.Windows.Forms.Label tableLabel;
        private System.Windows.Forms.ComboBox tableSelector;
        private System.Windows.Forms.ComboBox dataSource;
        private System.Windows.Forms.Button searchButton;
        private System.Windows.Forms.Label filterLabel;
        private System.Windows.Forms.DataGridView filterGrid;
        private System.Windows.Forms.TableLayoutPanel rightLayout;
        private System.Windows.Forms.TableLayoutPanel actionLayout;
        private System.Windows.Forms.Button applyButton;
        private System.Windows.Forms.Button rejectButton;
        private System.Windows.Forms.Label queryLabel;
        private System.Windows.Forms.ComboBox queryField;
        private System.Windows.Forms.TextBox queryValue;
        private System.Windows.Forms.DataGridView dataGrid;
        private System.Windows.Forms.Label status;
    }
}
