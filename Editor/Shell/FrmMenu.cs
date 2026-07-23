// 模块：编辑器 / 外壳。
// 职责范围：页面装配、菜单、工具栏、导航、生命周期和程序设置。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMenu : Form
    {
        private const int DefaultMenuButtonWidth = 143;
        private const int MenuIconSize = 50;
        private const int MenuGroupGap = 14;
        private readonly Font normalMenuButtonFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular);
        private readonly Font compactMenuButtonFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular);
        private readonly Button version_Page = new Button();
        private readonly Button runtimeDiagnostics_Page = new Button();
        private readonly Dictionary<Button, UiIconKind> menuIcons = new Dictionary<Button, UiIconKind>();
        private readonly Dictionary<Button, Image> inactiveMenuIconImages = new Dictionary<Button, Image>();
        private readonly Dictionary<Button, Image> activeMenuIconImages = new Dictionary<Button, Image>();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();
        private Button activeMenuButton;
        private bool aiAssistantActive;
        private bool runtimeDiagnosticsButtonVisible;

        private const int IoDebugPageIndex = 2;
        private const int VariablePageIndex = 3;
        private const int CommunicationPageIndex = 7;
        private const int PlcPageIndex = 8;
        private const int VersionPageIndex = 9;

        public FrmMenu()
        {
            InitializeComponent();
            ConfigureVersionButton();
            ConfigureRuntimeDiagnosticsButton();
            ConfigureMenuAppearance();
            ConfigureAdaptiveMenu();
            SetActiveMenuButton(process_Page);
            bool diagnosticsEnabled = AppConfigStorage.TryGetCached(out AppConfig config, out _)
                && config.EnableRuntimeDiagnostics;
            SetRuntimeDiagnosticsEnabled(diagnosticsEnabled);
            Disposed += (sender, args) =>
            {
                hoverAnimator.Dispose();
                foreach (Image image in inactiveMenuIconImages.Values)
                {
                    image.Dispose();
                }
                foreach (Image image in activeMenuIconImages.Values)
                {
                    image.Dispose();
                }
                inactiveMenuIconImages.Clear();
                activeMenuIconImages.Clear();
            };
        }

        private void ConfigureVersionButton()
        {
            version_Page.Name = "version_Page";
            version_Page.Text = "版本管理";
            version_Page.Click += version_Page_Click;
            panel1.Controls.Add(version_Page);
        }

        private void ConfigureRuntimeDiagnosticsButton()
        {
            runtimeDiagnostics_Page.Name = "runtimeDiagnostics_Page";
            runtimeDiagnostics_Page.Text = "智能诊断";
            runtimeDiagnostics_Page.Click += (sender, args) => Workspace.Main?.ShowRuntimeDiagnostics();
            panel1.Controls.Add(runtimeDiagnostics_Page);
        }

        internal void SetRuntimeDiagnosticsEnabled(bool enabled)
        {
            runtimeDiagnosticsButtonVisible = enabled;
            runtimeDiagnostics_Page.Enabled = enabled;
            runtimeDiagnostics_Page.Visible = enabled;
            if (!enabled && activeMenuButton == runtimeDiagnostics_Page)
            {
                SetActiveMenuButton(process_Page);
            }
            AdjustMenuButtons();
        }

        private void ConfigureMenuAppearance()
        {
            menuIcons.Add(process_Page, UiIconKind.Process);
            menuIcons.Add(station_Page, UiIconKind.Station);
            menuIcons.Add(value_Page, UiIconKind.Variable);
            menuIcons.Add(Io_Page, UiIconKind.Sliders);
            menuIcons.Add(communication_Page, UiIconKind.Communication);
            menuIcons.Add(Plc_Page, UiIconKind.Plc);
            menuIcons.Add(valueDebug_Page, UiIconKind.Debug);
            menuIcons.Add(Card_Page, UiIconKind.ControlCard);
            menuIcons.Add(version_Page, UiIconKind.History);
            menuIcons.Add(aiAssistant_Page, UiIconKind.Ai);
            menuIcons.Add(runtimeDiagnostics_Page, UiIconKind.Monitor);

            panel1.BackColor = UiPalette.Navigation;
            Io_Page.Text = "I/O 调试";
            aiAssistant_Page.Text = "AI 助手";
            foreach (Button button in GetMenuButtons())
            {
                string label = button.Text;
                button.BackColor = UiPalette.Navigation;
                button.ForeColor = UiPalette.NavigationTextMuted;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = UiPalette.Navigation;
                button.FlatAppearance.MouseDownBackColor = UiPalette.Navigation;
                button.UseVisualStyleBackColor = false;
                button.TabStop = false;
                SetMenuIcon(button, false);
                button.Tag = label;
                button.AccessibleName = label;
                button.Text = string.Empty;
                button.Image = null;
                button.Padding = Padding.Empty;
                button.Paint += MenuButton_Paint;
                Button menuButton = button;
                hoverAnimator.Attach(
                    menuButton,
                    () => IsMenuButtonActive(menuButton) ? UiPalette.NavigationActive : UiPalette.Navigation,
                    UiPalette.NavigationHover,
                    true);
            }
        }

        private void SetActiveMenuButton(Button button)
        {
            if (activeMenuButton != null)
            {
                activeMenuButton.BackColor = UiPalette.Navigation;
                activeMenuButton.ForeColor = UiPalette.NavigationTextMuted;
                SetMenuIcon(activeMenuButton, false);
                activeMenuButton.Invalidate();
            }
            activeMenuButton = button;
            if (activeMenuButton != null)
            {
                activeMenuButton.BackColor = UiPalette.NavigationActive;
                activeMenuButton.ForeColor = UiPalette.TextInverse;
                SetMenuIcon(activeMenuButton, true);
                activeMenuButton.Invalidate();
                if (activeMenuButton.IsHandleCreated)
                {
                    activeMenuButton.Update();
                }
            }
            hoverAnimator.RefreshRestingColors();
        }

        private bool IsMenuButtonActive(Button button)
        {
            return button == activeMenuButton;
        }

        private void SetMenuIcon(Button button, bool active)
        {
            if (!menuIcons.TryGetValue(button, out UiIconKind icon))
            {
                return;
            }
            Dictionary<Button, Image> cache = active ? activeMenuIconImages : inactiveMenuIconImages;
            if (!cache.ContainsKey(button))
            {
                cache[button] = UiIconFactory.CreateNavigation(icon, MenuIconSize, active);
            }
        }

        private void MenuButton_Paint(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            string label = button.Tag as string ?? string.Empty;
            Dictionary<Button, Image> cache = IsMenuButtonActive(button)
                ? activeMenuIconImages
                : inactiveMenuIconImages;
            cache.TryGetValue(button, out Image icon);
            if (button == activeMenuButton && button.ClientSize.Width > 0 && button.ClientSize.Height > 0)
            {
                using (LinearGradientBrush activeBackground = new LinearGradientBrush(
                    button.ClientRectangle,
                    UiPalette.NavigationActiveHighlight,
                    UiPalette.NavigationActive,
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(activeBackground, button.ClientRectangle);
                }
            }
            TextFormatFlags textFlags = TextFormatFlags.SingleLine
                | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix;
            int textHeight = Math.Max(button.Font.Height + 4, 18);
            int iconSize = icon == null
                ? 0
                : Math.Min(icon.Width, Math.Max(20, Math.Min(button.ClientSize.Width - 24, button.ClientSize.Height - textHeight - 12)));
            int gap = icon == null || string.IsNullOrEmpty(label) ? 0 : 6;
            int contentHeight = iconSize + gap + textHeight;
            int contentTop = Math.Max(0, (button.ClientSize.Height - 3 - contentHeight) / 2) + button.Padding.Top;
            int iconLeft = Math.Max(0, (button.ClientSize.Width - iconSize) / 2);
            if (icon != null)
            {
                InterpolationMode interpolationMode = e.Graphics.InterpolationMode;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(icon, new Rectangle(iconLeft, contentTop, iconSize, iconSize));
                e.Graphics.InterpolationMode = interpolationMode;
            }
            if (button == aiAssistant_Page && aiAssistantActive && icon != null)
            {
                int statusSize = Math.Max(5, iconSize / 9);
                int statusLeft = Math.Min(
                    button.ClientSize.Width - statusSize - 4,
                    iconLeft + iconSize - statusSize);
                using (SolidBrush statusBrush = new SolidBrush(UiPalette.NavigationAccent))
                using (Pen statusBorder = new Pen(UiPalette.Navigation, 1.5F))
                {
                    e.Graphics.FillEllipse(statusBrush, statusLeft, contentTop + 1, statusSize, statusSize);
                    e.Graphics.DrawEllipse(statusBorder, statusLeft, contentTop + 1, statusSize, statusSize);
                }
            }
            Rectangle textBounds = new Rectangle(
                6,
                contentTop + iconSize + gap,
                Math.Max(1, button.ClientSize.Width - 12),
                textHeight);
            TextRenderer.DrawText(e.Graphics, label, button.Font, textBounds, button.ForeColor, textFlags);

            if (button == activeMenuButton)
            {
                using (SolidBrush brush = new SolidBrush(UiPalette.NavigationAccent))
                {
                    e.Graphics.FillRectangle(
                        brush,
                        0,
                        button.ClientSize.Height - 3,
                        button.ClientSize.Width,
                        3);
                }
            }
        }

        private void ConfigureAdaptiveMenu()
        {
            foreach (Button button in GetMenuButtons())
            {
                button.AutoEllipsis = false;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.Dock = DockStyle.None;
            }
            panel1.AutoScroll = false;
            panel1.Paint += MenuPanel_Paint;
            panel1.Resize += (sender, args) => AdjustMenuButtons();
            Shown += (sender, args) => AdjustMenuButtons();
            AdjustMenuButtons();
        }

        private Button[] GetMenuButtons()
        {
            return new[]
            {
                process_Page,
                station_Page,
                value_Page,
                Io_Page,
                communication_Page,
                Plc_Page,
                Card_Page,
                valueDebug_Page,
                version_Page,
                aiAssistant_Page,
                runtimeDiagnostics_Page
            };
        }

        private void AdjustMenuButtons()
        {
            Button[] allButtons = GetMenuButtons();
            var visibleButtons = new List<Button>(allButtons.Length);
            foreach (Button button in allButtons)
            {
                // 构造阶段父窗体尚未显示，Control.Visible 会连带返回 false。
                // 这里只按按钮自身的功能状态筛选，首次显示时再使用真实尺寸重排。
                if (button != runtimeDiagnostics_Page || runtimeDiagnosticsButtonVisible)
                {
                    visibleButtons.Add(button);
                }
            }
            Button[] buttons = visibleButtons.ToArray();
            if (buttons.Length == 0)
            {
                return;
            }

            int availableWidth = Math.Max(0, panel1.ClientSize.Width);
            int groupGapCount = 0;
            for (int i = 1; i < buttons.Length; i++)
            {
                if (StartsNewMenuGroup(buttons[i]))
                {
                    groupGapCount++;
                }
            }
            int buttonAreaWidth = Math.Max(buttons.Length, availableWidth - groupGapCount * MenuGroupGap);
            int targetWidth = availableWidth > 0
                ? Math.Max(1, buttonAreaWidth / buttons.Length)
                : DefaultMenuButtonWidth;
            Font buttonFont = targetWidth >= 110 ? normalMenuButtonFont : compactMenuButtonFont;
            int targetHeight = Math.Max(1, panel1.ClientSize.Height);
            int widthRemainder = Math.Max(0, buttonAreaWidth - targetWidth * buttons.Length);
            int left = 0;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0 && StartsNewMenuGroup(buttons[i]))
                {
                    left += MenuGroupGap;
                }
                int width = targetWidth + (i < widthRemainder ? 1 : 0);
                buttons[i].Font = buttonFont;
                buttons[i].SetBounds(left, 0, width, targetHeight);
                left += width;
            }
            panel1.Invalidate();
        }

        private bool StartsNewMenuGroup(Button button)
        {
            return button == Io_Page || button == valueDebug_Page;
        }

        private void MenuPanel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen dividerPen = new Pen(UiPalette.NavigationBorder, 1F))
            {
                foreach (Button groupStart in new[] { Io_Page, valueDebug_Page })
                {
                    if (!groupStart.Visible || groupStart.Left <= 0)
                    {
                        continue;
                    }
                    int dividerX = groupStart.Left - MenuGroupGap / 2;
                    e.Graphics.DrawLine(dividerPen, dividerX, 18, dividerX, Math.Max(18, panel1.ClientSize.Height - 18));
                }
            }
        }

        private void value_Page_Click(object sender, EventArgs e)
        {
            ShowVariableWorkspace();
        }

        private void ShowVariableWorkspace()
        {
            ShowEmbeddedMainPage(Workspace.Value, value_Page, VariablePageIndex);
        }

        private void aiAssistant_Page_Click(object sender, EventArgs e)
        {
            Workspace.Main.EnsureAiInfrastructureStarted();
            // 切换 ai_panel 显示/隐藏，不切换主页面（curPage 不变）
            var p = Workspace.Main.ai_panel;
            if (p.Visible)
            {
                Workspace.Main.SuspendLayout();
                try
                {
                    p.Visible = false;
                    p.Width = 0;
                    aiAssistantActive = false;
                    aiAssistant_Page.BackColor = UiPalette.Navigation;
                    SetMenuIcon(aiAssistant_Page, false);
                    aiAssistant_Page.Invalidate();
                    hoverAnimator.RefreshRestingColors();
                    SetNoteColumnVisible(true);
                }
                finally
                {
                    Workspace.Main.ResumeLayout(true);
                }
            }
            else
            {
                // 优先使用 40% 宽度；窗口较小时为主工作区保留空间，避免遮挡或裁掉操作控件。
                bool assistantLoadedBeforeOpen = Workspace.AiAssistant.IsViewLoaded;
                Workspace.Main.SuspendLayout();
                try
                {
                    Workspace.Main.UpdateAiPanelWidth();
                    p.Visible = true;
                    if (!assistantLoadedBeforeOpen)
                    {
                        Workspace.AiAssistant.Show();
                    }
                    aiAssistantActive = true;
                    aiAssistant_Page.BackColor = UiPalette.Navigation;
                    SetMenuIcon(aiAssistant_Page, true);
                    aiAssistant_Page.Invalidate();
                    hoverAnimator.RefreshRestingColors();
                    SetNoteColumnVisible(false);
                }
                finally
                {
                    Workspace.Main.ResumeLayout(true);
                }
                if (Workspace.AiAssistant != null
                    && !Workspace.AiAssistant.IsDisposed
                    && assistantLoadedBeforeOpen
                    && Workspace.AiAssistant.IsViewLoaded)
                {
                    Workspace.AiAssistant.RefreshAssistantView();
                }
            }
        }

        public void ShowAiAssistant()
        {
            if (Workspace.Main?.ai_panel != null && !Workspace.Main.ai_panel.Visible)
            {
                aiAssistant_Page_Click(aiAssistant_Page, EventArgs.Empty);
            }
        }

        private void version_Page_Click(object sender, EventArgs e)
        {
            ShowEmbeddedMainPage(Workspace.GetOrCreateVersionManager(), version_Page, VersionPageIndex);
        }

        // AI 助手打开时隐藏流程列表的"备注"列，腾出空间给助手窗体
        private void SetNoteColumnVisible(bool visible)
        {
            if (Workspace.DataGrid?.dataGridView1 != null)
            {
                Workspace.DataGrid.dataGridView1.SetNoteColumnVisible(visible);
            }
        }

        private void Card_Page_Click(object sender, EventArgs e)
        {
            ShowIoConfigurationWorkspace();
        }

        internal void ShowIoConfigurationWorkspace()
        {
            if (Workspace.CurrentPage != 5)
            {
                Workspace.Main.UpdateEditorWorkspaceLayout(() =>
                {
                    Workspace.CurrentPage = 5;
                    SetActiveMenuButton(Card_Page);
                    SetMainPanelScrollSize(Size.Empty);
                    Workspace.Main.ShowEditorWorkspace();
                    Workspace.Main.panel_Info.Visible = false;

                    if (!Workspace.Main.DataGrid_panel.Controls.Contains(Workspace.IO))
                    {
                        Workspace.Main.loadFillForm(Workspace.Main.DataGrid_panel, Workspace.IO);
                    }
                    if (!Workspace.Main.treeView_panel.Controls.Contains(Workspace.Card))
                    {
                        Workspace.Main.loadFillForm(Workspace.Main.treeView_panel, Workspace.Card);
                    }

                    Workspace.Main.ToolBar_panel.Visible = true;
                    Workspace.Main.treeView_panel.Visible = true;
                    Workspace.Main.inspector_panel.Visible = true;
                    Workspace.Main.DataGrid_panel.Visible = true;
                    Workspace.Main.panel_Info.Visible = false;
                    Workspace.Main.state_panel.Visible = true;

                    Workspace.IO.Visible = true;
                    Workspace.Card.Visible = true;
                    Workspace.DataGrid.Visible = false;
                    Workspace.Proc.Visible = false;
                    Workspace.Card.BringToFront();
                    Workspace.IO.BringToFront();

                    Workspace.ToolBar.SetProcessRunControlsVisible(false);
                    Workspace.ToolBar.btnStopAll.Visible = false;
                    Workspace.ToolBar.btnAlarm.Visible = false;
                    Workspace.ToolBar.btnSearch.Visible = false;
                    Workspace.ToolBar.btnIOMonitor.Visible = true;
                    Workspace.ToolBar.btnIOMonitor.Enabled = true;
                    Workspace.ToolBar.btnIOMonitor.Text = Workspace.IO.IsIOMonitoring ? "停止监视" : "IO监视";
                });
            }
        }


        private void process_Page_Click(object sender, EventArgs e)
        {
            ShowProcessWorkspace();
        }

        internal void ShowProcessWorkspace()
        {
            if (Workspace.CurrentPage != 0)
            {
                Workspace.Main.UpdateEditorWorkspaceLayout(() =>
                {
                    Workspace.CurrentPage = 0;
                    SetActiveMenuButton(process_Page);
                    SetMainPanelScrollSize(Size.Empty);
                    Workspace.Main.ShowEditorWorkspace();
                    Workspace.Main.panel_Info.Visible = true;

                    if (!Workspace.Main.DataGrid_panel.Controls.Contains(Workspace.DataGrid))
                    {
                        Workspace.Main.loadFillForm(Workspace.Main.DataGrid_panel, Workspace.DataGrid);
                    }
                    if (!Workspace.Main.treeView_panel.Controls.Contains(Workspace.Proc))
                    {
                        Workspace.Main.loadFillForm(Workspace.Main.treeView_panel, Workspace.Proc);
                    }

                    Workspace.Main.ToolBar_panel.Visible = true;
                    Workspace.Main.treeView_panel.Visible = true;
                    Workspace.Main.inspector_panel.Visible = true;
                    Workspace.Main.DataGrid_panel.Visible = true;
                    Workspace.Main.panel_Info.Visible = true;
                    Workspace.Main.state_panel.Visible = true;

                    Workspace.DataGrid.Visible = true;
                    Workspace.Proc.Visible = true;
                    if (Workspace.Main.DataGrid_panel.Controls.Contains(Workspace.IO))
                    {
                        Workspace.IO.Visible = false;
                    }
                    if (Workspace.Main.treeView_panel.Controls.Contains(Workspace.Card))
                    {
                        Workspace.Card.Visible = false;
                    }
                    Workspace.ToolBar.SetProcessRunControlsVisible(true);
                    Workspace.ToolBar.btnStopAll.Visible = true;
                    Workspace.ToolBar.btnAlarm.Visible = true;
                    Workspace.ToolBar.btnSearch.Visible = true;

                    Workspace.ToolBar.btnStopAll.Enabled = true;
                    int procIndex = Workspace.Proc.SelectedProcNum;
                    EngineSnapshot snapshot = procIndex >= 0
                        ? Workspace.Runtime.ProcessEngine.GetSnapshot(procIndex)
                        : null;
                    Workspace.ToolBar.ApplyProcessRunState(
                        snapshot?.State ?? ProcRunState.Ready);
                    Workspace.ToolBar.btnLocate.Enabled = true;
                    Workspace.ToolBar.btnSearch.Enabled = true;
                    Workspace.ToolBar.btnAlarm.Enabled = true;
                    Workspace.ToolBar.btnIOMonitor.Visible = false;
                    Workspace.IO.StopIOMonitor();
                    Workspace.ToolBar.btnIOMonitor.Text = "IO监视";
                    Workspace.RestoreProcessEditorPresentation();
                });
            }
        }

        private void station_Page_Click(object sender, EventArgs e)
        {
            if (Workspace.CurrentPage != 1)
            {
                Workspace.CurrentPage = 1;
                SetActiveMenuButton(station_Page);
                SetMainPanelScrollSize(Size.Empty);
                if (!Workspace.Station.panel1.Controls.Contains(Workspace.Control))
                {
                    Workspace.Main.loadFillForm(Workspace.Station.panel1, Workspace.Control);
                }
                Workspace.Control.comboBox1.DisplayMember = "Name";
                Workspace.Control.comboBox1.DataSource = Workspace.Card.dataStation;
                Workspace.Main.ShowWorkspacePage(Workspace.Station);

                Workspace.ToolBar.btnIOMonitor.Visible = false;
                Workspace.IO.StopIOMonitor();
                Workspace.ToolBar.btnIOMonitor.Text = "IO监视";
             
            }
        }

        private void communication_Page_Click(object sender, EventArgs e)
        {
            ShowEmbeddedMainPage(Workspace.Communication, communication_Page, CommunicationPageIndex);
        }

        private void Io_Page_Click(object sender, EventArgs e)
        {
            ShowIoDebugWorkspace();
        }

        private void ShowIoDebugWorkspace()
        {
            ShowEmbeddedMainPage(Workspace.IODebug, Io_Page, IoDebugPageIndex);
        }

        private void valueDebug_Page_Click(object sender, EventArgs e)
        {
            if (Workspace.CurrentPage != 6)
            {
                Workspace.CurrentPage = 6;
                SetActiveMenuButton(valueDebug_Page);
                SetMainPanelScrollSize(Size.Empty);
                Workspace.Main.ShowWorkspacePage(Workspace.ValueDebug);

                Workspace.ToolBar.btnIOMonitor.Visible = false;
                Workspace.IO.StopIOMonitor();
                Workspace.ToolBar.btnIOMonitor.Text = "IO监视";
            }
        }

        private void Plc_Page_Click(object sender, EventArgs e)
        {
            ShowEmbeddedMainPage(Workspace.GetOrCreatePlc(), Plc_Page, PlcPageIndex);
        }

        internal void ShowDetachableWorkspacePage(Form page)
        {
            if (ReferenceEquals(page, Workspace.Value))
            {
                ShowVariableWorkspace();
                return;
            }
            if (ReferenceEquals(page, Workspace.IODebug))
            {
                ShowIoDebugWorkspace();
                return;
            }
            throw new ArgumentException("页面不支持弹出窗口。", nameof(page));
        }

        private void ShowEmbeddedMainPage(Form page, Button menuButton, int pageIndex)
        {
            if (page == null || page.IsDisposed)
            {
                return;
            }
            if (Workspace.Main.TryActivateDetachedWorkspacePage(page))
            {
                return;
            }
            if (Workspace.CurrentPage == pageIndex && page.Visible)
            {
                page.Focus();
                return;
            }

            Workspace.CurrentPage = pageIndex;
            SetActiveMenuButton(menuButton);
            SetMainPanelScrollSize(page.MinimumSize);
            Workspace.Main.ShowWorkspacePage(page);

            Workspace.ToolBar.btnIOMonitor.Visible = false;
            Workspace.IO.StopIOMonitor();
            Workspace.ToolBar.btnIOMonitor.Text = "IO监视";
        }

        private void SetMainPanelScrollSize(Size minimumSize)
        {
            Workspace.Main.main_panel.AutoScrollMinSize = minimumSize;
            Workspace.Main.main_panel.AutoScrollPosition = Point.Empty;
        }

    }
  
}
