using System;
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
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.FrmCard;
using static Automation.OperationTypePartial;
using static System.Net.Mime.MediaTypeNames;

namespace Automation
{
    public partial class FrmIO : Form
    {
        public List<List<IO>> IOMap = new List<List<IO>>();
        public Dictionary<string,IO> DicIO = new Dictionary<string,IO>();
        public System.Drawing.Image validImage = Properties.Resources.vaild;
        public System.Drawing.Image invalidImage = Properties.Resources.invalid;
        public int iSelectedIORow = -1;
        public List<string> IoOutItems = new List<string>();
        public List<string> IoInItems = new List<string>();
        public List<string> IoItems = new List<string>();
        public FrmIO()
        {
            InitializeComponent();

            dgvIO.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvIO.ReadOnly = true;
            dgvIO.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dgvIO.RowHeadersVisible = false;
            dgvIO.AutoGenerateColumns = false;

            Type dgvType = this.dgvIO.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dgvIO, true, null);

            dgvIO.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        public void RefleshIODic()
        {
            DicIO.Clear();
            IoOutItems.Clear();
            IoInItems.Clear();
            foreach (List<IO> list in IOMap)
            {
                foreach (IO item in list)
                {
                    if (item.Name != null && item.Name != "")
                    {
                        DicIO.Add(item.Name,item);
                        if(item.IOType == "通用输出")
                        {
                            IoOutItems.Add(item.Name);
                        }
                        if (item.IOType == "通用输入")
                        {
                            IoInItems.Add(item.Name);
                        }
                        IoItems.Add(item.Name);
                    }
                }
            }

        }
        //从文件更新表
        public void RefreshIOMap()
        {

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            if (!File.Exists(SF.ConfigPath + "IOMap.json"))
            {
                IOMap = new List<List<IO>>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", IOMap);
            }
            IOMap = SF.mainfrm.ReadJson<List<List<IO>>>(SF.ConfigPath, "IOMap");
            RefreshIODgv();
        }

