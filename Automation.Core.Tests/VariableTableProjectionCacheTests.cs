// 模块：核心测试 / 变量表。
// 职责范围：验证变量表作用域投影与搜索投影不依赖 DataGridView 行显隐。

using System;
using System.Collections.Generic;
using System.Linq;
using Automation.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class VariableTableProjectionCacheTests
    {
        [TestMethod]
        public void Build_ProducesStableScopeProjections()
        {
            Guid processId = Guid.NewGuid();
            var variables = new DicValue[ValueConfigStore.ValueCapacity];
            variables[1] = CreateVariable(
                1,
                "PublicValue",
                VariableScopeContract.Public,
                null);
            variables[2] = CreateVariable(
                2,
                "ProcessValue",
                VariableScopeContract.Process,
                processId);
            variables[ValueConfigStore.SystemValueStartIndex] = CreateVariable(
                ValueConfigStore.SystemValueStartIndex,
                "SystemValue",
                VariableScopeContract.System,
                null);

            VariableTableProjectionCache cache =
                VariableTableProjectionCache.Build(variables);

            Assert.AreEqual(
                ValueConfigStore.ValueCapacity,
                cache.GetSlots(VariableEditorService.AllScopes, null).Count);
            Assert.AreEqual(
                ValueConfigStore.NormalValueCapacity - 1,
                cache.GetSlots(VariableScopeContract.Public, null).Count,
                "公共投影应保留普通未配置槽位，只排除流程私有槽位。");
            CollectionAssert.AreEqual(
                new[] { 2 },
                cache.GetSlots(VariableScopeContract.Process, processId).ToArray());
            Assert.AreEqual(
                ValueConfigStore.SystemValueCapacity,
                cache.GetSlots(VariableScopeContract.System, null).Count);
        }

        [TestMethod]
        public void Search_UsesGlobalConfiguredVariableProjection()
        {
            Guid processId = Guid.NewGuid();
            var variables = new DicValue[ValueConfigStore.ValueCapacity];
            variables[8] = CreateVariable(
                8,
                "PressureTarget",
                VariableScopeContract.Process,
                processId);
            variables[8].Note = "工艺设定";
            var processNames = new Dictionary<Guid, string>
            {
                [processId] = "压合流程"
            };

            CollectionAssert.AreEqual(
                new[] { 8 },
                VariableTableProjectionCache.Search(
                    variables,
                    "压合",
                    processNames).ToArray());
            CollectionAssert.AreEqual(
                new[] { 8 },
                VariableTableProjectionCache.Search(
                    variables,
                    "pressure",
                    processNames).ToArray());
            Assert.AreEqual(
                0,
                VariableTableProjectionCache.Search(
                    variables,
                    "未配置",
                    processNames).Count,
                "搜索只返回已配置变量，不为固定容量空槽创建结果行。");
        }

        private static DicValue CreateVariable(
            int index,
            string name,
            string scope,
            Guid? ownerProcId)
        {
            return new DicValue
            {
                Id = Guid.NewGuid(),
                Index = index,
                Name = name,
                Type = "double",
                Value = "0",
                Scope = scope,
                OwnerProcId = ownerProcId
            };
        }
    }
}
