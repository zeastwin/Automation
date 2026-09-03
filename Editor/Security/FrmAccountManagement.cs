using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：编辑器 / 账户安全。
// 职责范围：系统管理员维护账户及完整权限清单；密码通过独立对话框设置且永不回显。

namespace Automation
{
    internal sealed class FrmAccountManagement : Form
    {
        private readonly AccountSecurityService accounts;
        private readonly ListBox accountList = new ListBox();
        private readonly TextBox userName = new TextBox();
        private readonly ComboBox level = new ComboBox();
        private readonly CheckBox enabled = new CheckBox();
        private readonly CheckedListBox permissions = new CheckedListBox();
        private readonly Button saveButton = new Button();
        private readonly Button deleteButton = new Button();
        private readonly Button resetPasswordButton = new Button();
        private AccountEditorSnapshot editing;
        private bool loading;
        private Image accountImage;

        public FrmAccountManagement(AccountSecurityService accounts)
        {
            this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            InitializeLayout();
            FillPermissionItems();
            accounts.Changed += Accounts_Changed;
            Disposed += (sender, args) =>
            {
                accounts.Changed -= Accounts_Changed;
                accountImage?.Dispose();
            };
            ReloadAccounts(null);
        }

        private void InitializeLayout()
        {
            Text = "账户与权限";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(920, 620);
            Size = new Size(1040, 700);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowIcon = false;
            Font = new Font("Microsoft YaHei UI", 9.5F);
            BackColor = UiPalette.Background;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = UiPalette.Navigation
            };
            accountImage = UiIconFactory.Create(UiIconKind.Account, UiPalette.NavigationAccent, 28);
            var headerIcon = new PictureBox
            {
                Location = new Point(24, 22),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = accountImage
            };
            var title = new Label
            {
                Text = "账户与权限",
                Location = new Point(68, 23),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = UiPalette.TextInverse
            };
            header.Controls.Add(headerIcon);
            header.Controls.Add(title);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.Background,
                Padding = new Padding(18),
                ColumnCount = 2,
                RowCount = 1
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Panel accountCard = CreateCard();
            accountCard.Margin = new Padding(0, 0, 9, 0);
            var accountColumn = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 0, 12, 10)
            };
            accountColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            accountColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            accountColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            accountColumn.Controls.Add(new Label
            {
                Text = "账户",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = UiPalette.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            accountList.Dock = DockStyle.Fill;
            accountList.Margin = Padding.Empty;
            accountList.MinimumSize = new Size(214, 0);
            accountList.BorderStyle = BorderStyle.FixedSingle;
            accountList.BackColor = UiPalette.SurfaceStrong;
            accountList.ForeColor = UiPalette.TextPrimary;
            accountList.Font = new Font("Microsoft YaHei UI", 10F);
            accountList.DrawMode = DrawMode.OwnerDrawFixed;
            accountList.ItemHeight = 36;
            accountList.DrawItem += AccountList_DrawItem;
            accountList.SelectedIndexChanged += AccountList_SelectedIndexChanged;
            accountColumn.Controls.Add(accountList, 0, 1);

            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 0),
                Margin = Padding.Empty
            };
            var newButton = new Button { Text = "新建账户", Width = 112, Height = 36 };
            deleteButton.Text = "删除";
            deleteButton.Width = 82;
            deleteButton.Height = 36;
            StyleSecondaryButton(newButton);
            StyleDangerButton(deleteButton);
            newButton.Click += NewButton_Click;
            deleteButton.Click += DeleteButton_Click;
            leftButtons.Controls.Add(newButton);
            leftButtons.Controls.Add(deleteButton);
            accountColumn.Controls.Add(leftButtons, 0, 2);
            accountCard.Controls.Add(accountColumn);
            content.Controls.Add(accountCard, 0, 0);

            var editor = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22, 12, 22, 10),
                ColumnCount = 2,
                RowCount = 5
            };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106F));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            userName.BorderStyle = BorderStyle.None;
            userName.Font = new Font("Microsoft YaHei UI", 10.5F);
            level.FlatStyle = FlatStyle.Flat;
            level.DropDownStyle = ComboBoxStyle.DropDownList;
            level.Font = new Font("Microsoft YaHei UI", 10.5F);
            level.Items.Add(new LevelItem(AccountLevel.Operator, "操作员"));
            level.Items.Add(new LevelItem(AccountLevel.Engineer, "工程师"));
            level.Items.Add(new LevelItem(AccountLevel.SystemAdministrator, "系统管理员"));
            level.SelectionChangeCommitted += Level_SelectionChangeCommitted;
            enabled.Text = "允许登录";
            enabled.AutoSize = true;
            enabled.Font = new Font("Microsoft YaHei UI", 10F);
            permissions.Dock = DockStyle.Fill;
            permissions.Margin = new Padding(0, 8, 0, 8);
            permissions.BorderStyle = BorderStyle.FixedSingle;
            permissions.BackColor = UiPalette.SurfaceStrong;
            permissions.ForeColor = UiPalette.TextPrimary;
            permissions.Font = new Font("Microsoft YaHei UI", 9.75F);
            permissions.ItemHeight = 25;
            permissions.CheckOnClick = true;
            permissions.IntegralHeight = false;
            editor.Controls.Add(CreateLabel("用户名"), 0, 0);
            editor.Controls.Add(CreateInputHost(userName), 1, 0);
            editor.Controls.Add(CreateLabel("账户级别"), 0, 1);
            editor.Controls.Add(CreateInputHost(level), 1, 1);
            editor.Controls.Add(CreateLabel("账户状态"), 0, 2);
            editor.Controls.Add(CreatePlainHost(enabled), 1, 2);
            editor.Controls.Add(CreateLabel("权限"), 0, 3);
            editor.Controls.Add(permissions, 1, 3);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 0),
                Margin = Padding.Empty
            };
            saveButton.Text = "保存";
            saveButton.Width = 92;
            saveButton.Height = 36;
            resetPasswordButton.Text = "重置密码";
            resetPasswordButton.Width = 106;
            resetPasswordButton.Height = 36;
            var closeButton = new Button
            {
                Text = "关闭",
                Width = 82,
                Height = 36,
                DialogResult = DialogResult.Cancel
            };
            StylePrimaryButton(saveButton);
            StyleSecondaryButton(resetPasswordButton);
            StyleSecondaryButton(closeButton);
            saveButton.Click += SaveButton_Click;
            resetPasswordButton.Click += ResetPasswordButton_Click;
            actions.Controls.Add(saveButton);
            actions.Controls.Add(resetPasswordButton);
            actions.Controls.Add(closeButton);
            editor.SetColumnSpan(actions, 2);
            editor.Controls.Add(actions, 0, 4);
            Panel editorCard = CreateCard();
            editorCard.Margin = new Padding(9, 0, 0, 0);
            editorCard.Controls.Add(editor);
            content.Controls.Add(editorCard, 1, 0);

            Controls.Add(content);
            Controls.Add(header);
            CancelButton = closeButton;
        }

        private void AccountList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= accountList.Items.Count)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? UiPalette.Selection : UiPalette.SurfaceStrong;
            Color foreground = selected ? UiPalette.SelectionText : UiPalette.TextPrimary;
            using (var brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            TextRenderer.DrawText(
                e.Graphics,
                accountList.Items[e.Index].ToString(),
                accountList.Font,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 16), e.Bounds.Height),
                foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private static Panel CreateCard()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.SurfaceStrong
            };
            card.Paint += (sender, args) =>
            {
                using (var pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        Math.Max(0, card.ClientSize.Width - 1),
                        Math.Max(0, card.ClientSize.Height - 1));
                }
            };
            return card;
        }

        private static Panel CreateInputHost(Control input)
        {
            Color normalBorder = UiPalette.StrokeStrong;
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 8),
                Padding = new Padding(1),
                BackColor = normalBorder
            };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = UiPalette.Input };
            input.BackColor = UiPalette.Input;
            input.Location = input is ComboBox ? new Point(8, 7) : new Point(10, 9);
            input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            inner.Controls.Add(input);
            inner.Resize += (sender, args) => input.Width = Math.Max(1, inner.ClientSize.Width - input.Left - 8);
            input.Enter += (sender, args) => host.BackColor = UiPalette.Focus;
            input.Leave += (sender, args) => host.BackColor = normalBorder;
            host.Controls.Add(inner);
            return host;
        }

        private static Panel CreatePlainHost(Control input)
        {
            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            input.Location = new Point(0, 16);
            host.Controls.Add(input);
            return host;
        }

        private static void StylePrimaryButton(Button button)
        {
            StyleButton(button, UiPalette.TextInverse, UiPalette.Brand, 0, UiPalette.Brand);
            button.FlatAppearance.MouseOverBackColor = UiPalette.BrandHover;
            button.FlatAppearance.MouseDownBackColor = UiPalette.BrandPressed;
        }

        private static void StyleSecondaryButton(Button button)
        {
            StyleButton(button, UiPalette.TextPrimary, UiPalette.SurfaceStrong, 1, UiPalette.StrokeStrong);
            button.FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = UiPalette.SurfacePressed;
        }

        private static void StyleDangerButton(Button button)
        {
            StyleButton(button, UiPalette.Danger, UiPalette.DangerSoft, 1, UiPalette.Danger);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(254, 226, 226);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(254, 202, 202);
        }

        private static void StyleButton(Button button, Color foreground, Color background, int borderSize, Color borderColor)
        {
            button.Font = new Font("Microsoft YaHei UI", 9.75F);
            button.ForeColor = foreground;
            button.BackColor = background;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = borderSize;
            button.FlatAppearance.BorderColor = borderColor;
            button.UseVisualStyleBackColor = false;
            button.Margin = new Padding(4, 0, 0, 0);
        }

        private void FillPermissionItems()
        {
            foreach (PermissionItem item in PermissionItem.CreateAll())
            {
                permissions.Items.Add(item, false);
            }
        }

        private void ReloadAccounts(string selectUserName)
        {
            IReadOnlyList<AccountEditorSnapshot> snapshots = accounts.GetAccounts(out string error);
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(this, error, "账户读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            loading = true;
            accountList.Items.Clear();
            foreach (AccountEditorSnapshot snapshot in snapshots)
            {
                accountList.Items.Add(new AccountListItem(snapshot));
            }
            AccountListItem selected = accountList.Items.Cast<AccountListItem>().FirstOrDefault(item =>
                string.Equals(item.Snapshot.UserName, selectUserName, StringComparison.OrdinalIgnoreCase));
            accountList.SelectedItem = selected ?? accountList.Items.Cast<object>().FirstOrDefault();
            loading = false;
            LoadSelectedAccount();
        }

        private void LoadSelectedAccount()
        {
            if (!(accountList.SelectedItem is AccountListItem item))
            {
                return;
            }
            editing = item.Snapshot;
            loading = true;
            userName.Text = editing.UserName;
            SelectLevel(editing.Level);
            enabled.Checked = editing.Enabled;
            ApplyPermissionChecks(editing.Permissions);
            ApplySystemProtection(editing.IsBuiltInSystem);
            loading = false;
        }

        private void AccountList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!loading)
            {
                LoadSelectedAccount();
            }
        }

        private void NewButton_Click(object sender, EventArgs e)
        {
            editing = new AccountEditorSnapshot
            {
                Id = Guid.Empty,
                Level = AccountLevel.Operator,
                Enabled = true,
                Permissions = AccountPermissionDefaults.ForLevel(AccountLevel.Operator),
                IsBuiltInSystem = false
            };
            accountList.ClearSelected();
            loading = true;
            userName.Clear();
            SelectLevel(AccountLevel.Operator);
            enabled.Checked = true;
            ApplyPermissionChecks(editing.Permissions);
            ApplySystemProtection(false);
            loading = false;
            userName.Focus();
        }

        private void Level_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (loading || !(level.SelectedItem is LevelItem selected))
            {
                return;
            }
            if (editing != null && editing.Id != Guid.Empty && editing.Level != selected.Level
                && MessageBox.Show(this,
                    "修改账户级别将用新级别的默认权限覆盖当前勾选，是否继续？",
                    "修改账户级别",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                loading = true;
                SelectLevel(editing.Level);
                loading = false;
                return;
            }
            IReadOnlyList<string> defaults = AccountPermissionDefaults.ForLevel(selected.Level);
            ApplyPermissionChecks(defaults);
            editing.Level = selected.Level;
            editing.Permissions = defaults;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            if (editing == null || !(level.SelectedItem is LevelItem selected))
            {
                return;
            }
            string name = userName.Text;
            List<string> selectedPermissions = GetCheckedPermissions();
            bool success;
            string error;
            if (editing.Id == Guid.Empty)
            {
                using (var dialog = new FrmAccountPassword("设置账户密码"))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    success = accounts.TryCreateAccount(name, selected.Level, enabled.Checked,
                        selectedPermissions, dialog.PasswordValue, out error);
                }
            }
            else
            {
                success = accounts.TryUpdateAccount(editing.Id, name, selected.Level, enabled.Checked,
                    selectedPermissions, out error);
            }
            if (!success)
            {
                MessageBox.Show(this, error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!CanContinueManaging())
            {
                Close();
                return;
            }
            MessageBox.Show(this, "账户已保存。", "账户与权限", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAccounts(name);
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (editing == null || editing.Id == Guid.Empty)
            {
                ReloadAccounts(null);
                return;
            }
            if (MessageBox.Show(this, $"确定删除账户“{editing.UserName}”吗？", "删除账户",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!accounts.TryDeleteAccount(editing.Id, out string error))
            {
                MessageBox.Show(this, error, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!CanContinueManaging())
            {
                Close();
                return;
            }
            ReloadAccounts(null);
        }

        private void ResetPasswordButton_Click(object sender, EventArgs e)
        {
            if (editing == null || editing.Id == Guid.Empty)
            {
                return;
            }
            using (var dialog = new FrmAccountPassword("重置账户密码"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!accounts.TryResetPassword(editing.Id, dialog.PasswordValue, out string error))
                {
                    MessageBox.Show(this, error, "重置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            if (!CanContinueManaging())
            {
                Close();
                return;
            }
            if (!IsDisposed && !Disposing)
            {
                MessageBox.Show(this, "密码已重置。", "账户与权限", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool CanContinueManaging()
        {
            AccountSessionSnapshot current = accounts.CurrentUser;
            return current != null
                && current.Level == AccountLevel.SystemAdministrator
                && current.Permissions.Contains(
                    PlatformPermissionCodes.AccountManage,
                    StringComparer.Ordinal);
        }

        private void Accounts_Changed(object sender, AccountSessionChangedEventArgs e)
        {
            AccountSessionSnapshot current = e.CurrentUser;
            bool allowed = current != null
                && current.Level == AccountLevel.SystemAdministrator
                && current.Permissions.Contains(PlatformPermissionCodes.AccountManage, StringComparer.Ordinal);
            if (allowed)
            {
                return;
            }
            if (IsHandleCreated)
            {
                BeginInvoke((Action)Close);
            }
        }

        private void ApplyPermissionChecks(IEnumerable<string> values)
        {
            var selected = new HashSet<string>(values ?? Array.Empty<string>(), StringComparer.Ordinal);
            for (int i = 0; i < permissions.Items.Count; i++)
            {
                permissions.SetItemChecked(i,
                    permissions.Items[i] is PermissionItem item && selected.Contains(item.Code));
            }
        }

        private List<string> GetCheckedPermissions()
        {
            return permissions.CheckedItems.Cast<PermissionItem>()
                .Select(item => item.Code)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }

        private void ApplySystemProtection(bool builtIn)
        {
            userName.ReadOnly = builtIn;
            level.Enabled = !builtIn;
            enabled.Enabled = !builtIn;
            permissions.Enabled = !builtIn;
            deleteButton.Enabled = !builtIn;
            saveButton.Enabled = !builtIn;
            resetPasswordButton.Enabled = editing != null && editing.Id != Guid.Empty;
        }

        private void SelectLevel(AccountLevel value)
        {
            level.SelectedItem = level.Items.Cast<LevelItem>().First(item => item.Level == value);
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Padding = new Padding(2, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UiPalette.TextPrimary
            };
        }

        private sealed class AccountListItem
        {
            public AccountListItem(AccountEditorSnapshot snapshot) { Snapshot = snapshot; }
            public AccountEditorSnapshot Snapshot { get; }
            public override string ToString() => $"{Snapshot.UserName}  ·  {LevelItem.GetText(Snapshot.Level)}"
                + (Snapshot.Enabled ? string.Empty : "（停用）");
        }

        private sealed class LevelItem
        {
            public LevelItem(AccountLevel level, string text) { Level = level; Text = text; }
            public AccountLevel Level { get; }
            public string Text { get; }
            public override string ToString() => Text;
            public static string GetText(AccountLevel level) => level == AccountLevel.Operator
                ? "操作员"
                : level == AccountLevel.Engineer ? "工程师" : "系统管理员";
        }

        private sealed class PermissionItem
        {
            public PermissionItem(string code, string text) { Code = code; Text = text; }
            public string Code { get; }
            public string Text { get; }
            public override string ToString() => Text;

            public static IEnumerable<PermissionItem> CreateAll()
            {
                yield return new PermissionItem(PlatformPermissionCodes.ProcessRun, "流程 / 运行控制");
                yield return new PermissionItem(PlatformPermissionCodes.ProcessEdit, "流程 / 编辑配置");
                yield return new PermissionItem(PlatformPermissionCodes.VariableRuntimeWrite, "变量 / 写入运行值");
                yield return new PermissionItem(PlatformPermissionCodes.VariableConfigure, "变量 / 配置定义");
                yield return new PermissionItem(PlatformPermissionCodes.VariableDebug, "变量 / 调试");
                yield return new PermissionItem(PlatformPermissionCodes.MotionOperate, "运动 / 手动操作");
                yield return new PermissionItem(PlatformPermissionCodes.MotionConfigure, "运动 / 工站配置");
                yield return new PermissionItem(PlatformPermissionCodes.IoDebug, "IO / 调试");
                yield return new PermissionItem(PlatformPermissionCodes.IoConfigure, "IO / 配置");
                yield return new PermissionItem(PlatformPermissionCodes.PlcOperate, "PLC / 操作");
                yield return new PermissionItem(PlatformPermissionCodes.PlcConfigure, "PLC / 配置");
                yield return new PermissionItem(PlatformPermissionCodes.CommunicationOperate, "通讯 / 操作");
                yield return new PermissionItem(PlatformPermissionCodes.CommunicationConfigure, "通讯 / 配置");
                yield return new PermissionItem(PlatformPermissionCodes.HardwareConfigure, "硬件 / 控制卡配置");
                yield return new PermissionItem(PlatformPermissionCodes.AlarmConfigure, "报警 / 配置");
                yield return new PermissionItem(PlatformPermissionCodes.DataStructureConfigure, "数据结构 / 配置");
                yield return new PermissionItem(PlatformPermissionCodes.PlatformEditorOpen, "平台 / 使用编辑器工作区");
                yield return new PermissionItem(PlatformPermissionCodes.PlatformDiagnosticsUse, "平台 / 使用诊断与性能分析");
                yield return new PermissionItem(PlatformPermissionCodes.PlatformAiUse, "平台 / 使用AI助手");
                yield return new PermissionItem(PlatformPermissionCodes.SourceReview, "源码 / 只读审查");
                yield return new PermissionItem(PlatformPermissionCodes.SourceDevelop, "源码 / 修改开发");
                yield return new PermissionItem(PlatformPermissionCodes.ApplicationConfigure, "平台 / 程序设置");
                yield return new PermissionItem(PlatformPermissionCodes.VersionManage, "平台 / 版本管理");
                yield return new PermissionItem(PlatformPermissionCodes.AccountManage, "平台 / 账户管理");
            }
        }
    }
}
