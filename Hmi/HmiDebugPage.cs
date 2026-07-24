using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 调试页面。
// 职责范围：复刻旧 DebugApp 的八个调试入口，并且只通过 IAutomationPlatform 使用平台能力。
// 排查入口：子页无数据时检查对应变量名称；动作失败时查看 EquipmentProcessMessageService 错误。

namespace Automation.Hmi
{
    public sealed partial class HmiDebugPage : Form
    {
        private readonly Dictionary<Button, Control> pages =
            new Dictionary<Button, Control>();
        private readonly List<ILegacyDebugPage> refreshablePages =
            new List<ILegacyDebugPage>();
        private IAutomationPlatform platform;
        private EquipmentProcessMessageService processMessages;

        public HmiDebugPage()
        {
            InitializeComponent();
            BuildLegacyLayout();
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
        {
            AttachPlatform(platform, processMessages, null);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages,
            LegacyEquipmentServices equipmentServices)
        {
            this.platform = platform;
            this.processMessages = processMessages;
            foreach (ILegacyDebugPage page in refreshablePages)
            {
                page.Attach(platform, processMessages);
            }
            foreach (LegacyDatabaseControl databasePage in
                refreshablePages.OfType<LegacyDatabaseControl>())
            {
                databasePage.AttachDatabaseService(equipmentServices?.Database);
            }
            RefreshRuntimeView();
        }

        internal void RefreshRuntimeView()
        {
            foreach (ILegacyDebugPage page in refreshablePages)
            {
                if (page.IsPageVisible)
                {
                    page.RefreshView();
                }
            }
        }

        internal void MarkProcessListDirty()
        {
            RefreshRuntimeView();
        }

        internal void UpdateRuntimeState(PlatformRuntimeStatus state, string message)
        {
            RefreshRuntimeView();
        }

        private void BuildLegacyLayout()
        {
            var outer = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 3,
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            var group = new GroupBox { Dock = DockStyle.Fill };
            var content = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var buttonBar = new TableLayoutPanel
            {
                ColumnCount = 8,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            for (int index = 0; index < 8; index++)
            {
                buttonBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5F));
            }

            AddDebugPage(buttonBar, 0, "MES", new LegacyProtocolDebugControl("MES"));
            AddDebugPage(buttonBar, 1, "PDCA", new LegacyProtocolDebugControl("PDCA"));
            AddDebugPage(buttonBar, 2, "Hive", new LegacyProtocolDebugControl("HIVE"));
            AddDebugPage(buttonBar, 3, "PLC", new LegacyPlcDebugControl());
            AddDebugPage(buttonBar, 4, "FingerPrint", new LegacyFingerprintControl());
            AddDebugPage(buttonBar, 5, "Tools", new LegacyToolsControl());
            AddDebugPage(buttonBar, 6, "Set", new LegacySetControl());
            AddDebugPage(buttonBar, 7, "Database", new LegacyDatabaseControl());

            content.Controls.Add(buttonBar, 0, 0);
            content.Controls.Add(pageHost, 0, 1);
            group.Controls.Add(content);
            outer.Controls.Add(group, 1, 1);
            debugRoot.Controls.Add(outer);
            ShowPage(pages.Keys.First());
        }

