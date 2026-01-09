using Automation.MotionControl;
using csLTDMC;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;

namespace Automation
{
    public partial class FrmMain : Form
    {
        public FrmDataGrid frmDataGrid = new FrmDataGrid();
        public FrmMenu frmMenu = new FrmMenu();
        public FrmProc frmProc = new FrmProc();
        public FrmPropertyGrid frmPropertyGrid = new FrmPropertyGrid();
        public FrmToolBar frmToolBar = new FrmToolBar();
        public FrmValue frmValue = new FrmValue();
        public FrmIO  frmIO = new FrmIO();
        public FrmCard  frmCard = new FrmCard();
        public DataRun dataRun = new DataRun();
        public CustomFunc customFunc = new CustomFunc();
        public FrmControl frmControl = new FrmControl();
        public FrmStation frmStation = new FrmStation();
        public FrmDataStruct frmdataStruct = new FrmDataStruct();
        public FrmIODebug frmIODebug = new FrmIODebug();
        public FrmComunication frmComunication = new FrmComunication();
        public FrmState frmState = new FrmState();
        public FrmAlarmConfig frmAlarmConfig = new FrmAlarmConfig();
        public FrmSearch frmSearch = new FrmSearch();
        public FrmSearch4Value frmSearch4Value = new FrmSearch4Value();
        public FrmInfo frmInfo = new FrmInfo();
        public FrmTest frmTest = new FrmTest();
        public MotionCtrl motion = new MotionCtrl();

        public FrmMain()
        {
            InitializeComponent();
            SF.cardStore = new CardConfigStore();
            SF.valueStore = new ValueConfigStore();
            SF.dataStructStore = new DataStructStore();
            SF.alarmInfoStore = new AlarmInfoStore();
            SF.comm = new CommunicationHub();
            SF.mainfrm = this;
            SF.frmMenu = frmMenu;
            SF.frmProc = frmProc;
            SF.frmDataGrid = frmDataGrid;
            SF.frmPropertyGrid = frmPropertyGrid;
            SF.frmToolBar = frmToolBar;
            SF.frmValue = frmValue;
            SF.frmIO = frmIO;
            SF.frmCard = frmCard;
            SF.DR = dataRun;
            SF.frmControl = frmControl;
            SF.frmStation = frmStation;
            SF.frmdataStruct = frmdataStruct;
            SF.motion = motion;
            SF.frmIODebug = frmIODebug;
            SF.frmComunication = frmComunication;
            SF.frmState = frmState;
            SF.customFunc = customFunc;
            SF.frmAlarmConfig = frmAlarmConfig;
            SF.frmSearch = frmSearch;
            SF.frmSearch4Value = frmSearch4Value;
            SF.frmInfo = frmInfo;
            SF.frmTest = frmTest;

            StartPosition = FormStartPosition.CenterScreen;

            loadFillForm(MenuPanel, SF.frmMenu);
            loadFillForm(treeView_panel, SF.frmProc);
            loadFillForm(DataGrid_panel, SF.frmDataGrid);
            loadFillForm(propertyGrid_panel, SF.frmPropertyGrid);
            loadFillForm(ToolBar_panel, SF.frmToolBar);
            loadFillForm(state_panel, SF.frmState);
            loadFillForm(panel_Info, SF.frmInfo);
        }

        

        void setDoubleBuffered()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        }
        private void FrmMain_Load(object sender, EventArgs e)
        {
            SF.frmValue.RefreshDic();
            SF.cardStore.Load(SF.ConfigPath);
            SF.frmIO.RefreshIOMap();
            SF.frmCard.RefreshStationList();
            SF.dataStructStore.Load(SF.ConfigPath);
            SF.frmdataStruct.RefreshDataSturctList();
            SF.frmIODebug.RefreshIODebugMap();
            SF.frmComunication.RefreshSocketMap();
            SF.frmComunication.RefreshSerialPortInfo();
            SF.frmAlarmConfig.RefreshAlarmInfo();
            SF.frmIODebug.RefleshIODebug();
            //初始化运动控制相关
            SF.motion.InitCardType();
            SF.motion.InitCard();
            SF.motion.DownLoadConfig();
            SF.motion.SetAllAxisSevonOn();
            SF.motion.SetAllAxisEquiv();
            Monitor();
            if (SF.frmProc?.procsList != null && SF.frmProc.procsList.Count > 0)
            {
                for (int i = 0; i < SF.frmProc.procsList.Count; i++)
                {
                    Proc proc = SF.frmProc.procsList[i];
                    if (proc?.head?.AutoStart != true)
                    {
                        continue;
                    }
                    if (SF.DR.ProcHandles[i] != null && SF.DR.ProcHandles[i].State != ProcRunState.Stopped)
                    {
                        continue;
                    }
                    SF.DR.StartProcAuto(proc, i);
                    ProcHandle handle = SF.DR.ProcHandles[i];
                    if (handle != null)
                    {
                        handle.m_evtRun.Set();
                        handle.m_evtTik.Set();
                        handle.m_evtTok.Set();
                        handle.State = ProcRunState.Running;
                        handle.isBreakpoint = false;
                        SF.DR.SetProcText(i, handle.State, handle.isBreakpoint);
                    }
                }
            }
        }
        
