using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 平台指令类型的唯一注册源。注册和实例化不依赖窗体、PropertyGrid 或当前编辑状态。
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
            () => new ReceoveTcpMsg(),
            () => new SerialPortOps(),
            () => new WaitSerialPort(),
            () => new SendSerialPortMsg(),
            () => new ReceoveSerialPortMsg(),
            () => new SendReceoveCommMsg(),
            () => new PlcReadWrite(),
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
                result.Add(operation.OperaType, factory);
            }
            return result;
        }
    }
}
