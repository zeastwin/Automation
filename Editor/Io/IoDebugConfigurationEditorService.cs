// 模块：编辑器 / IO。
// 职责范围：IO 配置、状态监视、调试布局和配置提交。

using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    internal enum IoDebugConnectionEndpoint
    {
        Input1,
        Input2,
        Output2
    }

    /// <summary>
    /// IO 调试布局的草稿构造与提交边界。正式内存只在 Store 落盘成功后替换。
    /// </summary>
    internal sealed class IoDebugConfigurationEditorService
    {
        private readonly PlatformRuntime runtime;

        public IoDebugConfigurationEditorService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        private bool TryCommit(IODebugMap candidate, out IODebugMap committed, out string error)
        {
            committed = null;
            if (!runtime.Stores.IoDebug.TryCommit(runtime.Paths.ConfigPath, candidate, out error))
                return false;
            committed = runtime.Stores.IoDebug.Current;
            return true;
        }

        public bool TryApplyIoSelection(
            IODebugMap current,
            string ioType,
            IReadOnlyList<string> selectedNames,
            IEnumerable<IO> catalog,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            if (!TryBuildCatalog(catalog, ioType, out Dictionary<string, IO> byName, out error))
                return false;
            IODebugMap draft = CloneMap(current);
            List<IO> source = string.Equals(ioType, "通用输入", StringComparison.Ordinal)
                ? draft.inputs
                : draft.outputs;
            List<IO> replacement = PreserveOrderAndRemarks(
                source,
                selectedNames ?? Array.Empty<string>(),
                byName);
            if (string.Equals(ioType, "通用输入", StringComparison.Ordinal))
                draft.inputs = replacement;
            else if (string.Equals(ioType, "通用输出", StringComparison.Ordinal))
                draft.outputs = replacement;
            else
            {
                error = $"IO 类型无效：{ioType}";
                return false;
            }
            return TryCommit(draft, out committed, out error);
        }

        public bool TryApplyConnectionSelection(
            IODebugMap current,
            int pageIndex,
            IReadOnlyList<string> selectedNames,
            IEnumerable<IO> catalog,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            if (!TryBuildCatalog(catalog, "通用输出", out Dictionary<string, IO> byName, out error))
                return false;
            IODebugMap draft = CloneMap(current);
            List<IOConnect> source = GetConnections(draft, pageIndex);
            var selected = new HashSet<string>(
                selectedNames ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var replacement = new List<IOConnect>();
            foreach (IOConnect connection in source)
            {
                string name = connection?.Output?.Name;
                if (connection?.Output?.IsRemark == true)
                {
                    replacement.Add(connection);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name) || !selected.Remove(name)) continue;
                if (byName.TryGetValue(name, out IO output)) connection.Output = output.CloneForDebug();
                replacement.Add(connection);
            }
            foreach (string name in selectedNames ?? Array.Empty<string>())
            {
                if (!selected.Remove(name) || !byName.TryGetValue(name, out IO output)) continue;
                replacement.Add(new IOConnect { Output = output.CloneForDebug() });
            }
            SetConnections(draft, pageIndex, replacement);
            return TryCommit(draft, out committed, out error);
        }

        public bool TryAddIoRemark(
            IODebugMap current,
            string ioType,
            string text,
            int insertIndex,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "备注内容为空。";
                return false;
            }
            IODebugMap draft = CloneMap(current);
            List<IO> target = string.Equals(ioType, "通用输入", StringComparison.Ordinal)
                ? draft.inputs
                : string.Equals(ioType, "通用输出", StringComparison.Ordinal)
                    ? draft.outputs
                    : null;
            if (target == null)
            {
                error = $"IO 类型无效：{ioType}";
                return false;
            }
            target.Insert(NormalizeInsertIndex(insertIndex, target.Count), new IO
            {
                Name = text.Trim(),
                IOType = ioType,
                IsRemark = true
            });
            return TryCommit(draft, out committed, out error);
        }

        public bool TryAddConnectionRemark(
            IODebugMap current,
            int pageIndex,
            string text,
            int insertIndex,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "备注内容为空。";
                return false;
            }
            IODebugMap draft = CloneMap(current);
            List<IOConnect> target = GetConnections(draft, pageIndex);
            var remark = new IOConnect();
            remark.Output.Name = text.Trim();
            remark.Output.IOType = "通用输出";
            remark.Output.IsRemark = true;
            target.Insert(NormalizeInsertIndex(insertIndex, target.Count), remark);
            return TryCommit(draft, out committed, out error);
        }

        public bool TryReorderIo(
            IODebugMap current,
            bool input,
            int sourceIndex,
            int insertionIndex,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            IODebugMap draft = CloneMap(current);
            List<IO> items = input ? draft.inputs : draft.outputs;
            if (sourceIndex < 0 || sourceIndex >= items.Count)
            {
                error = "待移动的 IO 调试项已失效。";
                return false;
            }
            int target = NormalizeInsertIndex(insertionIndex, items.Count);
            IO moving = items[sourceIndex];
            items.RemoveAt(sourceIndex);
            if (target > sourceIndex) target--;
            target = NormalizeInsertIndex(target, items.Count);
            items.Insert(target, moving);
            return TryCommit(draft, out committed, out error);
        }

        public bool TrySwapConnections(
            IODebugMap current,
            int pageIndex,
            int sourceIndex,
            int targetIndex,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            IODebugMap draft = CloneMap(current);
            List<IOConnect> items = GetConnections(draft, pageIndex);
            if (sourceIndex < 0 || sourceIndex >= items.Count
                || targetIndex < 0 || targetIndex >= items.Count)
            {
                error = "输入输出关联项的位置已失效。";
                return false;
            }
            IOConnect temporary = items[sourceIndex];
            items[sourceIndex] = items[targetIndex];
            items[targetIndex] = temporary;
            return TryCommit(draft, out committed, out error);
        }

        public bool TryAddConnection(
            IODebugMap current,
            int pageIndex,
            IO output,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            if (output == null || string.IsNullOrWhiteSpace(output.Name))
            {
                error = "关联输出不存在。";
                return false;
            }
            IODebugMap draft = CloneMap(current);
            List<IOConnect> connections = GetConnections(draft, pageIndex);
            if (connections.Any(item => string.Equals(
                item?.Output?.Name, output.Name, StringComparison.Ordinal)))
            {
                error = "调试列表已存在同名输出连接。";
                return false;
            }
            connections.Add(new IOConnect { Output = output.CloneForDebug() });
            return TryCommit(draft, out committed, out error);
        }

        public bool TrySetConnectionEndpoint(
            IODebugMap current,
            int pageIndex,
            IOConnect selectedConnection,
            IoDebugConnectionEndpoint endpoint,
            IO selectedIo,
            out IODebugMap committed,
            out string error)
        {
            committed = null;
            List<IOConnect> currentConnections = GetConnections(current, pageIndex);
            int connectionIndex = currentConnections.IndexOf(selectedConnection);
            if (connectionIndex < 0)
            {
                error = "当前关联项已失效。";
                return false;
            }
            if (selectedConnection?.Output == null || selectedConnection.Output.IsRemark)
            {
                error = "备注项不能编辑 IO 关联。";
                return false;
            }

            IODebugMap draft = CloneMap(current);
            IOConnect target = GetConnections(draft, pageIndex)[connectionIndex];
            IO value = selectedIo?.CloneForDebug() ?? new IO();
            switch (endpoint)
            {
                case IoDebugConnectionEndpoint.Input1:
                    target.Intput1 = value;
                    break;
                case IoDebugConnectionEndpoint.Input2:
                    target.Intput2 = value;
                    break;
                case IoDebugConnectionEndpoint.Output2:
                    target.Output2 = value;
                    break;
                default:
                    error = "IO 关联端点无效。";
                    return false;
            }
            return TryCommit(draft, out committed, out error);
        }

        private static IODebugMap CloneMap(IODebugMap source)
        {
            IODebugMap clone = ObjectGraphCloner.Clone(source ?? new IODebugMap());
            EnsureCollections(clone);
            return clone;
        }

        private static void EnsureCollections(IODebugMap map)
        {
            if (map.inputs == null) map.inputs = new List<IO>();
            if (map.outputs == null) map.outputs = new List<IO>();
            if (map.iOConnects == null) map.iOConnects = new List<IOConnect>();
            if (map.iOConnects2 == null) map.iOConnects2 = new List<IOConnect>();
            if (map.iOConnects3 == null) map.iOConnects3 = new List<IOConnect>();
        }

        private static bool TryBuildCatalog(
            IEnumerable<IO> catalog,
            string ioType,
            out Dictionary<string, IO> byName,
            out string error)
        {
            byName = (catalog ?? Enumerable.Empty<IO>())
                .Where(io => io != null
                    && string.Equals(io.IOType, ioType, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(io.Name))
                .GroupBy(io => io.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            error = null;
            return true;
        }

        private static List<IO> PreserveOrderAndRemarks(
            IEnumerable<IO> current,
            IReadOnlyList<string> selectedNames,
            IReadOnlyDictionary<string, IO> catalog)
        {
            var remaining = new HashSet<string>(selectedNames, StringComparer.Ordinal);
            var result = new List<IO>();
            foreach (IO item in current ?? Enumerable.Empty<IO>())
            {
                if (item == null) continue;
                if (item.IsRemark)
                {
                    result.Add(item);
                    continue;
                }
                if (!remaining.Remove(item.Name)) continue;
                if (catalog.TryGetValue(item.Name, out IO source)) result.Add(source.CloneForDebug());
            }
            foreach (string name in selectedNames)
            {
                if (remaining.Remove(name) && catalog.TryGetValue(name, out IO source))
                    result.Add(source.CloneForDebug());
            }
            return result;
        }

        private static int NormalizeInsertIndex(int index, int count)
        {
            return index < 0 || index > count ? count : index;
        }

        private static List<IOConnect> GetConnections(IODebugMap map, int pageIndex)
        {
            if (map == null) return new List<IOConnect>();
            EnsureCollections(map);
            switch (pageIndex)
            {
                case 1: return map.iOConnects2;
                case 2: return map.iOConnects3;
                default: return map.iOConnects;
            }
        }

        private static void SetConnections(IODebugMap map, int pageIndex, List<IOConnect> value)
        {
            switch (pageIndex)
            {
                case 1: map.iOConnects2 = value; break;
                case 2: map.iOConnects3 = value; break;
                default: map.iOConnects = value; break;
            }
        }
    }
}
