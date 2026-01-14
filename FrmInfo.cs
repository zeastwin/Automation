using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace Automation
{
    public partial class FrmInfo : Form
    {
     
        public FrmInfo()
        {
            InitializeComponent();
        }

        private void FrmInfo_Load(object sender, EventArgs e)
        {
           
        }

        private void btnClearInfo_Click(object sender, EventArgs e)
        {
            ReceiveTextBox.Clear();
        }

        [Browsable(false)]
        [JsonIgnore]
        public Level level { get; set; } = 0;

        public Level GetState()
        {
            return level;
        }
        public void SetState(Level level)
        {
            this.level = level;
        }
        public enum Level
        {
            Error = 0,
            Normal,
        }
        // InfoLevel 信息级别
        // 0 红色报警
        // 1 普通信息
        public void PrintInfo(string str, Level InfoLevel)
        {
            if (SF.frmInfo.IsDisposed)
                return;
            Invoke(new Action(() =>
            {
                int length = ReceiveTextBox.TextLength;
                str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH时mm分ss秒")}]：{str}\r\n";
                ReceiveTextBox.AppendText(str);

                Color color = Color.Black;
                if(InfoLevel == Level.Error)
                {
                    color = Color.Red;
                }
                else if(InfoLevel == Level.Normal)
                {
                    color = Color.BurlyWood;
                }
                ReceiveTextBox.Select(length, str.Length);
                ReceiveTextBox.SelectionBackColor = color;
                ReceiveTextBox.ScrollToCaret();
            }));
        }
    }
}