        private void AddDebugPage(
            TableLayoutPanel buttonBar,
            int column,
            string text,
            Control page)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 10.5F),
                Margin = new Padding(3),
                Text = text,
                UseVisualStyleBackColor = false
            };
            button.Click += (sender, args) => ShowPage(button);
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pages.Add(button, page);
            if (page is ILegacyDebugPage refreshable)
            {
                refreshablePages.Add(refreshable);
            }
            pageHost.Controls.Add(page);
            buttonBar.Controls.Add(button, column, 0);
        }

        private void ShowPage(Button selected)
        {
            foreach (KeyValuePair<Button, Control> pair in pages)
            {
                bool active = ReferenceEquals(pair.Key, selected);
                pair.Key.BackColor = active ? Color.GreenYellow : Color.Gray;
                pair.Value.Visible = active;
                if (active)
                {
                    pair.Value.BringToFront();
                    if (pair.Value is ILegacyDebugPage page)
                    {
                        page.RefreshView();
                    }
                }
            }
        }
    }

    internal interface ILegacyDebugPage
    {
        bool IsPageVisible { get; }

        void Attach(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages);

        void RefreshView();
    }

    internal abstract class LegacyDebugPageBase : UserControl, ILegacyDebugPage
    {
        protected IAutomationPlatform Platform { get; private set; }

        protected EquipmentProcessMessageService ProcessMessages { get; private set; }

        public bool IsPageVisible => Visible;

        public void Attach(
            IAutomationPlatform platform,
            EquipmentProcessMessageService processMessages)
        {
            Platform = platform;
            ProcessMessages = processMessages;
            OnAttached();
        }

        public abstract void RefreshView();

        protected virtual void OnAttached()
        {
        }

        protected bool TryRead(string name, out string value)
        {
            value = string.Empty;
            return Platform != null
                && Platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                && snapshot != null
                && (value = snapshot.Value ?? string.Empty) != null;
        }

        protected void SetValue(string name, object value)
        {
            if (Platform == null)
            {
                return;
            }
            if (!Platform.Values.Set(name, value, out string error))
            {
                MessageBox.Show(FindForm(), error, "变量写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class LegacyProtocolDebugControl : LegacyDebugPageBase
    {
        private readonly string systemName;
        private readonly Label titleLabel;
        private readonly TextBox urlText;
        private readonly ComboBox functionBox;
        private readonly ListView registerList;
        private readonly TextBox sendText;
        private readonly TextBox receiveText;

        internal LegacyProtocolDebugControl(string systemName)
        {
            this.systemName = systemName;
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var functionGroup = new GroupBox { Dock = DockStyle.Fill };
            var functionLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 6,
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            functionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 24F, FontStyle.Bold),
                Text = systemName,
                TextAlign = ContentAlignment.MiddleCenter
            };
            urlText = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 10.5F)
            };
            urlText.Validated += (sender, args) => SaveUrl();
            functionBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("宋体", 10.5F)
            };
            Button execute = CreateButton(systemName == "HIVE" ? "设备选择" : "数据发送");
            Button convert = CreateButton(systemName == "HIVE" ? "更新设备状态" : "数据转换");
            execute.Click += (sender, args) => ExecuteSelectedFunction();
            convert.Click += (sender, args) => ExecuteSecondaryFunction();
            functionLayout.Controls.Add(titleLabel, 0, 0);
            functionLayout.Controls.Add(CreateLabeledControl("URL：", urlText), 0, 1);
            functionLayout.Controls.Add(functionBox, 0, 2);
            functionLayout.Controls.Add(execute, 0, 3);
            functionLayout.Controls.Add(convert, 0, 4);
            functionLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 5);
            functionGroup.Controls.Add(functionLayout);

            registerList = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                View = View.Details
            };
            registerList.Columns.Add("变量", 180);
            registerList.Columns.Add("值", 140);
            registerList.ItemActivate += RegisterList_ItemActivate;
            var registerGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "寄存器"
            };
            registerGroup.Controls.Add(registerList);

            var communicationGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "发送&接收"
            };
            var communicationLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            communicationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            communicationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            sendText = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10F),
                Multiline = true,
                ScrollBars = ScrollBars.Both
            };
            receiveText = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10F),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both
            };
            communicationLayout.Controls.Add(sendText, 0, 0);
            communicationLayout.Controls.Add(receiveText, 0, 1);
            communicationGroup.Controls.Add(communicationLayout);
            root.Controls.Add(functionGroup, 0, 0);
            root.Controls.Add(registerGroup, 1, 0);
            root.Controls.Add(communicationGroup, 2, 0);
            Controls.Add(root);
        }

        public override void RefreshView()
        {
            if (Platform == null)
            {
                return;
            }
            string selected = registerList.SelectedItems.Count > 0
                ? registerList.SelectedItems[0].Text
                : string.Empty;
            registerList.BeginUpdate();
            registerList.Items.Clear();
            foreach (string name in Platform.Values.GetNames()
                .Where(IsSystemVariable)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                if (!Platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                    || snapshot == null)
                {
                    continue;
                }
                var item = new ListViewItem(name);
                item.SubItems.Add(snapshot.Value ?? string.Empty);
                registerList.Items.Add(item);
                if (string.Equals(name, selected, StringComparison.Ordinal))
                {
                    item.Selected = true;
                }
            }
            registerList.EndUpdate();

            if (!urlText.Focused)
            {
                string urlName = Platform.Values.GetNames()
                    .FirstOrDefault(name => IsSystemVariable(name)
                        && name.IndexOf("URL", StringComparison.OrdinalIgnoreCase) >= 0);
                urlText.Tag = urlName;
                urlText.Text = urlName != null && TryRead(urlName, out string url)
                    ? url
                    : string.Empty;
            }
            EquipmentProcessMessageSnapshot snapshotState = ProcessMessages?.GetSnapshot();
            if (snapshotState != null)
            {
                receiveText.Text = string.Join(
                    Environment.NewLine,
                    snapshotState.Logs
                        .Where(line => line.IndexOf(systemName, StringComparison.OrdinalIgnoreCase) >= 0)
                        .TakeLastCompatible(100));
            }
        }

        protected override void OnAttached()
        {
            functionBox.Items.Clear();
            if (ProcessMessages == null)
            {
                return;
            }
            string prefix = systemName == "HIVE" ? "HIVE" : systemName;
            foreach (string function in ProcessMessages.GetRegisteredFunctionNames()
                .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                functionBox.Items.Add(function);
            }
            if (functionBox.Items.Count > 0)
            {
                functionBox.SelectedIndex = 0;
            }
        }

        private bool IsSystemVariable(string name)
        {
            return name.IndexOf(systemName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SaveUrl()
        {
            if (urlText.Tag is string variableName && !string.IsNullOrWhiteSpace(variableName))
            {
                SetValue(variableName, urlText.Text);
            }
        }

        private void ExecuteSelectedFunction()
        {
            if (ProcessMessages == null || !(functionBox.SelectedItem is string functionName))
            {
                return;
            }
            sendText.Text = functionName;
            try
            {
                ProcessMessages.ExecuteMessage(functionName);
                RefreshView();
            }
            catch (Exception ex)
            {
                receiveText.Text = ex.Message;
            }
        }

        private void ExecuteSecondaryFunction()
        {
            if (ProcessMessages == null)
            {
                return;
            }
            string keyword = systemName == "HIVE" ? "更新设备状态" : "数据";
            string functionName = ProcessMessages.GetRegisteredFunctionNames()
                .FirstOrDefault(name =>
                    name.StartsWith(systemName, StringComparison.OrdinalIgnoreCase)
                    && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            if (functionName == null)
            {
                ExecuteSelectedFunction();
                return;
            }
            functionBox.SelectedItem = functionName;
            ExecuteSelectedFunction();
        }

        private void RegisterList_ItemActivate(object sender, EventArgs e)
        {
            if (registerList.SelectedItems.Count == 0)
            {
                return;
            }
            ListViewItem item = registerList.SelectedItems[0];
            using (var dialog = new LegacyValueInputDialog(item.Text, item.SubItems[1].Text))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    SetValue(item.Text, dialog.Value);
                    RefreshView();
                }
            }
        }

        private static Control CreateLabeledControl(string label, Control control)
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            layout.Controls.Add(control, 0, 1);
            return layout;
        }

        private static Button CreateButton(string text)
        {
            return new Button
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 10.5F),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }
    }

    internal sealed class LegacyPlcDebugControl : LegacyDebugPageBase
    {
        private readonly ComboBox filterBox;
        private readonly DataGridView grid;

        internal LegacyPlcDebugControl()
        {
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var toolbar = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            filterBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("宋体", 10.5F)
            };
            filterBox.Items.AddRange(new object[] { "全部PLC变量", "触发位", "结果位", "SN_Code" });
            filterBox.SelectedIndex = 0;
            filterBox.SelectedIndexChanged += (sender, args) => RefreshView();
            Button generate = new Button
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 10.5F),
                Text = "生成机器点位参数"
            };
            generate.Click += Generate_Click;
            toolbar.Controls.Add(filterBox, 0, 0);
            toolbar.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
            toolbar.Controls.Add(generate, 2, 0);
            grid = new DataGridView
            {
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                Dock = DockStyle.Fill,
                RowHeadersVisible = false
            };
            grid.CellEndEdit += Grid_CellEndEdit;
            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(grid, 0, 1);
            Controls.Add(root);
        }

        public override void RefreshView()
        {
            if (Platform == null)
            {
                return;
            }
            string filter = filterBox.SelectedIndex <= 0
                ? string.Empty
                : filterBox.SelectedItem.ToString();
            var rows = new BindingList<LegacyEditableValueRow>();
            foreach (string name in Platform.Values.GetNames()
                .Where(name => name.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("触发位", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("结果位", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(name => filter.Length == 0
                    || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (Platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                    && snapshot != null)
                {
                    rows.Add(new LegacyEditableValueRow
                    {
                        Name = name,
                        Value = snapshot.Value,
                        Type = snapshot.Type,
                        Note = snapshot.Note
                    });
                }
            }
            grid.DataSource = rows;
            if (grid.Columns[nameof(LegacyEditableValueRow.Name)] != null)
            {
                grid.Columns[nameof(LegacyEditableValueRow.Name)].ReadOnly = true;
                grid.Columns[nameof(LegacyEditableValueRow.Type)].ReadOnly = true;
                grid.Columns[nameof(LegacyEditableValueRow.Note)].ReadOnly = true;
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0
                || !(grid.Rows[e.RowIndex].DataBoundItem is LegacyEditableValueRow row)
                || e.ColumnIndex != grid.Columns[nameof(LegacyEditableValueRow.Value)].Index)
            {
                return;
            }
            SetValue(row.Name, row.Value);
        }

        private void Generate_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = "PLC点位设备参数_" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
            })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return;
                }
                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("Name,Value,Type,Note");
                    foreach (DataGridViewRow viewRow in grid.Rows)
                    {
                        if (viewRow.DataBoundItem is LegacyEditableValueRow row)
                        {
                            writer.WriteLine(string.Join(",", new[]
                            {
                                Csv(row.Name), Csv(row.Value), Csv(row.Type), Csv(row.Note)
                            }));
                        }
                    }
                }
            }
        }

        private static string Csv(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }

    internal sealed class LegacyFingerprintControl : LegacyDebugPageBase
    {
        private readonly TextBox userName;
        private readonly TextBox password;
        private readonly Label status;

        internal LegacyFingerprintControl()
        {
            BackColor = Color.White;
            var outer = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 3,
                Dock = DockStyle.Fill
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 18F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 64F));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 18F));
            var group = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "用户登录 / 指纹录取"
            };
            var form = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 6,
                Dock = DockStyle.Fill,
                Padding = new Padding(25)
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            userName = new TextBox { Dock = DockStyle.Fill, Font = new Font("宋体", 12F) };
            password = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 12F),
                UseSystemPasswordChar = true
            };
            form.Controls.Add(CreateLabel("用户"), 0, 0);
            form.Controls.Add(userName, 1, 0);
            form.Controls.Add(CreateLabel("密码"), 0, 1);
            form.Controls.Add(password, 1, 1);
            var roles = new FlowLayoutPanel { Dock = DockStyle.Fill };
            roles.Controls.Add(new RadioButton { Text = "操作员", Checked = true, AutoSize = true });
            roles.Controls.Add(new RadioButton { Text = "管理员", AutoSize = true });
            roles.Controls.Add(new RadioButton { Text = "工程师", AutoSize = true });
            form.SetColumnSpan(roles, 2);
            form.Controls.Add(roles, 0, 2);
            Button login = new Button { Dock = DockStyle.Fill, Text = "登录", Font = new Font("宋体", 11F) };
            Button enroll = new Button { Dock = DockStyle.Fill, Text = "指纹录取", Font = new Font("宋体", 11F) };
            login.Click += AuthenticationUnavailable_Click;
            enroll.Click += AuthenticationUnavailable_Click;
            form.SetColumnSpan(login, 2);
            form.SetColumnSpan(enroll, 2);
            form.Controls.Add(login, 0, 3);
            form.Controls.Add(enroll, 0, 4);
            status = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.Firebrick,
                Text = "新平台尚未公开用户认证/指纹设备接口，界面保留但不会伪造登录成功。",
                TextAlign = ContentAlignment.MiddleCenter
            };
            form.SetColumnSpan(status, 2);
            form.Controls.Add(status, 0, 5);
            group.Controls.Add(form);
            outer.Controls.Add(group, 1, 1);
            Controls.Add(outer);
        }

        public override void RefreshView()
        {
            if (TryRead("登录用户名称", out string current) && !string.IsNullOrWhiteSpace(current))
            {
                status.Text = "当前用户：" + current;
                status.ForeColor = Color.DarkGreen;
            }
        }

        private void AuthenticationUnavailable_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                FindForm(),
                "新平台公开契约中没有用户认证或指纹设备接口，不能绕过认证直接写入登录状态。",
                "认证接口未配置",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 12F),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }
    }

    internal sealed class LegacyToolsControl : LegacyDebugPageBase
    {
        private readonly DateTimePicker startPicker;
        private readonly DateTimePicker endPicker;
        private readonly ComboBox functionBox;
        private readonly DataGridView resultGrid;

        internal LegacyToolsControl()
        {
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var toolbar = new TableLayoutPanel
            {
                ColumnCount = 6,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            toolbar.Controls.Add(CreateToolbarLabel("开始时间"), 0, 0);
            toolbar.Controls.Add(CreateToolbarLabel("结束时间"), 2, 0);
            startPicker = CreateDatePicker();
            endPicker = CreateDatePicker();
            startPicker.Value = DateTime.Today.AddDays(-7);
            toolbar.Controls.Add(startPicker, 1, 0);
            toolbar.Controls.Add(endPicker, 3, 0);
            functionBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            functionBox.Items.AddRange(new object[] { "查询NG产品", "MES_查询", "MES_过站", "PDCA补传" });
            functionBox.SelectedIndex = 0;
            toolbar.Controls.Add(functionBox, 4, 0);
            Button search = new Button { Dock = DockStyle.Fill, Text = "查询" };
            search.Click += (sender, args) => Search();
            toolbar.Controls.Add(search, 5, 0);
            toolbar.SetColumnSpan(search, 1);
            var hint = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Text = "右键产品行可执行：清空数据 / MES_查询 / MES_过站 / PDCA补传",
                TextAlign = ContentAlignment.MiddleLeft
            };
            toolbar.SetColumnSpan(hint, 6);
            toolbar.Controls.Add(hint, 0, 1);

            resultGrid = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("清空数据", null, (sender, args) => resultGrid.DataSource = null);
            menu.Items.Add("MES_查询", null, (sender, args) => ExecuteForSelected("MES流程信息||消息 进站MES查询", "SN_Code-进站位"));
            menu.Items.Add("MES_过站", null, (sender, args) => ExecuteForSelected("MES流程信息||消息 MES过站", "SN_Code-出站位"));
            menu.Items.Add("PDCA补传", null, (sender, args) => ExecuteForSelected("PDCA流程信息||消息 PDCA上传", "PDCA上传SN"));
            resultGrid.ContextMenuStrip = menu;
            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(CreateGroup("NG产品", resultGrid), 0, 1);
            Controls.Add(root);
        }

        public override void RefreshView()
        {
        }

        private void Search()
        {
            var rows = new List<LegacyToolProductRow>();
            DateTime start = startPicker.Value.Date;
            DateTime end = endPicker.Value.Date;
            if (end < start)
            {
                MessageBox.Show(FindForm(), "结束时间不能早于开始时间。", "查询", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            for (DateTime date = start; date <= end; date = date.AddDays(1))
            {
                string path = Path.Combine(
                    @"D:\AutomationLogs",
                    "Hmi",
                    "Equipment",
                    "Production",
                    date.ToString("yyyyMMdd") + "_Output.csv");
                if (!File.Exists(path))
                {
                    continue;
                }
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    reader.ReadLine();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        List<string> fields = LegacyCsv.Parse(line);
                        if (fields.Count != 5
                            || (fields[3].IndexOf("NG", StringComparison.OrdinalIgnoreCase) < 0
                                && !fields[3].Contains("12")))
                        {
                            continue;
                        }
                        rows.Add(new LegacyToolProductRow
                        {
                            Time = fields[0],
                            SN = fields[1],
                            ProcessInfo = fields[2],
                            Result = fields[3],
                            Mode = fields[4]
                        });
                    }
                }
            }
            resultGrid.DataSource = new BindingList<LegacyToolProductRow>(rows);
        }

        private void ExecuteForSelected(string functionName, string snVariable)
        {
            if (resultGrid.SelectedRows.Count == 0
                || !(resultGrid.SelectedRows[0].DataBoundItem is LegacyToolProductRow row)
                || ProcessMessages == null)
            {
                return;
            }
            SetValue(snVariable, row.SN);
            try
            {
                ProcessMessages.ExecuteMessage(functionName);
                MessageBox.Show(FindForm(), "执行完成。", "Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), ex.Message, "Tools 执行失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Control CreateGroup(string text, Control content)
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = text };
            group.Controls.Add(content);
            return group;
        }

        private static Label CreateToolbarLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static DateTimePicker CreateDatePicker()
        {
            return new DateTimePicker
            {
                CustomFormat = "yyyy-MM-dd",
                Dock = DockStyle.Fill,
                Format = DateTimePickerFormat.Custom
            };
        }
    }

    internal sealed class LegacySetControl : LegacyDebugPageBase
    {
        private readonly ComboBox timeZone;
        private readonly Label platformState;

        internal LegacySetControl()
        {
            BackColor = Color.White;
            timeZone = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("宋体", 11F)
            };
            foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
            {
                timeZone.Items.Add(zone.Id);
            }
            timeZone.SelectedItem = TimeZoneInfo.Local.Id;
            timeZone.SelectionChangeCommitted += TimeZone_SelectionChangeCommitted;
            var layout = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 4,
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75F));
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 11F),
                Text = "TimeZone",
                TextAlign = ContentAlignment.MiddleCenter
            }, 0, 1);
            layout.Controls.Add(timeZone, 1, 1);
            Button openPlatform = new Button
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 11F),
                Text = "打开 Automation 平台（"
                    + FrmHmiMain.ShowPlatformEditorShortcutText
                    + "）"
            };
            openPlatform.Click += (sender, args) =>
            {
                try
                {
                    Platform?.ShowPlatformEditor();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), ex.Message, "平台不可用", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            layout.Controls.Add(openPlatform, 1, 2);
            platformState = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.TopCenter
            };
            layout.Controls.Add(platformState, 1, 3);
            Controls.Add(layout);
        }

        public override void RefreshView()
        {
            platformState.Text = Platform == null
                ? "平台未连接"
                : $"平台状态：{Platform.RuntimeStatus}\r\n{Platform.RuntimeMessage}";
        }

        private void TimeZone_SelectionChangeCommitted(object sender, EventArgs e)
        {
            string selected = timeZone.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(selected)
                || string.Equals(selected, TimeZoneInfo.Local.Id, StringComparison.Ordinal))
            {
                return;
            }
            if (MessageBox.Show(
                FindForm(),
                "确定将 Windows 时区切换为“" + selected + "”吗？",
                "设置 TimeZone",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                timeZone.SelectedItem = TimeZoneInfo.Local.Id;
                return;
            }
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "tzutil.exe",
                    Arguments = "/s \"" + selected + "\"",
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit(5000);
                    if (!process.HasExited || process.ExitCode != 0)
                    {
                        string error = process.HasExited
                            ? process.StandardError.ReadToEnd()
                            : "设置时区超时。";
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(error)
                                ? "tzutil 返回失败。"
                                : error.Trim());
                    }
                }
                TimeZoneInfo.ClearCachedData();
                timeZone.SelectedItem = TimeZoneInfo.Local.Id;
            }
            catch (Exception ex)
            {
                timeZone.SelectedItem = TimeZoneInfo.Local.Id;
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "设置时区失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class LegacyDatabaseControl : LegacyDebugPageBase
    {
        private readonly ComboBox dataSource;
        private readonly ComboBox tableSelector;
        private readonly ComboBox queryField;
        private readonly TextBox queryValue;
        private readonly DataGridView filterGrid;
        private readonly DataGridView dataGrid;
        private readonly Label status;
        private LegacyDatabaseService databaseService;
        private DataTable currentData;
        private bool profilesLoaded;
        private bool changingSelection;

        internal LegacyDatabaseControl()
        {
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            var left = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 142F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var filter = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 4,
                Dock = DockStyle.Fill
            };
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dataSource = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            dataSource.SelectedIndexChanged += (sender, args) => LoadTables();
            tableSelector = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            tableSelector.SelectedIndexChanged += (sender, args) => LoadColumnsAndQuery();
            queryField = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            queryValue = new TextBox { Dock = DockStyle.Fill };
            Button search = new Button { Dock = DockStyle.Fill, Text = "查询" };
            search.Click += (sender, args) => QuerySelectedTable();
            filter.Controls.Add(CreateFilterLabel("Database"), 0, 0);
            filter.Controls.Add(dataSource, 1, 0);
            filter.Controls.Add(CreateFilterLabel("TableName"), 0, 1);
            filter.Controls.Add(tableSelector, 1, 1);
            filter.Controls.Add(CreateFilterLabel("查询项"), 0, 2);
            filter.Controls.Add(queryField, 1, 2);
            filter.Controls.Add(CreateFilterLabel("数据"), 0, 3);
            var valueLayout = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill };
            valueLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            valueLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            valueLayout.Controls.Add(queryValue, 0, 0);
            valueLayout.Controls.Add(search, 1, 0);
            filter.Controls.Add(valueLayout, 1, 3);
            filterGrid = CreateGrid(true);
            filterGrid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0
                    && filterGrid.Rows[args.RowIndex].DataBoundItem
                        is LegacyDatabaseTableRow row)
                {
                    tableSelector.SelectedItem = row.TableName;
                }
            };
            left.Controls.Add(filter, 0, 0);
            left.Controls.Add(filterGrid, 0, 1);

            var right = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                Dock = DockStyle.Fill
            };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            dataGrid = CreateGrid(false);
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            Button reject = new Button { Size = new Size(110, 38), Text = "取消修改" };
            Button apply = new Button { Size = new Size(110, 38), Text = "应用修改" };
            reject.Click += (sender, args) => QuerySelectedTable();
            apply.Click += (sender, args) => ApplyChanges();
            actions.Controls.Add(reject);
            actions.Controls.Add(apply);
            right.Controls.Add(dataGrid, 0, 0);
            right.Controls.Add(actions, 0, 1);
            status = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Text = "数据库服务未连接",
                TextAlign = ContentAlignment.MiddleLeft
            };
            right.Controls.Add(status, 0, 2);
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            Controls.Add(root);
        }

        public override void RefreshView()
        {
            if (databaseService == null)
            {
                status.Text = "数据库服务未装载";
                return;
            }
            if (!profilesLoaded)
            {
                LoadProfiles();
            }
        }

        internal void AttachDatabaseService(LegacyDatabaseService service)
        {
            databaseService = service;
            profilesLoaded = false;
            if (IsHandleCreated)
            {
                LoadProfiles();
            }
        }

        private void LoadProfiles()
        {
            profilesLoaded = true;
            changingSelection = true;
            try
            {
                dataSource.Items.Clear();
                if (databaseService == null)
                {
                    status.Text = "数据库服务未装载";
                    return;
                }
                IReadOnlyList<LegacyDatabaseProfile> profiles =
                    databaseService.GetProfiles();
                dataSource.Items.AddRange(profiles.Cast<object>().ToArray());
                if (dataSource.Items.Count == 0)
                {
                    tableSelector.Items.Clear();
                    queryField.Items.Clear();
                    dataGrid.DataSource = null;
                    filterGrid.DataSource = null;
                    status.Text =
                        "未配置数据库服务器/数据库名；旧项目默认变量为数据库服务器0、数据库名0。";
                    return;
                }
                dataSource.SelectedIndex = 0;
            }
            finally
            {
                changingSelection = false;
            }
            LoadTables();
        }

        private void LoadTables()
        {
            if (changingSelection
                || databaseService == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile))
            {
                return;
            }
            changingSelection = true;
            try
            {
                IReadOnlyList<string> tables = databaseService.GetTables(profile);
                tableSelector.Items.Clear();
                tableSelector.Items.AddRange(tables.Cast<object>().ToArray());
                filterGrid.DataSource = new BindingList<LegacyDatabaseTableRow>(
                    tables.Select(name => new LegacyDatabaseTableRow { TableName = name })
                        .ToList());
                tableSelector.SelectedIndex = tableSelector.Items.Count > 0 ? 0 : -1;
                status.Text = tables.Count == 0
                    ? "数据库中没有数据表。"
                    : "已连接：" + profile;
            }
            catch (Exception ex)
            {
                tableSelector.Items.Clear();
                queryField.Items.Clear();
                dataGrid.DataSource = null;
                status.Text = "数据库连接失败：" + ex.Message;
            }
            finally
            {
                changingSelection = false;
            }
            LoadColumnsAndQuery();
        }

        private void LoadColumnsAndQuery()
        {
            if (changingSelection
                || databaseService == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile)
                || !(tableSelector.SelectedItem is string table))
            {
                return;
            }
            changingSelection = true;
            try
            {
                IReadOnlyList<string> columns =
                    databaseService.GetColumns(profile, table);
                queryField.Items.Clear();
                queryField.Items.AddRange(columns.Cast<object>().ToArray());
                queryField.SelectedIndex = queryField.Items.Count > 0 ? 0 : -1;
            }
            catch (Exception ex)
            {
                status.Text = "字段读取失败：" + ex.Message;
                return;
            }
            finally
            {
                changingSelection = false;
            }
            QuerySelectedTable();
        }

        private void QuerySelectedTable()
        {
            if (databaseService == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile)
                || !(tableSelector.SelectedItem is string table))
            {
                return;
            }
            try
            {
                currentData = databaseService.Query(
                    profile,
                    table,
                    queryField.SelectedItem?.ToString(),
                    queryValue.Text.Trim());
                dataGrid.DataSource = currentData;
                status.Text =
                    $"已加载 {currentData.Rows.Count} 行；单次最多显示 500 行。";
            }
            catch (Exception ex)
            {
                dataGrid.DataSource = null;
                currentData = null;
                status.Text = "查询失败：" + ex.Message;
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "数据库查询失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ApplyChanges()
        {
            if (databaseService == null
                || currentData == null
                || !(dataSource.SelectedItem is LegacyDatabaseProfile profile)
                || !(tableSelector.SelectedItem is string table))
            {
                return;
            }
            if (MessageBox.Show(
                FindForm(),
                "确定将新增、修改和删除提交到数据库表“" + table + "”吗？",
                "应用数据库修改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                dataGrid.EndEdit();
                int affected = databaseService.ApplyChanges(
                    profile,
                    table,
                    currentData);
                status.Text =
                    "数据库修改已提交，影响 "
                    + affected.ToString(CultureInfo.InvariantCulture)
                    + " 行。";
                QuerySelectedTable();
            }
            catch (Exception ex)
            {
                status.Text = "提交失败：" + ex.Message;
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "数据库修改失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static DataGridView CreateGrid(bool readOnly)
        {
            return new DataGridView
            {
                AllowUserToAddRows = !readOnly,
                AllowUserToDeleteRows = !readOnly,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                Dock = DockStyle.Fill,
                ReadOnly = readOnly,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
        }

        private static Label CreateFilterLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private sealed class LegacyDatabaseTableRow
        {
            [DisplayName("TableName")]
            public string TableName { get; set; }
        }
    }

    internal sealed class LegacyValueInputDialog : Form
    {
        private readonly TextBox input;

        internal LegacyValueInputDialog(string name, string value)
        {
            Text = name;
            ClientSize = new Size(460, 130);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            input = new TextBox { Dock = DockStyle.Fill, Text = value ?? string.Empty };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            Button cancel = new Button { DialogResult = DialogResult.Cancel, Text = "取消" };
            Button ok = new Button { DialogResult = DialogResult.OK, Text = "确定" };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(input, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal string Value => input.Text;
    }

    internal sealed class LegacyEditableValueRow
    {
        [DisplayName("变量")]
        public string Name { get; set; }

        [DisplayName("当前值")]
        public string Value { get; set; }

        [DisplayName("类型")]
        public string Type { get; set; }

        [DisplayName("说明")]
        public string Note { get; set; }
    }

    internal sealed class LegacyToolProductRow
    {
        [DisplayName("时间")]
        public string Time { get; set; }

        [DisplayName("SN")]
        public string SN { get; set; }

        [DisplayName("流程信息")]
        public string ProcessInfo { get; set; }

        [DisplayName("结果")]
        public string Result { get; set; }

        [DisplayName("模式")]
        public string Mode { get; set; }
    }

    internal sealed class LegacyDatabaseValueRow
    {
        [DisplayName("Name")]
        public string Name { get; set; }

        [DisplayName("Value")]
        public string Value { get; set; }

        [DisplayName("Type")]
        public string Type { get; set; }

        [DisplayName("Scope")]
        public string Scope { get; set; }

        [DisplayName("Note")]
        public string Note { get; set; }

        [Browsable(false)]
        public bool Dirty { get; set; }
    }
}
