using System;
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
            RecoverTransactions(runtime, messages);
            List<Proc> processes = LoadProcesses(runtime, messages);
            runtime.Stores.Values.Load(runtime.Paths.ConfigPath, processes);
            EnsureSystemValues(runtime, messages);

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

            PublishResourceState(runtime);
            PlatformDeviceInitializationResult deviceResult = runtime.Devices.Initialize();
            runtime.SystemStatus = new PlatformSystemStatusService(runtime);
            runtime.SystemStatus.Start();
            if (runtime.Safety.IsLocked)
            {
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
