using Automation.MotionControl;
using System;
using System.Collections.Generic;
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
    public static class SF
    {
        public static FrmMenu frmMenu;
        public static FrmProc frmProc;
        public static FrmDataGrid frmDataGrid;
        public static FrmPropertyGrid frmPropertyGrid;
        public static FrmToolBar frmToolBar;
        public static FrmValue frmValue;
        public static FrmMain mainfrm;
        public static DataRun DR;
        public static FrmCard frmCard;
        public static FrmIO frmIO;
        public static MotionCtrl motion;
        public static FrmControl frmControl;
        public static FrmStation frmStation;
        public static FrmDataStruct frmdataStruct;
        public static FrmIODebug frmIODebug;
        public static FrmComunication frmComunication;
        public static FrmState frmState;
        public static CustomFunc customFunc;
        public static FrmAlarmConfig frmAlarmConfig;
        public static FrmSearch frmSearch;
        public static FrmSearch4Value frmSearch4Value;
        public static FrmInfo frmInfo;
        public static FrmTest frmTest;

        /*0 修改proc
          1 修改ops
          2 修改controlCard
          3 修改轴
          4 修改工站
          5 修改IO
         */
        public static int isModify = -1;

        /*
       * 0 停止
       * 1 暂停
       * 2 运行
       */
        public static int SysState = -1;

        public static bool isAddOps = false;


        public static bool isDeleteOps = false;
        public static bool isFinBulidFrmValue = false;
        //标志是否完成编辑
        public static bool isEndEdit = true;
     
        public static bool isTrack = false;
        //指示当前页面
        public static int curPage = 0;

        //流程的储存路径
        public static string workPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config\\Work\\";
        //变量的储存路径
        public static string ConfigPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config\\";


        public static void Delay(int milliSecond)
        {
            int start = Environment.TickCount;
            while (Math.Abs(Environment.TickCount - start) < milliSecond)//毫秒
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        //public static async Task Delay(int milliseconds)
        //{
        //    var start = Environment.TickCount;

        //    while (unchecked(Environment.TickCount - start) < milliseconds)
        //    {
        //        Application.DoEvents(); 
        //        await Task.Yield();           // 比 Task.Delay(5) 更轻量，允许UI线程继续工作
        //    }
        //}

        public static void Delay2(int milliSecond)
        {
            int start = Environment.TickCount;
            while (Math.Abs(Environment.TickCount - start) < milliSecond)//毫秒
            {
                Application.DoEvents();
            }
        }
        //public static async Task Delay(int milliSecond)
        //{
        //    await Task.Delay(milliSecond);
        //}

    }
}
