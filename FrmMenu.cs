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

                SF.mainfrm.treeView_panel.Controls.Clear();

                SF.frmPropertyGrid.panel1.Visible = false;
                SF.mainfrm.panel_Info.Visible = false;

                SF.mainfrm.loadFillForm(SF.mainfrm.DataGrid_panel, SF.frmIO);
                SF.mainfrm.loadFillForm(SF.mainfrm.treeView_panel, SF.frmCard);

                SF.frmToolBar.btnPause.Visible = false;
                SF.frmToolBar.btnStop.Visible = false;
                SF.frmToolBar.SingleRun.Visible = false;
                SF.frmToolBar.btnTrack.Visible = false;
                SF.frmToolBar.btnAlarm.Visible = false;
                SF.frmToolBar.btnLocate.Visible = false;
                SF.frmToolBar.btnSearch.Visible = false;

                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.ToolBar_panel, DockStyle.Top);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.state_panel, DockStyle.Bottom);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.treeView_panel, DockStyle.Left);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.propertyGrid_panel, DockStyle.Right);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.DataGrid_panel, DockStyle.Fill);


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
                SF.mainfrm.main_panel.Controls.Clear();
                SF.frmPropertyGrid.panel1.Visible = true;
                SF.mainfrm.panel_Info.Visible = true;
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.ToolBar_panel, DockStyle.Top);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.state_panel, DockStyle.Bottom);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.treeView_panel, DockStyle.Left);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.propertyGrid_panel, DockStyle.Right);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.panel_Info, DockStyle.Bottom);
                AddPanel(SF.mainfrm.main_panel, SF.mainfrm.DataGrid_panel, DockStyle.Fill);
                SF.mainfrm.loadFillForm(SF.mainfrm.DataGrid_panel, SF.frmDataGrid);
                SF.mainfrm.loadFillForm(SF.mainfrm.treeView_panel, SF.frmProc);
                SF.frmToolBar.btnPause.Visible = true;
                SF.frmToolBar.btnStop.Visible = true;
                SF.frmToolBar.SingleRun.Visible = true;
                SF.frmToolBar.btnTrack.Visible = true;
                SF.frmToolBar.btnAlarm.Visible = true;
                SF.frmToolBar.btnLocate.Visible = true;
                SF.frmToolBar.btnSearch.Visible = true;
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
                SF.mainfrm.main_panel.Controls.Clear();
                SF.mainfrm.loadFillForm(SF.frmStation.panel1, SF.frmControl);
                SF.mainfrm.loadFillForm(SF.mainfrm.main_panel, SF.frmStation);
                
                SF.frmControl.comboBox1.DisplayMember = "Name";
                SF.frmControl.comboBox1.DataSource = SF.frmCard.dataStation;
             
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
