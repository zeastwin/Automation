// 模块：编辑器 / 流程 / Inspector。
// 职责范围：指令属性定义、编辑控件、选择器和值转换。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Automation
{
    /// <summary>
    /// 把编辑器运行时服务绑定到具体草稿对象图。绑定按对象实例隔离，
    /// 不存在进程级 Current，允许多个平台实例互不污染。
    /// </summary>
    internal static class EditorServiceRegistry
    {
        private sealed class RuntimeHolder
        {
            public RuntimeHolder(PlatformRuntime runtime)
            {
                Runtime = runtime;
            }

            public PlatformRuntime Runtime { get; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly ConditionalWeakTable<object, RuntimeHolder> runtimes =
            new ConditionalWeakTable<object, RuntimeHolder>();

        public static void AttachGraph(object root, PlatformRuntime runtime)
        {
            if (root == null || runtime == null)
            {
                return;
            }
            AttachRecursive(root, runtime, new HashSet<object>(ReferenceComparer.Instance), 0);
        }

        public static PlatformRuntime GetRuntime(object instance)
        {
            return instance != null && runtimes.TryGetValue(instance, out RuntimeHolder holder)
                ? holder.Runtime
                : null;
        }

        public static object GetService(object instance, Type serviceType)
        {
            PlatformRuntime runtime = GetRuntime(instance);
            return runtime != null && serviceType != null && serviceType.IsInstanceOfType(runtime)
                ? runtime
                : null;
        }

        private static void AttachRecursive(object value, PlatformRuntime runtime,
            HashSet<object> visited, int depth)
        {
            if (value == null || depth > 10 || value is string || value is Delegate)
            {
                return;
            }
            Type type = value.GetType();
            if (type.IsValueType || !visited.Add(value))
            {
                return;
            }
            runtimes.Remove(value);
            runtimes.Add(value, new RuntimeHolder(runtime));

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    AttachRecursive(item, runtime, visited, depth + 1);
                }
                return;
            }
            if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
            {
                return;
            }
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                try
                {
                    AttachRecursive(property.GetValue(value), runtime, visited, depth + 1);
                }
                catch
                {
                    // 编辑器服务绑定失败只影响该不可读属性，不改变草稿内容。
                }
            }
        }
    }
}
