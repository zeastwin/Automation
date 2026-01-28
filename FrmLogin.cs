using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmLogin : Form
    {
        private readonly AccountStore accountStore;
        private readonly TextBox txtUser;
        private readonly TextBox txtPassword;
        private readonly Label lblHint;
        private readonly Button btnLogin;
        private readonly Button btnExit;

        public FrmLogin(AccountStore store)
        {
            accountStore = store ?? throw new ArgumentNullException(nameof(store));

            Font uiFont = new Font("黑体", 11F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            Text = "用户登录";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 220);

            Label lblUser = new Label
            {
                Text = "用户名:",
                Font = uiFont,
                Location = new Point(40, 40),
                AutoSize = true
            };
            txtUser = new TextBox
            {
                Font = uiFont,
                Location = new Point(120, 36),
                Width = 230
            };
            Label lblPassword = new Label
            {
                Text = "口令:",
                Font = uiFont,
                Location = new Point(40, 82),
                AutoSize = true
            };
            txtPassword = new TextBox
            {
                Font = uiFont,
                Location = new Point(120, 78),
                Width = 230,
                UseSystemPasswordChar = true
            };

            lblHint = new Label
            {
                Font = new Font("黑体", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))),
                ForeColor = Color.Red,
                Location = new Point(40, 120),
                Size = new Size(330, 36)
            };

            btnLogin = new Button
            {
                Text = "登录",
                Font = uiFont,
                Location = new Point(120, 165),
                Size = new Size(90, 30)
            };
            btnLogin.Click += BtnLogin_Click;

            btnExit = new Button
            {
                Text = "退出",
                Font = uiFont,
                Location = new Point(230, 165),
                Size = new Size(90, 30)
            };
            btnExit.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.Add(lblUser);
            Controls.Add(txtUser);
            Controls.Add(lblPassword);
            Controls.Add(txtPassword);
            Controls.Add(lblHint);
            Controls.Add(btnLogin);
            Controls.Add(btnExit);

            AcceptButton = btnLogin;
            CancelButton = btnExit;

            if (!accountStore.IsConfigValid)
            {
                lblHint.Text = "账户配置异常，进入锁定模式";
            }
        }

        public UserSession Session { get; private set; }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            lblHint.Text = string.Empty;
            string userName = txtUser.Text;
            string password = txtPassword.Text;

            if (!accountStore.IsConfigValid)
            {
                if (!accountStore.IsDefaultCredential(userName, password))
                {
                    lblHint.Text = "锁定模式仅允许默认系统管理员登录";
                    return;
                }
                UserAccount recovery = accountStore.CreateRecoveryAccount();
                Session = new UserSession(recovery, true);
                DialogResult = DialogResult.OK;
                return;
            }

            if (!accountStore.TryAuthenticate(userName, password, out UserAccount account, out string error))
            {
                lblHint.Text = error ?? "登录失败";
                return;
            }

            Session = new UserSession(account, false);
            DialogResult = DialogResult.OK;
        }
    }
}