        public void RefreshIODgv()
        {
            RefleshIODic();
            if (SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
            {
                if (SF.cardStore.TryGetCardHead(cardIndex, out CardHead cardHead) && IOMap.Count > cardIndex)
                {
                    int inputCount = cardHead.InputCount;
                    int outputCount = cardHead.OutputCount;

                    List<IO> cacheIOs = IOMap[cardIndex];

                    WriteIODgv(inputCount, outputCount, cacheIOs);
                }
            }
        }

        public void WriteIODgv(int inputCount, int outputCount, List<IO> cacheIOs)
        {
            if (SF.isModify != ModifyKind.IO)
                dgvIO.Rows.Clear();
            if (inputCount >= cacheIOs.Count)
                return;
            IO cacheIO;
            for (int i = 0; i < inputCount; i++)
            {
                cacheIO = cacheIOs[i];
                if (cacheIO != null)
                {
                    if (SF.isModify != ModifyKind.IO)
                        dgvIO.Rows.Add();
                    dgvIO.Rows[i].Cells[0].Value = cacheIO.Index;
                    //dgvIO.Rows[i].Cells[1].Value = cacheIO.Status ? "|运行" : "|就绪"; ;
                    dgvIO.Rows[i].Cells[1].Value = invalidImage;
                    dgvIO.Rows[i].Cells[2].Value = cacheIO.Name;
                    dgvIO.Rows[i].Cells[3].Value = cacheIO.CardNum;
                    dgvIO.Rows[i].Cells[4].Value = cacheIO.Module;
                    dgvIO.Rows[i].Cells[5].Value = cacheIO.IOIndex;
                    dgvIO.Rows[i].Cells[6].Value = cacheIO.IOType;
                    dgvIO.Rows[i].Cells[7].Value = cacheIO.UsedType;
                    dgvIO.Rows[i].Cells[8].Value = cacheIO.EffectLevel;
                    dgvIO.Rows[i].Cells[9].Value = cacheIO.Debug;
                    dgvIO.Rows[i].Cells[10].Value = cacheIO.Note;
                }
            }
            for (int i = inputCount; i < inputCount + outputCount; i++)
            {
                cacheIO = cacheIOs[i];
                if (cacheIO != null)
                {
                    if (SF.isModify != ModifyKind.IO)
                        dgvIO.Rows.Add();
                    dgvIO.Rows[i].Cells[0].Value = cacheIO.Index;
                    dgvIO.Rows[i].Cells[1].Value = invalidImage;
                    dgvIO.Rows[i].Cells[2].Value = cacheIO.Name;
                    dgvIO.Rows[i].Cells[3].Value = cacheIO.CardNum;
                    dgvIO.Rows[i].Cells[4].Value = cacheIO.Module;
                    dgvIO.Rows[i].Cells[5].Value = cacheIO.IOIndex;
                    dgvIO.Rows[i].Cells[6].Value = cacheIO.IOType;
                    dgvIO.Rows[i].Cells[7].Value = cacheIO.UsedType;
                    dgvIO.Rows[i].Cells[8].Value = cacheIO.EffectLevel;
                    dgvIO.Rows[i].Cells[9].Value = cacheIO.Debug;
                    dgvIO.Rows[i].Cells[10].Value = cacheIO.Note;
                }
            }
        }
        //刷新IO界面
        public void FreshFrmIO()
        {
            if (!SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
                return;
            if (!SF.cardStore.TryGetCardHead(cardIndex, out CardHead cardHead))
                return;
            if (IOMap.Count <= cardIndex)
                return;
            int inputCount = cardHead.InputCount;
            int outputCount = cardHead.OutputCount;

            List<IO> cacheIOs = IOMap[cardIndex];
            IO cacheIO;
            for (int i = 0; i < inputCount; i++)
            {
                cacheIO = cacheIOs[i];
                if (cacheIO != null)
                {
                    dgvIO.Rows[i].Cells[0].Value = cacheIO.Index;
                    dgvIO.Rows[i].Cells[1].Value = invalidImage;
                    dgvIO.Rows[i].Cells[2].Value = cacheIO.Name;
                    dgvIO.Rows[i].Cells[3].Value = cacheIO.CardNum;
                    dgvIO.Rows[i].Cells[4].Value = cacheIO.Module;
                    dgvIO.Rows[i].Cells[5].Value = cacheIO.IOIndex;
                    dgvIO.Rows[i].Cells[6].Value = cacheIO.IOType;
                    dgvIO.Rows[i].Cells[7].Value = cacheIO.UsedType;
                    dgvIO.Rows[i].Cells[8].Value = cacheIO.EffectLevel;
                    dgvIO.Rows[i].Cells[9].Value = cacheIO.Debug;
                    dgvIO.Rows[i].Cells[10].Value = cacheIO.Note;

                }
            }
            for (int i = inputCount; i < inputCount + outputCount; i++)
            {
                cacheIO = cacheIOs[i];
                if (cacheIO != null)
                {
                    dgvIO.Rows[i].Cells[0].Value = cacheIO.Index;
                    dgvIO.Rows[i].Cells[1].Value = invalidImage;
                    dgvIO.Rows[i].Cells[2].Value = cacheIO.Name;
                    dgvIO.Rows[i].Cells[3].Value = cacheIO.CardNum;
                    dgvIO.Rows[i].Cells[4].Value = cacheIO.Module;
                    dgvIO.Rows[i].Cells[5].Value = cacheIO.IOIndex;
                    dgvIO.Rows[i].Cells[6].Value = cacheIO.IOType;
                    dgvIO.Rows[i].Cells[7].Value = cacheIO.UsedType;
                    dgvIO.Rows[i].Cells[8].Value = cacheIO.EffectLevel;
                    dgvIO.Rows[i].Cells[9].Value = cacheIO.Debug;
                    dgvIO.Rows[i].Cells[10].Value = cacheIO.Note;
                }
            }
        }

        private void dgvIO_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            dgvIO[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void dgvIO_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            

        }

        private void dgvIO_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                dgvIO.ClearSelection();
                dgvIO.Rows[e.RowIndex].Selected = true;

                if (SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = IOMap[cardIndex][e.RowIndex];
            }

            iSelectedIORow = e.RowIndex;
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (iSelectedIORow != -1)
            {
                SF.BeginEdit(ModifyKind.IO);
            }
          
        }

    }