        public List<Dictionary<int, char[]>> StateDic = new List<Dictionary<int, char[]>>();
        public object StateDicLock { get; } = new object();

    
        public void Monitor()
        {
            ReflshDgv();
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (SF.isModify != ModifyKind.ControlCard && !SF.frmCard.IsNewCard)
                        {
                            

                            for (int i = 0; i < SF.cardStore.GetControlCardCount(); i++)
                            {
                                for (int j = 0; j < SF.cardStore.GetAxisCount(i); j++)
                                {
                                    uint Number = csLTDMC.LTDMC.dmc_axis_io_status((ushort)i, (ushort)j);
                                    char[] state = Convert.ToString(Number, 2).PadLeft(16, '0').ToCharArray();
                                    lock (StateDicLock)
                                    {
                                        if (i >= StateDic.Count)
                                        {
                                            continue;
                                        }
                                        Dictionary<int, char[]> axisStates = StateDic[i];
                                        if (axisStates == null)
                                        {
                                            axisStates = new Dictionary<int, char[]>();
                                            StateDic[i] = axisStates;
                                        }
                                        axisStates[j] = state;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {

                    }
                    Thread.Sleep(1);
                }
            });
        }
       
        public void ReflshDgv()
        {
            lock (StateDicLock)
            {
                StateDic.Clear();
                for (int i = 0; i < SF.cardStore.GetControlCardCount(); i++)
                {
                    Dictionary<int, char[]> dictionary1 = new Dictionary<int, char[]>();
                    StateDic.Add(dictionary1);
                }
            }
        }

        public void CheckThreadStatus(Task task)
        {
            if (task == null)
                return;
            // 检查任务的状态
            if (task.Status == TaskStatus.RanToCompletion)
            {
                Console.WriteLine("Task has finished successfully.");
            }
            else if (task.Status == TaskStatus.Faulted)
            {
                Console.WriteLine("Task encountered an error.");
            }
            else if (task.Status == TaskStatus.Canceled)
            {
                Console.WriteLine("Task was canceled.");
            }
            else if (task.Status == TaskStatus.Running)
            {
                Console.WriteLine("Task is running.");
            }
            Thread.Sleep(1000);
        }
        public void loadFillForm(Panel panel, System.Windows.Forms.Form frm)
        {
            if (frm != null && panel != null)
            {
                frm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                frm.ShowIcon = false;
                frm.ShowInTaskbar = false;
                frm.TopLevel = false;
                frm.Dock = DockStyle.Fill;
                panel.Controls.Add(frm);
                frm.BringToFront();
                frm.Show();
                frm.Focus();
            }
        }
        public T ReadJson<T>(string FilePath, string Name)
        {

            String strFilePath = FilePath + Name + ".json";
            if (!File.Exists(strFilePath))
            {
                return default(T);
            }

            try
            {
                StreamReader r = new StreamReader(strFilePath);
                JsonTextReader reader = new JsonTextReader(r);
                string json = r.ReadToEnd();
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                };
                var temp = JsonConvert.DeserializeObject<T>(json, settings);

                r.Close();

                return temp;
            }

            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return default(T); 
            }

        }

        public bool SaveAsJson<T>(string FilePath, string Name, T t)
        {
            string strFilePath = FilePath + Name + ".json";
            string directory = Path.GetDirectoryName(strFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(t, settings);

            string tempPath = strFilePath + ".tmp";
            File.WriteAllText(tempPath, output);
            if (File.Exists(strFilePath))
            {
                File.Replace(tempPath, strFilePath, null);
            }
            else
            {
                File.Move(tempPath, strFilePath);
            }

            return true;
        }

        private void FrmMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F && e.Control)
            {
                if(SF.curPage == 0)
                {
                    SF.frmToolBar.btnSearch.PerformClick();
                }
                e.Handled = true; // 防止其他控件处理该键按下事件
            }
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (SF.frmProc != null && SF.frmProc.isStopPointDirty)
            {
                if (!Directory.Exists(SF.workPath))
                {
                    Directory.CreateDirectory(SF.workPath);
                }
                for (int i = 0; i < SF.frmProc.procsList.Count; i++)
                {
                    SaveAsJson(SF.workPath, i.ToString(), SF.frmProc.procsList[i]);
                }
            }
            SF.valueStore.Save(SF.ConfigPath);
            SF.dataStructStore.Save(SF.ConfigPath);
            SF.alarmInfoStore.Save(SF.ConfigPath);
            Environment.Exit(0);
        }
    }
}
