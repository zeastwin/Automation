using System;
// 模块：引擎 / 结构编辑。
// 职责范围：执行流程与指令结构变换、跳转重写、发布门禁和变量生命周期处理。

using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 流程编辑门禁。运行状态判断与提示统一从这里产生。
    /// </summary>
    public sealed class ProcessEditingPolicy
    {
        private readonly PlatformRuntime runtime;

        internal ProcessEditingPolicy(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool CanEditProcess(int procIndex)
        {
            if (runtime.Maintenance.Active)
            {
                Warn(runtime.Maintenance.Reason, "系统正在执行配置维护");
                return false;
            }
            if (runtime.Safety.IsLocked)
            {
                runtime.EditorUi?.ShowMessage(runtime.Safety.LockReason, "系统已安全锁定", true);
                return false;
            }
            if (procIndex < 0 || procIndex >= runtime.Stores.Processes.Items.Count)
            {
                return false;
            }
            if (runtime.ProcessEngine == null)
            {
                Warn("流程引擎未初始化，禁止编辑流程。", "流程编辑不可用");
                return false;
            }
            return true;
        }

        public bool CanEditStructure()
        {
            if (runtime.Maintenance.Active)
            {
                Warn(runtime.Maintenance.Reason, "系统正在执行配置维护");
                return false;
            }
            for (int i = 0; i < runtime.Stores.Processes.Items.Count; i++)
            {
                EngineSnapshot snapshot = runtime.ProcessEngine?.GetSnapshot(i);
                if (snapshot != null && snapshot.State != ProcRunState.Stopped)
                {
                    Warn("流程列表的新增、删除、复制或重排会改变procIndex，仅允许在全部流程Stopped后操作。流程内部的参数和步骤/指令编辑不受此门禁影响。",
                        "流程结构不可编辑");
                    return false;
                }
            }
            return true;
        }

        private void Warn(string message, string title)
        {
            runtime.EditorUi?.ShowMessage(message, title, false);
        }
    }

    /// <summary>
    /// 将编辑态流程验证、克隆并发布到运行引擎。
    /// </summary>
    public sealed class ProcessPublicationService
    {
        private readonly PlatformRuntime runtime;

        internal ProcessPublicationService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool Publish(int procIndex)
        {
            if (runtime.Maintenance.Active)
            {
                runtime.EditorUi?.ShowMessage(runtime.Maintenance.Reason,
                    "系统正在执行配置维护", false);
                return false;
            }
            List<Proc> processes = runtime.Stores.Processes.Items;
            if (runtime.ProcessEngine == null)
            {
                runtime.EditorUi?.ShowMessage("流程引擎未初始化，无法发布。", "流程发布失败", true);
                return false;
            }
            if (procIndex < 0 || procIndex >= processes.Count)
            {
                runtime.EditorUi?.ShowMessage("流程索引无效，无法发布。", "流程发布失败", true);
                return false;
            }

            Proc draft = processes[procIndex];
            var errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(
                procIndex, draft, errors, runtime.CreateProcessValidationContext());
            if (errors.Count > 0)
            {
                return Fail("流程发布失败：\r\n" + string.Join("\r\n", errors.Distinct()));
            }
            Proc runtimeProcess = ObjectGraphCloner.Clone(draft);
            if (!runtime.ProcessEngine.PublishProc(procIndex, runtimeProcess, out string error))
            {
                return Fail(string.IsNullOrWhiteSpace(error) ? "流程发布失败" : $"流程发布失败：{error}");
            }
            return true;
        }

        private bool Fail(string message)
        {
            runtime.EditorUi?.ShowMessage(message, "流程发布失败", true);
            runtime.EditorUi?.WriteInfo(message, LogLevel.Error);
            return false;
        }
    }
}
