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
        private readonly Button btnCancel = new Button();
        private readonly Button btnSelectAll = new Button();
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

            listView.Dock = DockStyle.Fill;
            listView.View = View.Details;
            listView.CheckBoxes = true;
            listView.HideSelection = false;
            listView.Columns.Add("名称", 300);
            foreach (string name in ioNames)
            {
                ListViewItem item = new ListViewItem(name);
                item.Checked = selectedNames.Contains(name);
                listView.Items.Add(item);
            }
            listView.ItemChecked += ListView_ItemChecked;

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.Padding = new Padding(8);
            panel.Height = 44;

            btnOk.Text = "确定";
            btnOk.Width = 80;
            btnOk.Click += BtnOk_Click;
            btnCancel.Text = "取消";
            btnCancel.Width = 80;
            btnCancel.Click += BtnCancel_Click;
            btnSelectAll.Text = "全选";
            btnSelectAll.Width = 80;
            btnSelectAll.Click += BtnSelectAll_Click;

            panel.Controls.Add(btnOk);
            panel.Controls.Add(btnCancel);
            panel.Controls.Add(btnSelectAll);

            Controls.Add(listView);
            Controls.Add(panel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
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

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
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
