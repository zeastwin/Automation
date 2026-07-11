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
        void ReplaceDraft(object draft);
        bool TryCommit(out string error);
        void Cancel();
    }

    public sealed class EditSession<T> : IEditSession where T : class
    {
        private readonly Func<T, string> validate;
        private readonly Action<T> commit;
        private readonly Action cancel;

        public EditSession(string name, T draft, Func<T, string> validate, Action<T> commit, Action cancel = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("编辑会话名称为空。", nameof(name)) : name;
            DraftValue = draft ?? throw new ArgumentNullException(nameof(draft));
            this.validate = validate;
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
            this.cancel = cancel;
        }

        public string Name { get; }
        public T DraftValue { get; private set; }
        public object Draft => DraftValue;

        public void ReplaceDraft(object draft)
        {
            DraftValue = draft as T ?? throw new InvalidOperationException("编辑草稿类型不匹配。");
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
    }

    public static class SF
    {
        public static FrmMenu frmMenu;
        public static FrmProc frmProc;
        public static FrmDataGrid frmDataGrid;
        public static FrmPropertyGrid frmPropertyGrid;
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
        public static FrmVersionManager frmVersionManager;
        public static ConfigurationVersionService versionService;
        public static IEditSession ActiveEditSession { get; private set; }

        private static bool securityLocked;
        private static string securityLockReason = string.Empty;
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
            if (DR == null || frmProc?.procsList == null)
            {
                return;
            }
            int count = frmProc.procsList.Count;
            for (int i = 0; i < count; i++)
            {
                DR.Stop(i);
            }
        }

        public static bool PublishProc(int procIndex)
        {
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
            EngineSnapshot snapshot = DR.GetSnapshot(procIndex);
            if (snapshot != null && frmInfo != null && !frmInfo.IsDisposed)
            {
                string status = snapshot.HasPendingUpdate
                    ? $"已发布版本{snapshot.PublishedRevision}，等待安全指令边界应用；当前运行版本{snapshot.AppliedRevision}"
                    : $"版本{snapshot.AppliedRevision}已生效";
                frmInfo.PrintInfo($"流程{procIndex}热更新：{status}", FrmInfo.Level.Normal);
            }
            return true;
        }

        public static bool CanEditProc(int procIndex)
        {
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
            // 运行中是否允许本次修改由 ProcessEngine.ValidateProcUpdate 根据草稿内容判定；
            // 此处仅检查编辑入口的基础状态，禁止用一个粗粒度状态门禁破坏安全热更新。
            return true;
        }

        public static bool CanEditProcStructure()
        {
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
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.Enabled = true;
                frmPropertyGrid.propertyGrid1.SelectedObject = session.Draft;
                frmPropertyGrid.OperationType.Enabled = session.Draft is OperationType;
            }
            if (frmToolBar != null)
            {
                frmToolBar.btnSave.Enabled = true;
                frmToolBar.btnCancel.Enabled = true;
            }
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
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.propertyGrid1.SelectedObject = null;
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
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.propertyGrid1.SelectedObject = draft;
            }
        }

        public static void EndEdit()
        {
            isModify = ModifyKind.None;
            if (frmPropertyGrid != null)
            {
                frmPropertyGrid.Enabled = false;
                frmPropertyGrid.OperationType.Enabled = false;
            }
            if (frmToolBar != null)
            {
                frmToolBar.btnSave.Enabled = false;
                frmToolBar.btnCancel.Enabled = false;
            }
        }

    }
}
