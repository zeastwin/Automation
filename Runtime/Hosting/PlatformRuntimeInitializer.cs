using System;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。
// 失败语义：初始化尽量完成并收集 Messages；Safety Lock 与 Readiness 才决定哪些能力禁止运行。

using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    internal sealed class PlatformRuntimeInitializationResult
    {
        public PlatformDeviceInitializationResult Device { get; set; }
        public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 无 UI 的平台配置加载与运行时发布入口。
    /// 缺失配置由各 Store 按当前契约补齐；非法配置按影响范围锁定或降级。
    /// </summary>
    internal static class PlatformRuntimeInitializer
    {
        private const string ResetStatusValueName = "复位状态";
        private const string SystemStatusValueName = "系统状态";

        public static PlatformRuntimeInitializationResult Initialize(PlatformRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (runtime.ProcessEngine == null || runtime.Devices == null)
            {
                throw new InvalidOperationException("平台内核尚未完成组合，无法加载配置。");
            }

            var messages = new List<string>();
            // 持久化事务必须先恢复，否则后续 Store 可能分别读到新旧两代配置。
            RecoverTransactions(runtime, messages);
            // 流程和变量互相引用：先得到流程集合，再加载变量并补齐平台保留变量。
            List<Proc> processes = LoadProcesses(runtime, messages);
            runtime.Stores.Values.Load(runtime.Paths.ConfigPath, processes);
            EnsureSystemValues(runtime, messages);

            // 下列配置按受影响能力分别降级。需要安全锁的配置会禁止危险动作，但不能阻止 HMI 完成初始化。
            if (!runtime.Stores.Cards.Load(runtime.Paths.ConfigPath, out string cardError))
            {
                Log(runtime,
                    $"轴配置加载校验失败；MES/通讯流程仍可运行，运动指令将继续执行运行时门禁。{cardError}",
                    LogLevel.Error,
                    messages);
            }
            if (!runtime.Stores.IoConfiguration.Load(runtime.Paths.ConfigPath, out string ioError))
            {
                runtime.Safety.Lock(ioError);
                messages.Add(ioError);
            }
            if (!runtime.Stores.Stations.Load(runtime.Paths.ConfigPath, out string stationError))
            {
                runtime.Safety.Lock(stationError);
                messages.Add(stationError);
            }
            if (!runtime.Stores.Cards.TryValidateStations(
                runtime.Stores.Stations.Items,
                out List<string> stationErrors))
            {
                Log(runtime,
                    "工站配置加载校验失败：" + string.Join("；", stationErrors),
                    LogLevel.Error,
                    messages);
            }

            runtime.Stores.DataStructures.Load(runtime.Paths.ConfigPath);
            if (!runtime.Stores.IoDebug.Load(runtime.Paths.ConfigPath, out string ioDebugError))
            {
                Log(runtime, $"输入输出调试配置加载失败:{ioDebugError}", LogLevel.Error, messages);
            }
            if (!runtime.Stores.Communication.Load(runtime.Paths.ConfigPath, out string communicationError))
            {
                runtime.Safety.Lock(communicationError);
                messages.Add(communicationError);
            }
            runtime.Stores.Alarms.Load(runtime.Paths.ConfigPath);
            if (!runtime.Stores.Plc.Load(runtime.Paths.ConfigPath, runtime.Stores.Values, out string plcConfigError))
            {
                Log(runtime, plcConfigError, LogLevel.Error, messages);
            }
            if (!runtime.PlcRuntime.Initialize(out string plcRuntimeError))
            {
                Log(runtime, plcRuntimeError, LogLevel.Error, messages);
            }

            // 先发布已验证的资源可用性，再初始化实际设备；运行闸门据此给出精确的不可用原因。
            PublishResourceState(runtime);
            PlatformDeviceInitializationResult deviceResult = runtime.Devices.Initialize();
            runtime.SystemStatus = new PlatformSystemStatusService(runtime);
            runtime.SystemStatus.Start();
            if (runtime.Safety.IsLocked)
            {
                // 初始化仍返回可用平台，但所有流程保持停止；这是降级成功，不是通过异常终止启动。
                string lockReason = string.IsNullOrWhiteSpace(runtime.Safety.LockReason)
                    ? "系统处于安全锁定模式，禁止自动启动流程。"
                    : $"系统处于安全锁定模式，禁止自动启动流程。锁定原因：{runtime.Safety.LockReason}";
                runtime.Safety.StopAllProcesses(lockReason);
            }
            return new PlatformRuntimeInitializationResult
            {
                Device = deviceResult,
                Messages = messages
            };
        }

        private static void RecoverTransactions(PlatformRuntime runtime, ICollection<string> messages)
        {
            if (!ConfigurationBatchWriter.RecoverPendingTransactions(
                runtime.Paths.ConfigPath,
                out string transactionError))
            {
                runtime.Safety.Lock(transactionError);
                Log(runtime,
                    $"配置事务恢复未完成，平台继续初始化并保持安全锁定：{transactionError}",
                    LogLevel.Error,
                    messages);
            }
            if (!ProcessVariableConfigurationTransaction.RecoverPendingTransactions(
                runtime.Paths.ConfigPath,
                out string changeSetTransactionError))
            {
                runtime.Safety.Lock(changeSetTransactionError);
                Log(runtime,
                    $"ChangeSet事务恢复未完成，平台继续初始化并保持安全锁定：{changeSetTransactionError}",
                    LogLevel.Error,
                    messages);
            }
        }

        private static List<Proc> LoadProcesses(PlatformRuntime runtime, ICollection<string> messages)
        {
            runtime.Readiness.ProcConfigFaulted = false;
            List<Proc> processes = ProcessWorkDirectoryTransaction.Load(
                runtime.Paths.WorkPath,
                runtime.CreateProcessValidationContext(),
                out List<string> loadErrors,
                out string recoveryMessage);
            if (!string.IsNullOrWhiteSpace(recoveryMessage))
            {
                Log(runtime, recoveryMessage, LogLevel.Error, messages);
            }
            runtime.Stores.Processes.ReplaceAll(processes);
            runtime.ProcessEngine.Context.Procs = processes
                .Select(ObjectGraphCloner.Clone)
                .ToList();
            runtime.ProcessEngine.ClearPendingProcUpdates();

            if (loadErrors.Count > 0)
            {
                runtime.Readiness.ProcConfigFaulted = true;
                string reason = "流程配置加载失败，所有流程已停止且禁止启动。请处理以下报警："
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, loadErrors.Distinct());
                messages.Add(reason);
                runtime.Safety.StopAllProcesses(reason);
            }
            return processes;
        }

        private static void EnsureSystemValues(PlatformRuntime runtime, ICollection<string> messages)
        {
            if (runtime.Stores.Values.ConfigurationFaulted)
            {
                string error = "变量配置格式或归属校验失败，已保留原文件；删除或修复 value.json 后再重新加载。";
                runtime.Safety.Lock(error);
                messages.Add(error);
                return;
            }
            bool created = EnsureSystemValue(runtime, ResetStatusValueName, "系统保留变量：复位状态", messages);
            created = EnsureSystemValue(runtime, SystemStatusValueName, "系统保留变量：系统状态", messages)
                || created;
            if (created && !runtime.Stores.Values.Save(runtime.Paths.ConfigPath))
            {
                string error = "系统保留变量保存失败。";
                runtime.Safety.Lock(error);
                messages.Add(error);
                return;
            }
            string resetError;
            string systemError = null;
            if (!TryValidateSystemValue(runtime, ResetStatusValueName, out resetError)
                || !TryValidateSystemValue(runtime, SystemStatusValueName, out systemError))
            {
                string error = resetError ?? systemError;
                runtime.Safety.Lock(error);
                messages.Add(error);
                return;
            }
            if (!runtime.Stores.Values.setValueByName(ResetStatusValueName, 0d, "复位状态初始化")
                || !runtime.Stores.Values.setValueByName(
                    SystemStatusValueName,
                    (double)SystemStatus.Uninitialized,
                    "系统状态初始化"))
            {
                string error = "系统状态变量初始化失败。";
                runtime.Safety.Lock(error);
                messages.Add(error);
            }
        }

        private static bool EnsureSystemValue(
            PlatformRuntime runtime,
            string name,
            string note,
            ICollection<string> messages)
        {
            if (runtime.Stores.Values.TryGetValueByName(name, out DicValue existing) && existing != null)
            {
                if (!ValueConfigStore.IsSystemValueIndex(existing.Index))
                {
                    string error = $"系统保留变量“{name}”必须位于索引范围 "
                        + $"[{ValueConfigStore.SystemValueStartIndex}, {ValueConfigStore.ValueCapacity})。";
                    runtime.Safety.Lock(error);
                    messages.Add(error);
                }
                return false;
            }
            for (int i = ValueConfigStore.SystemValueStartIndex; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (runtime.Stores.Values.TryGetValueByIndex(i, out _))
                {
                    continue;
                }
                if (!runtime.Stores.Values.TrySetValue(
                    i,
                    name,
                    "double",
                    "0",
                    note,
                    "系统保留变量初始化"))
                {
                    string error = $"创建系统保留变量失败：{name}";
                    runtime.Safety.Lock(error);
                    messages.Add(error);
                    return false;
                }
                Log(runtime, $"已补齐系统保留变量：{name}", LogLevel.Normal, messages);
                return true;
            }
            string fullError = $"系统变量区已满（{ValueConfigStore.SystemValueCapacity} 个槽位），无法创建系统保留变量：{name}";
            runtime.Safety.Lock(fullError);
            messages.Add(fullError);
            return false;
        }

        private static bool TryValidateSystemValue(
            PlatformRuntime runtime,
            string name,
            out string error)
        {
            error = null;
            if (!runtime.Stores.Values.TryGetValueByName(name, out DicValue value) || value == null)
            {
                error = $"缺少变量：{name}";
                return false;
            }
            if (!string.Equals(value.Type, "double", StringComparison.Ordinal))
            {
                error = $"变量“{name}”类型不是double。";
                return false;
            }
            return true;
        }

        private static void PublishResourceState(PlatformRuntime runtime)
        {
            EngineContext context = runtime.ProcessEngine.Context;
            context.Stations = runtime.Stores.Stations.Items;
            context.SocketInfos = runtime.Stores.Communication.GetSocketSnapshot().ToList();
            context.SerialPortInfos = runtime.Stores.Communication.GetSerialSnapshot().ToList();
            context.IoMap = runtime.Stores.IoConfiguration.ByName;
            context.PlcRuntime = runtime.PlcRuntime;
        }

        private static void Log(
            PlatformRuntime runtime,
            string message,
            LogLevel level,
            ICollection<string> messages)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            runtime.ProcessEngine?.Logger?.Log(message, level);
            messages?.Add(message);
        }
    }
}
