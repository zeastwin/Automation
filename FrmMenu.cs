using System;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMenu : Form
    {
     
        public FrmMenu()
        {
            InitializeComponent();
        }

        private void value_Page_Click(object sender, EventArgs e)
        {
           // SF.frmValue.Owner = this;
            SF.frmValue.StartPosition = FormStartPosition.CenterScreen;
            SF.frmValue.Show();
            SF.frmValue.BringToFront();
            SF.frmValue.WindowState = FormWindowState.Normal;
        }

        private void Card_Page_Click(object sender, EventArgs e)
        {
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
                SF.frmToolBar.btnIOMonitor.Text = SF.frmIO.IsIOMonitoring ? "停止监视" : "IO监视";
            }
        }


        public void AddPanel(Panel parent,Panel panel,DockStyle dockStyle)
        {
            parent.Controls.Add(panel);
            panel.BringToFront();
            panel.Dock = dockStyle;
        }
        private void process_Page_Click(object sender, EventArgs e)
        {
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

                SF.frmToolBar.btnPause.Visible = true;
                SF.frmToolBar.btnStop.Visible = true;
                SF.frmToolBar.btnStopAll.Visible = true;
                SF.frmToolBar.SingleRun.Visible = true;
                SF.frmToolBar.btnAlarm.Visible = true;
                SF.frmToolBar.btnLocate.Visible = true;
                SF.frmToolBar.btnSearch.Visible = true;
                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
                if(SF.isAddOps)
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                else
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
            }
        }

        private void station_Page_Click(object sender, EventArgs e)
        {
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
                
                SF.frmControl.comboBox1.DisplayMember = "Name";
                SF.frmControl.comboBox1.DataSource = SF.frmCard.dataStation;

                SF.frmToolBar.btnIOMonitor.Visible = false;
                SF.frmIO.StopIOMonitor();
                SF.frmToolBar.btnIOMonitor.Text = "IO监视";
             
            }
        }

        private void StructData_Page_Click(object sender, EventArgs e)
        {
            SF.frmdataStruct.StartPosition = FormStartPosition.CenterScreen;
            SF.frmdataStruct.Show();
            SF.frmdataStruct.BringToFront();
            SF.frmdataStruct.WindowState = FormWindowState.Normal;
        }

        private void communication_Page_Click(object sender, EventArgs e)
        {
            SF.frmComunication.StartPosition = FormStartPosition.CenterScreen;
            SF.frmComunication.Show();
            SF.frmComunication.BringToFront();
            SF.frmComunication.WindowState = FormWindowState.Normal;
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

        private void Test_Page_Click(object sender, EventArgs e)
        {
            SF.frmTest.StartPosition = FormStartPosition.CenterScreen;
            SF.frmTest.Show();
            SF.frmTest.BringToFront();
            SF.frmTest.WindowState = FormWindowState.Normal;
        }
    }
  
}
