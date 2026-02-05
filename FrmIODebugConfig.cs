using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public class FrmIODebugConfig : Form
    {
        private readonly ListView listView = new ListView();
        private readonly Button btnOk = new Button();
        private readonly Button btnSelectAll = new Button();
        private readonly Label tipLabel = new Label();
        private bool isBatchSelecting = false;

        public List<string> SelectedNames { get; private set; } = new List<string>();

        public FrmIODebugConfig(string title, List<string> ioNames, HashSet<string> selectedNames)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 360;
            Height = 520;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("微软雅黑", 9F, FontStyle.Regular);

            listView.Dock = DockStyle.Fill;
            listView.View = View.Details;
            listView.CheckBoxes = true;
            listView.HideSelection = false;
            listView.FullRowSelect = true;
            listView.MultiSelect = false;
            listView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            listView.BorderStyle = BorderStyle.None;
            listView.BackColor = Color.White;
            listView.Columns.Add("名称", 280);
            foreach (string name in ioNames)
            {
                ListViewItem item = new ListViewItem(name);
                item.Checked = selectedNames.Contains(name);
                listView.Items.Add(item);
            }
            listView.ItemChecked += ListView_ItemChecked;

            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 46;
            headerPanel.Padding = new Padding(12, 10, 12, 0);
            headerPanel.BackColor = BackColor;

            tipLabel.Dock = DockStyle.Fill;
            tipLabel.Text = "勾选需要显示的IO";
            tipLabel.Font = new Font("微软雅黑", 10F, FontStyle.Bold);
            tipLabel.ForeColor = Color.FromArgb(48, 52, 59);
            tipLabel.TextAlign = ContentAlignment.MiddleLeft;
            headerPanel.Controls.Add(tipLabel);

            Panel listPanel = new Panel();
            listPanel.Dock = DockStyle.Fill;
            listPanel.Padding = new Padding(12, 6, 12, 12);
            listPanel.BackColor = BackColor;
            listPanel.Controls.Add(listView);

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.Padding = new Padding(12, 8, 12, 8);
            panel.Height = 52;
            panel.BackColor = BackColor;

            btnOk.Text = "确定";
            btnOk.Width = 80;
            btnOk.Height = 30;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.BackColor = Color.FromArgb(45, 129, 247);
            btnOk.ForeColor = Color.White;
            btnOk.Click += BtnOk_Click;
            btnSelectAll.Text = "全选";
            btnSelectAll.Width = 80;
            btnSelectAll.Height = 30;
            btnSelectAll.FlatStyle = FlatStyle.Flat;
            btnSelectAll.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 211);
            btnSelectAll.BackColor = Color.FromArgb(240, 242, 246);
            btnSelectAll.ForeColor = Color.FromArgb(64, 64, 64);
            btnSelectAll.Click += BtnSelectAll_Click;

            panel.Controls.Add(btnOk);
            panel.Controls.Add(btnSelectAll);

            Controls.Add(listPanel);
            Controls.Add(panel);
            Controls.Add(headerPanel);

            AcceptButton = btnOk;
            UpdateSelectAllText();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            List<string> result = new List<string>();
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Checked)
                {
                    result.Add(item.Text);
                }
            }
            SelectedNames = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnSelectAll_Click(object sender, EventArgs e)
        {
            bool hasUnchecked = false;
            foreach (ListViewItem item in listView.Items)
            {
                if (!item.Checked)
                {
                    hasUnchecked = true;
                    break;
                }
            }
            isBatchSelecting = true;
            foreach (ListViewItem item in listView.Items)
            {
                item.Checked = hasUnchecked;
            }
            isBatchSelecting = false;
            UpdateSelectAllText();
        }
        private void ListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (isBatchSelecting)
            {
                return;
            }
            UpdateSelectAllText();
        }
        private void UpdateSelectAllText()
        {
            if (listView.Items.Count == 0)
            {
                btnSelectAll.Text = "全选";
                return;
            }
            bool allChecked = true;
            foreach (ListViewItem item in listView.Items)
            {
                if (!item.Checked)
                {
                    allChecked = false;
                    break;
                }
            }
            btnSelectAll.Text = allChecked ? "全不选" : "全选";
        }
    }
}
