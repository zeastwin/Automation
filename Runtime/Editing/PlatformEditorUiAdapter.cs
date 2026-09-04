using System;
// 模块：运行时 / 编辑协作。
// 职责范围：管理编辑会话、历史、剪贴板、联合提交以及编辑器 UI 适配边界。

using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public sealed class PlatformEditorSelection
    {
        public int ProcIndex { get; set; }
        public int StepIndex { get; set; }
        public int OperationIndex { get; set; }
    }

    public sealed class PlatformInfoLogEntry
    {
        public string TimeText { get; set; }
        public string Message { get; set; }
        public string Level { get; set; }
    }

    /// <summary>
    /// AI 提交后单条指令的变更定位；OpIndex 为提交后新列表中的行号。
    /// </summary>
    public sealed class ProcessOperationChangeNotice
    {
        public int OpIndex { get; set; }

        public ProcChangeKind Kind { get; set; }
    }

    /// <summary>
    /// AI 提交后单个步骤的变更定位；StepIndex 为提交后新列表中的步骤索引。
    /// </summary>
    public sealed class ProcessStepChangeNotice
    {
        public Guid StepId { get; set; }

        public int StepIndex { get; set; }

        public ProcChangeKind Kind { get; set; }

        public List<ProcessOperationChangeNotice> Operations { get; } =
            new List<ProcessOperationChangeNotice>();
    }

    /// <summary>
    /// AI 提交后单个流程的变更定位，用于流程树节点与指令表的闪烁提示。
    /// </summary>
    public sealed class ProcessChangeNotice
    {
        public int ProcIndex { get; set; }

        public Guid ProcId { get; set; }

        public string Name { get; set; }

        public ProcChangeKind Kind { get; set; }

        public List<ProcessStepChangeNotice> Steps { get; } =
            new List<ProcessStepChangeNotice>();
    }

    /// <summary>
    /// 非 UI 模块访问平台编辑器时使用的唯一 WinForms 适配边界。
    /// </summary>
    public interface IPlatformEditorUiAdapter
    {
        bool IsReady { get; }
        bool IsAutoApproveMode { get; }
        IODebugMap IoDebugMap { get; }
        OperationType CurrentOperationContext { get; }
        int SelectedVariableSlotIndex { get; }
        IWin32Window DialogOwner { get; }
        PlatformEditorSelection GetSelection();
        void SelectProcessContext(int procIndex, int stepIndex);
        IReadOnlyList<PlatformInfoLogEntry> GetInfoLogTail(int maxCount);
        void NotifyProcessChanged(IReadOnlyList<ProcessChangeNotice> notices);
        bool RebuildWorkConfig(int startIndex);
        void RefreshProcesses();
        void RefreshProcess(int procIndex);
        void RefreshVariables();
        void RefreshDataStructures();
        void RefreshMotionIo();
        void RefreshIoDebug();
        void RefreshCommunication();
        void RefreshAlarmConfiguration();
        void BeginEditSession(object draft);
        void PresentEditDraft(object draft);
        void ClearEditDraft(object canceledDraft);
        void EndEditSession();
        void RefreshEditorHistoryActions();
        void WriteInfo(string message, LogLevel level);
        void ShowMessage(string message, string title, bool error);
        T WithOperationContext<T>(OperationType operation, bool enableEditBehavior, Func<T> action);
    }

    public sealed class WinFormsPlatformEditorUiAdapter : IPlatformEditorUiAdapter
    {
        private readonly FrmMain owner;

        public WinFormsPlatformEditorUiAdapter(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public bool IsReady => !owner.IsDisposed
            && owner.frmProc != null && !owner.frmProc.IsDisposed
            && owner.frmInspector != null && !owner.frmInspector.IsDisposed;

        public bool IsAutoApproveMode => owner.frmAiAssistant?.IsAutoApproveMode == true;

        public IODebugMap IoDebugMap => owner.frmIODebug?.IODebugMaps ?? new IODebugMap();

        public OperationType CurrentOperationContext => owner.frmDataGrid?.OperationTemp;

        public int SelectedVariableSlotIndex => owner.frmValue?.GetSelectedVariableSlotIndex() ?? -1;

        public IWin32Window DialogOwner => !owner.IsDisposed && owner.Visible ? owner : null;

        public PlatformEditorSelection GetSelection()
        {
            return new PlatformEditorSelection
            {
                ProcIndex = owner.frmProc?.SelectedProcNum ?? -1,
                StepIndex = owner.frmProc?.SelectedStepNum ?? -1,
                OperationIndex = owner.frmDataGrid?.iSelectedRow ?? -1
            };
        }

        public void SelectProcessContext(int procIndex, int stepIndex)
        {
            owner.frmProc?.SelectAiContext(procIndex, stepIndex);
        }

        public IReadOnlyList<PlatformInfoLogEntry> GetInfoLogTail(int maxCount)
        {
            return (owner.frmInfo?.GetInfoLogTail(maxCount) ?? new List<FrmInfo.InfoLogSnapshot>())
                .Select(item => new PlatformInfoLogEntry
                {
                    TimeText = item.TimeText,
                    Message = item.Message,
                    Level = item.Level.ToString()
                })
                .ToList();
        }

        public void NotifyProcessChanged(IReadOnlyList<ProcessChangeNotice> notices)
        {
            if (notices == null || notices.Count == 0 || owner.IsDisposed)
            {
                return;
            }
            try
            {
                if (owner.InvokeRequired)
                {
                    owner.BeginInvoke((Action)(() => NotifyOnUiThread(notices)));
                    return;
                }
                NotifyOnUiThread(notices);
            }
            catch
            {
                // 编辑器动效失败不改变配置提交结果。
            }
        }

        private void NotifyOnUiThread(IReadOnlyList<ProcessChangeNotice> notices)
        {
            // 经 BeginInvoke 派发后异常不会再回到调用方 try/catch，这里必须自兜底，
            // 保证闪烁动效失败不影响配置提交结果。
            try
            {
                if (owner.IsDisposed)
                {
                    return;
                }
                owner.RefreshProcessFlowGraph();
                if (owner.frmProc == null || owner.frmProc.IsDisposed)
                {
                    return;
                }
                // 指令级改动（既有步骤内的新增/修改指令）只闪烁指令行，不闪流程树，避免分散注意力；
                // 涉及流程/步骤增删或步骤本身改动时才闪烁树节点定位改动位置。
                bool operationOnlyChange = notices.All(notice =>
                    notice.Kind == ProcChangeKind.Modified
                    && notice.Steps.All(step => step.Kind == ProcChangeKind.Modified));
                if (!operationOnlyChange)
                {
                    owner.frmProc.NotifyAiProcessChanged(notices);
                }
                if (owner.frmDataGrid == null || owner.frmDataGrid.IsDisposed)
                {
                    return;
                }
                // 指令表优先跟随用户正在浏览的被改流程；否则定位到第一个被改流程。
                // 目标是让用户直接看到 AI 改了哪里，因此总是切换到改动步骤并闪烁对应指令行。
                int selectedProc = owner.frmProc.SelectedProcNum;
                ProcessChangeNotice current = notices.FirstOrDefault(item =>
                    item.ProcIndex == selectedProc)
                    ?? notices.FirstOrDefault();
                if (current == null)
                {
                    return;
                }
                // 新增/修改的步骤或指令不在当前显示的步骤内时，自动切换选中到改动步骤，
                // 让指令表立即显示该步骤并闪烁其中被改动的指令行。
                ProcessStepChangeNotice targetStep = current.Steps
                    .FirstOrDefault(step => step.Operations.Count > 0)
                    ?? current.Steps.FirstOrDefault();
                bool located = false;
                if (targetStep != null
                    && (selectedProc != current.ProcIndex
                        || owner.frmProc.SelectedStepNum != targetStep.StepIndex))
                {
                    located = owner.frmProc.TrySelectProcessStep(
                        current.ProcIndex,
                        targetStep.StepIndex);
                }
                if (!located)
                {
                    owner.frmProc.RefreshCurrentBinding();
                }
                var affectedOps = new List<(int stepIndex, int opIndex, ProcChangeKind kind)>();
                foreach (ProcessStepChangeNotice step in current.Steps)
                {
                    foreach (ProcessOperationChangeNotice operation in step.Operations)
                    {
                        affectedOps.Add((step.StepIndex, operation.OpIndex, operation.Kind));
                    }
                }
                if (affectedOps.Count > 0)
                {
                    owner.frmDataGrid.FlashRows(affectedOps);
                }
                else if (current.Steps.Count > 0)
                {
                    // 当前流程被改但改动无法落到指令行（如步骤更名）时闪烁整个网格。
                    owner.frmDataGrid.FlashGrid(current.Kind);
                }
            }
            catch
            {
                // 编辑器动效失败不改变配置提交结果。
            }
        }

        public bool RebuildWorkConfig(int startIndex)
        {
            return owner.frmProc?.RebuildWorkConfig(startIndex) == true;
        }

        public void RefreshProcesses()
        {
            owner.frmProc?.RefreshProcList();
            owner.frmSearch?.PrewarmIndex();
            owner.PrewarmProcessFlowGraphs();
        }

        public void RefreshProcess(int procIndex)
        {
            owner.frmProc?.RefreshProcView(procIndex);
            owner.frmSearch?.PrewarmIndex();
            owner.PrewarmProcessFlowGraph(procIndex);
        }

        public void RefreshVariables()
        {
            owner.frmValue?.FreshFrmValue();
        }

        public void RefreshDataStructures()
        {
            owner.frmdataStruct?.RefreshDataSturctList();
            owner.frmdataStruct?.RefreshDataSturctTree();
        }

        public void RefreshMotionIo()
        {
            owner.frmIO?.RefreshIOMap();
            owner.frmCard?.RefreshCardTree();
        }

        public void RefreshIoDebug()
        {
            owner.frmIODebug?.RefreshIODebugMap();
            owner.frmIODebug?.RefreshIODebugMapFrm();
        }

        public void RefreshCommunication()
        {
            owner.frmCommunication?.RefreshSocketMap();
            owner.frmCommunication?.RefreshSerialPortInfo();
        }

        public void RefreshAlarmConfiguration()
        {
            owner.frmAlarmConfig?.RefreshAlarmInfo();
        }

        public void BeginEditSession(object draft)
        {
            owner.frmInspector?.ShowObject(draft);
            owner.RefreshEditorNavigationActions();
            owner.frmToolBar?.RefreshHistoryAvailability();
        }

        public void PresentEditDraft(object draft)
        {
            EditorServiceRegistry.AttachGraph(draft, owner.Runtime);
            if (draft is OperationType operation)
            {
                operation.RefreshInspector?.Invoke();
                if (owner.frmDataGrid != null)
                {
                    owner.frmDataGrid.OperationTemp = operation;
                }
            }
            owner.frmInspector?.ShowObject(draft);
        }

        public void ClearEditDraft(object canceledDraft)
        {
            if (canceledDraft is OperationType
                && owner.frmDataGrid?.TryRestoreSelectedOperationPresentation() == true)
            {
                return;
            }
            owner.frmInspector?.ClearObject();
        }

        public void EndEditSession()
        {
            owner.frmInspector?.SetEditingState(false);
            owner.RefreshEditorNavigationActions();
            owner.frmToolBar?.RefreshHistoryAvailability();
        }

        public void RefreshEditorHistoryActions()
        {
            owner.frmToolBar?.RefreshHistoryAvailability();
        }

        public void WriteInfo(string message, LogLevel level)
        {
            owner.frmInfo?.PrintInfo(message,
                level == LogLevel.Error ? FrmInfo.Level.Error : FrmInfo.Level.Normal);
        }

        public void ShowMessage(string message, string title, bool error)
        {
            MessageBox.Show(owner, message ?? string.Empty, title ?? string.Empty,
                MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
        }

        public T WithOperationContext<T>(OperationType operation, bool enableEditBehavior, Func<T> action)
        {
            EditorServiceRegistry.AttachGraph(operation, owner.Runtime);
            OperationType originalOperation = owner.frmDataGrid?.OperationTemp;
            ModifyKind originalModify = owner.Runtime.Editor.ModifyKind;
            bool originalAddOperation = owner.Runtime.Editor.IsAddingOperations;
            try
            {
                if (owner.frmDataGrid != null)
                {
                    owner.frmDataGrid.OperationTemp = operation;
                }
                owner.Runtime.Editor.ModifyKind = enableEditBehavior ? ModifyKind.Operation : ModifyKind.None;
                owner.Runtime.Editor.IsAddingOperations = false;
                return action();
            }
            finally
            {
                if (owner.frmDataGrid != null)
                {
                    owner.frmDataGrid.OperationTemp = originalOperation;
                }
                owner.Runtime.Editor.ModifyKind = originalModify;
                owner.Runtime.Editor.IsAddingOperations = originalAddOperation;
            }
        }
    }
}
