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
        void NotifyProcessChanged(int procIndex, ProcChangeKind kind,
            List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOperations = null);
        bool RebuildWorkConfig(int startIndex);
        void RefreshProcesses();
        void RefreshProcess(int procIndex);
        void RefreshVariables();
        void RefreshStations();
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

        public void NotifyProcessChanged(int procIndex, ProcChangeKind kind,
            List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOperations = null)
        {
            owner.RefreshProcessFlowGraph();
            try
            {
                if (owner.frmProc == null || owner.frmProc.IsDisposed
                    || owner.frmDataGrid == null || owner.frmDataGrid.IsDisposed
                    || owner.frmProc.SelectedProcNum != procIndex)
                {
                    return;
                }
                owner.frmProc.RefreshCurrentBinding();
                if (affectedOperations != null && affectedOperations.Count > 0)
                {
                    owner.frmDataGrid.FlashRows(affectedOperations);
                }
                else
                {
                    owner.frmDataGrid.FlashGrid(kind);
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

        public void RefreshStations()
        {
            owner.frmCard?.RefreshStationList();
            owner.frmCard?.RefreshStationTree();
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
