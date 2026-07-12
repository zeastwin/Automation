using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Automation
{
    public class CustomFunc
    {
        public delegate void FunctionDelegate();
        private readonly Dictionary<string, FunctionDelegate> functionMap;

        public List<string> funcName { get; } = new List<string>();

        public CustomFunc()
        {
            functionMap = new Dictionary<string, FunctionDelegate>
            {
                { "FunctionA", SS },
                { "FunctionB", SS2 }, 
                { "Functsdfdsfsdf", SS2 }, 
                { "CalcSumTiming", SumFrom1To100000AndTiming },
                { "EndTiming", EndTiming },
            };
            foreach (KeyValuePair<string, FunctionDelegate> item in functionMap)
            {
                funcName.Add(item.Key);
            }
        }

        public void RegisterFunction(string name, FunctionDelegate function)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("自定义方法名称不能为空。", nameof(name));
            }
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            if (functionMap.ContainsKey(name))
            {
                throw new InvalidOperationException($"自定义方法重复注册:{name}");
            }
            functionMap.Add(name, function);
            funcName.Add(name);
        }

        public bool RunFunc(string funcName)
        {
            if (string.IsNullOrWhiteSpace(funcName)
                || !functionMap.TryGetValue(funcName, out FunctionDelegate funcToRun))
            {
                return false;
            }
            funcToRun();
            return true;
        }

        private readonly Stopwatch stopwatch = new Stopwatch();

        public void SS()
        {
            //for (int i = 0; i < 1; i++)
            //{
            //    Console.WriteLine("?");
            //    SF.Delay(50);
            //}
            stopwatch.Restart();
        }

        public void SS2()
        {
            //for (int i = 0; i < 3; i++)
            //{
            //    Console.WriteLine("!");
            //    SF.Delay(500);
            //} 
            double time = stopwatch.ElapsedMilliseconds;
            SF.frmInfo.PrintInfo("毫秒：" + time, FrmInfo.Level.Normal);
          //  Console.WriteLine("毫秒：" + Stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// 结束计时：从"循环计数"读取已累加次数，计算累加和 sum = n*(n+1)/2，
        /// 总耗时写入"耗时毫秒"，累加和写入"累加和"，并在信息日志输出结果。
        /// </summary>
        public void EndTiming()
        {
            double count = 0;
            if (SF.valueStore != null)
            {
                count = SF.valueStore.get_D_ValueByName("循环计数");
            }

            long n = (long)count;
            long sum = n * (n + 1) / 2;
            double elapsedMs = stopwatch.ElapsedMilliseconds;

            if (SF.valueStore != null)
            {
                SF.valueStore.setValueByName("累加和", sum, "CustomFunc.EndTiming");
                SF.valueStore.setValueByName("耗时毫秒", elapsedMs, "CustomFunc.EndTiming");
            }

            if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo(
                    $"累加完成：1+2+...+{n} = {sum}，耗时 {elapsedMs:F2} 毫秒",
                    FrmInfo.Level.Normal);
            }
        }

        /// <summary>
        /// 从 1 累加到 100000，记录耗时（毫秒）写入变量"耗时毫秒"，累加结果写入变量"累加和"，
        /// 并在信息日志中输出累加结果和耗时。
        /// </summary>
        public void SumFrom1To100000AndTiming()
        {
            Stopwatch sw = Stopwatch.StartNew();
            long sum = 0;
            for (int i = 1; i <= 100000; i++)
            {
                sum += i;
            }
            sw.Stop();
            double elapsedMs = sw.Elapsed.TotalMilliseconds;

            // 将累加结果写入变量"累加和"（索引3）
            if (SF.valueStore != null)
            {
                SF.valueStore.setValueByName("累加和", sum, "CustomFunc.SumFrom1To100000AndTiming");
                SF.valueStore.setValueByName("耗时毫秒", elapsedMs, "CustomFunc.SumFrom1To100000AndTiming");
            }

            if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo(
                    $"累加完成：1+2+...+100000 = {sum}，耗时 {elapsedMs:F2} 毫秒",
                    FrmInfo.Level.Normal);
            }
        }
    }
}
