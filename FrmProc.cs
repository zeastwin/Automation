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

        private ProcHead editProcHeadBackup;
        private Step editStepBackup;
        private int editProcIndex = -1;
        private int editStepIndex = -1;
        private bool hasEditBackup = false;

        public FrmProc()
        {
            InitializeComponent();
            NewProcNum = -1;
            NewStepNum = -1;
            this.proc_treeView.HideSelection = false;
            proc_treeView.BeforeSelect += proc_treeView_BeforeSelect;
        }

        public void NewProcSave()
        {
            Proc proc = new Proc();
            proc.head = HeadTemp;

            if (SelectedProcNum == -1)
            {
                procsList.Add(proc);
                SF.mainfrm.SaveAsJson(SF.workPath,(procsList.Count - 1).ToString(), proc);
            }
            else
            {
                procsList.Insert(SelectedProcNum+1, proc);
                RebuildWorkConfig();
            }

            NewProcNum = -1;

        }
       
        public void NewStepSave()
        {
            if (SelectedStepNum == -1)
            {
                procsList[SelectedProcNum].steps.Add(StepTemp);
            }
            else
            {
                procsList[SelectedProcNum].steps.Insert(SelectedStepNum + 1, StepTemp);
            }

            SF.mainfrm.SaveAsJson(SF.workPath,SelectedProcNum.ToString(), procsList[SelectedProcNum]);

            NewStepNum = -1;

        }
        public void RebuildWorkConfig()
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

            proc_treeView.Nodes.Clear();

            string path = SF.workPath.TrimEnd('\\');

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            List<int> indices = new List<int>();
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out int index))
                {
                    indices.Add(index);
                }
            }
            indices.Sort();

            for (int i = 0; i < indices.Count; i++)
            {
                Proc proc = SF.mainfrm.ReadJson<Proc>(SF.workPath, indices[i].ToString());
                if (proc == null)
                {
                    continue;
                }

                procsListTemp.Add(proc);

                TreeNode treeNode = new TreeNode(i + "：" + proc.head.Name);
                proc_treeView.Nodes.Add(treeNode);

                if (proc.steps != null)
                {
                    for (int j = 0; j < proc.steps.Count; j++)
                    {
                        TreeNode chnode = new TreeNode(j + "：" + proc.steps[j].Name);
                        proc_treeView.Nodes[i].Nodes.Add(chnode);
                    }
                }
            }
            procsList = procsListTemp;

            proc_treeView.ExpandAll();
            procListItem.Clear();
            procListItemCount.Clear();
            foreach (var item in SF.frmProc.procsList)
            {
                procListItem.Add(item.head.Name);
                procListItemCount.Add((procListItemCount.Count+1).ToString());
            }
            if (SF.DR?.Context != null)
            {
                SF.DR.Context.Procs = procsList;
            }
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
                            proc_treeView.Nodes[editProcIndex].Text = $"{editProcIndex}：{procsList[editProcIndex].head.Name}";
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
                    proc_treeView.Nodes[editProcIndex].Nodes[editStepIndex].Text = $"{editStepIndex}：{step.Name}";
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
                RebuildWorkConfig();
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
                procsList[SelectedProcNum].steps.RemoveAt(SelectedStepNum);
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

                    bindingSource.DataSource = procsList[SelectedProcNum].steps[SelectedStepNum].Ops;

                    SF.frmPropertyGrid.propertyGrid1.SelectedObject =procsList[SelectedProcNum].steps[SF.frmProc.SelectedStepNum];


                }
                else
                {
                    SelectedProcNum = proc_treeView.SelectedNode.Index;

                    SelectedStepNum = -1;

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
            if (SF.isAddOps || SF.isModify == ModifyKind.Operation)
            {
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                {
                    var treeView = (System.Windows.Forms.TreeView)sender;
                    var clickedNode = treeView.GetNodeAt(e.Location);
                    if (clickedNode == null || clickedNode != treeView.SelectedNode)
                    {
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
                var clickedNode = treeView.GetNodeAt(e.Location);

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
                SF.DR.StartProc(SF.frmProc.procsList[SF.frmProc.SelectedProcNum], SF.frmProc.SelectedProcNum);
            }
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
                                string[] key = currentValue.Split('-');
                                if(SelectedProcNum == int.Parse(key[0]) &&  SelectedStepNum == int.Parse(key[1]))
                                {
                                    OperationType temp  = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.FirstOrDefault(sc => sc.Num == int.Parse(key[2]));
                                    if (temp != null)
                                    {
                                        int tp = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.IndexOf(temp);
                                        propertyInfo.SetValue(obj,$"{SelectedProcNum}-{SelectedStepNum}-{tp}");
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

                                        string[] key = markedPropertyValue.ToString().Split('-');
                                        if (SelectedProcNum == int.Parse(key[0]) && SelectedStepNum == int.Parse(key[1]))
                                        {
                                            OperationType temp = procsList[SelectedProcNum].steps[SelectedStepNum].Ops.FirstOrDefault(sc => sc.Num == int.Parse(key[2]));
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

    //存放单个流程信息
    public class Proc
    {
        public ProcHead head;
        public List<Step> steps = new List<Step>();
    }
    public class ProcHead
    {
        public ProcHead()
        {
            ParamListConverter<PauseIoParam>.Name = "暂停IO";
            ParamListConverter<PauseValueParam>.Name = "暂停变量";
            PauseIoParams = new CustomList<PauseIoParam>();
            PauseValueParams = new CustomList<PauseValueParam>();
        }

        [DisplayName("流程名称"), Category("流程信息"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("自启动"), Category("流程信息"), Description("启动程序时自动运行"), ReadOnly(false)]
        public bool AutoStart { get; set; }

        private string pauseIoCount;
        [DisplayName("暂停IO数"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(IOCountItem))]
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
        [DisplayName("暂停变量数"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(IOCountItem))]
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
    public class Step
    {
        [DisplayName("步骤名称"), Category("步骤信息"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        public List<OperationType> Ops = new List<OperationType>();
    }
}
