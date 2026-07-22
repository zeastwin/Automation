using System;
using System.Collections.Generic;
using System.Linq;
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
        private const int MaxDraftHistoryCount = 100;
        private readonly Func<T, string> validate;
        private readonly Action<T> commit;
        private readonly Action cancel;
        private readonly List<T> draftHistory = new List<T>();
        private int draftHistoryIndex;

        public EditSession(string name, T draft, Func<T, string> validate, Action<T> commit, Action cancel = null)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("编辑会话名称为空。", nameof(name))
                : name;
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
                draftHistory.RemoveRange(draftHistoryIndex + 1, draftHistory.Count - draftHistoryIndex - 1);
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

    /// <summary>
    /// 编辑草稿、局部撤销重做和编辑器状态的实例服务。
    /// </summary>
    public sealed class EditorSessionCoordinator
    {
        private readonly PlatformRuntime runtime;

        internal EditorSessionCoordinator(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public EditorHistoryService History { get; } = new EditorHistoryService();
        public IEditSession ActiveSession { get; private set; }
        public ModifyKind ModifyKind { get; set; } = ModifyKind.None;
        public bool IsAddingOperations { get; set; }

        public void Begin(IEditSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (ActiveSession != null)
            {
                throw new InvalidOperationException($"已有编辑会话尚未结束:{ActiveSession.Name}");
            }
            ActiveSession = session;
            runtime.EditorUi?.BeginEditSession(session.Draft);
        }

        public bool TryCommit(out string error)
        {
            error = null;
            IEditSession session = ActiveSession;
            if (session == null)
            {
                error = "当前没有活动编辑会话。";
                return false;
            }
            if (!session.TryCommit(out error))
            {
                return false;
            }
            ActiveSession = null;
            End();
            return true;
        }

        public void Cancel()
        {
            IEditSession session = ActiveSession;
            ActiveSession = null;
            session?.Cancel();
            runtime.EditorUi?.ClearEditDraft();
            End();
        }

        public void ReplaceDraft(object draft)
        {
            if (ActiveSession == null)
            {
                throw new InvalidOperationException("当前没有活动编辑会话。");
            }
            ActiveSession.ReplaceDraft(draft);
            ActiveSession.CaptureDraftSnapshot();
            runtime.EditorUi?.PresentEditDraft(draft);
            RefreshHistoryActions();
        }

        public void CaptureSnapshot()
        {
            ActiveSession?.CaptureDraftSnapshot();
            RefreshHistoryActions();
        }

        public bool TryUndo(out string description, out string error)
        {
            description = string.Empty;
            error = null;
            if (ActiveSession != null)
            {
                description = ActiveSession.Name;
                if (!ActiveSession.TryUndo(out object draft, out error))
                {
                    return false;
                }
                runtime.EditorUi?.PresentEditDraft(draft);
                RefreshHistoryActions();
                return true;
            }
            return History.TryUndo(out description, out error);
        }

        public bool TryRedo(out string description, out string error)
        {
            description = string.Empty;
            error = null;
            if (ActiveSession != null)
            {
                description = ActiveSession.Name;
                if (!ActiveSession.TryRedo(out object draft, out error))
                {
                    return false;
                }
                runtime.EditorUi?.PresentEditDraft(draft);
                RefreshHistoryActions();
                return true;
            }
            return History.TryRedo(out description, out error);
        }

        public bool TryHandleHistoryShortcut(Control scope, KeyEventArgs e)
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
            bool success = undo
                ? TryUndo(out string description, out string error)
                : TryRedo(out description, out error);
            if (!success && string.IsNullOrWhiteSpace(error))
            {
                return false;
            }
            if (!success)
            {
                runtime.EditorUi?.ShowMessage(error, undo ? "撤销失败" : "重做失败", true);
            }
            else
            {
                runtime.EditorUi?.WriteInfo($"已{(undo ? "撤销" : "重做")}：{description}", LogLevel.Normal);
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        public void RefreshHistoryActions()
        {
            runtime.EditorUi?.RefreshEditorHistoryActions();
        }

        public void End()
        {
            ModifyKind = ModifyKind.None;
            runtime.EditorUi?.EndEditSession();
        }

        private static bool IsTextInputFocused(Control scope)
        {
            Control focused = FindFocusedControl(scope);
            return focused is TextBoxBase
                || focused is ComboBox comboBox && comboBox.DropDownStyle != ComboBoxStyle.DropDownList
                || focused is DataGridView dataGridView && dataGridView.IsCurrentCellInEditMode
                || scope is DataGridView scopedGrid && scopedGrid.IsCurrentCellInEditMode;
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
    }
}
