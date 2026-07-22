using System;
// 模块：持久化 / 流程。
// 职责范围：管理流程配置、工作目录事务、流程变量联合提交和运行日志落盘。
// 状态所有权：Items 是编辑态事实源；ProcessEngine 持有运行克隆，排查数据不一致时先确认观察的是哪一份。

using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 平台当前可编辑流程定义的内存单一事实源。
    /// 配置编辑在 UI 线程完成；发布到流程引擎时仍使用独立克隆，避免编辑态污染运行态。
    /// </summary>
    public sealed class ProcessDefinitionRepository
    {
        private readonly List<Proc> items = new List<Proc>();

        public List<Proc> Items => items;

        public void ReplaceAll(IEnumerable<Proc> processes)
        {
            if (processes == null)
            {
                throw new ArgumentNullException(nameof(processes));
            }
            List<Proc> replacement = processes.ToList();
            items.Clear();
            items.AddRange(replacement);
        }

        public List<Proc> CreateSnapshot()
        {
            return items.Select(ObjectGraphCloner.Clone).ToList();
        }
    }
}