    public class IO
    {
 
        [DisplayName("编号"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public int Index { get; set; }
        [Browsable(false)]
        public bool Status { get; set; }
        [DisplayName("名称"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public string Name { get; set; }
        [DisplayName("卡号"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public int CardNum { get; set; }
        [DisplayName("模块(从站)号"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public int  Module { get; set; }
        [DisplayName("IO编号"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public string IOIndex { get; set; }
        [DisplayName("IO类型"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public string IOType { get; set; }
        [DisplayName("使用类型"), Category("设置"), Description(""), ReadOnly(false), Browsable(true), TypeConverter(typeof(IOUsedItem))]
        public string UsedType { get; set; }
        [DisplayName("电平"), Category("设置"), Description(""), ReadOnly(false), Browsable(true), TypeConverter(typeof(IOLevelItem))]
        public string EffectLevel { get; set; }
        [DisplayName("备注"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public string Note { get; set; }
       
        private bool debug;
        [DisplayName("调试"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public bool Debug
        {
            get { return debug; }
            set
            {
                bool debugTemp = debug;
                debug = value;
                if (SF.isModify == ModifyKind.IO)
                {
                    if (Name == "")
                    {
                        MessageBox.Show("名称为空");
                        debug = debugTemp;
                        return;
                    }
                    if (!SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
                    {
                        return;
                    }
                    List<IO> cacheIOs = SF.frmIO.IOMap[cardIndex];
                    if (value == true)
                    {
                        if (cacheIOs[SF.frmIO.iSelectedIORow].IOType == "通用输入")
                        {
                            string name = cacheIOs[SF.frmIO.iSelectedIORow].Name;
                            if (SF.frmIODebug.IODebugMaps.inputs.Any(item => item != null && item.Name == name))
                            {
                                MessageBox.Show("调试列表已存在同名输入，已沿用现有配置。");
                                return;
                            }
                            SF.frmIODebug.IODebugMaps.inputs.Add(cacheIOs[SF.frmIO.iSelectedIORow].CloneForDebug());
                        }
                        else if (cacheIOs[SF.frmIO.iSelectedIORow].IOType == "通用输出")
                        {
                            string name = cacheIOs[SF.frmIO.iSelectedIORow].Name;
                            if (SF.frmIODebug.IODebugMaps.outputs.Any(item => item != null && item.Name == name))
                            {
                                MessageBox.Show("调试列表已存在同名输出，已沿用现有配置。");
                                return;
                            }
                            SF.frmIODebug.IODebugMaps.outputs.Add(cacheIOs[SF.frmIO.iSelectedIORow].CloneForDebug());
                        }

                    }
                    else
                    {  
                        if (cacheIOs[SF.frmIO.iSelectedIORow].IOType == "通用输入")
                        {
                            string name = cacheIOs[SF.frmIO.iSelectedIORow].Name;
                            SF.frmIODebug.IODebugMaps.inputs.RemoveAll(item => item != null && item.Name == name);
                        }
                        else if (cacheIOs[SF.frmIO.iSelectedIORow].IOType == "通用输出")
                        {
                            string name = cacheIOs[SF.frmIO.iSelectedIORow].Name;
                            SF.frmIODebug.IODebugMaps.outputs.RemoveAll(item => item != null && item.Name == name);
                        }
                    }

                }
            }
        }
        public IO()
        {
            Name = "";
        }
        public IO CloneForDebug()
        {
            return new IO
            {
                Index = Index,
                Name = Name,
                CardNum = CardNum,
                Module = Module,
                IOIndex = IOIndex,
                IOType = IOType,
                UsedType = UsedType,
                EffectLevel = EffectLevel,
                Note = Note
            };
        }

    }
}
