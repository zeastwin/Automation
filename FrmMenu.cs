using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMenu : Form
    {
        private const int DefaultMenuButtonWidth = 143;
        private const int CompactMenuColumns = 5;
        private static readonly Color MenuBackColor = Color.FromArgb(52, 58, 64);
        private static readonly Color MenuHoverColor = Color.FromArgb(65, 72, 80);
        private static readonly Color MenuActiveColor = Color.FromArgb(61, 68, 75);
        private static readonly Color MenuForeColor = Color.FromArgb(218, 224, 229);
        private static readonly Color MenuAccentColor = Color.FromArgb(72, 169, 218);
        private static readonly Color MenuIconColor = Color.FromArgb(142, 194, 216);
        private readonly Font normalMenuButtonFont = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Regular);
        private readonly Font compactMenuButtonFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular);
        private readonly Button version_Page = new Button();
        private readonly Dictionary<Button, UiIconKind> menuIcons = new Dictionary<Button, UiIconKind>();
        private readonly Dictionary<Button, Image> menuIconImages = new Dictionary<Button, Image>();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();
        private Button activeMenuButton;
        private bool aiAssistantActive;

        private const int CommunicationPageIndex = 7;
        private const int PlcPageIndex = 8;
        private const int VersionPageIndex = 9;

        public FrmMenu()
        {
            InitializeComponent();
            ConfigureVersionButton();
            ConfigureMenuAppearance();
            ConfigureAdaptiveMenu();
            SetActiveMenuButton(process_Page);
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

            panel1.BackColor = MenuBackColor;
            Io_Page.Text = "I/O 调试";
            foreach (Button button in GetMenuButtons())
            {
                string label = button.Text;
                button.BackColor = MenuBackColor;
                button.ForeColor = MenuForeColor;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = MenuBackColor;
                button.FlatAppearance.MouseDownBackColor = MenuBackColor;
                button.UseVisualStyleBackColor = false;
                button.TabStop = false;
                SetMenuIcon(button, MenuIconColor);
                button.Tag = label;
                button.AccessibleName = label;
                button.Text = string.Empty;
                button.Image = null;
                button.Padding = Padding.Empty;
                button.Paint += MenuButton_Paint;
                Button menuButton = button;
                hoverAnimator.Attach(
                    menuButton,
                    () => IsMenuButtonActive(menuButton) ? MenuActiveColor : MenuBackColor,
                    MenuHoverColor,
                    true);
            }
        }

        private void SetActiveMenuButton(Button button)
        {
            if (activeMenuButton != null)
            {
                activeMenuButton.BackColor = MenuBackColor;
                activeMenuButton.ForeColor = MenuForeColor;
                SetMenuIcon(activeMenuButton, MenuIconColor);
                activeMenuButton.Invalidate();
            }
            activeMenuButton = button;
            if (activeMenuButton != null)
            {
                activeMenuButton.BackColor = MenuActiveColor;
                activeMenuButton.ForeColor = Color.White;
                SetMenuIcon(activeMenuButton, Color.FromArgb(103, 202, 244));
                activeMenuButton.Invalidate();
            }
            hoverAnimator.RefreshRestingColors();
        }

        private bool IsMenuButtonActive(Button button)
        {
            return button == activeMenuButton || (button == aiAssistant_Page && aiAssistantActive);
        }

        private void SetMenuIcon(Button button, Color color)
        {
            if (!menuIcons.TryGetValue(button, out UiIconKind icon))
            {
                return;
            }
            menuIconImages.TryGetValue(button, out Image previous);
            menuIconImages[button] = UiIconFactory.Create(icon, color, 23);
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
            TextFormatFlags textFlags = TextFormatFlags.NoPadding
                | TextFormatFlags.SingleLine
                | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter;
            Size textSize = TextRenderer.MeasureText(e.Graphics, label, button.Font, Size.Empty, textFlags);
            int iconWidth = icon?.Width ?? 0;
            int gap = icon == null || string.IsNullOrEmpty(label) ? 0 : 7;
            int contentWidth = iconWidth + gap + textSize.Width;
            int contentHeight = Math.Max(icon?.Height ?? 0, textSize.Height);
            int contentLeft = Math.Max(0, (button.ClientSize.Width - contentWidth) / 2);
            int contentTop = Math.Max(0, (button.ClientSize.Height - 3 - contentHeight) / 2) + button.Padding.Top;
            if (icon != null)
            {
                e.Graphics.DrawImageUnscaled(icon, contentLeft, contentTop + (contentHeight - icon.Height) / 2);
            }
            Rectangle textBounds = new Rectangle(
                contentLeft + iconWidth + gap,
                contentTop,
                textSize.Width,
                contentHeight);
            TextRenderer.DrawText(e.Graphics, label, button.Font, textBounds, button.ForeColor, textFlags);

            if (button == activeMenuButton || (button == aiAssistant_Page && aiAssistantActive))
            {
                using (SolidBrush brush = new SolidBrush(MenuAccentColor))
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
            panel1.Resize += (sender, args) => AdjustMenuButtons();
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
                aiAssistant_Page
            };
        }

        private void AdjustMenuButtons()
        {
            Button[] buttons = GetMenuButtons();
            if (buttons.Length == 0)
            {
                return;
            }

            int availableWidth = Math.Max(0, panel1.ClientSize.Width);
            int defaultTotalWidth = DefaultMenuButtonWidth * buttons.Length;
            int rowCount = availableWidth >= defaultTotalWidth ? 1 : 2;
            int columnCount = rowCount == 1 ? buttons.Length : CompactMenuColumns;
            Font buttonFont = rowCount == 1 ? normalMenuButtonFont : compactMenuButtonFont;
            int targetWidth = availableWidth > 0
                ? Math.Max(1, availableWidth / columnCount)
                : DefaultMenuButtonWidth;
            int targetHeight = Math.Max(1, panel1.ClientSize.Height / rowCount);

            for (int i = 0; i < buttons.Length; i++)
            {
                int row = i / columnCount;
                int column = i % columnCount;
                int left = column * targetWidth;
                int width = column == columnCount - 1
                    ? Math.Max(1, availableWidth - left)
                    : targetWidth;
                buttons[i].Font = buttonFont;
                buttons[i].SetBounds(left, row * targetHeight, width, targetHeight);
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
                aiAssistant_Page.BackColor = MenuBackColor;
                SetMenuIcon(aiAssistant_Page, MenuIconColor);
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
                aiAssistant_Page.BackColor = MenuActiveColor;
                SetMenuIcon(aiAssistant_Page, Color.FromArgb(103, 202, 244));
                aiAssistant_Page.Invalidate();
                hoverAnimator.RefreshRestingColors();
                SetNoteColumnVisible(false);
                if (SF.frmAiAssistant != null && !SF.frmAiAssistant.IsDisposed)
                {
                    SF.frmAiAssistant.RefreshAssistantView();
                }
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
                SF.frmPropertyGrid.panel1.Visible = false;
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
                SF.mainfrm.propertyGrid_panel.Visible = true;
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
                SF.frmPropertyGrid.panel1.Visible = true;
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
                SF.mainfrm.propertyGrid_panel.Visible = true;
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
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                }
                else
                {
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
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
