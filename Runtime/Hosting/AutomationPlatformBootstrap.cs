using System;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。

using System.Collections.Generic;

namespace Automation
{
    /// <summary>
    /// 设备主程序启动平台前的统一准备入口。
    /// </summary>
    public static class AutomationPlatformBootstrap
    {
        public static bool TryPrepare(
            string[] args,
            out IReadOnlyList<string> warnings,
            out string error)
        {
            var startupWarnings = new List<string>();
            warnings = startupWarnings;
            error = null;

            RuntimeExceptionLogger.Initialize();
            if (!AppConfigStorage.TryLoad(out AppConfig appConfig, out error))
            {
                error = error ?? "程序参数配置异常，平台未启动。";
                return false;
            }
            if (!AutomationRuntimeOptions.TryConfigure(
                args ?? Array.Empty<string>(), appConfig, out _, out error))
            {
                return false;
            }

            // EW-AI 是平台辅助能力。固定 Git 或 Goose 缺失时，
            // 跳过其配置与上下文部署，避免辅助组件影响 HMI 和生产流程启动。
            if (!System.IO.File.Exists(GooseRuntimeEnvironment.MachineGitExecutablePath)
                || !System.IO.File.Exists(GooseRuntimeEnvironment.MachineGooseExecutablePath))
            {
                return true;
            }
            if (!GooseConfigStorage.TryLoad(out GooseConfig aiConfig, out string aiConfigError))
            {
                startupWarnings.Add(aiConfigError);
                return true;
            }
            if (!GooseRuntimeEnvironment.TryValidate(aiConfig.GooseExecutablePath, out _))
            {
                return true;
            }
            if (!GooseConfigStorage.TryApplyStartupSafetyDefaults(out string aiSafetyError))
            {
                startupWarnings.Add(aiSafetyError);
            }
            if (!GooseRuntimeProvisioner.TryEnsureManagedContext(out string contextMessage))
            {
                startupWarnings.Add(contextMessage);
            }
            return true;
        }
    }
}
