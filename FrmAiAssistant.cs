using System;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmAiAssistant : Form
    {
        public FrmAiAssistant()
        {
            InitializeComponent();
        }

        private void btnNavPropose_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabPropose;
        }

        private void btnNavVerify_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabVerify;
        }

        private void btnNavSim_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabSim;
        }

        private void btnNavDiff_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabDiff;
        }

        private void btnNavTelemetry_Click(object sender, EventArgs e)
        {
            tabMain.SelectedTab = tabTelemetry;
        }
    }
}
