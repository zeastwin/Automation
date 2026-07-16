using System;
using System.Diagnostics;
using Automation.DeviceSdk;

namespace Automation.Hmi
{
    /// <summary>
    /// 平台内置 HMI 的自定义函数入口，只通过公开平台接口访问运行能力。
    /// </summary>
    internal static class CustomFunctions
    {
        private static readonly Stopwatch Stopwatch = new Stopwatch();

        public static void Register(IAutomationPlatform platform)
        {
            if (platform == null)
            {
                throw new ArgumentNullException(nameof(platform));
            }

            platform.RegisterCustomFunction("FunctionA", Stopwatch.Restart);
            platform.RegisterCustomFunction("FunctionB", () =>
                platform.Values.Set("耗时毫秒", Stopwatch.Elapsed.TotalMilliseconds, out _));
            platform.RegisterCustomFunction("CalcSumTiming", () => CalculateFixedSum(platform));
            platform.RegisterCustomFunction("EndTiming", () => FinishLoopTiming(platform));
        }

        private static void CalculateFixedSum(IAutomationPlatform platform)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            long sum = 0;
            for (int i = 1; i <= 100000; i++)
            {
                sum += i;
            }
            timer.Stop();
            platform.Values.Set("累加和", sum, out _);
            platform.Values.Set("耗时毫秒", timer.Elapsed.TotalMilliseconds, out _);
        }

        private static void FinishLoopTiming(IAutomationPlatform platform)
        {
            if (!platform.Values.TryGet("循环计数", out ValueSnapshot countValue, out _)
                || !double.TryParse(countValue.Value, out double count))
            {
                return;
            }

            long integerCount = (long)count;
            long sum = integerCount * (integerCount + 1) / 2;
            platform.Values.Set("累加和", sum, out _);
            platform.Values.Set("耗时毫秒", Stopwatch.Elapsed.TotalMilliseconds, out _);
        }
    }
}
