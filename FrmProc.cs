using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.OperationTypePartial;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace Automation
{
    public partial class FrmProc : Form
    {
        //存放所有流程信息
        public List<Proc> procsList = new List<Proc> ();
        //存放临时流程信息
        public ProcHead HeadTemp;
        //存放临时步骤信息
        public Step StepTemp;

        public List<string> procListItem = new List<string>();
        public List<string> procListItemCount = new List<string>();

        public int NewProcNum { get; set; }
        public int NewStepNum { get; set; }

        public int SelectedProcNum { get; set; }
        public int SelectedStepNum { get; set; }

        public bool isStopPointDirty = false;

        private static readonly Color DisabledNodeColor = Color.Gainsboro;
        private const string DisabledTag = "[禁用]";
        private readonly object procNodeMapLock = new object();
        private readonly Dictionary<Guid, TreeNode> procNodeMap = new Dictionary<Guid, TreeNode>();
        private readonly Dictionary<Guid, int> procIndexMap = new Dictionary<Guid, int>();

        private ProcHead editProcHeadBackup;
        private Step editStepBackup;
        private int editProcIndex = -1;
        private int editStepIndex = -1;
        private bool hasEditBackup = false;
        private bool suppressContextMenuOnce = false;
        private Proc clipboardProc;
        private Step clipboardStep;

        public FrmProc()
        {
            InitializeComponent();
            NewProcNum = -1;
            NewStepNum = -1;
            this.proc_treeView.HideSelection = false;
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(proc_treeView, true, null);
            proc_treeView.BeforeSelect += proc_treeView_BeforeSelect;
        }

        public void NewProcSave()
        {
            if (HeadTemp == null)
            {
                MessageBox.Show("流程头信息为空，无法保存。");
                return;
            }
            Proc proc = new Proc();
            proc.head = HeadTemp;

            if (SelectedProcNum == -1)
            {
                procsList.Add(proc);
                int procIndex = procsList.Count - 1;
                List<string> errors = new List<string>();
                NormalizeProc(procIndex, proc, errors);
                if (errors.Count > 0)
                {
                    MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                    return;
                }
                SF.mainfrm.SaveAsJson(SF.workPath, procIndex.ToString(), proc);
                SF.PublishProc(procIndex);
            }
            else
            {
                if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                {
                    MessageBox.Show("当前流程索引无效，无法插入流程。");
                    return;
                }
                int insertIndex = SelectedProcNum + 1;
                procsList.Insert(insertIndex, proc);
                RebuildWorkConfig(insertIndex);
            }

            NewProcNum = -1;

        }
       
        public void NewStepSave()
        {
            if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
            {
                MessageBox.Show("当前流程索引无效，无法新增步骤。");
                return;
            }
            if (StepTemp == null)
            {
                MessageBox.Show("步骤信息为空，无法保存。");
                return;
            }
            if (StepTemp.Id == Guid.Empty)
            {
                StepTemp.Id = Guid.NewGuid();
            }
            if (SelectedStepNum == -1)
            {
                procsList[SelectedProcNum].steps.Add(StepTemp);
            }
            else
            {
                if (SelectedStepNum < 0 || SelectedStepNum >= procsList[SelectedProcNum].steps.Count)
                {
                    MessageBox.Show("当前步骤索引无效，无法新增步骤。");
                    return;
                }
                procsList[SelectedProcNum].steps.Insert(SelectedStepNum + 1, StepTemp);
            }

            int insertIndex = SelectedStepNum == -1
                ? procsList[SelectedProcNum].steps.Count - 1
                : SelectedStepNum + 1;
            ShiftGotoStepIndexForInsert(SelectedProcNum, insertIndex);

            List<string> errors = new List<string>();
            NormalizeProc(SelectedProcNum, procsList[SelectedProcNum], errors);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return;
            }
            SF.mainfrm.SaveAsJson(SF.workPath,SelectedProcNum.ToString(), procsList[SelectedProcNum]);
            SF.PublishProc(SelectedProcNum);

            NewStepNum = -1;

        }
        public void RebuildWorkConfig(int startIndex = 0)
        {
            string workDir = SF.workPath.TrimEnd('\\');
            string configDir = Path.GetDirectoryName(workDir);
            if (string.IsNullOrEmpty(configDir))
            {
                MessageBox.Show("流程目录无效");
                return;
            }

            if (!Directory.Exists(workDir))
            {
                Directory.CreateDirectory(workDir);
            }

            string tempDir = Path.Combine(configDir, "Work_tmp");
            string backupDir = Path.Combine(configDir, "Work_bak");

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            AdaptGotoProcIndexForAllProcs(startIndex);

            string tempPath = tempDir + "\\";
            for (int i = 0; i < procsList.Count; i++)
            {
                SF.mainfrm.SaveAsJson(tempPath, i.ToString(), procsList[i]);
            }

            try
            {
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
                if (Directory.Exists(workDir))
                {
                    Directory.Move(workDir, backupDir);
                }
                Directory.Move(tempDir, workDir);
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
            }
            catch (Exception ex)
            {
                if (!Directory.Exists(workDir) && Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.Move(backupDir, workDir);
                    }
                    catch
                    {
                    }
                }
                MessageBox.Show(ex.Message);
                return;
            }

            SF.frmProc.Refresh();
        }
        public void Refresh()
        {
            List<Proc> procsListTemp = new List<Proc>();
            List<string> loadErrors = new List<string>();

            proc_treeView.Nodes.Clear();
            lock (procNodeMapLock)
            {
                procNodeMap.Clear();
                procIndexMap.Clear();
            }
            SF.ProcConfigFaulted = false;

            string path = SF.workPath.TrimEnd('\\');

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Dictionary<int, string> indexMap = new Dictionary<int, string>();
            int maxIndex = -1;
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out int index))
                {
                    indexMap[index] = file;
                    if (index > maxIndex)
                    {
                        maxIndex = index;
                    }
                }
            }
            if (indexMap.Count > 0)
            {
                if (!indexMap.ContainsKey(0))
                {
                    loadErrors.Add("流程文件索引必须从0开始。");
                }
                for (int i = 0; i <= maxIndex; i++)
                {
                    if (!indexMap.ContainsKey(i))
                    {
                        loadErrors.Add($"流程文件缺失：{i}.json");
                    }
                }
            }

            for (int i = 0; i <= maxIndex; i++)
            {
                Proc proc = null;
                if (indexMap.ContainsKey(i))
                {
                    proc = SF.mainfrm.ReadJson<Proc>(SF.workPath, i.ToString());
                }
                if (proc == null)
                {
                    loadErrors.Add($"流程文件加载失败：{i}.json");
                    proc = new Proc();
                }

                NormalizeProc(i, proc, loadErrors);
                procsListTemp.Add(proc);

                TreeNode treeNode = new TreeNode(BuildProcNodeText(i, proc));
                if (proc?.head?.Disable == true)
                {
                    treeNode.ForeColor = DisabledNodeColor;
                }
                Guid procId = proc?.head?.Id ?? Guid.Empty;
                if (procId != Guid.Empty)
                {
                    treeNode.Tag = procId;
                    lock (procNodeMapLock)
                    {
                        procNodeMap[procId] = treeNode;
                        procIndexMap[procId] = i;
                    }
                }
                proc_treeView.Nodes.Add(treeNode);

                if (proc.steps != null)
                {
                    for (int j = 0; j < proc.steps.Count; j++)
                    {
                        Step step = proc.steps[j];
                        TreeNode chnode = new TreeNode(BuildStepNodeText(i, j, proc, step));
                        if (proc?.head?.Disable == true || proc.steps[j]?.Disable == true)
                        {
                            chnode.ForeColor = DisabledNodeColor;
                        }
                        proc_treeView.Nodes[i].Nodes.Add(chnode);
                    }
                }
            }
            procsList = procsListTemp;

            procListItem.Clear();
            procListItemCount.Clear();
            foreach (var item in SF.frmProc.procsList)
            {
                string procName = string.IsNullOrWhiteSpace(item?.head?.Name) ? $"流程{procListItemCount.Count}" : item.head.Name;
                procListItem.Add(procName);
                procListItemCount.Add((procListItemCount.Count + 1).ToString());
            }
            if (SF.DR?.Context != null)
            {
                List<Proc> runtimeProcs = new List<Proc>(procsList.Count);
                for (int i = 0; i < procsList.Count; i++)
                {
                    runtimeProcs.Add(FrmPropertyGrid.DeepCopy(procsList[i]));
                }
                SF.DR.Context.Procs = runtimeProcs;
                SF.DR.ClearPendingProcUpdates();
            }
            if (loadErrors.Count > 0)
            {
                SF.ProcConfigFaulted = true;
                string reason = "流程配置加载失败，已停机。\r\n" + string.Join("\r\n", loadErrors.Distinct());
                SF.StopAllProcs(reason);
                MessageBox.Show(reason, "流程配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal void NormalizeProc(int procIndex, Proc proc, List<string> errors)
        {
            if (proc.head == null)
            {
                proc.head = new ProcHead();
                errors.Add($"流程{procIndex}头信息缺失");
            }
            if (proc.head.Id == Guid.Empty)
            {
                proc.head.Id = Guid.NewGuid();
            }
            if (string.IsNullOrWhiteSpace(proc.head.Name))
            {
                proc.head.Name = $"流程{procIndex}";
            }
            if (proc.head.PauseIoParams == null)
            {
                proc.head.PauseIoParams = new CustomList<PauseIoParam>();
            }
            if (proc.head.PauseValueParams == null)
            {
                proc.head.PauseValueParams = new CustomList<PauseValueParam>();
            }
            if (proc.steps == null)
            {
                proc.steps = new List<Step>();
                errors.Add($"流程{procIndex}步骤列表缺失");
            }
            for (int i = 0; i < proc.steps.Count; i++)
            {
                if (proc.steps[i] == null)
                {
                    proc.steps[i] = new Step();
                    errors.Add($"流程{procIndex}步骤{i}为空");
                }
                Step step = proc.steps[i];
                if (step.Id == Guid.Empty)
                {
                    step.Id = Guid.NewGuid();
                }
                if (string.IsNullOrWhiteSpace(step.Name))
                {
                    step.Name = $"步骤{i}";
                }
                if (step.Ops == null)
                {
                    step.Ops = new List<OperationType>();
                    errors.Add($"流程{procIndex}步骤{i}指令列表缺失");
                }
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    if (step.Ops[j] == null)
                    {
                        step.Ops[j] = new OperationType
                        {
                            Name = "空指令",
                            OperaType = "无效指令",
                            Disable = true
                        };
                        errors.Add($"流程{procIndex}步骤{i}指令{j}为空");
                    }
                    if (step.Ops[j].Id == Guid.Empty)
                    {
                        step.Ops[j].Id = Guid.NewGuid();
                    }
                    step.Ops[j].Num = j;
                }
            }
            for (int i = 0; i < proc.steps.Count; i++)
            {
                Step step = proc.steps[i];
                for (int j = 0; j < step.Ops.Count; j++)
                {
                    ValidateGotoTargets(step.Ops[j], procIndex, proc, errors, $"流程{procIndex}步骤{i}指令{j}");
                }
            }
        }

        private void ValidateGotoTargets(object obj, int procIndex, Proc proc, List<string> errors, string context)
        {
            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(string) && propertyInfo.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string value = propertyInfo.GetValue(obj) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (!TryParseGotoKey(value, out int gotoProc, out int gotoStep, out int gotoOp))
                        {
                            errors.Add($"{context}跳转地址格式错误：{value}");
                        }
                        else if (gotoProc != procIndex)
                        {
                            errors.Add($"{context}跳转地址跨流程：{value}");
                        }
                        else if (!TryValidateGotoRange(proc, procIndex, gotoStep, gotoOp, out string rangeError))
                        {
                            errors.Add($"{context} {rangeError}");
                        }
                    }
                }

                var propertyValue = propertyInfo.GetValue(obj);
                if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        ValidateGotoTargets(item, procIndex, proc, errors, context);
                    }
                }
            }
        }

        private bool TryValidateGotoRange(Proc proc, int procIndex, int stepIndex, int opIndex, out string error)
        {
            error = null;
            if (proc?.steps == null || stepIndex < 0 || stepIndex >= proc.steps.Count)
            {
                error = $"跳转地址步骤越界：{procIndex}-{stepIndex}-{opIndex}";
                return false;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex < 0 || opIndex >= step.Ops.Count)
            {
                error = $"跳转地址指令越界：{procIndex}-{stepIndex}-{opIndex}";
                return false;
            }
            return true;
        }

        private bool TryParseGotoKey(string value, out int procIndex, out int stepIndex, out int opIndex)
        {
            procIndex = -1;
            stepIndex = -1;
            opIndex = -1;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string[] parts = value.Split('-');
            if (parts.Length != 3)
            {
                return false;
            }
            return int.TryParse(parts[0], out procIndex)
                && int.TryParse(parts[1], out stepIndex)
                && int.TryParse(parts[2], out opIndex);
        }

        private void CaptureEditBackup()
        {
            hasEditBackup = false;
            editProcIndex = SelectedProcNum;
            editStepIndex = SelectedStepNum;

            if (editProcIndex < 0 || editProcIndex >= procsList.Count)
            {
                return;
            }

            if (editStepIndex == -1)
            {
                editProcHeadBackup = FrmPropertyGrid.DeepCopy(procsList[editProcIndex].head);
                editStepBackup = null;
            }
            else if (editStepIndex >= 0 && editStepIndex < procsList[editProcIndex].steps.Count)
            {
                editStepBackup = FrmPropertyGrid.DeepCopy(procsList[editProcIndex].steps[editStepIndex]);
                editProcHeadBackup = null;
            }

            hasEditBackup = editProcHeadBackup != null || editStepBackup != null;
        }

        public void RollbackEdit()
        {
            if (!hasEditBackup)
            {
                return;
            }

            if (editProcIndex < 0 || editProcIndex >= procsList.Count)
            {
                ClearEditBackup();
                return;
            }

            if (editStepIndex == -1)
            {
                if (editProcHeadBackup != null)
                {
                    ProcHead head = procsList[editProcIndex].head;
                    head.Name = editProcHeadBackup.Name;
                    head.AutoStart = editProcHeadBackup.AutoStart;
                    head.PauseIoCount = editProcHeadBackup.PauseIoCount;
                    if (editProcHeadBackup.PauseIoParams == null)
                    {
                        head.PauseIoParams = new CustomList<PauseIoParam>();
                    }
                    else
                    {
                        head.PauseIoParams = new CustomList<PauseIoParam>();
                        head.PauseIoParams.AddRange(editProcHeadBackup.PauseIoParams);
                    }
                    head.PauseValueCount = editProcHeadBackup.PauseValueCount;
                    if (editProcHeadBackup.PauseValueParams == null)
                    {
                        head.PauseValueParams = new CustomList<PauseValueParam>();
                    }
                    else
                    {
                        head.PauseValueParams = new CustomList<PauseValueParam>();
                        head.PauseValueParams.AddRange(editProcHeadBackup.PauseValueParams);
                    }
                    if (editProcIndex < proc_treeView.Nodes.Count)
                    {
                        if (SF.DR != null)
                        {
                            SF.DR.RefreshProcName(editProcIndex);
                        }
                        else
                        {
                            proc_treeView.Nodes[editProcIndex].Text = BuildProcNodeText(editProcIndex);
                        }
                    }
                }
            }
            else if (editStepIndex >= 0 && editStepIndex < procsList[editProcIndex].steps.Count)
            {
                Step step = procsList[editProcIndex].steps[editStepIndex];
                if (editStepBackup != null)
                {
                    step.Name = editStepBackup.Name;
                    step.Ops.Clear();
                    step.Ops.AddRange(editStepBackup.Ops);
                }
                if (editProcIndex < proc_treeView.Nodes.Count && editStepIndex < proc_treeView.Nodes[editProcIndex].Nodes.Count)
                {
                    proc_treeView.Nodes[editProcIndex].Nodes[editStepIndex].Text = BuildStepNodeText(editProcIndex, editStepIndex);
                }
            }

            bindingSource.ResetBindings(true);
            ClearEditBackup();
        }

        public void ClearEditBackup()
        {
            hasEditBackup = false;
            editProcHeadBackup = null;
            editStepBackup = null;
            editProcIndex = -1;
            editStepIndex = -1;
        }

        private void AddProc_Click(object sender, EventArgs e)
        {   
            if (!SF.CanEditProcStructure())
            {
                return;
            }
            if (proc_treeView.SelectedNode == null)
            {
                NewProcNum = procsList.Count;
            }
            else
            {
                NewProcNum = SelectedProcNum;
            }
            HeadTemp = new ProcHead();

            SF.frmPropertyGrid.propertyGrid1.SelectedObject = HeadTemp;
            SF.BeginEdit(ModifyKind.None);
        }

        private void AddStep_Click(object sender, EventArgs e)
        {

            TreeNode selectdnode = proc_treeView.SelectedNode;
            if (selectdnode != null)
            {
                if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    return;
                }
                NewStepNum = 1;

                StepTemp = new Step();

                SF.frmPropertyGrid.propertyGrid1.SelectedObject = StepTemp;
                SF.BeginEdit(ModifyKind.None);
            }
            else
            {
                MessageBox.Show("请选择需要添加子节点");
            }

        }

        private async void Remove_Click(object sender, EventArgs e)
        {
            TreeNode selectnode = proc_treeView.SelectedNode;
            if (selectnode == null)
            {
                MessageBox.Show("请选择需要删除的流程或步骤");
                return;
            }
            TreeNode parentnode = selectnode.Parent;
            if (parentnode == null)
            {
                if (!SF.CanEditProcStructure())
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = $"索引{procIndex}";
                }
                string warnMsg = $"警告：即将删除流程【{procName}】\r\n此操作不可恢复，确认删除？";
                var tcs = new TaskCompletionSource<bool>();
                Message confirmForm = new Message(
                    "删除流程确认",
                    warnMsg,
                    () => tcs.TrySetResult(true),
                    () => tcs.TrySetResult(false),
                    "删除",
                    "取消",
                    false);
                confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                confirmForm.txtMsg.ForeColor = Color.Red;
                bool confirmed = await tcs.Task;
                if (!confirmed)
                {
                    return;
                }
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                procsList.RemoveAt(procIndex);
                RebuildWorkConfig(procIndex);
            }
            else
            {
                if (!SF.CanEditProc(SelectedProcNum))
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                int stepIndex = SelectedStepNum;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
                {
                    return;
                }
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = $"索引{procIndex}";
                }
                string stepName = procsList[procIndex].steps[stepIndex]?.Name;
                if (string.IsNullOrWhiteSpace(stepName))
                {
                    stepName = $"索引{stepIndex}";
                }
                string warnMsg = $"警告：即将删除步骤【{stepName}】\r\n所属流程：【{procName}】\r\n此操作不可恢复，确认删除？";
                var tcs = new TaskCompletionSource<bool>();
                Message confirmForm = new Message(
                    "删除步骤确认",
                    warnMsg,
                    () => tcs.TrySetResult(true),
                    () => tcs.TrySetResult(false),
                    "删除",
                    "取消",
                    false);
                confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                confirmForm.txtMsg.ForeColor = Color.Red;
                bool confirmed = await tcs.Task;
                if (!confirmed)
                {
                    return;
                }
                int removedIndex = SelectedStepNum;
                procsList[SelectedProcNum].steps.RemoveAt(removedIndex);
                ShiftGotoStepIndexForRemove(SelectedProcNum, removedIndex);
                SF.frmDataGrid.SaveSingleProc(SelectedProcNum);
                Refresh();
            }
        }
     
    
        public BindingSource bindingSource = new BindingSource();
        private void proc_treeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (proc_treeView.SelectedNode != null)
            {
                
                if (proc_treeView.SelectedNode.Parent != null)
                {
                    SelectedProcNum = proc_treeView.SelectedNode.Parent.Index;

                    SelectedStepNum = proc_treeView.SelectedNode.Index;

                    SF.frmDataGrid.iSelectedRow = -1;

                    if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                    {
                        MessageBox.Show("流程索引无效，无法加载步骤。");
                        SelectedProcNum = -1;
                        SelectedStepNum = -1;
                        bindingSource.DataSource = null;
                        SF.frmDataGrid.dataGridView1.DataSource = null;
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                        return;
                    }
                    if (SelectedStepNum < 0 || SelectedStepNum >= procsList[SelectedProcNum].steps.Count)
                    {
                        MessageBox.Show("步骤索引无效，无法加载指令。");
                        SelectedStepNum = -1;
                        bindingSource.DataSource = null;
                        SF.frmDataGrid.dataGridView1.DataSource = null;
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                        return;
                    }
                    bindingSource.DataSource = procsList[SelectedProcNum].steps[SelectedStepNum].Ops;

                    SF.frmPropertyGrid.propertyGrid1.SelectedObject =procsList[SelectedProcNum].steps[SF.frmProc.SelectedStepNum];


                }
                else
                {
                    SelectedProcNum = proc_treeView.SelectedNode.Index;

                    SelectedStepNum = -1;

                    if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                    {
                        MessageBox.Show("流程索引无效，无法加载。");
                        SelectedProcNum = -1;
                        bindingSource.DataSource = null;
                        SF.frmDataGrid.dataGridView1.DataSource = null;
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                        return;
                    }
                    bindingSource.DataSource = null;

                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = procsList[SelectedProcNum].head;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();


                }
                SF.frmDataGrid.dataGridView1.DataSource = bindingSource;

                if (SF.DR != null && SF.frmProc.SelectedProcNum != -1)
                {
                    EngineSnapshot snapshot = SF.DR.GetSnapshot(SF.frmProc.SelectedProcNum);
                    if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
                    {
                        SF.frmToolBar.btnPause.Text = "暂停";
                    }
                    else if (snapshot != null && (snapshot.State == ProcRunState.Paused || snapshot.State == ProcRunState.SingleStep))
                    {
                        SF.frmToolBar.btnPause.Text = "继续";
                    }
                    else
                    {
                        SF.frmToolBar.btnPause.Text = "暂停";
                    }
                    if (snapshot != null)
                    {
                        SF.frmToolBar.btnPause.Enabled = snapshot.State != ProcRunState.Paused;
                        SF.frmToolBar.SingleRun.Enabled = snapshot.State == ProcRunState.SingleStep;
                    }
                    else
                    {
                        SF.frmToolBar.btnPause.Enabled = true;
                        SF.frmToolBar.SingleRun.Enabled = true;
                    }
                }
                else
                {
                    SF.frmToolBar.btnPause.Text = "暂停";
                    SF.frmToolBar.btnPause.Enabled = true;
                    SF.frmToolBar.SingleRun.Enabled = true;
                }
              
            }
        }

        private void proc_treeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (SF.isAddOps || SF.isModify == ModifyKind.Operation)
            {
                if (proc_treeView.SelectedNode != e.Node)
                {
                    e.Cancel = true;
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("新增或编辑指令中，禁止切换流程。", FrmInfo.Level.Error);
                    }
                    else
                    {
                        MessageBox.Show("新增或编辑指令中，禁止切换流程。");
                    }
                }
            }
        }

        private void proc_treeView_MouseDown(object sender, MouseEventArgs e)
        {
            suppressContextMenuOnce = false;
            if (SF.isAddOps || SF.isModify == ModifyKind.Operation)
            {
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                {
                    var treeView = (System.Windows.Forms.TreeView)sender;
                    var clickedNode = treeView.HitTest(e.Location).Node;
                    if (clickedNode == null || clickedNode != treeView.SelectedNode)
                    {
                        if (e.Button == MouseButtons.Right)
                        {
                            suppressContextMenuOnce = true;
                        }
                        if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                        {
                            SF.frmInfo.PrintInfo("新增或编辑指令中，禁止切换流程。", FrmInfo.Level.Error);
                        }
                        else
                        {
                            MessageBox.Show("新增或编辑指令中，禁止切换流程。");
                        }
                        return;
                    }
                }
            }
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                var treeView = (System.Windows.Forms.TreeView)sender;
                var clickedNode = treeView.HitTest(e.Location).Node;

                if (clickedNode == null) // 点击的是空白区域
                {
                    treeView.SelectedNode = null; // 取消当前节点选择

                    SelectedProcNum = -1;

                    SelectedStepNum = -1;
                    SF.frmDataGrid.iSelectedRow = -1;
                    SF.frmDataGrid.OperationTemp = null;
                    bindingSource.DataSource = null;
                    SF.frmDataGrid.dataGridView1.DataSource = null;
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                }
                if (clickedNode != null)
                {
                    // 选择右键点击的节点
                    treeView.SelectedNode = clickedNode;
                }
            }
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (SelectedProcNum < 0)
            {
                MessageBox.Show("请选择流程或步骤");
                return;
            }
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            CaptureEditBackup();
            SF.BeginEdit(ModifyKind.Proc);
        }

        private void FrmProc_Load(object sender, EventArgs e)
        {
            Refresh();
      
        }

        private void startProc_Click(object sender, EventArgs e)
        {
            if (SelectedProcNum != -1)
            {
                if (!SF.EnsurePermission(PermissionKeys.ProcessRun, "启动流程"))
                {
                    return;
                }
                if (SelectedProcNum >= 0 && SelectedProcNum < procsList.Count && procsList[SelectedProcNum]?.head?.Disable == true)
                {
                    MessageBox.Show("流程已禁用，无法启动。");
                    return;
                }
                int procIndex = SelectedProcNum;
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = "未命名";
                }
                string message = $"确认启动流程：{procIndex}-{procName}";
                Message confirmForm = new Message("启动确认", message,
                    () => SF.DR.StartProc(null, procIndex),
                    () => { },
                    "启动", "取消", false);
                confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                confirmForm.txtMsg.ForeColor = Color.Red;
            }
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (suppressContextMenuOnce)
            {
                suppressContextMenuOnce = false;
                e.Cancel = true;
                return;
            }
            UpdateToggleDisableMenu();
            UpdateCopyPasteMenu();
        }

        private void UpdateToggleDisableMenu()
        {
            if (ToggleDisable == null)
            {
                return;
            }
            TreeNode node = proc_treeView.SelectedNode;
            if (node == null)
            {
                ToggleDisable.Enabled = false;
                ToggleDisable.Text = "禁用";
                return;
            }
            bool isStep = node.Parent != null;
            int procIndex = isStep ? node.Parent.Index : node.Index;
            int stepIndex = isStep ? node.Index : -1;
            bool isDisabled = false;
            if (procIndex >= 0 && procIndex < procsList.Count)
            {
                if (isStep)
                {
                    if (stepIndex >= 0 && stepIndex < procsList[procIndex].steps.Count)
                    {
                        isDisabled = procsList[procIndex].steps[stepIndex]?.Disable == true;
                    }
                }
                else
                {
                    isDisabled = procsList[procIndex]?.head?.Disable == true;
                }
            }
            ToggleDisable.Text = isDisabled
                ? (isStep ? "启用步骤" : "启用流程")
                : (isStep ? "禁用步骤" : "禁用流程");
            ToggleDisable.Enabled = SF.HasPermission(PermissionKeys.ProcessEdit);
        }

        private void UpdateCopyPasteMenu()
        {
            if (CopyProcStep != null)
            {
                CopyProcStep.Enabled = proc_treeView.SelectedNode != null;
            }
            if (PasteProcStep != null)
            {
                PasteProcStep.Enabled = clipboardProc != null || clipboardStep != null;
            }
        }

        private void CopyProcStep_Click(object sender, EventArgs e)
        {
            TreeNode node = proc_treeView.SelectedNode;
            if (node == null)
            {
                MessageBox.Show("请选择需要复制的流程或步骤。");
                return;
            }
            if (node.Parent == null)
            {
                int procIndex = node.Index;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    MessageBox.Show("流程索引无效，无法复制。");
                    return;
                }
                clipboardProc = FrmPropertyGrid.DeepCopy(procsList[procIndex]);
                clipboardStep = null;
            }
            else
            {
                int procIndex = node.Parent.Index;
                int stepIndex = node.Index;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    MessageBox.Show("流程索引无效，无法复制步骤。");
                    return;
                }
                if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
                {
                    MessageBox.Show("步骤索引无效，无法复制。");
                    return;
                }
                clipboardStep = FrmPropertyGrid.DeepCopy(procsList[procIndex].steps[stepIndex]);
                clipboardProc = null;
            }
            UpdateCopyPasteMenu();
        }

        private void PasteProcStep_Click(object sender, EventArgs e)
        {
            if (clipboardProc != null)
            {
                PasteProc();
                return;
            }
            if (clipboardStep != null)
            {
                PasteStep();
                return;
            }
            MessageBox.Show("剪贴板为空，无法粘贴。");
        }

        private void PasteProc()
        {
            if (!SF.CanEditProcStructure())
            {
                return;
            }
            Proc newProc = FrmPropertyGrid.DeepCopy(clipboardProc);
            ResetProcIdentity(newProc);
            UpdateCopiedProcName(newProc);
            int insertIndex = SelectedProcNum >= 0 ? SelectedProcNum + 1 : procsList.Count;
            if (insertIndex < 0 || insertIndex > procsList.Count)
            {
                insertIndex = procsList.Count;
            }
            AdaptGotoProcIndex(newProc, insertIndex);
            List<string> errors = new List<string>();
            NormalizeProc(insertIndex, newProc, errors);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return;
            }
            procsList.Insert(insertIndex, newProc);
            RebuildWorkConfig(insertIndex);
            SelectProcNode(insertIndex, -1);
        }

        private void PasteStep()
        {
            if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
            {
                MessageBox.Show("请选择需要粘贴到的流程。");
                return;
            }
            if (!SF.CanEditProc(SelectedProcNum))
            {
                return;
            }
            Step newStep = FrmPropertyGrid.DeepCopy(clipboardStep);
            ResetStepIdentity(newStep);
            int procIndex = SelectedProcNum;
            int insertIndex = SelectedStepNum >= 0 ? SelectedStepNum + 1 : procsList[procIndex].steps.Count;
            if (insertIndex < 0 || insertIndex > procsList[procIndex].steps.Count)
            {
                insertIndex = procsList[procIndex].steps.Count;
            }
            procsList[procIndex].steps.Insert(insertIndex, newStep);
            ShiftGotoStepIndexForInsert(procIndex, insertIndex);
            List<string> errors = new List<string>();
            NormalizeProc(procIndex, procsList[procIndex], errors);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return;
            }
            SF.mainfrm.SaveAsJson(SF.workPath, procIndex.ToString(), procsList[procIndex]);
            SF.PublishProc(procIndex);
            Refresh();
            SelectProcNode(procIndex, insertIndex);
        }

        private void ResetProcIdentity(Proc proc)
        {
            if (proc == null)
            {
                return;
            }
            if (proc.head == null)
            {
                proc.head = new ProcHead();
            }
            proc.head.Id = Guid.NewGuid();
            if (proc.steps == null)
            {
                proc.steps = new List<Step>();
            }
            foreach (Step step in proc.steps)
            {
                ResetStepIdentity(step);
            }
        }

        private void UpdateCopiedProcName(Proc proc)
        {
            if (proc?.head == null)
            {
                return;
            }
            string sourceName = proc.head.Name;
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = "流程";
            }
            string baseName = GetProcNameBase(sourceName);
            int maxSuffix = 0;
            foreach (Proc item in procsList)
            {
                string name = item?.head?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                if (string.Equals(name, baseName, StringComparison.Ordinal))
                {
                    maxSuffix = Math.Max(maxSuffix, 0);
                    continue;
                }
                if (name.StartsWith(baseName + "_", StringComparison.Ordinal))
                {
                    string suffixText = name.Substring(baseName.Length + 1);
                    if (int.TryParse(suffixText, out int suffix) && suffix > maxSuffix)
                    {
                        maxSuffix = suffix;
                    }
                }
            }
            proc.head.Name = $"{baseName}_{maxSuffix + 1}";
        }

        private string GetProcNameBase(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "流程";
            }
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore <= 0 || lastUnderscore >= name.Length - 1)
            {
                return name;
            }
            string suffixText = name.Substring(lastUnderscore + 1);
            if (!int.TryParse(suffixText, out _))
            {
                return name;
            }
            return name.Substring(0, lastUnderscore);
        }

        private void ResetStepIdentity(Step step)
        {
            if (step == null)
            {
                return;
            }
            step.Id = Guid.NewGuid();
            if (step.Ops == null)
            {
                step.Ops = new List<OperationType>();
            }
            foreach (OperationType op in step.Ops)
            {
                if (op != null)
                {
                    op.Id = Guid.NewGuid();
                }
            }
        }

        private void AdaptGotoProcIndex(Proc proc, int procIndex)
        {
            if (proc?.steps == null)
            {
                return;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType op in step.Ops)
                {
                    AdaptGotoProcIndex(op, procIndex);
                }
            }
        }

        private void AdaptGotoProcIndexForAllProcs(int startIndex)
        {
            if (procsList == null)
            {
                return;
            }
            if (procsList.Count == 0)
            {
                return;
            }
            if (startIndex < 0)
            {
                startIndex = 0;
            }
            if (startIndex >= procsList.Count)
            {
                return;
            }
            for (int i = startIndex; i < procsList.Count; i++)
            {
                AdaptGotoProcIndex(procsList[i], i);
            }
        }

        private void AdaptGotoProcIndex(object obj, int procIndex)
        {
            if (obj == null)
            {
                return;
            }
            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(string)
                    && propertyInfo.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string value = propertyInfo.GetValue(obj) as string;
                    if (!string.IsNullOrWhiteSpace(value)
                        && TryParseGotoKey(value, out _, out int stepIndex, out int opIndex))
                    {
                        propertyInfo.SetValue(obj, $"{procIndex}-{stepIndex}-{opIndex}");
                    }
                }
                var propertyValue = propertyInfo.GetValue(obj);
                if (propertyValue is IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        AdaptGotoProcIndex(item, procIndex);
                    }
                }
            }
        }

        private void ShiftGotoStepIndexForInsert(int procIndex, int insertIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return;
            }
            Proc proc = procsList[procIndex];
            AdjustGotoStepIndex(proc, procIndex, stepIndex =>
            {
                if (stepIndex >= insertIndex)
                {
                    return stepIndex + 1;
                }
                return stepIndex;
            });
        }

        private void ShiftGotoStepIndexForRemove(int procIndex, int removedIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return;
            }
            Proc proc = procsList[procIndex];
            if (proc?.steps == null || proc.steps.Count == 0)
            {
                return;
            }
            int maxIndex = proc.steps.Count - 1;
            AdjustGotoStepIndex(proc, procIndex, stepIndex =>
            {
                if (stepIndex > removedIndex)
                {
                    return stepIndex - 1;
                }
                if (stepIndex == removedIndex)
                {
                    return Math.Min(removedIndex, maxIndex);
                }
                return stepIndex;
            });
        }

        private void AdjustGotoStepIndex(Proc proc, int procIndex, Func<int, int> adjuster)
        {
            if (proc?.steps == null)
            {
                return;
            }
            foreach (Step step in proc.steps)
            {
                if (step?.Ops == null)
                {
                    continue;
                }
                foreach (OperationType op in step.Ops)
                {
                    AdjustGotoStepIndex(op, procIndex, adjuster);
                }
            }
        }

        private void AdjustGotoStepIndex(object obj, int procIndex, Func<int, int> adjuster)
        {
            if (obj == null)
            {
                return;
            }
            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                if (propertyInfo.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(string)
                    && propertyInfo.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    string value = propertyInfo.GetValue(obj) as string;
                    if (!string.IsNullOrWhiteSpace(value)
                        && TryParseGotoKey(value, out int gotoProc, out int gotoStep, out int gotoOp)
                        && gotoProc == procIndex)
                    {
                        int newStep = adjuster(gotoStep);
                        if (newStep != gotoStep)
                        {
                            propertyInfo.SetValue(obj, $"{procIndex}-{newStep}-{gotoOp}");
                        }
                    }
                }
                var propertyValue = propertyInfo.GetValue(obj);
                if (propertyValue is IEnumerable enumerable && !(propertyValue is string))
                {
                    foreach (var item in enumerable)
                    {
                        AdjustGotoStepIndex(item, procIndex, adjuster);
                    }
                }
            }
        }

        private void SelectProcNode(int procIndex, int stepIndex)
        {
            if (proc_treeView == null || proc_treeView.Nodes.Count == 0)
            {
                return;
            }
            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count)
            {
                return;
            }
            TreeNode target = proc_treeView.Nodes[procIndex];
            if (stepIndex >= 0 && stepIndex < target.Nodes.Count)
            {
                target = target.Nodes[stepIndex];
            }
            proc_treeView.SelectedNode = target;
            target.EnsureVisible();
        }

        private void ToggleDisable_Click(object sender, EventArgs e)
        {
            TreeNode node = proc_treeView.SelectedNode;
            if (node == null)
            {
                return;
            }
            bool isStep = node.Parent != null;
            int procIndex = isStep ? node.Parent.Index : node.Index;
            int stepIndex = isStep ? node.Index : -1;
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return;
            }
            if (!SF.CanEditProc(procIndex))
            {
                return;
            }
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            if (snapshot != null && snapshot.State != ProcRunState.Stopped)
            {
                MessageBox.Show("流程运行中禁止禁用或启用。");
                return;
            }
            Proc proc = procsList[procIndex];
            if (proc == null)
            {
                return;
            }
            if (isStep)
            {
                if (stepIndex < 0 || stepIndex >= proc.steps.Count)
                {
                    return;
                }
                Step step = proc.steps[stepIndex];
                if (step == null)
                {
                    return;
                }
                step.Disable = !step.Disable;
                UpdateStepNodeStyle(procIndex, stepIndex);
            }
            else
            {
                if (proc.head == null)
                {
                    proc.head = new ProcHead();
                }
                proc.head.Disable = !proc.head.Disable;
                UpdateProcNodeStyle(procIndex);
                UpdateStepNodeStylesForProc(procIndex);
            }
            SF.frmDataGrid.SaveSingleProc(procIndex);
        }

        private void UpdateProcNodeStyle(int procIndex)
        {
            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count || procIndex >= procsList.Count)
            {
                return;
            }
            bool disabled = procsList[procIndex]?.head?.Disable == true;
            TreeNode node = proc_treeView.Nodes[procIndex];
            if (node != null)
            {
                node.ForeColor = disabled ? DisabledNodeColor : Color.Black;
                node.Text = BuildProcNodeText(procIndex);
            }
        }

        private void UpdateStepNodeStyle(int procIndex, int stepIndex)
        {
            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count || procIndex >= procsList.Count)
            {
                return;
            }
            if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
            {
                return;
            }
            if (stepIndex >= proc_treeView.Nodes[procIndex].Nodes.Count)
            {
                return;
            }
            bool disabled = procsList[procIndex]?.head?.Disable == true || procsList[procIndex].steps[stepIndex]?.Disable == true;
            TreeNode node = proc_treeView.Nodes[procIndex].Nodes[stepIndex];
            if (node != null)
            {
                node.ForeColor = disabled ? DisabledNodeColor : Color.Black;
                node.Text = BuildStepNodeText(procIndex, stepIndex);
            }
        }

        private void UpdateStepNodeStylesForProc(int procIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return;
            }
            int stepCount = procsList[procIndex].steps.Count;
            for (int i = 0; i < stepCount; i++)
            {
                UpdateStepNodeStyle(procIndex, i);
            }
        }

        private string BuildProcNodeText(int procIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return string.Empty;
            }
            Proc proc = procsList[procIndex];
            return BuildProcNodeText(procIndex, proc);
        }

        private string BuildStepNodeText(int procIndex, int stepIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return string.Empty;
            }
            if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
            {
                return string.Empty;
            }
            Step step = procsList[procIndex].steps[stepIndex];
            Proc proc = procsList[procIndex];
            return BuildStepNodeText(procIndex, stepIndex, proc, step);
        }

        private string BuildProcNodeText(int procIndex, Proc proc)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, null);
            bool disabled = proc?.head?.Disable == true;
            return BuildProcNodeTextCore(procIndex, procName, disabled, string.Empty);
        }

        internal string BuildProcNodeTextWithState(int procIndex, Proc proc, EngineSnapshot snapshot)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, snapshot);
            bool disabled = proc?.head?.Disable == true;
            string suffix = string.Empty;
            if (snapshot != null)
            {
                suffix = $"|{GetProcStateText(snapshot.State)}";
                if (snapshot.IsBreakpoint)
                {
                    suffix += "|断点";
                }
            }
            return BuildProcNodeTextCore(procIndex, procName, disabled, suffix);
        }

        internal bool TryGetProcNode(Guid procId, out TreeNode node, out int procIndex)
        {
            node = null;
            procIndex = -1;
            if (procId == Guid.Empty)
            {
                return false;
            }
            lock (procNodeMapLock)
            {
                if (procNodeMap.TryGetValue(procId, out TreeNode cachedNode)
                    && procIndexMap.TryGetValue(procId, out int cachedIndex))
                {
                    node = cachedNode;
                    procIndex = cachedIndex;
                    return true;
                }
            }
            return false;
        }

        private string BuildProcNodeTextCore(int procIndex, string procName, bool disabled, string suffix)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string safeName = string.IsNullOrWhiteSpace(procName) ? $"流程{procIndex}" : procName;
            string tag = disabled ? DisabledTag : string.Empty;
            string text = $"{procIndex}：{tag}{safeName}";
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                text += suffix;
            }
            return text;
        }

        private string ResolveProcName(int procIndex, Proc proc, EngineSnapshot snapshot)
        {
            string name = snapshot?.ProcName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"流程{procIndex}" : proc.head.Name;
            }
            return name;
        }

        private static string GetProcStateText(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Stopped:
                    return "停止";
                case ProcRunState.Paused:
                    return "暂停";
                case ProcRunState.SingleStep:
                    return "单步";
                case ProcRunState.Running:
                    return "运行";
                case ProcRunState.Alarming:
                    return "报警中";
                default:
                    return "未知";
            }
        }

        private string BuildStepNodeText(int procIndex, int stepIndex, Proc proc, Step step)
        {
            if (procIndex < 0 || stepIndex < 0)
            {
                return string.Empty;
            }
            string stepName = string.IsNullOrWhiteSpace(step?.Name) ? $"步骤{stepIndex}" : step.Name;
            bool disabled = proc?.head?.Disable == true || step?.Disable == true;
            string tag = disabled ? DisabledTag : string.Empty;
            return $"{stepIndex}：{tag}{stepName}";
        }
        public Tuple<int, int, int> FindOperationTypeIndex(OperationType hash)
        {
            
            for (int procIndex = 0; procIndex < procsList.Count; procIndex++)
            {
                Proc proc = procsList[procIndex];
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        if (step.Ops[opIndex] == hash)
                        {
                            return new Tuple<int, int, int>(procIndex, stepIndex, opIndex);
                        }
                    }
                }
            }
            return new Tuple<int, int, int>(-1, -1, -1);
        }

        public void RefleshGoto()
        {
            if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
            {
                return;
            }
            if (SelectedStepNum < 0 || SelectedStepNum >= procsList[SelectedProcNum].steps.Count)
            {
                return;
            }
            for (int i = 0; i < procsList[SelectedProcNum].steps.Count; i++)
            {
                for (int j = 0; j < procsList[SelectedProcNum].steps[i].Ops.Count; j++)
                {
                    var obj = procsList[SelectedProcNum].steps[i].Ops[j];

                    foreach (var propertyInfo in obj.GetType().GetProperties())
                    {
                        var markedAttribute = propertyInfo.GetCustomAttribute<MarkedGotoAttribute>();

                        if (markedAttribute != null)
                        {
                            string currentValue = (string)propertyInfo.GetValue(obj);

                            if (!string.IsNullOrEmpty(currentValue)) 
                            {
                                if (TryParseGotoKey(currentValue, out int gotoProc, out int gotoStep, out int gotoOp))
                                {
                                    if (SelectedProcNum == gotoProc && SelectedStepNum == gotoStep)
                                    {
                                        OperationType temp = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.FirstOrDefault(sc => sc.Num == gotoOp);
                                        if (temp != null)
                                        {
                                            int tp = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.IndexOf(temp);
                                            propertyInfo.SetValue(obj, $"{SelectedProcNum}-{SelectedStepNum}-{tp}");
                                        }
                                    }
                                }
                               
                            }
                        }
                        // 获取属性的值
                        var propertyValue = propertyInfo.GetValue(obj);
                        // 如果属性的值是 List<T> 类型，进一步迭代获取列表元素的属性信息
                        if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                        {
                            foreach (var listItem in enumerable)
                            {
                                foreach (var listItemPropertyInfo in listItem.GetType().GetProperties())
                                {
                                    var listItemMarkedAttribute = listItemPropertyInfo.GetCustomAttribute <MarkedGotoAttribute>();

                                    if (listItemMarkedAttribute != null)
                                    {

                                        // 获取标记了 MarkedGotoAttribute 的属性值
                                        var markedPropertyValue = listItemPropertyInfo.GetValue(listItem);

                                        if (markedPropertyValue != null
                                            && TryParseGotoKey(markedPropertyValue.ToString(), out int gotoProc, out int gotoStep, out int gotoOp))
                                        {
                                            if (SelectedProcNum == gotoProc && SelectedStepNum == gotoStep)
                                            {
                                                OperationType temp = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.FirstOrDefault(sc => sc.Num == gotoOp);
                                                if (temp != null)
                                                {
                                                    int tp = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.IndexOf(temp);
                                                    listItemPropertyInfo.SetValue(listItem, $"{SelectedProcNum}-{SelectedStepNum}-{tp}");
                                                }

                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }

                }
            }
        }
    }

    //存放单个流程信息
    [Serializable]
    public class Proc
    {
        public ProcHead head;
        public List<Step> steps = new List<Step>();
    }
    [Serializable]
    public class ProcHead
    {
        public ProcHead()
        {
            ParamListConverter<PauseIoParam>.Name = "暂停IO";
            ParamListConverter<PauseValueParam>.Name = "暂停变量";
            PauseIoParams = new CustomList<PauseIoParam>();
            PauseValueParams = new CustomList<PauseValueParam>();
            Id = Guid.NewGuid();
        }

        [Browsable(false)]
        public Guid Id { get; set; }

        [DisplayName("流程名称"), Category("流程信息"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("自启动"), Category("流程信息"), Description("启动程序时自动运行"), ReadOnly(false)]
        public bool AutoStart { get; set; }

        [DisplayName("禁用"), Category("流程信息"), Description(""), ReadOnly(false)]
        public bool Disable { get; set; }

        private string pauseIoCount;
        [DisplayName("暂停IO数"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(PauseCountItem))]
        public string PauseIoCount
        {
            get { return pauseIoCount; }
            set
            {
                pauseIoCount = value;
                if (SF.frmPropertyGrid?.propertyGrid1?.SelectedObject != this)
                {
                    return;
                }
                if (!int.TryParse(pauseIoCount, out int count) || count <= 0)
                {
                    PauseIoParams?.Clear();
                }
                else
                {
                    if (PauseIoParams == null)
                    {
                        PauseIoParams = new CustomList<PauseIoParam>();
                    }
                    PauseIoParams.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        PauseIoParams.Add(new PauseIoParam());
                    }
                }
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = this;
                SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
            }
        }

        [DisplayName("暂停IO"), Category("暂停信号"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<PauseIoParam>))]
        public CustomList<PauseIoParam> PauseIoParams { get; set; }

        private string pauseValueCount;
        [DisplayName("暂停变量数"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(PauseCountItem))]
        public string PauseValueCount
        {
            get { return pauseValueCount; }
            set
            {
                pauseValueCount = value;
                if (SF.frmPropertyGrid?.propertyGrid1?.SelectedObject != this)
                {
                    return;
                }
                if (!int.TryParse(pauseValueCount, out int count) || count <= 0)
                {
                    PauseValueParams?.Clear();
                }
                else
                {
                    if (PauseValueParams == null)
                    {
                        PauseValueParams = new CustomList<PauseValueParam>();
                    }
                    PauseValueParams.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        PauseValueParams.Add(new PauseValueParam());
                    }
                }
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = this;
                SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
            }
        }

        [DisplayName("暂停变量"), Category("暂停信号"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<PauseValueParam>))]
        public CustomList<PauseValueParam> PauseValueParams { get; set; }

    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class PauseIoParam
    {
        [DisplayName("名称"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(IoInItem))]
        public string IOName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class PauseValueParam
    {
        [DisplayName("变量名称"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class Step
    {
        [Browsable(false)]
        public Guid Id { get; set; }

        [DisplayName("步骤名称"), Category("步骤信息"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("禁用"), Category("步骤信息"), Description(""), ReadOnly(false)]
        public bool Disable { get; set; }

        public List<OperationType> Ops = new List<OperationType>();
    }
}
