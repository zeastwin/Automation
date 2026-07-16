using System;
using System.Collections.Generic;

namespace Automation
{
    public class CustomFunc
    {
        public delegate void FunctionDelegate();
        private readonly Dictionary<string, FunctionDelegate> functionMap;

        public List<string> funcName { get; } = new List<string>();

        public CustomFunc()
        {
            functionMap = new Dictionary<string, FunctionDelegate>(StringComparer.Ordinal);
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

    }
}
