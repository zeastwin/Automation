using System;
using System.Collections.Generic;

namespace Automation
{
    public delegate bool EditorHistoryAction(out string error);

    /// <summary>
    /// 保存当前编辑器进程内的业务撤销历史。每条记录仍通过原配置事务执行，
    /// 本服务只负责顺序、重做分支和回放状态。
    /// </summary>
    public sealed class EditorHistoryService
    {
        private const int MaxHistoryCount = 100;
        private readonly List<EditorHistoryEntry> undoEntries = new List<EditorHistoryEntry>();
        private readonly List<EditorHistoryEntry> redoEntries = new List<EditorHistoryEntry>();

        public event EventHandler StateChanged;

        public bool IsReplaying { get; private set; }
        public bool CanUndo => undoEntries.Count > 0;
        public bool CanRedo => redoEntries.Count > 0;
        public string UndoDescription => CanUndo ? undoEntries[undoEntries.Count - 1].Description : string.Empty;
        public string RedoDescription => CanRedo ? redoEntries[redoEntries.Count - 1].Description : string.Empty;

        public void Record(
            string description,
            EditorHistoryAction undo,
            EditorHistoryAction redo)
        {
            if (IsReplaying)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException("撤销记录说明不能为空。", nameof(description));
            }
            if (undo == null)
            {
                throw new ArgumentNullException(nameof(undo));
            }
            if (redo == null)
            {
                throw new ArgumentNullException(nameof(redo));
            }

            undoEntries.Add(new EditorHistoryEntry(description.Trim(), undo, redo));
            redoEntries.Clear();
            if (undoEntries.Count > MaxHistoryCount)
            {
                undoEntries.RemoveAt(0);
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool TryUndo(out string description, out string error)
        {
            return TryReplay(undoEntries, redoEntries, true, out description, out error);
        }

        public bool TryRedo(out string description, out string error)
        {
            return TryReplay(redoEntries, undoEntries, false, out description, out error);
        }

        public void Clear()
        {
            if (IsReplaying)
            {
                return;
            }
            bool changed = undoEntries.Count > 0 || redoEntries.Count > 0;
            undoEntries.Clear();
            redoEntries.Clear();
            if (changed)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool TryReplay(
            IList<EditorHistoryEntry> source,
            IList<EditorHistoryEntry> destination,
            bool undo,
            out string description,
            out string error)
        {
            description = string.Empty;
            error = null;
            if (IsReplaying)
            {
                error = "撤销或重做操作正在执行。";
                return false;
            }
            if (source.Count == 0)
            {
                return false;
            }

            EditorHistoryEntry entry = source[source.Count - 1];
            description = entry.Description;
            IsReplaying = true;
            bool success;
            try
            {
                success = (undo ? entry.Undo : entry.Redo)(out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                success = false;
            }
            finally
            {
                IsReplaying = false;
            }

            if (!success)
            {
                return false;
            }
            source.RemoveAt(source.Count - 1);
            destination.Add(entry);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private sealed class EditorHistoryEntry
        {
            public EditorHistoryEntry(
                string description,
                EditorHistoryAction undo,
                EditorHistoryAction redo)
            {
                Description = description;
                Undo = undo;
                Redo = redo;
            }

            public string Description { get; }
            public EditorHistoryAction Undo { get; }
            public EditorHistoryAction Redo { get; }
        }
    }
}
