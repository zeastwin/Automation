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
    }
}
