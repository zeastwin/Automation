using System;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMenu : Form
    {
        public FrmMenu()
        {
            InitializeComponent();
            if (!SF.AiFlowEnabled)
            {
                AI_Page.Visible = false;
                AI_Page.Enabled = false;
            }
            ApplyPermissions();
        }

        private void value_Page_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ValueAccess, "进入变量模块"))
            {
                return;
            }
            SF.frmValue.FreshFrmValue();
           // SF.frmValue.Owner = this;
            SF.frmValue.StartPosition = FormStartPosition.CenterScreen;
            SF.frmValue.Show();
            SF.frmValue.BringToFront();
            SF.frmValue.WindowState = FormWindowState.Normal;
        }

        private void Card_Page_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.CardConfigAccess, "进入控制卡/IO配置"))
            {
                return;
            }
            if (SF.curPage != 5)
            {
                SF.curPage = 5;
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
                if (SF.mainfrm.main_panel.Controls.Contains(SF.frmStation))
                {
                    SF.frmStation.Visible = false;
                }
                if (SF.mainfrm.main_panel.Controls.Contains(SF.frmValueDebug))
                {
                    SF.frmValueDebug.Visible = false;
                }

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
                SF.frmToolBar.btnIOMonitor.Enabled = SF.HasPermission(PermissionKeys.IOMonitorUse);
                SF.frmToolBar.btnIOMonitor.Text = SF.frmIO.IsIOMonitoring ? "停止监视" : "IO监视";
            }
        }


        private void process_Page_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.ProcessAccess, "进入流程模块"))
            {
                return;
            }
            if (SF.curPage != 0)
            {
                SF.curPage = 0;
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
                if (SF.mainfrm.main_panel.Controls.Contains(SF.frmStation))
                {
                    SF.frmStation.Visible = false;
                }
                if (SF.mainfrm.main_panel.Controls.Contains(SF.frmValueDebug))
                {
                    SF.frmValueDebug.Visible = false;
                }

                bool canRun = SF.HasPermission(PermissionKeys.ProcessRun);
                bool canSearch = SF.HasPermission(PermissionKeys.ProcessSearch);
                SF.frmToolBar.btnPause.Visible = true;
                SF.frmToolBar.btnStop.Visible = true;
                SF.frmToolBar.btnStopAll.Visible = true;
                SF.frmToolBar.SingleRun.Visible = true;
                SF.frmToolBar.btnAlarm.Visible = true;
                SF.frmToolBar.btnLocate.Visible = true;
                SF.frmToolBar.btnSearch.Visible = true;

                SF.frmToolBar.btnPause.Enabled = canRun;
                SF.frmToolBar.btnStop.Enabled = true;
                SF.frmToolBar.btnStopAll.Enabled = true;
                SF.frmToolBar.SingleRun.Enabled = canRun;
                SF.frmToolBar.btnLocate.Enabled = canSearch;
                SF.frmToolBar.btnSearch.Enabled = canSearch;
                SF.frmToolBar.btnAlarm.Enabled = SF.HasPermission(PermissionKeys.AlarmConfigAccess);
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
            if (!SF.EnsurePermission(PermissionKeys.StationAccess, "进入工站模块"))
            {
                return;
            }
            if (SF.curPage != 1)
            {
                SF.curPage = 1;
                if (!SF.frmStation.panel1.Controls.Contains(SF.frmControl))
                {
                    SF.mainfrm.loadFillForm(SF.frmStation.panel1, SF.frmControl);
                }
                if (!SF.mainfrm.main_panel.Controls.Contains(SF.frmStation))
                {
                SF.mainfrm.loadFillForm(SF.mainfrm.main_panel, SF.frmStation);
                }

                SF.mainfrm.ToolBar_panel.Visible = false;
                SF.mainfrm.treeView_panel.Visible = false;
                SF.mainfrm.propertyGrid_panel.Visible = false;
                SF.mainfrm.DataGrid_panel.Visible = false;
                SF.mainfrm.panel_Info.Visible = false;
                SF.mainfrm.state_panel.Visible = false;

                SF.frmStation.Visible = true;
                SF.frmStation.BringToFront();

                SF.frmDataGrid.Visible = false;
                SF.frmProc.Visible = false;
                if (SF.mainfrm.DataGrid_panel.Controls.Contains(SF.frmIO))
                {
                    SF.frmIO.Visible = false;
                }
                if (SF.mainfrm.treeView_panel.Controls.Contains(SF.frmCard))
                {
                    SF.frmCard.Visible = false;
                }
                if (SF.mainfrm.main_panel.Controls.Contains(SF.frmValueDebug))
                {
                    SF.frmValueDebug.Visible = false;
                }
                
                SF.frmControl.comboBox1.DisplayMember = "Name";
                SF.frmControl.comboBox1.DataSource = SF.frmCard.dataStation;

                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
             
            }
        }

        private void communication_Page_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.CommunicationAccess, "进入通讯模块"))
            {
                return;
            }
            SF.frmComunication.StartPosition = FormStartPosition.CenterScreen;
            SF.frmComunication.Show();
            SF.frmComunication.BringToFront();
            SF.frmComunication.WindowState = FormWindowState.Normal;
        }

        private void Io_Page_Click(object sender, EventArgs e)
        {
            if (SF.curPage != 2)
            {
                if (!SF.EnsurePermission(PermissionKeys.IODebugAccess, "进入IO调试"))
                {
                    return;
                }
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
                if (!SF.EnsurePermission(PermissionKeys.ValueDebugAccess, "进入变量调试"))
                {
                    return;
                }
                SF.curPage = 6;
                if (!SF.mainfrm.main_panel.Controls.Contains(SF.frmValueDebug))
                {
                    SF.mainfrm.loadFillForm(SF.mainfrm.main_panel, SF.frmValueDebug);
                }
                SF.frmValueDebug.RefreshCheckList();
                SF.frmValueDebug.RefreshEditList();

                SF.mainfrm.ToolBar_panel.Visible = false;
                SF.mainfrm.treeView_panel.Visible = false;
                SF.mainfrm.propertyGrid_panel.Visible = false;
                SF.mainfrm.DataGrid_panel.Visible = false;
                SF.mainfrm.panel_Info.Visible = false;
                SF.mainfrm.state_panel.Visible = false;

                SF.frmValueDebug.Visible = true;
                SF.frmValueDebug.BringToFront();

                SF.frmDataGrid.Visible = false;
                SF.frmProc.Visible = false;
                if (SF.mainfrm.DataGrid_panel.Controls.Contains(SF.frmIO))
                {
                    SF.frmIO.Visible = false;
                }
                if (SF.mainfrm.treeView_panel.Controls.Contains(SF.frmCard))
                {
                    SF.frmCard.Visible = false;
                }
                if (SF.mainfrm.main_panel.Controls.Contains(SF.frmStation))
                {
                    SF.frmStation.Visible = false;
                }

                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
            }
        }

        private void AI_Page_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.AiAccess, "进入AI助手"))
            {
                return;
            }
            if (!SF.AiFlowEnabled)
            {
                MessageBox.Show("AI功能已禁用。");
                return;
            }
            if (SF.frmAiAssistant == null || SF.frmAiAssistant.IsDisposed)
            {
                SF.frmAiAssistant = new FrmAiAssistant();
            }
            SF.frmAiAssistant.StartPosition = FormStartPosition.CenterScreen;
            SF.frmAiAssistant.Show();
            SF.frmAiAssistant.BringToFront();
            SF.frmAiAssistant.WindowState = FormWindowState.Normal;
        }

        private void Plc_Page_Click(object sender, EventArgs e)
        {
            if (!SF.EnsurePermission(PermissionKeys.PlcAccess, "进入PLC模块"))
            {
                return;
            }
            if (SF.frmPlc == null || SF.frmPlc.IsDisposed)
            {
                SF.frmPlc = new FrmPlc();
            }
            SF.frmPlc.StartPosition = FormStartPosition.CenterScreen;
            SF.frmPlc.Show();
            SF.frmPlc.BringToFront();
            SF.frmPlc.WindowState = FormWindowState.Normal;
        }

        public void ApplyPermissions()
        {
            process_Page.Visible = true;
            station_Page.Visible = true;
            Io_Page.Visible = true;
            communication_Page.Visible = true;
            value_Page.Visible = true;
            valueDebug_Page.Visible = true;
            Card_Page.Visible = true;
            AI_Page.Visible = SF.AiFlowEnabled;
            Plc_Page.Visible = true;

            process_Page.Enabled = SF.HasPermission(PermissionKeys.ProcessAccess);
            station_Page.Enabled = SF.HasPermission(PermissionKeys.StationAccess);
            Io_Page.Enabled = SF.HasPermission(PermissionKeys.IODebugAccess);
            communication_Page.Enabled = SF.HasPermission(PermissionKeys.CommunicationAccess);
            value_Page.Enabled = SF.HasPermission(PermissionKeys.ValueAccess);
            valueDebug_Page.Enabled = SF.HasPermission(PermissionKeys.ValueDebugAccess);
            Card_Page.Enabled = SF.HasPermission(PermissionKeys.CardConfigAccess);
            AI_Page.Enabled = SF.AiFlowEnabled && SF.HasPermission(PermissionKeys.AiAccess);
            Plc_Page.Enabled = SF.HasPermission(PermissionKeys.PlcAccess);
        }

    }
  
}
