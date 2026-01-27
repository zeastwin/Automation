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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
            btnIOMonitor.Visible = false;

        }

        private void btnAppConfig_Click(object sender, EventArgs e)
        {
            using (FrmAppConfig frm = new FrmAppConfig())
            {
                frm.ShowDialog(this);
            }
        }
      
        private void btnSave_Click(object sender, EventArgs e)
        {
            if(SF.curPage == 0)
            {
                if (SF.frmProc.NewProcNum != -1 && !SF.CanEditProcStructure())
                {
                    SF.CancelProcEditing();
                    return;
                }
                bool isProcEdit = SF.frmProc.NewStepNum != -1
                    || SF.isAddOps
                    || SF.isModify == ModifyKind.Operation
                    || SF.isModify == ModifyKind.Proc;
                if (isProcEdit && !SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    SF.CancelProcEditing();
                    return;
                }
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
                    DataGridView grid = SF.frmDataGrid.dataGridView1;
                    int firstDisplayedRow = -1;
                    int selectedRow = SF.frmDataGrid.iSelectedRow;
                    if (grid != null && grid.RowCount > 0)
                    {
                        firstDisplayedRow = grid.FirstDisplayedScrollingRowIndex;
                    }
                    SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops[SF.frmDataGrid.iSelectedRow] = SF.frmDataGrid.OperationTemp;
                    SF.frmDataGrid.SaveSingleProc(SF.frmProc.SelectedProcNum);
                    SF.frmProc.bindingSource.ResetBindings(true);
                    if (grid != null && grid.RowCount > 0)
                    {
                        if (firstDisplayedRow >= 0 && firstDisplayedRow < grid.RowCount)
                        {
                            grid.FirstDisplayedScrollingRowIndex = firstDisplayedRow;
                        }
                        if (selectedRow >= 0 && selectedRow < grid.RowCount)
                        {
                            grid.ClearSelection();
                            grid.Rows[selectedRow].Selected = true;
                        }
                    }
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
                    if (SF.frmIO.IOMap == null)
                    {
                        MessageBox.Show("IO列表为空。");
                        return;
                    }
                    HashSet<string> nameSet = new HashSet<string>();
                    foreach (List<IO> list in SF.frmIO.IOMap)
                    {
                        if (list == null)
                        {
                            continue;
                        }
                        foreach (IO io in list)
                        {
                            if (io == null)
                            {
                                continue;
                            }
                            string name = io.Name?.Trim();
                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }
                            if (!nameSet.Add(name))
                            {
                                MessageBox.Show($"IO名称重复：{name}");
                                return;
                            }
                        }
                    }
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

        private void btnIOMonitor_Click(object sender, EventArgs e)
        {
            if (SF.frmIO == null)
            {
                return;
            }
            bool enabled = SF.frmIO.ToggleIOMonitor();
            btnIOMonitor.Text = enabled ? "停止监视" : "IO监视";
        }

        private async void btnPause_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            if (procIndex < 0)
            {
                return;
            }
            EngineSnapshot snapshot = SF.DR.GetSnapshot(procIndex);
            if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
            {
                SF.DR.Pause(procIndex);
                btnPause.Text = "继续";
            }
            else if (snapshot != null && snapshot.State == ProcRunState.Paused)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("流程已暂停，禁止继续运行。", FrmInfo.Level.Error);
                }
                return;
            }
            else if (snapshot != null && snapshot.State == ProcRunState.SingleStep)
            {
                Proc proc = null;
                if (SF.frmProc?.procsList != null && procIndex >= 0 && procIndex < SF.frmProc.procsList.Count)
                {
                    proc = SF.frmProc.procsList[procIndex];
                }
                string procName = snapshot.ProcName ?? proc?.head?.Name ?? $"索引{procIndex}";
                int stepIndex = snapshot.StepIndex;
                int opIndex = snapshot.OpIndex;
                string position = $"{procIndex}-{stepIndex}-{opIndex}";
                string opName = null;
                string opType = null;
                if (proc?.steps != null && stepIndex >= 0 && stepIndex < proc.steps.Count)
                {
                    Step step = proc.steps[stepIndex];
                    if (step?.Ops != null && opIndex >= 0 && opIndex < step.Ops.Count)
                    {
                        OperationType op = step.Ops[opIndex];
                        opName = op?.Name;
                        opType = op?.OperaType;
                    }
                }
                string opText = opIndex >= 0
                    ? $"{opIndex}{(string.IsNullOrWhiteSpace(opType) ? "" : $"({opType})")}{(string.IsNullOrWhiteSpace(opName) ? "" : $" {opName}")}"
                    : "未知";
                string message = $"位置: {position}\r\n操作: {opText}";
                btnPause.Enabled = false;
                try
                {
                    var tcs = new TaskCompletionSource<bool>();
                    Message confirmForm = new Message(
                        "继续运行确认",
                        message,
                        () => tcs.TrySetResult(true),
                        () => tcs.TrySetResult(false),
                        "继续",
                        "取消",
                        false);
                    confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                    confirmForm.txtMsg.ForeColor = Color.Red;
                    bool confirmed = await tcs.Task;
                    if (!confirmed)
                    {
                        return;
                    }
                    SF.DR.Resume(procIndex);
                    btnPause.Text = "暂停";
                }
                finally
                {
                    btnPause.Enabled = true;
                }
            }
        }

        private void SingleRun_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            if (procIndex != -1)
            {
                EngineSnapshot snapshot = SF.DR.GetSnapshot(procIndex);
                if (snapshot != null && snapshot.State == ProcRunState.Paused)
                {
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("流程已暂停，禁止单步继续。", FrmInfo.Level.Error);
                    }
                    return;
                }
                if (SF.frmProc.SelectedStepNum != -1 && snapshot != null
                    && snapshot.State == ProcRunState.SingleStep)
                {
                    SF.DR.Step(procIndex);
                    SF.isSingleStepFollowPending = true;
                    SF.singleStepFollowProcIndex = procIndex;
                }
            }
                
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum >= 0)
            {
                SF.DR.Stop(SF.frmProc.SelectedProcNum);
            }

        }

        private void btnStopAll_Click(object sender, EventArgs e)
        {
            if (SF.frmProc?.procsList == null)
            {
                return;
            }

            int count = SF.frmProc.procsList.Count;
            for (int i = 0; i < count; i++)
            {
                Proc proc = SF.frmProc.procsList[i];
                string procName = proc?.head?.Name;
                if (!string.IsNullOrEmpty(procName) && procName.StartsWith("系统", StringComparison.Ordinal))
                {
                    continue;
                }

                SF.DR.Stop(i);
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
            int procIndex = SF.frmProc.SelectedProcNum;
            if (procIndex < 0)
            {
                return;
            }
            EngineSnapshot snapshot = SF.DR.GetSnapshot(procIndex);
            if (snapshot == null || snapshot.StepIndex < 0 || snapshot.OpIndex < 0)
            {
                return;
            }
            SF.frmDataGrid.SelectChildNode(procIndex, snapshot.StepIndex);
            SF.frmDataGrid.ScrollRowToCenter(snapshot.OpIndex);
            SF.frmDataGrid.SetRowColor(snapshot.OpIndex, Color.LightBlue);
        }
    }
}
