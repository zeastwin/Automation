using Automation.MotionControl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

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

    public interface IEditSession
    {
        string Name { get; }
        object Draft { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        void ReplaceDraft(object draft);
        void CaptureDraftSnapshot();
        bool TryUndo(out object draft, out string error);
        bool TryRedo(out object draft, out string error);
        bool TryCommit(out string error);
        void Cancel();
    }

    public sealed class EditSession<T> : IEditSession where T : class
    {
        private readonly Func<T, string> validate;
        private readonly Action<T> commit;
        private readonly Action cancel;
        private readonly List<T> draftHistory = new List<T>();
        private int draftHistoryIndex;
        private const int MaxDraftHistoryCount = 100;

        public EditSession(string name, T draft, Func<T, string> validate, Action<T> commit, Action cancel = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("编辑会话名称为空。", nameof(name)) : name;
            DraftValue = draft ?? throw new ArgumentNullException(nameof(draft));
            this.validate = validate;
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
            this.cancel = cancel;
            draftHistory.Add(ObjectGraphCloner.Clone(DraftValue));
        }

        public string Name { get; }
        public T DraftValue { get; private set; }
        public object Draft => DraftValue;
        public bool CanUndo => draftHistoryIndex > 0;
        public bool CanRedo => draftHistoryIndex >= 0 && draftHistoryIndex < draftHistory.Count - 1;

        public void ReplaceDraft(object draft)
        {
            DraftValue = draft as T ?? throw new InvalidOperationException("编辑草稿类型不匹配。");
        }

        public void CaptureDraftSnapshot()
        {
            if (draftHistoryIndex < draftHistory.Count - 1)
            {
                draftHistory.RemoveRange(
                    draftHistoryIndex + 1,
                    draftHistory.Count - draftHistoryIndex - 1);
            }
            draftHistory.Add(ObjectGraphCloner.Clone(DraftValue));
            draftHistoryIndex = draftHistory.Count - 1;
            if (draftHistory.Count > MaxDraftHistoryCount)
            {
                draftHistory.RemoveAt(0);
                draftHistoryIndex--;
            }
        }

        public bool TryUndo(out object draft, out string error)
        {
            return TryMoveDraftHistory(-1, out draft, out error);
        }

        public bool TryRedo(out object draft, out string error)
        {
            return TryMoveDraftHistory(1, out draft, out error);
        }

        public bool TryCommit(out string error)
        {
            error = validate?.Invoke(DraftValue);
            if (!string.IsNullOrEmpty(error))
            {
                return false;
            }
            commit(DraftValue);
            return true;
        }

        public void Cancel()
        {
            cancel?.Invoke();
        }

        private bool TryMoveDraftHistory(int offset, out object draft, out string error)
        {
            draft = DraftValue;
            error = null;
            int targetIndex = draftHistoryIndex + offset;
            if (targetIndex < 0 || targetIndex >= draftHistory.Count)
            {
                return false;
            }
            try
            {
                draftHistoryIndex = targetIndex;
                DraftValue = ObjectGraphCloner.Clone(draftHistory[draftHistoryIndex]);
                draft = DraftValue;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public static class SF
    {
        public static FrmMenu frmMenu;
        public static FrmProc frmProc;
        public static FrmDataGrid frmDataGrid;
        public static FrmInspector frmInspector;
        public static FrmToolBar frmToolBar;
        public static FrmValue frmValue;
        public static FrmMain mainfrm;
        public static ProcessEngine DR;
        public static FrmCard frmCard;
        public static FrmIO frmIO;
        public static IMotionRuntime motion;
        public static IIoRuntime io;
        public static FrmControl frmControl;
        public static FrmStation frmStation;
        public static FrmDataStruct frmdataStruct;
        public static FrmIODebug frmIODebug;
        public static FrmComunication frmComunication;
        public static FrmState frmState;
        public static FrmValueDebug frmValueDebug;
        public static FrmAiAssistant frmAiAssistant;
        public static CustomFunc customFunc;
        public static FrmAlarmConfig frmAlarmConfig;
        public static FrmSearch frmSearch;
        public static FrmSearch4Value frmSearch4Value;
        public static FrmInfo frmInfo;
        public static FrmPlc frmPlc;
        public static CardConfigStore cardStore;
        public static ValueConfigStore valueStore;
        public static DataStructStore dataStructStore;
        public static TrayPointStore trayPointStore;
        public static AlarmInfoStore alarmInfoStore;
        public static IProcessEngineStore procStore;
        public static CommunicationHub comm;
        public static CommunicationConfigStore communicationStore;
        public static PlcConfigStore plcStore;
        public static PlcRuntimeService plcRuntime;
        public static FrmVersionManager frmVersionManager;
        public static ConfigurationVersionService versionService;
        public static EditorHistoryService EditorHistory { get; } = new EditorHistoryService();
        public static IEditSession ActiveEditSession { get; private set; }

        private static volatile bool securityLocked;
        private static string securityLockReason = string.Empty;
        private static readonly object maintenanceLock = new object();
        private static bool maintenanceActive;
        private static string maintenanceReason = string.Empty;
        //编辑状态
        public static ModifyKind isModify = ModifyKind.None;

        public static bool isAddOps = false;
        //指示当前页面
        public static int curPage = 0;

        //流程的储存路径
        public static string workPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config\\Work\\";
        //变量的储存路径
        public static string ConfigPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config\\";

        public static bool ProcConfigFaulted { get; set; }
        public static bool VersionRestartRequired { get; set; }
        public static bool MotionConfigRestartRequired { get; set; }

        public static void Delay(int milliSecond)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < milliSecond)//毫秒
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        public static bool SecurityLocked => securityLocked;
        public static string SecurityLockReason => securityLockReason;
        public static bool MaintenanceActive
        {
            get
            {
                lock (maintenanceLock)
                {
                    return maintenanceActive;
                }
            }
        }
        public static string MaintenanceReason
        {
            get
            {
                lock (maintenanceLock)
                {
                    return maintenanceReason;
                }
            }
        }

        public static bool TryBeginMaintenance(string reason, out IDisposable lease, out string error)
        {
            lock (maintenanceLock)
            {
                if (maintenanceActive)
                {
                    lease = null;
                    error = string.IsNullOrWhiteSpace(maintenanceReason)
                        ? "系统正在执行配置维护。"
                        : $"系统正在执行配置维护:{maintenanceReason}";
                    return false;
                }
                maintenanceReason = string.IsNullOrWhiteSpace(reason) ? "配置维护" : reason.Trim();
                maintenanceActive = true;
                lease = new MaintenanceLease();
                error = null;
                return true;
            }
        }

        private sealed class MaintenanceLease : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }
                lock (maintenanceLock)
                {
                    maintenanceActive = false;
                    maintenanceReason = string.Empty;
                }
            }
        }

        public static void SetSecurityLock(string reason)
        {
            securityLocked = true;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                securityLockReason = reason;
            }
            StopAllProcs(reason);
        }

        public static void ClearSecurityLock()
        {
            securityLocked = false;
            securityLockReason = string.Empty;
        }

        public static void StopAllProcs(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                if (DR?.Logger != null)
                {
                    DR.Logger.Log(reason, LogLevel.Error);
                }
                else if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo(reason, FrmInfo.Level.Error);
                }
            }
            DR?.StopAllManualMotion();
            if (DR == null)
            {
                return;
            }
            int count = DR.Context?.Procs?.Count ?? frmProc?.procsList?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                DR.Stop(i);
            }
        }

        public static bool PublishProc(int procIndex)
        {
            if (MaintenanceActive)
            {
                MessageBox.Show(MaintenanceReason, "系统正在执行配置维护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (frmProc?.procsList == null)
            {
                MessageBox.Show("流程数据未就绪，无法发布。");
                return false;
            }
            if (DR == null)
            {
                MessageBox.Show("流程引擎未初始化，无法发布。");
                return false;
            }
            if (procIndex < 0 || procIndex >= frmProc.procsList.Count)
            {
                MessageBox.Show("流程索引无效，无法发布。");
                return false;
            }
            Proc draft = frmProc.procsList[procIndex];
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(procIndex, draft, errors);
            if (errors.Count > 0)
            {
                string message = "流程发布失败：\r\n" + string.Join("\r\n", errors.Distinct());
                MessageBox.Show(message, "流程发布失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo(message, FrmInfo.Level.Error);
                }
                return false;
            }
            Proc runtime = ObjectGraphCloner.Clone(draft);
            if (!DR.PublishProc(procIndex, runtime, out string error))
            {
                string message = string.IsNullOrWhiteSpace(error) ? "流程发布失败" : $"流程发布失败：{error}";
                MessageBox.Show(message, "流程发布失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (frmInfo != null && !frmInfo.IsDisposed)
                {
                    frmInfo.PrintInfo(message, FrmInfo.Level.Error);
                }
                return false;
            }
            return true;
        }

        public static bool CanEditProc(int procIndex)
        {
            if (MaintenanceActive)
            {
                MessageBox.Show(MaintenanceReason, "系统正在执行配置维护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (SecurityLocked)
            {
                MessageBox.Show(SecurityLockReason, "系统已安全锁定", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (frmProc?.procsList == null || procIndex < 0 || procIndex >= frmProc.procsList.Count)
            {
                return false;
            }
            if (DR == null)
            {
                MessageBox.Show("流程引擎未初始化，禁止编辑流程。", "流程编辑不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            // 编辑器修改草稿；运行实例始终持有启动时的独立流程对象。
            return true;
        }

        public static bool CanEditProcStructure()
        {
            if (MaintenanceActive)
            {
                MessageBox.Show(MaintenanceReason, "系统正在执行配置维护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (frmProc?.procsList == null)
            {
                return true;
            }
            for (int i = 0; i < frmProc.procsList.Count; i++)
            {
                EngineSnapshot snapshot = DR?.GetSnapshot(i);
                if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                {
                    MessageBox.Show("流程列表的新增、删除、复制或重排会改变procIndex，仅允许在全部流程Stopped后操作。流程内部的参数和步骤/指令编辑不受此门禁影响。");
                    return false;
                }
            }
            return true;
        }

        public static void BeginEditSession(IEditSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (ActiveEditSession != null)
            {
                throw new InvalidOperationException($"已有编辑会话尚未结束:{ActiveEditSession.Name}");
            }
            ActiveEditSession = session;
            if (frmInspector != null)
            {
                frmInspector.ShowObject(session.Draft);
                frmInspector.SetEditingState(true);
            }
            if (frmToolBar != null)
            {
                frmToolBar.btnSave.Enabled = true;
                frmToolBar.btnCancel.Enabled = true;
            }
            mainfrm?.RefreshEditorNavigationActions();
            RefreshEditorHistoryActions();
        }

        public static bool TryCommitEditSession(out string error)
        {
            error = null;
            IEditSession session = ActiveEditSession;
            if (session == null)
            {
                error = "当前没有活动编辑会话。";
                return false;
            }
            if (!session.TryCommit(out error))
            {
                return false;
            }
            ActiveEditSession = null;
            EndEdit();
            return true;
        }

        public static void CancelEditSession()
        {
            IEditSession session = ActiveEditSession;
            ActiveEditSession = null;
            session?.Cancel();
            if (frmInspector != null)
            {
                frmInspector.ClearObject();
            }
            EndEdit();
        }

        public static void ReplaceActiveEditDraft(object draft)
        {
            if (ActiveEditSession == null)
            {
                throw new InvalidOperationException("当前没有活动编辑会话。");
            }
            ActiveEditSession.ReplaceDraft(draft);
            ActiveEditSession.CaptureDraftSnapshot();
            PresentActiveEditDraft(draft);
            RefreshEditorHistoryActions();
        }

        public static void CaptureActiveEditSnapshot()
        {
            ActiveEditSession?.CaptureDraftSnapshot();
            RefreshEditorHistoryActions();
        }

        public static bool TryUndoEditorChange(out string description, out string error)
        {
            description = string.Empty;
            error = null;
            if (ActiveEditSession != null)
            {
                description = ActiveEditSession.Name;
                if (!ActiveEditSession.TryUndo(out object draft, out error))
                {
                    return false;
                }
                PresentActiveEditDraft(draft);
                RefreshEditorHistoryActions();
                return true;
            }
            return EditorHistory.TryUndo(out description, out error);
        }

        public static bool TryRedoEditorChange(out string description, out string error)
        {
            description = string.Empty;
            error = null;
            if (ActiveEditSession != null)
            {
                description = ActiveEditSession.Name;
                if (!ActiveEditSession.TryRedo(out object draft, out error))
                {
                    return false;
                }
                PresentActiveEditDraft(draft);
                RefreshEditorHistoryActions();
                return true;
            }
            return EditorHistory.TryRedo(out description, out error);
        }

        public static bool TryHandleEditorHistoryShortcut(Control scope, KeyEventArgs e)
        {
            if (e == null || !e.Control || e.Alt)
            {
                return false;
            }
            bool undo = e.KeyCode == Keys.Z && !e.Shift;
            bool redo = e.KeyCode == Keys.Y || e.KeyCode == Keys.Z && e.Shift;
            if (!undo && !redo || IsTextInputFocused(scope))
            {
                return false;
            }

            string description;
            string error;
            bool success = undo
                ? TryUndoEditorChange(out description, out error)
                : TryRedoEditorChange(out description, out error);
            if (!success && string.IsNullOrWhiteSpace(error))
            {
                return false;
            }
            if (!success)
            {
                MessageBox.Show(
                    error,
                    undo ? "撤销失败" : "重做失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else if (frmInfo != null && !frmInfo.IsDisposed)
            {
                frmInfo.PrintInfo(
                    $"已{(undo ? "撤销" : "重做")}：{description}",
                    FrmInfo.Level.Normal);
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        public static void RefreshEditorHistoryActions()
        {
            frmToolBar?.RefreshHistoryAvailability();
        }

        private static void PresentActiveEditDraft(object draft)
        {
            if (draft is OperationType operation)
            {
                operation.RefreshInspector?.Invoke();
                if (frmDataGrid != null)
                {
                    frmDataGrid.OperationTemp = operation;
                }
            }
            frmInspector?.ShowObject(draft);
        }

        private static bool IsTextInputFocused(Control scope)
        {
            Control focused = FindFocusedControl(scope);
            if (focused is TextBoxBase)
            {
                return true;
            }
            if (focused is ComboBox comboBox && comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
            {
                return true;
            }
            if (focused is DataGridView dataGridView && dataGridView.IsCurrentCellInEditMode)
            {
                return true;
            }
            if (scope is DataGridView scopedGrid && scopedGrid.IsCurrentCellInEditMode)
            {
                return true;
            }
            return false;
        }

        private static Control FindFocusedControl(Control root)
        {
            Control current = root;
            while (current != null)
            {
                if (current is ContainerControl container && container.ActiveControl != null)
                {
                    current = container.ActiveControl;
                    continue;
                }
                Control child = current.Controls.Cast<Control>()
                    .FirstOrDefault(control => control.ContainsFocus);
                if (child == null)
                {
                    return current.ContainsFocus ? current : null;
                }
                current = child;
            }
            return null;
        }

        public static void EndEdit()
        {
            isModify = ModifyKind.None;
            if (frmInspector != null)
            {
                frmInspector.SetEditingState(false);
            }
            if (frmToolBar != null)
            {
                frmToolBar.btnSave.Enabled = false;
                frmToolBar.btnCancel.Enabled = false;
            }
            mainfrm?.RefreshEditorNavigationActions();
            RefreshEditorHistoryActions();
        }

    }
}
