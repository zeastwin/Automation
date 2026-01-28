using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmChangePassword : Form
    {
        private readonly AccountStore accountStore;
        private readonly UserAccount targetAccount;
        private readonly bool requireOldPassword;
        private readonly Label lblHint;
        private readonly TextBox txtOld;
        private readonly TextBox txtNew;
        private readonly TextBox txtConfirm;
        private readonly Button btnOk;
        private readonly Button btnCancel;

        public FrmChangePassword(AccountStore store, UserAccount account, bool requireOldPassword)
        {
            accountStore = store ?? throw new ArgumentNullException(nameof(store));
            targetAccount = account ?? throw new ArgumentNullException(nameof(account));
            this.requireOldPassword = requireOldPassword;

            Font uiFont = new Font("黑体", 11F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            Text = "修改口令";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, requireOldPassword ? 230 : 190);

            Label lblUser = new Label
            {
                Text = $"账号: {targetAccount.UserName}",
                Font = uiFont,
                Location = new Point(20, 16),
                AutoSize = true
            };

            int rowTop = 50;
            if (requireOldPassword)
            {
                Label lblOld = new Label
                {
                    Text = "旧口令:",
                    Font = uiFont,
                    Location = new Point(20, rowTop),
                    AutoSize = true
                };
                txtOld = new TextBox
                {
                    Font = uiFont,
                    Location = new Point(110, rowTop - 3),
                    Width = 260,
                    UseSystemPasswordChar = true
                };
                Controls.Add(lblOld);
                Controls.Add(txtOld);
                rowTop += 38;
            }
            else
            {
                txtOld = new TextBox();
            }

            Label lblNew = new Label
            {
                Text = "新口令:",
                Font = uiFont,
                Location = new Point(20, rowTop),
                AutoSize = true
            };
            txtNew = new TextBox
            {
                Font = uiFont,
                Location = new Point(110, rowTop - 3),
                Width = 260,
                UseSystemPasswordChar = true
            };
            rowTop += 38;

            Label lblConfirm = new Label
            {
                Text = "确认口令:",
                Font = uiFont,
                Location = new Point(20, rowTop),
                AutoSize = true
            };
            txtConfirm = new TextBox
            {
                Font = uiFont,
                Location = new Point(110, rowTop - 3),
                Width = 260,
                UseSystemPasswordChar = true
            };

            lblHint = new Label
            {
                Font = new Font("黑体", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))),
                ForeColor = Color.Red,
                Location = new Point(20, rowTop + 32),
                Width = 380,
                Height = 30
            };

            btnOk = new Button
            {
                Text = "确定",
                Font = uiFont,
                Location = new Point(110, rowTop + 70),
                Size = new Size(90, 30)
            };
            btnOk.Click += BtnOk_Click;

            btnCancel = new Button
            {
                Text = "取消",
                Font = uiFont,
                Location = new Point(220, rowTop + 70),
                Size = new Size(90, 30)
            };
            btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.Add(lblUser);
            Controls.Add(lblNew);
            Controls.Add(txtNew);
            Controls.Add(lblConfirm);
            Controls.Add(txtConfirm);
            Controls.Add(lblHint);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        public string NewPassword { get; private set; }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            lblHint.Text = string.Empty;
            if (requireOldPassword)
            {
                string oldPassword = txtOld.Text;
                if (!accountStore.VerifyPassword(targetAccount, oldPassword))
                {
                    lblHint.Text = "旧口令错误";
                    return;
                }
            }

            string newPassword = txtNew.Text;
            string confirm = txtConfirm.Text;
            if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
            {
                lblHint.Text = "两次口令不一致";
                return;
            }
            if (!AccountStore.TryValidatePassword(newPassword, out string error))
            {
                lblHint.Text = error;
                return;
            }
            NewPassword = newPassword;
            DialogResult = DialogResult.OK;
        }
    }
}
