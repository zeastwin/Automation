using System;
// 模块：引擎 / 指令定义。
// 职责范围：维护原生指令类型、字段、行为和引用元数据的权威事实。

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Automation
{
    /// <summary>
    /// 平台指令类型的唯一注册源。注册和实例化不依赖窗体、界面检查器或当前编辑状态。
    /// </summary>
    public static class OperationDefinitionRegistry
    {
        private static readonly Func<OperationType>[] Factories =
        {
            () => new HomeRun(),
            () => new StationRunPos(),
            () => new ModifyStationPos(),
            () => new GetStationPos(),
            () => new CreateTray(),
            () => new TrayRunPos(),
            () => new StationRunRel(),
            () => new SetStationVel(),
            () => new StationStop(),
            () => new WaitStationStop(),
            () => new CallCustomFunc(),
            () => new IoOperate(),
            () => new IoCheck(),
            () => new IoGroup(),
            () => new IoLogicGoto(),
            () => new ProcOps(),
            () => new WaitProc(),
            () => new CycleTimeProbe(),
            () => new Goto(),
            () => new ParamGoto(),
            () => new Delay(),
            () => new EndProcess(),
            () => new PopupDialog(),
            () => new GetValue(),
            () => new ModifyValue(),
            () => new StringFormat(),
            () => new Split(),
            () => new Replace(),
            () => new TcpOps(),
            () => new WaitTcp(),
            () => new SendTcpMsg(),
            () => new ReceiveTcpMsg(),
            () => new SerialPortOps(),
            () => new WaitSerialPort(),
            () => new SendSerialPortMsg(),
            () => new ReceiveSerialPortMsg(),
            () => new SendReceiveCommMsg(),
            () => new PlcReadWrite(),
            () => new PlcMappingControl(),
            () => new GetDataStructCount(),
            () => new SetDataStructItem(),
            () => new GetDataStructItem(),
            () => new CopyDataStructItem(),
            () => new InsertDataStructItem(),
            () => new DelDataStructItem(),
            () => new FindDataStructItem()
        };

        private static readonly IReadOnlyDictionary<string, Func<OperationType>> FactoryByOperaType
            = BuildFactoryMap();

        public static IReadOnlyList<OperationType> CreateAll()
        {
            return Factories.Select(factory => factory()).ToList();
        }

        public static OperationType Create(string operaType)
        {
            if (string.IsNullOrWhiteSpace(operaType))
            {
                throw new ArgumentException("指令类型不能为空。", nameof(operaType));
            }
            if (!FactoryByOperaType.TryGetValue(operaType, out Func<OperationType> factory))
            {
                throw new KeyNotFoundException($"未找到指令类型：{operaType}");
            }
            return factory();
        }

        private static IReadOnlyDictionary<string, Func<OperationType>> BuildFactoryMap()
        {
            var result = new Dictionary<string, Func<OperationType>>(StringComparer.Ordinal);
            foreach (Func<OperationType> factory in Factories)
            {
                OperationType operation = factory();
                if (operation == null || string.IsNullOrWhiteSpace(operation.OperaType))
                {
                    throw new InvalidOperationException("指令注册项未提供有效 OperaType。");
                }
                if (result.ContainsKey(operation.OperaType))
                {
                    throw new InvalidOperationException($"指令类型重复注册：{operation.OperaType}");
                }
                ValidateModel(operation);
                ValidateBehaviorContract(operation);
                result.Add(operation.OperaType, factory);
            }
            return result;
        }

        private static void ValidateModel(OperationType operation)
        {
            PropertyInfo[] properties = operation.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public);
            ValidatePropertyNames(operation.GetType(), operation.OperaType, new HashSet<Type>());
            IGrouping<string, PropertyInfo> duplicate = properties
                .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    $"指令类型[{operation.OperaType}]存在仅大小写不同的属性："
                    + string.Join("/", duplicate.Select(property => property.Name)));
            }

            foreach (PropertyInfo property in properties)
            {
                InlineListAttribute list = property.GetCustomAttribute<InlineListAttribute>();
                if (list == null)
                {
                    continue;
                }
                if (!typeof(IList).IsAssignableFrom(property.PropertyType)
                    || !property.PropertyType.IsGenericType)
                {
                    throw new InvalidOperationException(
                        $"指令类型[{operation.OperaType}]的列表字段[{property.Name}]必须是泛型 IList。");
                }
                if (list.MinItems < 0 || list.MaxItems < list.MinItems)
                {
                    throw new InvalidOperationException(
                        $"指令类型[{operation.OperaType}]的列表字段[{property.Name}]数量边界无效。");
                }
                if (!(property.GetValue(operation) is IList items))
                {
                    throw new InvalidOperationException(
                        $"指令类型[{operation.OperaType}]的列表字段[{property.Name}]必须由构造函数初始化。");
                }
                if (items.Count < list.MinItems || items.Count > list.MaxItems)
                {
                    throw new InvalidOperationException(
                        $"指令类型[{operation.OperaType}]的列表字段[{property.Name}]默认数量超出边界。");
                }
            }
        }

        private static void ValidatePropertyNames(Type type, string path, HashSet<Type> visited)
        {
            if (type == null || !visited.Add(type))
            {
                return;
            }
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            IGrouping<string, PropertyInfo> duplicate = properties
                .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    $"指令模型[{path}]存在仅大小写不同的属性："
                    + string.Join("/", duplicate.Select(property => property.Name)));
            }
            foreach (PropertyInfo property in properties)
            {
                if (string.IsNullOrEmpty(property.Name) || !char.IsUpper(property.Name[0]))
                {
                    throw new InvalidOperationException(
                        $"指令模型[{path}]的属性[{property.Name}]必须使用 PascalCase。");
                }
                NumericRangeAttribute range = property.GetCustomAttribute<NumericRangeAttribute>();
                if (range != null)
                {
                    Type valueType = Nullable.GetUnderlyingType(property.PropertyType)
                        ?? property.PropertyType;
                    TypeCode typeCode = Type.GetTypeCode(valueType);
                    bool numeric = typeCode == TypeCode.Byte || typeCode == TypeCode.SByte
                        || typeCode == TypeCode.Int16 || typeCode == TypeCode.UInt16
                        || typeCode == TypeCode.Int32 || typeCode == TypeCode.UInt32
                        || typeCode == TypeCode.Int64 || typeCode == TypeCode.UInt64
                        || typeCode == TypeCode.Single || typeCode == TypeCode.Double
                        || typeCode == TypeCode.Decimal;
                    if (!numeric || double.IsNaN(range.Minimum) || double.IsNaN(range.Maximum)
                        || range.Maximum < range.Minimum)
                    {
                        throw new InvalidOperationException(
                            $"指令模型[{path}]的属性[{property.Name}]数值范围无效。");
                    }
                }
                InlineListAttribute list = property.GetCustomAttribute<InlineListAttribute>();
                if (list != null && property.PropertyType.IsGenericType)
                {
                    ValidatePropertyNames(
                        property.PropertyType.GetGenericArguments()[0],
                        path + "." + property.Name + "[]",
                        visited);
                }
                else if (property.GetCustomAttribute<InlineGroupAttribute>() != null)
                {
                    ValidatePropertyNames(property.PropertyType, path + "." + property.Name, visited);
                }
            }
        }

        private static void ValidateBehaviorContract(OperationType operation)
        {
            JObject contract = OperationBehaviorCatalog.BuildContract(operation);
            if (!string.Equals(contract?["coverage"]?.ToString(), "specialized", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"指令类型[{operation.OperaType}]缺少专用运行行为契约。");
            }
            JObject fieldRules = contract["fieldRules"] as JObject;
            if (fieldRules == null)
            {
                throw new InvalidOperationException(
                    $"指令类型[{operation.OperaType}]的运行行为契约缺少 fieldRules。");
            }
            foreach (JProperty fieldRule in fieldRules.Properties())
            {
                if (operation.GetType().GetProperty(fieldRule.Name) == null)
                {
                    throw new InvalidOperationException(
                        $"指令类型[{operation.OperaType}]的行为字段[{fieldRule.Name}]不存在。");
                }
            }
        }
    }
}
