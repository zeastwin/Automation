using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmState : Form , INotifyPropertyChanged
    {

        string _info;
        public string Info
        {
            get
            {
                return _info;
            }
            set
            {
                _info = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Info)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FrmState()
        {
            InitializeComponent();
           
        }
        private void FrmState_Load(object sender, EventArgs e)
        {
            SysInfo.DataBindings.Add(new Binding("Text", this, "Info"));
        }
    }
}
