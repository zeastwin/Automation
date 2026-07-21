using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class FrmDataBreakpoints : Form
    {
        private sealed class ResourceOption
        {
            public Guid Id { get; set; }
            public string Text { get; set; }

            public override string ToString()
            {
                return Text ?? string.Empty;
            }
        }

        private sealed class StateOption
        {
            public ProcRunState State { get; set; }
            public string Text { get; set; }

            public override string ToString()
            {
                return Text ?? State.ToString();
            }
        }

        private readonly FrmMain owner;
        private readonly DataBreakpointService service;
        private readonly DataGridView grid = new DataGridView();
        private readonly ComboBox variableCombo = new ComboBox();
        private readonly ComboBox variablePauseProcCombo = new ComboBox();
        private readonly ComboBox observedProcCombo = new ComboBox();
        private readonly ComboBox targetStateCombo = new ComboBox();
        private readonly ComboBox statePauseProcCombo = new ComboBox();
        private readonly Label statusLabel = new Label();
        private readonly Button locateButton = new Button();
        private readonly Button removeButton = new Button();
        private readonly Button clearButton = new Button();
        private readonly Button refreshButton = new Button();
        private bool refreshing;

        public FrmDataBreakpoints(FrmMain owner, DataBreakpointService service)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            InitializeView();
            service.RulesChanged += Service_RulesChanged;
            service.BreakpointHit += Service_BreakpointHit;
            Load += (sender, args) => RefreshAll();
            FormClosed += FrmDataBreakpoints_FormClosed;
        }

        private void InitializeView()
        {
            Text = "数据断点";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 560);
            Size = new Size(1120, 680);
            BackColor = UiPalette.Background;
            ForeColor = UiPalette.TextPrimary;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);

            TabControl addTabs = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 146,
                Padding = new Point(18, 7)
            };
            addTabs.TabPages.Add(CreateVariableTab());
            addTabs.TabPages.Add(CreateProcessStateTab());

            ConfigureGrid();
            Panel bottomPanel = CreateBottomPanel();
            Controls.Add(grid);
            Controls.Add(bottomPanel);
            Controls.Add(addTabs);
        }

        private TabPage CreateVariableTab()
        {
            TabPage page = new TabPage("变量变化")
            {
                BackColor = UiPalette.Surface,
                Padding = new Padding(12)
            };
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ConfigureCombo(variableCombo);
            ConfigureCombo(variablePauseProcCombo);
            Button addButton = CreateButton("添加断点", true);
            addButton.Click += AddVariableBreakpoint_Click;
            layout.Controls.Add(CreateLabel("变量"), 0, 0);
            layout.Controls.Add(variableCombo, 1, 0);
            layout.Controls.Add(CreateLabel("暂停流程"), 2, 0);
            layout.Controls.Add(variablePauseProcCombo, 3, 0);
            layout.Controls.Add(addButton, 4, 0);
            Label note = new Label
            {
                Text = "变量通过流程指令、HMI、PLC、EW-AI 或人工调试发生真实变化时命中；重复写入相同值不会触发。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiPalette.TextSecondary,
                Padding = new Padding(4, 0, 0, 0)
            };
            layout.Controls.Add(note, 0, 1);
            layout.SetColumnSpan(note, 5);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage CreateProcessStateTab()
        {
            TabPage page = new TabPage("流程状态")
            {
                BackColor = UiPalette.Surface,
                Padding = new Padding(12)
            };
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ConfigureCombo(observedProcCombo);
            ConfigureCombo(targetStateCombo);
            ConfigureCombo(statePauseProcCombo);
            Button addButton = CreateButton("添加断点", true);
            addButton.Click += AddProcessStateBreakpoint_Click;
            layout.Controls.Add(CreateLabel("观察流程"), 0, 0);
            layout.Controls.Add(observedProcCombo, 1, 0);
            layout.Controls.Add(CreateLabel("进入状态"), 2, 0);
            layout.Controls.Add(targetStateCombo, 3, 0);
            layout.Controls.Add(CreateLabel("暂停流程"), 4, 0);
            layout.Controls.Add(statePauseProcCombo, 5, 0);
            layout.Controls.Add(addButton, 6, 0);
            Label note = new Label
            {
                Text = "观察流程发生真实状态迁移时命中。进入停止、报警等非运行状态时，应选择另一个需要暂停的流程。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiPalette.TextSecondary,
                Padding = new Padding(4, 0, 0, 0)
            };
            layout.Controls.Add(note, 0, 1);
            layout.SetColumnSpan(note, 7);
            page.Controls.Add(layout);
            return page;
        }

        private void ConfigureGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = UiPalette.Background;
            grid.BorderStyle = BorderStyle.None;
            grid.ReadOnly = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false;
            grid.AutoGenerateColumns = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.RowTemplate.Height = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.SurfaceSubtle;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.SurfaceSubtle;
            grid.DefaultCellStyle.BackColor = UiPalette.SurfaceStrong;
            grid.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = UiPalette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = UiPalette.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Surface;

            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "启用",
                Width = 56
            });
            grid.Columns.Add(CreateTextColumn("Kind", "类型", 92));
            grid.Columns.Add(CreateTextColumn("Observed", "观察对象", 205));
            grid.Columns.Add(CreateTextColumn("Condition", "条件", 120));
            grid.Columns.Add(CreateTextColumn("PauseTarget", "暂停流程", 160));
            grid.Columns.Add(CreateTextColumn("HitCount", "命中", 64));
            grid.Columns.Add(CreateTextColumn("LastTime", "最后命中", 104));
            DataGridViewTextBoxColumn sourceColumn = CreateTextColumn("LastSource", "最后触发源", 260);
            sourceColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(sourceColumn);

            grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.SelectionChanged += (sender, args) => ShowSelectedHit();
            grid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0)
                {
                    LocateSelectedHit();
                }
            };
        }

        private Panel CreateBottomPanel()
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                BackColor = UiPalette.Surface,
                Padding = new Padding(10, 8, 10, 8)
            };
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            locateButton.Text = "定位触发源";
            removeButton.Text = "删除断点";
            clearButton.Text = "清空";
            refreshButton.Text = "刷新资源";
            foreach (Button button in new[] { locateButton, removeButton, clearButton, refreshButton })
            {
                ConfigureButton(button, button == locateButton);
                actions.Controls.Add(button);
            }
            locateButton.Click += (sender, args) => LocateSelectedHit();
            removeButton.Click += RemoveBreakpoint_Click;
            clearButton.Click += ClearBreakpoints_Click;
            refreshButton.Click += (sender, args) => RefreshAll();

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;
            statusLabel.ForeColor = UiPalette.TextSecondary;
            statusLabel.Text = "数据断点仅在当前程序会话有效。";
            panel.Controls.Add(statusLabel);
            panel.Controls.Add(actions);
            return panel;
        }

        private void RefreshAll()
        {
            RefreshSources();
            RefreshRules(null);
        }

        private void RefreshSources()
        {
            Guid selectedVariable = GetSelectedId(variableCombo);
            Guid selectedVariablePause = GetSelectedId(variablePauseProcCombo);
            Guid selectedObserved = GetSelectedId(observedProcCombo);
            Guid selectedStatePause = GetSelectedId(statePauseProcCombo);
            Guid currentProcId = GetCurrentProcId();

            List<DicValue> valueSnapshots = owner.Runtime.Stores.Values.GetValuesSnapshot();
            List<ResourceOption> variables = valueSnapshots
                .Select(value => new ResourceOption
                {
                    Id = value.Id,
                    Text = $"[{value.Index:D3}] {value.Name} ({value.Type})"
                })
                .ToList();
            if (selectedVariable == Guid.Empty)
            {
                int selectedIndex = owner.frmValue?.GetSelectedVariableSlotIndex() ?? -1;
                selectedVariable = valueSnapshots
                    .FirstOrDefault(value => value.Index == selectedIndex)?.Id ?? Guid.Empty;
            }
            List<ResourceOption> processes = owner.frmProc?.procsList
                .Select((proc, index) => new ResourceOption
                {
                    Id = proc?.head?.Id ?? Guid.Empty,
                    Text = $"[{index}] {proc?.head?.Name}"
                })
                .Where(option => option.Id != Guid.Empty)
                .ToList() ?? new List<ResourceOption>();

            SetOptions(variableCombo, variables, selectedVariable);
            SetOptions(variablePauseProcCombo, processes,
                selectedVariablePause != Guid.Empty ? selectedVariablePause : currentProcId);
            SetOptions(observedProcCombo, processes,
                selectedObserved != Guid.Empty ? selectedObserved : currentProcId);
            SetOptions(statePauseProcCombo, processes,
                selectedStatePause != Guid.Empty ? selectedStatePause : currentProcId);
            if (targetStateCombo.Items.Count == 0)
            {
                targetStateCombo.Items.AddRange(new object[]
                {
                    new StateOption { State = ProcRunState.Running, Text = "运行 Running" },
                    new StateOption { State = ProcRunState.SingleStep, Text = "单步 SingleStep" },
                    new StateOption { State = ProcRunState.Alarming, Text = "报警 Alarming" },
                    new StateOption { State = ProcRunState.Stopping, Text = "停止中 Stopping" },
                    new StateOption { State = ProcRunState.Stopped, Text = "已停止 Stopped" }
                });
                targetStateCombo.SelectedIndex = 0;
            }
        }

        private void RefreshRules(Guid? selectRuleId)
        {
            Guid selectedId = selectRuleId ?? GetSelectedRuleId();
            IReadOnlyList<DataBreakpointRuleSnapshot> rules = service.GetRulesSnapshot();
            refreshing = true;
            try
            {
                grid.Rows.Clear();
                foreach (DataBreakpointRuleSnapshot rule in rules)
                {
                    DataBreakpointHit hit = rule.LastHit;
                    int rowIndex = grid.Rows.Add(
                        rule.Enabled,
                        rule.Kind == DataBreakpointKind.VariableChanged ? "变量变化" : "流程状态",
                        BuildObservedText(rule),
                        BuildConditionText(rule),
                        ResolveProcessText(rule.PauseProcId),
                        rule.HitCount,
                        hit == null ? string.Empty : hit.HitTime.ToString("HH:mm:ss.fff"),
                        hit?.TriggerDescription ?? string.Empty);
                    DataGridViewRow row = grid.Rows[rowIndex];
                    row.Tag = rule.Id;
                    if (!rule.Enabled)
                    {
                        row.DefaultCellStyle.ForeColor = UiPalette.TextDisabled;
                    }
                    if (hit != null)
                    {
                        row.DefaultCellStyle.BackColor = UiPalette.BreakpointSoft;
                    }
                }
                if (selectedId != Guid.Empty)
                {
                    DataGridViewRow selectedRow = grid.Rows.Cast<DataGridViewRow>()
                        .FirstOrDefault(row => row.Tag is Guid id && id == selectedId);
                    if (selectedRow != null)
                    {
                        selectedRow.Selected = true;
                        grid.CurrentCell = selectedRow.Cells[0];
                    }
                }
            }
            finally
            {
                refreshing = false;
            }
            if (grid.Rows.Count == 0)
            {
                statusLabel.Text = "尚未设置数据断点；断点仅在当前程序会话有效。";
            }
            else
            {
                ShowSelectedHit();
            }
        }

        private void AddVariableBreakpoint_Click(object sender, EventArgs e)
        {
            if (!(variableCombo.SelectedItem is ResourceOption variable)
                || !(variablePauseProcCombo.SelectedItem is ResourceOption pauseProc))
            {
                ShowError("请选择变量和暂停目标流程。");
                return;
            }
            if (!service.TryAddVariableBreakpoint(variable.Id, pauseProc.Id, out string error))
            {
                ShowError(error);
                return;
            }
            ShowSuccess($"已添加变量断点：{variable.Text}。" );
        }

        private void AddProcessStateBreakpoint_Click(object sender, EventArgs e)
        {
            if (!(observedProcCombo.SelectedItem is ResourceOption observedProc)
                || !(targetStateCombo.SelectedItem is StateOption state)
                || !(statePauseProcCombo.SelectedItem is ResourceOption pauseProc))
            {
                ShowError("请选择观察流程、目标状态和暂停目标流程。");
                return;
            }
            if (!service.TryAddProcessStateBreakpoint(
                observedProc.Id, state.State, pauseProc.Id, out string error))
            {
                ShowError(error);
                return;
            }
            ShowSuccess($"已添加流程状态断点：{observedProc.Text} -> {state.Text}。" );
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (refreshing || e.RowIndex < 0 || e.ColumnIndex != grid.Columns["Enabled"].Index)
            {
                return;
            }
            DataGridViewRow row = grid.Rows[e.RowIndex];
            if (!(row.Tag is Guid ruleId))
            {
                return;
            }
            bool enabled = row.Cells[e.ColumnIndex].Value is bool value && value;
            service.SetEnabled(ruleId, enabled);
        }

        private void RemoveBreakpoint_Click(object sender, EventArgs e)
        {
            Guid ruleId = GetSelectedRuleId();
            if (ruleId == Guid.Empty)
            {
                ShowError("请选择要删除的断点。");
                return;
            }
            if (service.Remove(ruleId))
            {
                ShowSuccess("数据断点已删除。" );
            }
        }

        private void ClearBreakpoints_Click(object sender, EventArgs e)
        {
            if (service.GetRulesSnapshot().Count == 0)
            {
                return;
            }
            if (MessageBox.Show(this, "清空当前会话的全部数据断点？", "清空数据断点",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            service.Clear();
            ShowSuccess("当前会话的数据断点已清空。" );
        }

        private void LocateSelectedHit()
        {
            Guid ruleId = GetSelectedRuleId();
            if (ruleId == Guid.Empty
                || !service.TryGetRule(ruleId, out DataBreakpointRuleSnapshot rule)
                || rule.LastHit == null)
            {
                ShowError("该断点尚未命中，没有可定位的触发源。");
                return;
            }
            if (!owner.NavigateToDataBreakpointTrigger(rule.LastHit, out string error))
            {
                ShowError(error);
                return;
            }
            ShowSuccess("已定位到最后一次触发位置，可使用工具栏后退返回。" );
        }

        private void ShowSelectedHit()
        {
            Guid ruleId = GetSelectedRuleId();
            if (ruleId != Guid.Empty
                && service.TryGetRule(ruleId, out DataBreakpointRuleSnapshot rule)
                && rule.LastHit != null)
            {
                statusLabel.ForeColor = UiPalette.Breakpoint;
                statusLabel.Text = rule.LastHit.BuildSummary();
                return;
            }
            statusLabel.ForeColor = UiPalette.TextSecondary;
            statusLabel.Text = "数据断点仅在当前程序会话有效；双击已命中断点可定位触发源。";
        }

        private void Service_RulesChanged(object sender, EventArgs e)
        {
            QueueRefresh(null);
        }

        private void Service_BreakpointHit(object sender, DataBreakpointHit hit)
        {
            QueueRefresh(hit?.RuleId);
        }

        private void QueueRefresh(Guid? selectRuleId)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() => RefreshRules(selectRuleId)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private string BuildObservedText(DataBreakpointRuleSnapshot rule)
        {
            if (rule.Kind == DataBreakpointKind.VariableChanged)
            {
                DicValue value = owner.Runtime.Stores.Values.GetValuesSnapshot()
                    .FirstOrDefault(candidate => candidate.Id == rule.VariableId);
                return value == null
                    ? $"已失效变量 {rule.VariableId:D}"
                    : $"[{value.Index:D3}] {value.Name}";
            }
            return ResolveProcessText(rule.ObservedProcId);
        }

        private static string BuildConditionText(DataBreakpointRuleSnapshot rule)
        {
            return rule.Kind == DataBreakpointKind.VariableChanged
                ? "值发生变化"
                : $"进入 {rule.TargetState}";
        }

        private string ResolveProcessText(Guid procId)
        {
            if (owner.frmProc?.procsList != null)
            {
                for (int i = 0; i < owner.frmProc.procsList.Count; i++)
                {
                    Proc proc = owner.frmProc.procsList[i];
                    if (proc?.head?.Id == procId)
                    {
                        return $"[{i}] {proc.head.Name}";
                    }
                }
            }
            return $"已失效流程 {procId:D}";
        }

        private Guid GetCurrentProcId()
        {
            int index = owner.frmProc?.SelectedProcNum ?? -1;
            return index >= 0 && index < (owner.frmProc?.procsList?.Count ?? 0)
                ? owner.frmProc.procsList[index]?.head?.Id ?? Guid.Empty
                : Guid.Empty;
        }

        private Guid GetSelectedRuleId()
        {
            return grid.CurrentRow?.Tag is Guid id ? id : Guid.Empty;
        }

        private static Guid GetSelectedId(ComboBox combo)
        {
            return combo.SelectedItem is ResourceOption option ? option.Id : Guid.Empty;
        }

        private static void SetOptions(ComboBox combo, IList<ResourceOption> options, Guid selectedId)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                combo.Items.AddRange(options.Cast<object>().ToArray());
                ResourceOption selected = options.FirstOrDefault(option => option.Id == selectedId);
                combo.SelectedItem = selected ?? options.FirstOrDefault();
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        private static void ConfigureCombo(ComboBox combo)
        {
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Margin = new Padding(3, 5, 6, 5);
            combo.BackColor = UiPalette.Input;
            combo.ForeColor = UiPalette.TextPrimary;
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiPalette.TextPrimary,
                Margin = new Padding(3)
            };
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static Button CreateButton(string text, bool primary)
        {
            Button button = new Button { Text = text };
            ConfigureButton(button, primary);
            return button;
        }

        private static void ConfigureButton(Button button, bool primary)
        {
            button.Width = 104;
            button.Height = 32;
            button.Margin = new Padding(3);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? UiPalette.Brand : UiPalette.StrokeStrong;
            button.BackColor = primary ? UiPalette.Brand : UiPalette.SurfaceStrong;
            button.ForeColor = primary ? UiPalette.TextInverse : UiPalette.TextPrimary;
            button.UseVisualStyleBackColor = false;
        }

        private void ShowError(string message)
        {
            statusLabel.ForeColor = UiPalette.Danger;
            statusLabel.Text = string.IsNullOrWhiteSpace(message) ? "操作失败。" : message;
        }

        private void ShowSuccess(string message)
        {
            statusLabel.ForeColor = UiPalette.Success;
            statusLabel.Text = message;
        }

        private void FrmDataBreakpoints_FormClosed(object sender, FormClosedEventArgs e)
        {
            service.RulesChanged -= Service_RulesChanged;
            service.BreakpointHit -= Service_BreakpointHit;
        }
    }
}
