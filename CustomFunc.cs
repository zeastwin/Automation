using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Automation
{
    public class CustomFunc
    {
        public List<string> funcName = new List<string>();
        public delegate void FunctionDelegate();
        Dictionary<string, FunctionDelegate> functionMap;

        public CustomFunc()
        {
            functionMap = new Dictionary<string, FunctionDelegate>
            {
                { "FunctionA", SS },
                { "FunctionB", SS2 }, 
                { "Functsdfdsfsdf", SS2 }, 
            };
            foreach (var item in functionMap)
            {
                funcName.Add(item.Key);
            }
        }
        public bool RunFunc(string funcName)
        {
            if (functionMap.ContainsKey(funcName))
            {
                FunctionDelegate funcToRun = functionMap[funcName];
                funcToRun();
                return true;
            }
            else
            {
                return false;
            }
        }
        Stopwatch Stopwatch = new Stopwatch();
        public void SS()
        {
            //for (int i = 0; i < 1; i++)
            //{
            //    Console.WriteLine("?");
            //    SF.Delay(50);
            //}
            Stopwatch.Restart();
        }
        public void SS2()
        {
            //for (int i = 0; i < 3; i++)
            //{
            //    Console.WriteLine("!");
            //    SF.Delay(500);
            //} 
            double time = Stopwatch.ElapsedMilliseconds;
            SF.frmInfo.PrintInfo("毫秒：" + time, FrmInfo.Level.Normal);
          //  Console.WriteLine("毫秒：" + Stopwatch.ElapsedMilliseconds);
        }
    }
}
