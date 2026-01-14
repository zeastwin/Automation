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
    public enum ModifyKind
    {
        None = -1,
        Proc = 0,
        Operation = 1,
        ControlCard = 2,
        Axis = 3,
        Station = 4,
        IO = 5
    }

    public static class SF
    {
        public static FrmMenu frmMenu;
        public static FrmProc frmProc;
        public static FrmDataGrid frmDataGrid;
        public static FrmPropertyGrid frmPropertyGrid;
        public static FrmToolBar frmToolBar;
        public static FrmValue frmValue;
        public static FrmMain mainfrm;
        public static ProcessEngine DR;
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
        public static CardConfigStore cardStore;
        public static ValueConfigStore valueStore;
        public static DataStructStore dataStructStore;
        public static AlarmInfoStore alarmInfoStore;
        public static IProcessEngineStore procStore;
        public static CommunicationHub comm;


        //编辑状态
        public static ModifyKind isModify = ModifyKind.None;

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
     
        //指示当前页面
        public static int curPage = 0;

        //流程的储存路径
        public static string workPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config\\Work\\";
        //变量的储存路径
        public static string ConfigPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config\\";


        public static void Delay(int milliSecond)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < milliSecond)//毫秒
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }


        public static void Delay2(int milliSecond)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < milliSecond)//毫秒
            {
                Application.DoEvents();
            }
        }

        public static bool CanEditProc(int procIndex)
        {
            if (procIndex < 0)
            {
                return true;
            }
            if (DR == null)
            {
                return true;
            }
            EngineSnapshot snapshot = DR.GetSnapshot(procIndex);
            if (snapshot == null)
            {
                return true;
            }
            if (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming)
            {
                MessageBox.Show("当前流程运行中禁止编辑，请先停止。");
                return false;
            }
            return true;
        }

        public static bool CanEditProcStructure()
        {
            if (frmProc?.procsList == null)
            {
                return true;
            }
            for (int i = 0; i < frmProc.procsList.Count; i++)
            {
                EngineSnapshot snapshot = DR?.GetSnapshot(i);
                if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
                {
                    MessageBox.Show("存在运行中的流程，禁止新增或删除流程，请先暂停或单步。");
                    return false;
                }
            }
            return true;
        }

        public static void CancelProcEditing()
        {
            isModify = ModifyKind.None;
            isAddOps = false;
            if (frmProc != null)
            {
                frmProc.NewProcNum = -1;
                frmProc.NewStepNum = -1;
                frmProc.Enabled = true;
            }
            if (frmDataGrid != null)
            {
                frmDataGrid.OperationTemp = null;
                frmDataGrid.dataGridView1.Enabled = true;
            }
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.propertyGrid1.SelectedObject = null;
            }
            EndEdit();
        }

        public static void BeginEdit(ModifyKind kind)
        {
            isModify = kind;
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.Enabled = true;
            }
            if (frmToolBar != null)
            {
                frmToolBar.btnSave.Enabled = true;
                frmToolBar.btnCancel.Enabled = true;
            }
        }

        public static void EndEdit()
        {
            isModify = ModifyKind.None;
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.Enabled = false;
            }
            if (frmToolBar != null)
            {
                frmToolBar.btnSave.Enabled = false;
                frmToolBar.btnCancel.Enabled = false;
            }
        }

    }
}
