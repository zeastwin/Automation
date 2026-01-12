using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Automation
{
    //new FrmMessage("1", new EventHandler1(() => { MessageBox.Show("1234567"); }), "是");
    //new FrmMessage("2", dsCallBackEvent, dsCallBackEvent, "是", "否");
    //new FrmMessage("3", dsCallBackEvent, dsCallBackEvent, dsCallBackEvent, "是", "否", "d");
    //new FrmMessage("读取中", 5000);
    public delegate void EventHandler1();


    public partial class Message : Form
    {
        public EventHandler1 btn1E;
        public EventHandler1 btn2E;
        public EventHandler1 btn3E;

        public bool isChoiced = false;


        public Message(string headText,string msg, EventHandler1 eventHandler1, string btnTxt1,bool isWait)
        {
            InitializeComponent();
            this.Text = headText;
            SetMsgFrom(msg);
            btn1.Visible = false;
            btn2.Visible = true;
            btn3.Visible = false;
            btn2.Text = btnTxt1;
            btn2E = eventHandler1;
            btn2E += btnCanel;
            Show();
            BringToFront();
            WatiChoice(isWait);
        }


        public Message(string headText, string msg, EventHandler1 eventHandler1, EventHandler1 eventHandler2, string btnTxt1, string btnTxt2,bool isWait)
        {
            InitializeComponent();
            this.Text = headText;
            SetMsgFrom(msg);
            btn1.Visible = true;
            btn2.Visible = false;
            btn3.Visible = true;
            btn1E = eventHandler1;
            btn3E = eventHandler2;
            btn1.Text = btnTxt1;
            btn3.Text = btnTxt2;
            btn1E += btnCanel;
            btn3E += btnCanel;
            Show();
            BringToFront();
            WatiChoice(isWait);
        }
        public Message(string headText, string msg, EventHandler1 eventHandler1, EventHandler1 eventHandler2, EventHandler1 eventHandler3, string btnTxt1, string btnTxt2, string btnTxt3, bool isWait)
        {
            InitializeComponent();
            this.Text = headText;
            SetMsgFrom(msg);
            btn1.Visible = true;
            btn2.Visible = true;
            btn3.Visible = true;
            btn1E = eventHandler1;
            btn2E = eventHandler2;
            btn3E = eventHandler3;
            btn1.Text = btnTxt1;
            btn2.Text = btnTxt2;
            btn3.Text = btnTxt3;
            btn1E += btnCanel;
            btn2E += btnCanel;
            btn3E += btnCanel;
            Show();
            BringToFront();
           
            WatiChoice(isWait);
        }
        public Message(string headText, string msg, int timeOut)
        {
           
            InitializeComponent();
            this.Text = headText;
            SetMsgFrom(msg);
            panelBtn.Visible = false;
            Show();
            BringToFront();
            SF.Delay(timeOut);
            btnCanel();
            
        }
        public Message()
        {
           
        }
        public void WatiChoice(bool isWait)
        {
            if (!isWait)
                return;
            Select();
            while (isChoiced == false)
            {
                Thread.Sleep(100);
            }
        }
        public void SetMsgFrom(string msg)
        {
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            Width = SF.mainfrm.Width / 2;
            Height = SF.mainfrm.Height / 4;
            StartPosition = FormStartPosition.CenterScreen;
            txtMsg.Text = msg;
        }
        private void frmMessage_Load(object sender, EventArgs e)
        {
         
        }

        public void ShowMessage()
        {

        }

        public void btnCanel()
        {
            this.Hide();
            this.Close();
            this.Dispose();
            isChoiced = true;
        }
        private void btn1_Click(object sender, EventArgs e)
        {

            btn1E();
        }

        private void btn2_Click(object sender, EventArgs e)
        {
            btn2E();
        }

        private void btn3_Click(object sender, EventArgs e)
        {
            btn3E();
        }

        private void FrmMessage_FormClosed(object sender, FormClosedEventArgs e)
        {

        }
    }
}
