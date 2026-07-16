using System;
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
