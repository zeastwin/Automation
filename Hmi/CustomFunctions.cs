using System;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 自定义函数。
// 职责范围：注册 HMI 业务函数；仅通过 IAutomationPlatform 使用平台能力。
// 排查入口：函数未执行时先检查 Register 是否在宿主初始化前调用，再检查函数名与流程配置是否一致。

namespace Automation.Hmi
{
    /// <summary>
    /// 平台内置 HMI 的自定义函数入口，只通过公开平台接口访问运行能力。
    /// </summary>
    internal static class CustomFunctions
    {
        public static EquipmentProcessMessageService Register(IAutomationPlatform platform)
        {
            return Register(platform, null);
        }

        internal static EquipmentProcessMessageService Register(
            IAutomationPlatform platform,
            LegacyEquipmentServices equipmentServices)
        {
            if (platform == null)
            {
                throw new ArgumentNullException(nameof(platform));
            }

            var processMessages = new EquipmentProcessMessageService(
                platform.Values,
                equipmentServices: equipmentServices);
            foreach (string legacyMessageName in processMessages.GetRegisteredFunctionNames())
            {
                string functionName = legacyMessageName;
                platform.RegisterCustomFunction(
                    functionName,
                    () => processMessages.ExecuteMessage(functionName));
            }
            return processMessages;
        }
    }
}
