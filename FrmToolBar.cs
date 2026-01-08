using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static Automation.FrmCard;
using static Automation.OperationTypePartial;
using static System.Collections.Specialized.BitVector32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Automation
{
    public partial class FrmToolBar : Form
    {
        public FrmToolBar()
        {
            InitializeComponent();
            btnSave.Enabled = false;
            btnCancel.Enabled = false;

        }
      
        private void btnSave_Click(object sender, EventArgs e)
        {
            if(SF.curPage == 0)
            {
                if (SF.frmProc.NewProcNum != -1)
                {
                    SF.frmProc.NewProcSave();
                    SF.frmProc.Refresh();
                }

                if (SF.frmProc.NewStepNum != -1)
                {
                    SF.frmProc.NewStepSave();
                    SF.frmProc.Refresh();
                }

                if (SF.isAddOps == true && SF.frmProc.SelectedStepNum != -1)
                {
                    if (!TryValidateGotoTargets(SF.frmDataGrid.OperationTemp, SF.frmProc.SelectedProcNum, out string gotoError))
                    {
                        MessageBox.Show(gotoError);
                        return;
                    }
                    if (SF.frmDataGrid.iSelectedRow == -1)
                    {
                        SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Add(SF.frmDataGrid.OperationTemp);

                    }
                    else
                    {
                        SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Insert(SF.frmDataGrid.iSelectedRow + 1, SF.frmDataGrid.OperationTemp);

                        SF.frmProc.RefleshGoto();
                        for (int i = SF.frmDataGrid.iSelectedRow + 2; i < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count; i++)
                        {

                            OperationType obj = SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[i];

                            obj.Num += 1;
                        }

                    }

                    SF.frmDataGrid.SaveSingleProc(SF.frmProc.SelectedProcNum);
                    SF.frmProc.bindingSource.ResetBindings(true);
                   

                }
                if (SF.isModify == ModifyKind.Operation)
                {
                    if (!TryValidateGotoTargets(SF.frmDataGrid.OperationTemp, SF.frmProc.SelectedProcNum, out string gotoError))
                    {
                        MessageBox.Show(gotoError);
                        return;
                    }
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[SF.frmDataGrid.iSelectedRow] = SF.frmDataGrid.OperationTemp;
                    SF.frmDataGrid.SaveSingleProc(SF.frmProc.SelectedProcNum);
                    SF.frmProc.bindingSource.ResetBindings(true);
                }
                else if (SF.isModify == ModifyKind.Proc)
                {
                    SF.frmDataGrid.SaveSingleProc(SF.frmProc.SelectedProcNum);
                    SF.frmProc.bindingSource.ResetBindings(true);
                    SF.frmProc.Refresh();
                    SF.frmProc.ClearEditBackup();
                }
                SF.valueStore.Save(SF.ConfigPath);


                SF.frmProc.NewStepNum = -1;
                SF.frmProc.NewProcNum = -1;
                SF.isModify = ModifyKind.None;
                SF.isAddOps = false;

            }
            if (SF.curPage == 5)
            {
                if (SF.frmCard.IsNewCard)
                {
                    int newCardIndex = SF.cardStore.AddControlCard(SF.frmCard.controlCardTemp);
                    int AxisCount =SF.frmCard.controlCardTemp.cardHead.AxisCount;
                    for (int i = 0; i < AxisCount; i++)
                    {
                        Axis axis = new Axis() { AxisName = $"Axis{i}" ,AxisNum = i};
                        SF.frmCard.controlCardTemp.axis.Add(axis);
                    }
                  

                    int inputCount = SF.frmCard.controlCardTemp.cardHead.InputCount;
                    int outputCount = SF.frmCard.controlCardTemp.cardHead.OutputCount;
                    List<IO> iOs = new List<IO>();
                    SF.frmIO.IOMap.Add(iOs);

                    for (int i = 0; i < inputCount; i++)
                    {
                        IO io = new IO()
                        {
                            Index = i,
                            Status = false,
                            Name = "",
                            CardNum = newCardIndex,
                            Module = 0,
                            IOIndex = i.ToString(),
                            IOType = "通用输入",
                            UsedType = "通用",
                            EffectLevel = "正常"
                        };
                        SF.frmIO.IOMap[newCardIndex].Add(io);
                    }
                    for (int i = 0; i < outputCount; i++)
                    {
                        IO io = new IO()
                        {
                            Index = i+ outputCount,
                            Status = false,
                            Name = "",
                            CardNum = newCardIndex,
                            Module = 0,
                            IOIndex = i.ToString(),
                            IOType = "通用输出",
                            UsedType = "通用",
                            EffectLevel = "正常"
                        };
                        SF.frmIO.IOMap[newCardIndex].Add(io);
                    }
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", SF.frmIO.IOMap);
                    SF.cardStore.Save(SF.ConfigPath);
                    SF.frmCard.RefreshCardTree(); 
                    SF.frmIO.RefreshIOMap();
                    SF.mainfrm.ReflshDgv();
                   
                    SF.frmCard.EndNewCard();
                }
                if (SF.isModify == ModifyKind.ControlCard)
                {
                    if (SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
                    {
                        if (SF.cardStore.TryGetControlCard(cardIndex, out ControlCard controlCard))
                        {
                            int AxisCount = controlCard.cardHead.AxisCount;
                            controlCard.axis.Clear();
                        for (int i = 0; i < AxisCount; i++)
                        {
                            Axis axis = new Axis() { AxisName = $"Axis{i}" };
                            controlCard.axis.Add(axis);
                        }
                        SF.cardStore.Save(SF.ConfigPath);
                        SF.frmCard.RefreshCardTree();
                        SF.mainfrm.ReflshDgv();
                          
                            SF.isModify = ModifyKind.None;
                        }
                    }
                   
                }
                if (SF.isModify == ModifyKind.Axis)
                {
                    SF.cardStore.Save(SF.ConfigPath);
                    SF.frmCard.RefreshCardTree();
                    SF.motion.SetAllAxisEquiv();
                    SF.isModify = ModifyKind.None;
                }
                if (SF.isModify == ModifyKind.Station)
                {
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
                    SF.frmCard.RefreshStationList();
                    SF.frmCard.RefreshStationTree();
               //     SF.frmStation.SetAxisMotionParam();
                    SF.isModify = ModifyKind.None;
                }
                if (SF.isModify == ModifyKind.IO)
                {
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", SF.frmIO.IOMap);
                    SF.frmIO.RefreshIODgv();
                   // SF.frmIO.FreshFrmIO();
                    SF.isModify = ModifyKind.None;

                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "IODebugMap", SF.frmIODebug.IODebugMaps);
                    SF.frmIODebug.RefreshIODebugMapFrm();

                }

                if (SF.frmCard.IsNewStation)
                {
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
                    SF.frmCard.RefreshStationList();
                    SF.frmCard.RefreshStationTree();
                    SF.frmCard.EndNewStation();
                }
              
            }
            SF.EndEdit();
            SF.frmDataGrid.dataGridView1.Enabled = true;
            SF.frmProc.Enabled = true;
        }
 
        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (SF.isModify == ModifyKind.Proc)
            {
                SF.frmProc.RollbackEdit();
            }
            SF.frmProc.NewStepNum = -1;
            SF.frmProc.NewProcNum = -1;
            SF.frmCard.EndNewCard();

            SF.isModify = ModifyKind.None;
            SF.isAddOps = false;

            SF.frmDataGrid.OperationTemp = null;
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;

            SF.EndEdit();
            SF.frmDataGrid.dataGridView1.Enabled = true;
            SF.frmProc.Enabled = true;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config";
            System.Diagnostics.Process.Start("explorer.exe", path);

        }
        
        private void btnSearch_Click(object sender, EventArgs e)
        {
            SF.frmSearch.StartPosition = FormStartPosition.CenterScreen;
            SF.frmSearch.Show();
            SF.frmSearch.BringToFront();
            SF.frmSearch.WindowState = FormWindowState.Normal;
            SF.frmSearch.textBox1.Focus();
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum != -1 && SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isRun == 2)
            {
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtRun.Reset();
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTik.Reset();
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTok.Set();

                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isRun = 1;

                btnPause.Text = "继续";
            }
            else if(SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isRun == 1)
            {
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtRun.Set();
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTik.Set();
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTok.Set();

                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isRun = 2;

                btnPause.Text = "暂停";
            }
        }

        private void SingleRun_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum!= -1 && SF.DR.ProcHandles[SF.frmProc.SelectedProcNum] != null)
            {
                if (SF.frmProc.SelectedStepNum != -1 && SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isRun == 1)
                {
                    SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtRun.Set();
                    SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTok.Reset();
                    SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTik.Set();
                    SF.Delay(10);
                    SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTik.Reset();
                    SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTok.Set();
                }
            }
                
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            if(SF.frmProc.SelectedProcNum >=0&& SF.DR.ProcHandles[SF.frmProc.SelectedProcNum] != null)
            {
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isThStop = true;
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].isRun = 0;
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtRun.Set();
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTik.Set();
                SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].m_evtTok.Set();
            }

        }

        private bool TryValidateGotoTargets(OperationType operation, int procNum, out string error)
        {
            error = null;
            if (operation == null)
            {
                return true;
            }
            return ValidateGotoTargets(operation, procNum, ref error);
        }

        private bool ValidateGotoTargets(object obj, int procNum, ref string error)
        {
            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(string) && IsGotoProperty(propertyInfo))
                {
                    string value = propertyInfo.GetValue(obj) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (!TryParseGoto(value, out int gotoProc))
                        {
                            error = $"跳转地址格式错误：{value}";
                            return false;
                        }
                        if (gotoProc != procNum)
                        {
                            error = $"跳转地址不允许跨流程：{value}";
                            return false;
                        }
                    }
                }

                var propertyValue = propertyInfo.GetValue(obj);
                if (propertyValue is IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        if (!ValidateGotoTargets(item, procNum, ref error))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private bool IsGotoProperty(PropertyInfo propertyInfo)
        {
            var converterAttr = propertyInfo.GetCustomAttribute<TypeConverterAttribute>();
            if (converterAttr == null)
            {
                return false;
            }

            var converterType = Type.GetType(converterAttr.ConverterTypeName);
            if (converterType == typeof(GotoItem))
            {
                return true;
            }

            return converterAttr.ConverterTypeName != null
                && converterAttr.ConverterTypeName.Contains("GotoItem", StringComparison.Ordinal);
        }

        private bool TryParseGoto(string value, out int procNum)
        {
            procNum = -1;
            string[] parts = value.Split('-');
            if (parts.Length != 3)
            {
                return false;
            }
            return int.TryParse(parts[0], out procNum)
                && int.TryParse(parts[1], out _)
                && int.TryParse(parts[2], out _);
        }

        private void btnAlarm_Click(object sender, EventArgs e)
        {
            SF.frmAlarmConfig.StartPosition = FormStartPosition.CenterScreen;
            SF.frmAlarmConfig.Show();
            SF.frmAlarmConfig.BringToFront();
            SF.frmAlarmConfig.WindowState = FormWindowState.Normal;
        }

        private void btnLocate_Click(object sender, EventArgs e)
        {
            SF.frmDataGrid.SelectChildNode(SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].procNum, SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].stepNum);
            SF.frmDataGrid.ScrollRowToCenter(SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].opsNum);
            SF.frmDataGrid.SetRowColor(SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].opsNum, Color.LightBlue);
        }
    }
}
