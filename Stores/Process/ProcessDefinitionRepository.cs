using System;
// 模块：持久化 / 流程。
// 职责范围：管理流程配置、工作目录事务、流程变量联合提交和运行日志落盘。
// 状态所有权：Items 是编辑态事实源；ProcessEngine 持有运行克隆，排查数据不一致时先确认观察的是哪一份。

using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Automation
{
    /// <summary>
    /// 平台当前可编辑流程定义的内存单一事实源。
    /// 配置编辑在 UI 线程完成；发布到流程引擎时仍使用独立克隆，避免编辑态污染运行态。
    /// </summary>
    public sealed class ProcessDefinitionRepository
    {
        private readonly List<Proc> items = new List<Proc>();
        private readonly object revisionLock = new object();
        private readonly Dictionary<Guid, long> revisions = new Dictionary<Guid, long>();
        private long version;
        private long revisionSequence;

        public List<Proc> Items => items;
        public long Version => Interlocked.Read(ref version);

        public void ReplaceAll(IEnumerable<Proc> processes)
        {
            if (processes == null)
            {
                throw new ArgumentNullException(nameof(processes));
            }
            List<Proc> replacement = processes.ToList();
            items.Clear();
            items.AddRange(replacement);
            lock (revisionLock)
            {
                revisions.Clear();
                foreach (Proc process in replacement)
                {
                    Guid id = process?.head?.Id ?? Guid.Empty;
                    if (id != Guid.Empty)
                    {
                        revisions[id] = ++revisionSequence;
                    }
                }
            }
            Interlocked.Increment(ref version);
        }

        public void ReplaceAt(int index, Proc process)
        {
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            Guid previousId = items[index]?.head?.Id ?? Guid.Empty;
            items[index] = process;
            Guid id = process?.head?.Id ?? Guid.Empty;
            lock (revisionLock)
            {
                if (previousId != Guid.Empty && previousId != id)
                {
                    revisions.Remove(previousId);
                }
                if (id != Guid.Empty)
                {
                    revisions[id] = ++revisionSequence;
                }
            }
            Interlocked.Increment(ref version);
        }

        public long GetRevision(Guid processId)
        {
            if (processId == Guid.Empty)
            {
                return Version;
            }
            lock (revisionLock)
            {
                return revisions.TryGetValue(processId, out long revision)
                    ? revision
                    : Version;
            }
        }

        public List<Proc> CreateSnapshot()
        {
            return items.Select(ObjectGraphCloner.Clone).ToList();
        }
    }
}
