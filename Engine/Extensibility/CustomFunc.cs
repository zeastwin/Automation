using System;
// 模块：引擎 / 扩展点。
// 职责范围：提供平台内部自定义函数注册与执行容器。

using System.Collections.Generic;

namespace Automation
{
    /// <summary>
    /// 平台内部的自定义函数注册与执行容器。业务函数通过 IAutomationPlatform 注册，不直接修改此类型。
    /// </summary>
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
            if (!TryGetFunction(funcName, out FunctionDelegate funcToRun))
            {
                return false;
            }
            funcToRun();
            return true;
        }

        internal bool TryGetFunction(string name, out FunctionDelegate function)
        {
            function = null;
            return !string.IsNullOrWhiteSpace(name)
                && functionMap.TryGetValue(name, out function);
        }
    }
}
