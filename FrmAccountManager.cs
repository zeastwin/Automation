using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmAccountManager : Form
    {
        private readonly AccountStore accountStore;
        private readonly DataGridView dgvAccounts;
        private readonly TextBox txtUserName;
        private readonly ComboBox cboRole;
        private readonly CheckBox chkDisabled;
        private readonly TreeView treePermissions;
        private readonly Button btnNew;
        private readonly Button btnSave;
        private readonly Button btnDelete;
        private readonly Button btnResetPassword;
        private readonly Button btnApplyTemplate;
        private readonly Button btnSelectAll;
        private readonly Label lblHint;
        private readonly Panel rightPanel;

        private UserAccount currentAccount;
        private bool editingNew;
        private bool suppressTreeEvent;

        public FrmAccountManager()
        {
            accountStore = SF.accountStore ?? throw new InvalidOperationException("账户系统未初始化");

            Text = "账户管理";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1176, 620);
            MinimumSize = new Size(1056, 600);

            Font uiFont = new Font("黑体", 10.5F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));

            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1
            };
            Shown += (s, e) => ApplySplitLayout(split);

            dgvAccounts = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = uiFont
            };
            dgvAccounts.ColumnHeadersDefaultCellStyle.Font = new Font("黑体", 10.5F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            dgvAccounts.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvAccounts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvAccounts.ColumnHeadersHeight = 28;
            dgvAccounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "colUser", HeaderText = "用户名", FillWeight = 40 });
            dgvAccounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRole", HeaderText = "角色", FillWeight = 30 });
            dgvAccounts.Columns.Add(new DataGridViewTextBoxColumn { Name = "colState", HeaderText = "状态", FillWeight = 30 });
            dgvAccounts.SelectionChanged += DgvAccounts_SelectionChanged;

            split.Panel1.Controls.Add(dgvAccounts);

            rightPanel = new Panel { Dock = DockStyle.Fill };

            Label lblUser = new Label { Text = "用户名:", Font = uiFont, Location = new Point(20, 20), AutoSize = true };
            txtUserName = new TextBox { Font = uiFont, Location = new Point(120, 16), Width = 200, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            Label lblRole = new Label { Text = "角色:", Font = uiFont, Location = new Point(20, 60), AutoSize = true };
            cboRole = new ComboBox
            {
                Font = uiFont,
                Location = new Point(120, 56),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            cboRole.Items.Add(UserRole.SystemAdmin);
            cboRole.Items.Add(UserRole.Admin);
            cboRole.Items.Add(UserRole.Operator);
            cboRole.SelectedIndexChanged += CboRole_SelectedIndexChanged;

            chkDisabled = new CheckBox { Text = "禁用账户", Font = uiFont, Location = new Point(350, 18), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };

            Label lblPerm = new Label { Text = "权限授权:", Font = uiFont, Location = new Point(20, 100), AutoSize = true };
            btnSelectAll = new Button { Text = "全选", Font = uiFont, Location = new Point(120, 95), Size = new Size(60, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            btnSelectAll.Click += BtnSelectAll_Click;
            treePermissions = new TreeView
            {
                Location = new Point(20, 130),
                Size = new Size(560, 300),
                CheckBoxes = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            treePermissions.AfterCheck += TreePermissions_AfterCheck;
            BuildPermissionTree();

            btnNew = new Button { Text = "新建", Font = uiFont, Location = new Point(20, 490), Size = new Size(80, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnNew.Click += BtnNew_Click;

            btnSave = new Button { Text = "保存", Font = uiFont, Location = new Point(110, 490), Size = new Size(80, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnSave.Click += BtnSave_Click;

            btnDelete = new Button { Text = "删除", Font = uiFont, Location = new Point(200, 490), Size = new Size(80, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnDelete.Click += BtnDelete_Click;

            btnResetPassword = new Button { Text = "重置口令", Font = uiFont, Location = new Point(290, 490), Size = new Size(90, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnResetPassword.Click += BtnResetPassword_Click;

            btnApplyTemplate = new Button { Text = "应用角色模板", Font = uiFont, Location = new Point(390, 490), Size = new Size(120, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnApplyTemplate.Click += BtnApplyTemplate_Click;

            lblHint = new Label { Font = new Font("黑体", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))), ForeColor = Color.Red, Location = new Point(20, 530), Size = new Size(560, 50), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            rightPanel.Controls.Add(lblUser);
            rightPanel.Controls.Add(txtUserName);
            rightPanel.Controls.Add(lblRole);
            rightPanel.Controls.Add(cboRole);
            rightPanel.Controls.Add(chkDisabled);
            rightPanel.Controls.Add(lblPerm);
            rightPanel.Controls.Add(btnSelectAll);
            rightPanel.Controls.Add(treePermissions);
            rightPanel.Controls.Add(btnNew);
            rightPanel.Controls.Add(btnSave);
            rightPanel.Controls.Add(btnDelete);
            rightPanel.Controls.Add(btnResetPassword);
            rightPanel.Controls.Add(btnApplyTemplate);
            rightPanel.Controls.Add(lblHint);

            split.Panel2.Controls.Add(rightPanel);
            Controls.Add(split);

            LoadAccounts();
        }

        private void ApplySplitLayout(SplitContainer split)
        {
            if (split == null || split.Width <= 0)
            {
                return;
            }
            int panel1Min = 320;
            int panel2Min = 520;
            if (split.Width < panel1Min + panel2Min)
            {
                panel2Min = Math.Max(0, split.Width - panel1Min);
            }
            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
            int maxDistance = Math.Max(panel1Min, split.Width - panel2Min);
            int distance = Math.Min(432, maxDistance);
            if (distance < panel1Min)
            {
                distance = panel1Min;
            }
            split.SplitterDistance = distance;
        }

        public void ApplyPermissions()
        {
            bool canManage = SF.HasPermission(PermissionKeys.AccountManage) || (SF.SecurityLocked && SF.userSession?.IsRecovery == true);
            if (!canManage)
            {
                Close();
                return;
            }
            UpdateCreatePermissionUi();
        }

        private void LoadAccounts()
        {
            dgvAccounts.Rows.Clear();
            foreach (UserAccount account in accountStore.Accounts.OrderBy(item => item.UserName, StringComparer.OrdinalIgnoreCase))
            {
                int row = dgvAccounts.Rows.Add(account.UserName, account.Role.ToString(), account.Disabled ? "禁用" : "启用");
                dgvAccounts.Rows[row].Tag = account.Id;
            }
            if (dgvAccounts.Rows.Count > 0)
            {
                dgvAccounts.Rows[0].Selected = true;
                DgvAccounts_SelectionChanged(dgvAccounts, EventArgs.Empty);
            }
            UpdateCreatePermissionUi();
        }

        private void DgvAccounts_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvAccounts.SelectedRows.Count == 0)
            {
                return;
            }
            string accountId = dgvAccounts.SelectedRows[0].Tag as string;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }
            UserAccount account = accountStore.Accounts.FirstOrDefault(item => string.Equals(item.Id, accountId, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                return;
            }
            editingNew = false;
            currentAccount = account;
            FillEditor(account);
        }

        private void FillEditor(UserAccount account)
        {
            suppressTreeEvent = true;
            txtUserName.Text = account.UserName;
            cboRole.SelectedItem = account.Role;
            chkDisabled.Checked = account.Disabled;
            ApplyPermissionChecks(account.Permissions);
            UpdatePermissionTreeState();
            ApplySystemAccountLockState(account);
            suppressTreeEvent = false;
        }

        private void BuildPermissionTree()
        {
            treePermissions.Nodes.Clear();
            foreach (var group in PermissionCatalog.All.GroupBy(item => item.GroupId).OrderBy(item => item.Key))
            {
                string groupTitle = group.First().GroupTitle;
                TreeNode groupNode = new TreeNode(groupTitle) { Tag = group.Key };
                foreach (PermissionDefinition def in group.OrderBy(item => item.Order))
                {
                    TreeNode child = new TreeNode(def.Title) { Tag = def.Key };
                    groupNode.Nodes.Add(child);
                }
                treePermissions.Nodes.Add(groupNode);
            }
            treePermissions.ExpandAll();
        }

        private void ApplyPermissionChecks(List<string> permissions)
        {
            HashSet<string> set = new HashSet<string>(permissions ?? new List<string>(), StringComparer.Ordinal);
            foreach (TreeNode groupNode in treePermissions.Nodes)
            {
                foreach (TreeNode child in groupNode.Nodes)
                {
                    if (child.Tag is string key)
                    {
                        child.Checked = set.Contains(key);
                    }
                }
                groupNode.Checked = groupNode.Nodes.Cast<TreeNode>().Any(node => node.Checked);
            }
        }

        private void UpdatePermissionTreeState()
        {
            bool isSystemAdmin = cboRole.SelectedItem is UserRole role && role == UserRole.SystemAdmin;
            treePermissions.Enabled = !isSystemAdmin;
            btnApplyTemplate.Enabled = !isSystemAdmin;
            btnSelectAll.Enabled = !isSystemAdmin;
            if (isSystemAdmin)
            {
                ApplyPermissionChecks(PermissionCatalog.GetDefaultPermissions(UserRole.SystemAdmin).ToList());
            }
        }

        private void TreePermissions_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (suppressTreeEvent)
            {
                return;
            }
            suppressTreeEvent = true;
            if (e.Node.Parent == null)
            {
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = e.Node.Checked;
                }
            }
            else
            {
                TreeNode groupNode = e.Node.Parent;
                string groupId = groupNode.Tag as string;
                groupNode.Checked = groupNode.Nodes.Cast<TreeNode>().Any(node => node.Checked);
                if (groupId != null && PermissionCatalog.GroupAccessKeys.TryGetValue(groupId, out string accessKey))
                {
                    if (e.Node.Tag is string key && !string.Equals(key, accessKey, StringComparison.Ordinal) && e.Node.Checked)
                    {
                        TreeNode accessNode = groupNode.Nodes.Cast<TreeNode>().FirstOrDefault(node => string.Equals(node.Tag as string, accessKey, StringComparison.Ordinal));
                        if (accessNode != null)
                        {
                            accessNode.Checked = true;
                        }
                    }
                    if (e.Node.Tag is string key2 && string.Equals(key2, accessKey, StringComparison.Ordinal) && !e.Node.Checked)
                    {
                        bool hasOther = groupNode.Nodes.Cast<TreeNode>().Any(node => node.Checked);
                        if (hasOther)
                        {
                            e.Node.Checked = true;
                        }
                    }
                }
            }
            suppressTreeEvent = false;
        }

        private void BtnNew_Click(object sender, EventArgs e)
        {
            if (!CanCreateAccount())
            {
                lblHint.Text = "仅系统管理员可新建账号";
                return;
            }
            editingNew = true;
            currentAccount = null;
            txtUserName.Text = string.Empty;
            cboRole.SelectedItem = UserRole.Operator;
            chkDisabled.Checked = false;
            ApplyPermissionChecks(PermissionCatalog.GetDefaultPermissions(UserRole.Operator).ToList());
            UpdatePermissionTreeState();
            ApplySystemAccountLockState(null);
            lblHint.Text = "";
        }

        private void BtnApplyTemplate_Click(object sender, EventArgs e)
        {
            if (cboRole.SelectedItem is UserRole role)
            {
                ApplyPermissionChecks(PermissionCatalog.GetDefaultPermissions(role).ToList());
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            lblHint.Text = string.Empty;
            string userName = txtUserName.Text;
            if (string.IsNullOrWhiteSpace(userName))
            {
                lblHint.Text = "用户名不能为空";
                return;
            }
            if (!(cboRole.SelectedItem is UserRole role))
            {
                lblHint.Text = "请选择角色";
                return;
            }

            List<string> permissions = CollectPermissions();
            if (role == UserRole.SystemAdmin)
            {
                permissions = PermissionCatalog.GetDefaultPermissions(UserRole.SystemAdmin).ToList();
            }

            if (editingNew)
            {
                if (!CanCreateAccount())
                {
                    lblHint.Text = "仅系统管理员可新建账号";
                    return;
                }
                if (accountStore.IsSystemUserName(userName))
                {
                    lblHint.Text = "系统管理员账号不可新增";
                    return;
                }
                UserAccount account = new UserAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = userName,
                    Role = role,
                    Disabled = chkDisabled.Checked,
                    Permissions = permissions
                };

                using (FrmChangePassword dialog = new FrmChangePassword(accountStore, account, false))
                {
                    dialog.Text = "设置初始口令";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        lblHint.Text = "请先设置口令";
                        return;
                    }
                    if (!accountStore.TrySetPassword(account, dialog.NewPassword, out string pwdError))
                    {
                        lblHint.Text = pwdError;
                        return;
                    }
                }

                if (!accountStore.TryAddAccount(account, out string addError))
                {
                    lblHint.Text = addError;
                    return;
                }
            }
            else
            {
                if (currentAccount == null)
                {
                    lblHint.Text = "未选择账户";
                    return;
                }
                if (accountStore.IsSystemUserName(currentAccount.UserName))
                {
                    lblHint.Text = "系统管理员账号不可修改";
                    return;
                }
                UserAccount updated = new UserAccount
                {
                    Id = currentAccount.Id,
                    UserName = userName,
                    Role = role,
                    PasswordHash = currentAccount.PasswordHash,
                    Salt = currentAccount.Salt,
                    Iterations = currentAccount.Iterations,
                    Disabled = chkDisabled.Checked,
                    Permissions = permissions
                };
                if (!accountStore.TryUpdateAccount(updated, out string updateError))
                {
                    lblHint.Text = updateError;
                    return;
                }
                currentAccount = updated;
                if (SF.userSession?.Account != null && string.Equals(SF.userSession.Account.Id, updated.Id, StringComparison.OrdinalIgnoreCase))
                {
                    SF.SetUserSession(new UserSession(updated, SF.userSession.IsRecovery));
                }
            }

            if (!accountStore.TrySave(SF.ConfigPath, out string saveError))
            {
                lblHint.Text = saveError;
                return;
            }
            if (SF.SecurityLocked)
            {
                SF.ClearSecurityLock();
            }
            LoadAccounts();
            lblHint.Text = "保存成功";
            SF.RefreshPermissionUi();
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            lblHint.Text = string.Empty;
            if (currentAccount == null)
            {
                lblHint.Text = "未选择账户";
                return;
            }
            if (accountStore.IsSystemUserName(currentAccount.UserName))
            {
                lblHint.Text = "系统管理员账号不可删除";
                return;
            }
            if (SF.userSession?.Account != null && string.Equals(SF.userSession.Account.Id, currentAccount.Id, StringComparison.OrdinalIgnoreCase))
            {
                lblHint.Text = "不能删除当前登录账号";
                return;
            }
            if (MessageBox.Show("确认删除该账户？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!accountStore.TryRemoveAccount(currentAccount.Id, out string error))
            {
                lblHint.Text = error;
                return;
            }
            if (!accountStore.TrySave(SF.ConfigPath, out string saveError))
            {
                lblHint.Text = saveError;
                return;
            }
            LoadAccounts();
            lblHint.Text = "删除成功";
            SF.RefreshPermissionUi();
        }

        private void BtnResetPassword_Click(object sender, EventArgs e)
        {
            lblHint.Text = string.Empty;
            if (currentAccount == null)
            {
                lblHint.Text = "未选择账户";
                return;
            }
            if (accountStore.IsSystemUserName(currentAccount.UserName))
            {
                lblHint.Text = "系统管理员账号不可修改口令";
                return;
            }
            using (FrmChangePassword dialog = new FrmChangePassword(accountStore, currentAccount, false))
            {
                dialog.Text = "重置口令";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!accountStore.TrySetPassword(currentAccount, dialog.NewPassword, out string error))
                {
                    lblHint.Text = error;
                    return;
                }
            }
            if (!accountStore.TrySave(SF.ConfigPath, out string saveError))
            {
                lblHint.Text = saveError;
                return;
            }
            LoadAccounts();
            lblHint.Text = "口令已重置";
        }

        private void CboRole_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressTreeEvent)
            {
                return;
            }
            UpdatePermissionTreeState();
        }

        private void BtnSelectAll_Click(object sender, EventArgs e)
        {
            if (!treePermissions.Enabled)
            {
                return;
            }
            suppressTreeEvent = true;
            foreach (TreeNode groupNode in treePermissions.Nodes)
            {
                foreach (TreeNode child in groupNode.Nodes)
                {
                    child.Checked = true;
                }
                groupNode.Checked = true;
            }
            suppressTreeEvent = false;
        }

        private bool CanCreateAccount()
        {
            return SF.userSession?.Account != null
                && !SF.userSession.IsRecovery
                && SF.userSession.Account.Role == UserRole.SystemAdmin;
        }

        private void UpdateCreatePermissionUi()
        {
            btnNew.Enabled = CanCreateAccount();
        }

        private void ApplySystemAccountLockState(UserAccount account)
        {
            bool isSystemAccount = account != null && accountStore.IsSystemUserName(account.UserName);
            bool isNewAccount = account == null;

            txtUserName.ReadOnly = isSystemAccount;
            cboRole.Enabled = !isSystemAccount;
            chkDisabled.Enabled = !isSystemAccount;

            if (isSystemAccount)
            {
                treePermissions.Enabled = false;
                btnApplyTemplate.Enabled = false;
                btnSelectAll.Enabled = false;
                btnSave.Enabled = false;
                btnDelete.Enabled = false;
                btnResetPassword.Enabled = false;
            }
            else
            {
                UpdatePermissionTreeState();
                btnSave.Enabled = true;
                btnDelete.Enabled = !isNewAccount;
                btnResetPassword.Enabled = !isNewAccount;
            }
        }

        private List<string> CollectPermissions()
        {
            List<string> list = new List<string>();
            foreach (TreeNode groupNode in treePermissions.Nodes)
            {
                foreach (TreeNode child in groupNode.Nodes)
                {
                    if (child.Checked && child.Tag is string key)
                    {
                        list.Add(key);
                    }
                }
            }
            return list;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // FrmAccountManager
            // 
            this.ClientSize = new System.Drawing.Size(1176, 620);
            this.Name = "FrmAccountManager";
            this.ResumeLayout(false);

        }
    }
}
