using System;
using System.Collections.Generic;

// 模块：Automation 协议 / 变量契约。
// 职责范围：集中定义变量作用域、ChangeSet 声明规则和公开索引边界，供持久化、Bridge、MCP 与编译器共同引用。
// 排查入口：变量定位异常时先区分稳定 Id、名称和当前物理索引，避免把索引当跨版本身份。

namespace Automation.Protocol
{
    /// <summary>
    /// 变量作用域公开契约。持久化、Bridge 与 MCP 均使用这些小写值。
    /// </summary>
    public static class VariableScopeContract
    {
        public const string Public = "public";
        public const string Process = "process";
        public const string System = "system";
        public const string SupportedScopes = "public、process、system";

        public static bool IsValid(string scope)
        {
            return scope == Public || scope == Process || scope == System;
        }
    }

    /// <summary>
    /// ChangeSet 变量声明公开契约。MCP、Bridge 与编译器共享类型、策略、默认值和基础校验。
    /// </summary>
    public static class VariableChangeContract
    {
        public const string DoubleType = "double";
        public const string StringType = "string";
        public const string DefaultType = DoubleType;
        public const string SupportedTypes = "double、string";

        public const string ReusePolicy = "reuse";
        public const string CreatePolicy = "create";
        public const string UpdatePolicy = "update";
        public const string ReplacePolicy = "replace";
        public const string RequirePolicy = "require";
        public const string DefaultPolicy = ReusePolicy;
        public const string SupportedPolicies = "reuse/create/update/replace/require";

        public static bool IsValidType(string type)
        {
            return type == DoubleType || type == StringType;
        }

        public static bool IsValidPolicy(string policy)
        {
            return policy == ReusePolicy
                || policy == CreatePolicy
                || policy == UpdatePolicy
                || policy == ReplacePolicy
                || policy == RequirePolicy;
        }

        public static string Validate(IEnumerable<VariableChange> variables)
        {
            int index = 0;
            foreach (VariableChange variable in variables ?? Array.Empty<VariableChange>())
            {
                if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
                    return $"variables[{index}].name 不能为空。";
                if (!VariableScopeContract.IsValid(variable.Scope))
                    return $"variables[{index}].scope 必须是 {VariableScopeContract.SupportedScopes}。";
                bool processScope = string.Equals(
                    variable.Scope, VariableScopeContract.Process, StringComparison.Ordinal);
                if (processScope && variable.OwnerProcess == null)
                    return $"variables[{index}].scope=process 时 ownerProcess 必填。";
                if (!processScope && variable.OwnerProcess != null)
                    return $"variables[{index}].scope={variable.Scope} 时不能携带 ownerProcess。";
                if (variable.Index.HasValue
                    && (variable.Index.Value < 0
                        || variable.Index.Value >= VariableIndexContract.NormalValueCapacity))
                {
                    return $"variables[{index}].index 必须位于普通变量区 "
                        + $"{VariableIndexContract.NormalValueIndexRange}。";
                }
                string type = string.IsNullOrWhiteSpace(variable.Type)
                    ? DefaultType
                    : variable.Type;
                if (!IsValidType(type))
                    return $"variables[{index}].type 只能是 {SupportedTypes}。";
                string policy = string.IsNullOrWhiteSpace(variable.Policy)
                    ? DefaultPolicy
                    : variable.Policy;
                if (!IsValidPolicy(policy))
                    return $"variables[{index}].policy 只能是 {SupportedPolicies}。";
                index++;
            }
            return null;
        }
    }

    /// <summary>
    /// 变量固定槽位分区契约，供平台 Store、Bridge 与 MCP Schema 共享。
    /// </summary>
    public static class VariableIndexContract
    {
        public const int NormalValueCapacity = 1000;
        public const int SystemValueCapacity = 200;
        public const int SystemValueStartIndex = NormalValueCapacity;
        public const int ValueCapacity = NormalValueCapacity + SystemValueCapacity;
        public const int MaximumNormalValueIndex = NormalValueCapacity - 1;
        public const int MaximumValueIndex = ValueCapacity - 1;

        public const string NormalValueIndexRange = "[0,1000)";
        public const string SystemValueIndexRange = "[1000,1200)";
        public const string ValueIndexRange = "[0,1200)";

        public static bool IsSystemIndex(int index)
        {
            return index >= SystemValueStartIndex && index < ValueCapacity;
        }
    }
}
