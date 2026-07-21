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
        private const int MenuIconSize = 48;
        private readonly Font normalMenuButtonFont = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Regular);
        private readonly Font compactMenuButtonFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
        private readonly Button version_Page = new Button();
        private readonly Button runtimeDiagnostics_Page = new Button();
        private readonly Dictionary<Button, UiIconKind> menuIcons = new Dictionary<Button, UiIconKind>();
        private readonly Dictionary<Button, Image> menuIconImages = new Dictionary<Button, Image>();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();
        private Button activeMenuButton;
        private bool aiAssistantActive;
        private bool runtimeDiagnosticsButtonVisible;

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
                foreach (Image image in menuIconImages.Values)
                {
                    image.Dispose();
                }
                menuIconImages.Clear();
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
            runtimeDiagnostics_Page.Click += (sender, args) => SF.mainfrm?.ShowRuntimeDiagnostics();
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
            foreach (Button button in GetMenuButtons())
            {
                string label = button.Text;
                button.BackColor = UiPalette.Navigation;
                button.ForeColor = UiPalette.NavigationText;
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
                activeMenuButton.ForeColor = UiPalette.NavigationText;
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
            menuIconImages.TryGetValue(button, out Image previous);
            menuIconImages[button] = UiIconFactory.CreateNavigation(icon, MenuIconSize, active);
            previous?.Dispose();
            button.Invalidate();
        }

        private void MenuButton_Paint(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            string label = button.Tag as string ?? string.Empty;
            menuIconImages.TryGetValue(button, out Image icon);
            TextFormatFlags textFlags = TextFormatFlags.SingleLine
                | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix;
            int textHeight = Math.Max(button.Font.Height + 4, 18);
            int iconSize = icon == null
                ? 0
                : Math.Min(icon.Width, Math.Max(20, Math.Min(button.ClientSize.Width - 24, button.ClientSize.Height - textHeight - 12)));
            int gap = icon == null || string.IsNullOrEmpty(label) ? 0 : 3;
            int contentHeight = iconSize + gap + textHeight;
            int contentTop = Math.Max(0, (button.ClientSize.Height - 2 - contentHeight) / 2) + button.Padding.Top;
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
                        button.ClientSize.Height - 2,
                        button.ClientSize.Width,
                        2);
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
                valueDebug_Page,
                Card_Page,
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
            int targetWidth = availableWidth > 0
                ? Math.Max(1, availableWidth / buttons.Length)
                : DefaultMenuButtonWidth;
            Font buttonFont = targetWidth >= 110 ? normalMenuButtonFont : compactMenuButtonFont;
            int targetHeight = Math.Max(1, panel1.ClientSize.Height);

            for (int i = 0; i < buttons.Length; i++)
            {
                int left = i * targetWidth;
                int width = i == buttons.Length - 1
                    ? Math.Max(1, availableWidth - left)
                    : targetWidth;
                buttons[i].Font = buttonFont;
                buttons[i].SetBounds(left, 0, width, targetHeight);
            }
        }

        private void value_Page_Click(object sender, EventArgs e)
        {
            SF.frmValue.FreshFrmValue();
           // SF.frmValue.Owner = this;
            SF.frmValue.StartPosition = FormStartPosition.CenterScreen;
            SF.frmValue.Show();
            SF.frmValue.BringToFront();
            SF.frmValue.WindowState = FormWindowState.Normal;
        }

        private void aiAssistant_Page_Click(object sender, EventArgs e)
        {
            SF.mainfrm.EnsureAiInfrastructureStarted();
            // 切换 ai_panel 显示/隐藏，不切换主页面（curPage 不变）
            var p = SF.mainfrm.ai_panel;
            if (p.Visible)
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
            else
            {
                // 优先使用 40% 宽度；窗口较小时为主工作区保留空间，避免遮挡或裁掉操作控件。
                SF.mainfrm.UpdateAiPanelWidth();
                p.Visible = true;
                aiAssistantActive = true;
                aiAssistant_Page.BackColor = UiPalette.Navigation;
                SetMenuIcon(aiAssistant_Page, true);
                aiAssistant_Page.Invalidate();
                hoverAnimator.RefreshRestingColors();
                SetNoteColumnVisible(false);
                if (SF.frmAiAssistant != null && !SF.frmAiAssistant.IsDisposed)
                {
                    SF.frmAiAssistant.RefreshAssistantView();
                }
            }
        }

        public void ShowAiAssistant()
        {
            if (SF.mainfrm?.ai_panel != null && !SF.mainfrm.ai_panel.Visible)
            {
                aiAssistant_Page_Click(aiAssistant_Page, EventArgs.Empty);
            }
        }

        private void version_Page_Click(object sender, EventArgs e)
        {
            if (SF.frmVersionManager == null || SF.frmVersionManager.IsDisposed)
            {
                SF.frmVersionManager = new FrmVersionManager();
            }
            ShowEmbeddedMainPage(SF.frmVersionManager, version_Page, VersionPageIndex);
        }

        // AI 助手打开时隐藏流程列表的"备注"列，腾出空间给助手窗体
        private void SetNoteColumnVisible(bool visible)
        {
            if (SF.frmDataGrid?.dataGridView1 != null)
            {
                SF.frmDataGrid.dataGridView1.SetNoteColumnVisible(visible);
            }
        }

        private void Card_Page_Click(object sender, EventArgs e)
        {
            if (SF.curPage != 5)
            {
                SF.curPage = 5;
                SetActiveMenuButton(Card_Page);
                SetMainPanelScrollSize(Size.Empty);
                SF.mainfrm.ShowEditorWorkspace();
                SF.mainfrm.panel_Info.Visible = false;

                if (!SF.mainfrm.DataGrid_panel.Controls.Contains(SF.frmIO))
                {
                    SF.mainfrm.loadFillForm(SF.mainfrm.DataGrid_panel, SF.frmIO);
                }
                if (!SF.mainfrm.treeView_panel.Controls.Contains(SF.frmCard))
                {
                    SF.mainfrm.loadFillForm(SF.mainfrm.treeView_panel, SF.frmCard);
                }

                SF.mainfrm.ToolBar_panel.Visible = true;
                SF.mainfrm.treeView_panel.Visible = true;
                SF.mainfrm.inspector_panel.Visible = true;
                SF.mainfrm.DataGrid_panel.Visible = true;
                SF.mainfrm.panel_Info.Visible = false;
                SF.mainfrm.state_panel.Visible = true;

                SF.frmIO.Visible = true;
                SF.frmCard.Visible = true;
                SF.frmDataGrid.Visible = false;
                SF.frmProc.Visible = false;
                SF.frmCard.BringToFront();
                SF.frmIO.BringToFront();

                SF.frmToolBar.btnPause.Visible = false;
                SF.frmToolBar.btnStop.Visible = false;
                SF.frmToolBar.btnStopAll.Visible = false;
                SF.frmToolBar.SingleRun.Visible = false;
                SF.frmToolBar.btnAlarm.Visible = false;
                SF.frmToolBar.btnLocate.Visible = false;
                SF.frmToolBar.btnSearch.Visible = false;
                SF.frmToolBar.btnIOMonitor.Visible = true;
                SF.frmToolBar.btnIOMonitor.Enabled = true;
                SF.frmToolBar.btnIOMonitor.Text = SF.frmIO.IsIOMonitoring ? "停止监视" : "IO监视";
            }
        }


        private void process_Page_Click(object sender, EventArgs e)
        {
            if (SF.curPage != 0)
            {
                SF.curPage = 0;
                SetActiveMenuButton(process_Page);
                SetMainPanelScrollSize(Size.Empty);
                SF.mainfrm.ShowEditorWorkspace();
                SF.mainfrm.panel_Info.Visible = true;

                if (!SF.mainfrm.DataGrid_panel.Controls.Contains(SF.frmDataGrid))
                {
                    SF.mainfrm.loadFillForm(SF.mainfrm.DataGrid_panel, SF.frmDataGrid);
                }
                if (!SF.mainfrm.treeView_panel.Controls.Contains(SF.frmProc))
                {
                    SF.mainfrm.loadFillForm(SF.mainfrm.treeView_panel, SF.frmProc);
                }

                SF.mainfrm.ToolBar_panel.Visible = true;
                SF.mainfrm.treeView_panel.Visible = true;
                SF.mainfrm.inspector_panel.Visible = true;
                SF.mainfrm.DataGrid_panel.Visible = true;
                SF.mainfrm.panel_Info.Visible = true;
                SF.mainfrm.state_panel.Visible = true;

                SF.frmDataGrid.Visible = true;
                SF.frmProc.Visible = true;
                if (SF.mainfrm.DataGrid_panel.Controls.Contains(SF.frmIO))
                {
                    SF.frmIO.Visible = false;
                }
                if (SF.mainfrm.treeView_panel.Controls.Contains(SF.frmCard))
                {
                    SF.frmCard.Visible = false;
                }
                SF.frmToolBar.btnPause.Visible = true;
                SF.frmToolBar.btnStop.Visible = true;
                SF.frmToolBar.btnStopAll.Visible = true;
                SF.frmToolBar.SingleRun.Visible = true;
                SF.frmToolBar.btnAlarm.Visible = true;
                SF.frmToolBar.btnLocate.Visible = true;
                SF.frmToolBar.btnSearch.Visible = true;

                SF.frmToolBar.btnPause.Enabled = true;
                SF.frmToolBar.btnStop.Enabled = true;
                SF.frmToolBar.btnStopAll.Enabled = true;
                SF.frmToolBar.SingleRun.Enabled = true;
                SF.frmToolBar.btnLocate.Enabled = true;
                SF.frmToolBar.btnSearch.Enabled = true;
                SF.frmToolBar.btnAlarm.Enabled = true;
                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
                if (SF.isAddOps)
                {
                    SF.frmInspector.ShowObject(SF.frmDataGrid.OperationTemp);
                }
                else
                {
                    SF.frmInspector.ClearObject();
                }
            }
        }

        private void station_Page_Click(object sender, EventArgs e)
        {
            if (SF.curPage != 1)
            {
                SF.curPage = 1;
                SetActiveMenuButton(station_Page);
                SetMainPanelScrollSize(Size.Empty);
                if (!SF.frmStation.panel1.Controls.Contains(SF.frmControl))
                {
                    SF.mainfrm.loadFillForm(SF.frmStation.panel1, SF.frmControl);
                }
                SF.frmControl.comboBox1.DisplayMember = "Name";
                SF.frmControl.comboBox1.DataSource = SF.frmCard.dataStation;
                SF.mainfrm.ShowWorkspacePage(SF.frmStation);

                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
             
            }
        }

        private void communication_Page_Click(object sender, EventArgs e)
        {
            ShowEmbeddedMainPage(SF.frmComunication, communication_Page, CommunicationPageIndex);
        }

        private void Io_Page_Click(object sender, EventArgs e)
        {
            if (SF.curPage != 2)
            {
                SF.frmIODebug.StartPosition = FormStartPosition.CenterScreen;
                SF.frmIODebug.Show();
                SF.frmIODebug.BringToFront();
                SF.frmIODebug.WindowState = FormWindowState.Normal;


            }
        }

        private void valueDebug_Page_Click(object sender, EventArgs e)
        {
            if (SF.curPage != 6)
            {
                SF.curPage = 6;
                SetActiveMenuButton(valueDebug_Page);
                SetMainPanelScrollSize(Size.Empty);
                SF.frmValueDebug.RefreshCheckList();
                SF.frmValueDebug.RefreshEditList();
                SF.mainfrm.ShowWorkspacePage(SF.frmValueDebug);

                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
            }
        }

        private void Plc_Page_Click(object sender, EventArgs e)
        {
            if (SF.frmPlc == null || SF.frmPlc.IsDisposed)
            {
                SF.frmPlc = new FrmPlc();
            }
            ShowEmbeddedMainPage(SF.frmPlc, Plc_Page, PlcPageIndex);
        }

        private void ShowEmbeddedMainPage(Form page, Button menuButton, int pageIndex)
        {
            if (page == null || page.IsDisposed)
            {
                return;
            }

            SF.curPage = pageIndex;
            SetActiveMenuButton(menuButton);
            SetMainPanelScrollSize(page.MinimumSize);
            SF.mainfrm.ShowWorkspacePage(page);

            SF.frmToolBar.btnIOMonitor.Visible = false;
            SF.frmIO.StopIOMonitor();
            SF.frmToolBar.btnIOMonitor.Text = "IO监视";
        }

        private static void SetMainPanelScrollSize(Size minimumSize)
        {
            SF.mainfrm.main_panel.AutoScrollMinSize = minimumSize;
            SF.mainfrm.main_panel.AutoScrollPosition = Point.Empty;
        }

    }
  
}
