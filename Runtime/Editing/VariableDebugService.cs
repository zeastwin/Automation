// 模块：运行时 / 编辑协作。
// 职责范围：统一变量调试配置提交、稳定身份解析、运行值写入和安全闸门。

using System;
using System.Collections.Generic;

namespace Automation
{
    internal sealed class VariableDebugService
    {
        private readonly PlatformRuntime runtime;

        public VariableDebugService(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public long ConfigurationVersion => runtime.Stores.ValueDebug.Version;

        public long ValueConfigurationVersion =>
            runtime.Stores.Values?.ConfigurationVersion ?? -1;

        public bool TryLoadConfiguration(
            out ValueDebugConfiguration configuration,
            out long version,
            out string error)
        {
            configuration = null;
            version = runtime.Stores.ValueDebug.Version;
            if (!CanAccessConfiguration(out error))
            {
                return false;
            }
            if (!runtime.Stores.ValueDebug.Load(
                runtime.Paths.ConfigPath,
                runtime.Stores.Values,
                out error))
            {
                return false;
            }
            configuration = runtime.Stores.ValueDebug.GetSnapshot(out version);
            return true;
        }

        public ValueDebugConfiguration GetConfigurationSnapshot(out long version)
        {
            return runtime.Stores.ValueDebug.GetSnapshot(out version);
        }

        public bool TryCommitConfiguration(
            ValueDebugConfiguration candidate,
            long expectedVersion,
            out ValueDebugConfiguration committed,
            out long committedVersion,
            out string error)
        {
            committed = null;
            committedVersion = runtime.Stores.ValueDebug.Version;
            if (!CanAccessConfiguration(out error))
            {
                return false;
            }
            if (!runtime.Stores.ValueDebug.TryCommit(
                runtime.Paths.ConfigPath,
                candidate,
                expectedVersion,
                out error))
            {
                return false;
            }
            committed = runtime.Stores.ValueDebug.GetSnapshot(out committedVersion);
            return true;
        }

        public IReadOnlyList<DicValue> GetVariablesSnapshot()
        {
            return runtime.Stores.Values?.GetValuesSnapshot()
                ?? new List<DicValue>();
        }

        public bool TryGetValue(Guid variableId, out DicValue value)
        {
            value = null;
            return runtime.Stores.Values != null
                && runtime.Stores.Values.TryGetValueById(variableId, out value);
        }

        public bool TryApplyValue(
            Guid variableId,
            string newValue,
            string source,
            out string error)
        {
            error = null;
            if (!CanWriteRuntimeValue(out error))
            {
                return false;
            }
            if (variableId == Guid.Empty
                || !runtime.Stores.Values.TryGetValueById(variableId, out DicValue value))
            {
                error = $"变量不存在或已删除:{variableId:D}";
                return false;
            }

            string oldValue = value.Value;
            if (!runtime.Stores.Values.TryModifyValueByIndex(
                value.Index,
                value,
                _ => newValue,
                out error,
                source))
            {
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = "变量写入失败。";
                }
                return false;
            }

            string message =
                $"变量调试修改成功：[{value.Index:D3}] {value.Name} {oldValue} -> {newValue}";
            if (runtime.ProcessEngine?.Logger != null)
            {
                runtime.ProcessEngine.Logger.Log(message, LogLevel.Normal);
            }
            else
            {
                runtime.EditorUi?.WriteInfo(message, LogLevel.Normal);
            }
            return true;
        }

        private bool CanAccessConfiguration(out string error)
        {
            error = null;
            if (runtime.Maintenance.Active)
            {
                error = string.IsNullOrWhiteSpace(runtime.Maintenance.Reason)
                    ? "系统正在执行配置维护。"
                    : $"系统正在执行配置维护:{runtime.Maintenance.Reason}";
                return false;
            }
            if (runtime.Readiness.VersionRestartRequired)
            {
                error = "配置版本已还原，必须重启程序后才能修改变量调试配置。";
                return false;
            }
            return true;
        }

        private bool CanWriteRuntimeValue(out string error)
        {
            if (!CanAccessConfiguration(out error))
            {
                return false;
            }
            if (runtime.Safety.IsLocked)
            {
                error = string.IsNullOrWhiteSpace(runtime.Safety.LockReason)
                    ? "系统处于安全锁定状态。"
                    : $"系统处于安全锁定状态:{runtime.Safety.LockReason}";
                return false;
            }
            if (runtime.Readiness.ProcConfigFaulted)
            {
                error = "流程配置异常，禁止变量调试写入。";
                return false;
            }
            if (runtime.Stores.Values == null
                || runtime.Stores.Values.ConfigurationFaulted)
            {
                error = "变量配置异常，禁止变量调试写入。";
                return false;
            }
            return true;
        }
    }
}
