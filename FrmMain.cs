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
            SF.frmIO.RefreshIOMap();
            SF.frmCard.RefreshCardList();
            SF.frmCard.RefreshStationList();
            SF.frmdataStruct.RefreshDataSturctList();
            SF.frmIODebug.RefreshIODebugMap();
            SF.frmComunication.RefreshSocketMap();
            SF.frmComunication.RefreshSerialPortInfo();
            SF.frmAlarmConfig.RefreshAlarmInfo();
            SF.frmToolBar.RefleshMark();
            SF.frmIODebug.RefleshIODebug();
            //初始化运动控制相关
            SF.motion.InitCardType();
            SF.motion.InitCard();
            SF.motion.DownLoadConfig();
            SF.motion.SetAllAxisSevonOn();
            SF.motion.SetAllAxisEquiv();
            Monitor();
        }
        
        public List<Dictionary<int, char[]>> StateDic = new List<Dictionary<int, char[]>>();

        //public bool GetInPut(int cardNum,string Dtext, ref bool value)
        //{
        //    if (Dtext == "")
        //        return false;
        //    try
        //    {
        //        int io = int.Parse(Dtext);
        //        value = InputArray[cardNum,io];
        //        return true;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }
        //}

        //public bool GetOutPut(int cardNum, string Dtext, ref bool value)
        //{
        //    if (Dtext == "")
        //        return false;
        //    try
        //    {
        //        int io = int.Parse(Dtext);
        //        value = OutputArray[cardNum, io];
        //        return true;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }
        //}
        //  public uint[] InputArray = null;
        //   public uint[] OutputArray = null;
        //// 创建二维矩阵
        //bool[,] InputArray = new bool[50, 1000];
        //bool[,] OutputArray = new bool[50, 1000];

        // 在矩阵中赋值或访问元素
        //matrix[0, 0] = 42; // 设置第一行第一列的值为42
        //uint value = matrix[1, 1]; // 获取第二行第二列的值

        public void Monitor()
        {
            ReflshDgv();
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (SF.isModify != 2 && SF.frmCard.NewCardNum != 0)
                        {
                            //for (int i = 0; i < SF.frmCard.card.controlCards.Count; i++)
                            //{
                            //    for (int j = 0; j < SF.frmCard.card.controlCards[i].cardHead.InputCount; j++)
                            //    {
                            //        bool Number = false;
                            //        SF.motion.GetInIO((ushort)i, j.ToString(), ref Number);
                            //        InputArray[i, j] = Number;
                            //    }
                            //    for (int j = 0; j < SF.frmCard.card.controlCards[i].cardHead.OutputCount; j++)
                            //    {
                            //        bool Number = false ;
                            //        SF.motion.GetOutIO((ushort)i, j.ToString(),ref Number);
                            //        OutputArray[i, j] = Number;
                            //    }
                            //}

                            for (int i = 0; i < SF.frmCard.card.controlCards.Count; i++)
                            {
                                for (int j = 0; j < SF.frmCard.card.controlCards[i].axis.Count; j++)
                                {
                                    uint Number = csLTDMC.LTDMC.dmc_axis_io_status((ushort)i, (ushort)j);

                                    StateDic[i][j] = Convert.ToString(Number, 2).PadLeft(16, '0').ToCharArray();
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
            StateDic.Clear();
            for (int i = 0; i < SF.frmCard.card.controlCards.Count; i++)
            {
                Dictionary<int, char[]> dictionary1 = new Dictionary<int, char[]>();
                StateDic.Add(dictionary1);
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
            FileStream fs = null;

            string strFilePath = FilePath + Name + ".json";

            if (!File.Exists(strFilePath))
            {
                fs = new FileStream(strFilePath, FileMode.Create, FileAccess.ReadWrite);
                fs.Close();
            }
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(t, settings);

            File.WriteAllText(strFilePath, output);

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
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", SF.frmValue.dicValues);
            Environment.Exit(0);
        }
    }
}
