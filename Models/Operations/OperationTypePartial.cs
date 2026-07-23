// 模块：共享模型 / 指令。
// 职责范围：定义流程指令、参数特性和当前编辑器元数据；由 Engine、Bridge 与 Editor 共同使用。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using static System.ComponentModel.TypeConverter;

namespace Automation
{
    public class OperationTypePartial
    {
        private static PlatformRuntime GetRuntime(ITypeDescriptorContext context)
        {
            return context?.GetService(typeof(PlatformRuntime)) as PlatformRuntime;
        }

        public class IoOutItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(GetRuntime(context)?.Stores.IoConfiguration?.OutputNames ?? new List<string>());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class IoInItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(GetRuntime(context)?.Stores.IoConfiguration?.InputNames ?? new List<string>());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class funcNameItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(GetRuntime(context)?.CustomFunctions.funcName ?? new List<string>());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class AlarmItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "报警停止", "报警忽略", "自动处理", "弹框确定", "弹框确定与否", "弹框确定与否与取消" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class PopupTypeItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "弹是", "弹是与否", "弹是与否与取消" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class PopupInfoTypeItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "自定义提示信息", "变量类型", "报警信息库" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class EnableItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "启用", "禁用" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class BuzzerTimeTypeItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "自定义时间", "持续蜂鸣" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class IOLevelItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "正常", "取反" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class IOUsedItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "通用" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class AlarmInfoItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (GetRuntime(context)?.Stores.Alarms == null)
                {
                    return new StandardValuesCollection(new List<int>());
                }
                return new StandardValuesCollection(GetRuntime(context)?.Stores.Alarms.GetValidIndices());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class DataStItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(GetRuntime(context)?.Stores.DataStructures.GetStructNames() ?? new List<string>());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class ProcItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                List<string> names = GetRuntime(context)?.Stores.Processes?.Items?
                    .Select((proc, index) => string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"流程{index}" : proc.head.Name)
                    .ToList() ?? new List<string>();
                return new StandardValuesCollection(names);
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }
        public class ProcItemOps : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "运行", "停止" });
            }
        }

        public class ProcWaitStateItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "运行", "就绪" });
            }
        }

        public class WaitProcWorkModeItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>()
                {
                    WaitProc.WaitReadyMode,
                    WaitProc.StateJumpMode,
                    WaitProc.GetStateMode
                });
            }
        }

        public class CommunicationOps : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "启动", "断开" });
            }
        }
        public class CommType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "TCP", "串口" });
            }
        }
        public class CommItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (context?.Instance is SendReceiveCommMsg commMsg)
                {
                    if (string.Equals(commMsg.CommType, "TCP", StringComparison.OrdinalIgnoreCase))
                    {
                        return new StandardValuesCollection((GetRuntime(context)?.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                            .Where(item => item != null).Select(item => item.Name).ToList());
                    }
                    if (string.Equals(commMsg.CommType, "串口", StringComparison.OrdinalIgnoreCase))
                    {
                        return new StandardValuesCollection((GetRuntime(context)?.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
                            .Where(item => item != null).Select(item => item.Name).ToList());
                    }
                }

                List<string> all = new List<string>();
                all.AddRange((GetRuntime(context)?.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                    .Where(item => item != null).Select(item => item.Name));
                all.AddRange((GetRuntime(context)?.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
                    .Where(item => item != null).Select(item => item.Name));
                return new StandardValuesCollection(all);
            }
        }
        public class SturctItemType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "double", "string" });
            }
        }

        public class ReplaceType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "替换指定字符", "替换指定区间" });
            }
        }

        public class ValueItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(GetRuntime(context)?.Stores.Values.GetValueNames() ?? new List<string>());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }
        public class TcpItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection((GetRuntime(context)?.Stores.Communication?.GetSocketSnapshot() ?? Array.Empty<SocketInfo>())
                    .Where(item => item != null).Select(item => item.Name).ToList());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class SerialPortItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection((GetRuntime(context)?.Stores.Communication?.GetSerialSnapshot() ?? Array.Empty<SerialPortInfo>())
                    .Where(item => item != null).Select(item => item.Name).ToList());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class ModifyType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "替换", "叠加", "乘法", "除法", "求余", "绝对值" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class FindType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "名称等于key", "字符串等于key", "数值等于key"});
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }
        public class JudgeMode : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "值在区间左", "值在区间右", "值在区间内", "等于特征字符"});
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }
        public class CommunicationResponseJudgeModeItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>()
                {
                    "非空", "字段存在", "等于特征字符", "包含特征字符",
                    "值在区间左", "值在区间右", "值在区间内"
                });
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class Operator : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "且", "或"});
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }
        public class LogicOperator : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "与", "或" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class PlcItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (GetRuntime(context)?.Stores.Plc == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return new StandardValuesCollection(GetRuntime(context)?.Stores.Plc.GetSnapshot().Devices
                    .Select(device => device.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList());
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public sealed class PlcMappingActionItem : EnumConverter
        {
            public PlcMappingActionItem() : base(typeof(PlcMappingAction))
            {
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                switch (value as string)
                {
                    case "重新初始化": return PlcMappingAction.Reinitialize;
                    case "启动映射": return PlcMappingAction.Start;
                    case "停止映射": return PlcMappingAction.Stop;
                    default: return base.ConvertFrom(context, culture, value);
                }
            }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                if (destinationType == typeof(string) && value is PlcMappingAction action)
                {
                    switch (action)
                    {
                        case PlcMappingAction.Reinitialize: return "重新初始化";
                        case PlcMappingAction.Start: return "启动映射";
                        case PlcMappingAction.Stop: return "停止映射";
                    }
                }
                return base.ConvertTo(context, culture, value, destinationType);
            }
        }

        public sealed class PlcAccessModeItem : PlcChineseEnumItem<PlcAccessMode>
        {
            public PlcAccessModeItem() : base(new Dictionary<PlcAccessMode, string>
            {
                [PlcAccessMode.Items] = "按项",
                [PlcAccessMode.ContinuousBatch] = "连续批量"
            }) { }
        }

        public abstract class PlcChineseEnumItem<T> : EnumConverter where T : struct
        {
            private readonly Dictionary<T, string> displays;

            protected PlcChineseEnumItem(Dictionary<T, string> displays) : base(typeof(T))
            {
                this.displays = displays;
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string text)
                {
                    foreach (KeyValuePair<T, string> item in displays)
                    {
                        if (string.Equals(item.Value, text, StringComparison.Ordinal)) return item.Key;
                    }
                }
                return base.ConvertFrom(context, culture, value);
            }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value,
                Type destinationType)
            {
                if (destinationType == typeof(string) && value is T typed
                    && displays.TryGetValue(typed, out string display)) return display;
                return base.ConvertTo(context, culture, value, destinationType);
            }
        }

        public sealed class PlcAccessActionItem : PlcChineseEnumItem<PlcAccessAction>
        {
            public PlcAccessActionItem() : base(new Dictionary<PlcAccessAction, string>
            {
                [PlcAccessAction.Read] = "读取",
                [PlcAccessAction.Write] = "写入"
            }) { }
        }

        public sealed class PlcAreaItem : PlcChineseEnumItem<PlcArea>
        {
            public PlcAreaItem() : base(new Dictionary<PlcArea, string>
            {
                [PlcArea.Coil] = "线圈",
                [PlcArea.DiscreteInput] = "离散输入",
                [PlcArea.HoldingRegister] = "保持寄存器",
                [PlcArea.InputRegister] = "输入寄存器"
            }) { }
        }

        public sealed class PlcValueSourceItem : PlcChineseEnumItem<PlcValueSource>
        {
            public PlcValueSourceItem() : base(new Dictionary<PlcValueSource, string>
            {
                [PlcValueSource.Variable] = "变量",
                [PlcValueSource.Constant] = "固定值"
            }) { }
        }

        public sealed class PlcWriteItemConverter : SerializableExpandableObjectConverter
        {
            public override bool GetPropertiesSupported(ITypeDescriptorContext context) => true;

            public override PropertyDescriptorCollection GetProperties(
                ITypeDescriptorContext context, object value, Attribute[] attributes)
            {
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(
                    typeof(PlcWriteItem), attributes);
                if (!(value is PlcWriteItem item)) return properties;
                string hidden = item.Source == PlcValueSource.Variable
                    ? nameof(PlcWriteItem.ConstantValue)
                    : nameof(PlcWriteItem.VariableName);
                return new PropertyDescriptorCollection(properties.Cast<PropertyDescriptor>()
                    .Where(property => !string.Equals(property.Name, hidden, StringComparison.Ordinal))
                    .ToArray(), true);
            }
        }

        public sealed class PlcWriteBatchConverter : SerializableExpandableObjectConverter
        {
            public override bool GetPropertiesSupported(ITypeDescriptorContext context) => true;

            public override PropertyDescriptorCollection GetProperties(
                ITypeDescriptorContext context, object value, Attribute[] attributes)
            {
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(
                    typeof(PlcWriteBatch), attributes);
                if (!(value is PlcWriteBatch batch)) return properties;
                string hidden = batch.Source == PlcValueSource.Variable
                    ? nameof(PlcWriteBatch.ConstantValue)
                    : nameof(PlcWriteBatch.FirstVariableName);
                return new PropertyDescriptorCollection(properties.Cast<PropertyDescriptor>()
                    .Where(property => !string.Equals(property.Name, hidden, StringComparison.Ordinal))
                    .ToArray(), true);
            }
        }

        public class PointModifyType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "叠加", "替换" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class GotoItem : StringConverter
        {
            List<string> Item(ITypeDescriptorContext context)
            {
                List<string> gotoItem = new List<string>();
                if (GetRuntime(context)?.Stores.Processes?.Items == null)
                {
                    return gotoItem;
                }
                int procIndex = GetRuntime(context)?.EditorUi?.GetSelection()?.ProcIndex ?? -1;
                if (procIndex < 0 || procIndex >= GetRuntime(context)?.Stores.Processes.Items.Count)
                {
                    return gotoItem;
                }
                if (GetRuntime(context)?.Stores.Processes.Items[procIndex]?.steps == null)
                {
                    return gotoItem;
                }
                for (int i = 0; i < GetRuntime(context)?.Stores.Processes.Items[procIndex].steps.Count; i++)
                {
                    var ops = GetRuntime(context)?.Stores.Processes.Items[procIndex].steps[i]?.Ops;
                    if (ops == null)
                    {
                        continue;
                    }
                    for (int j = 0; j < ops.Count; j++)
                    {
                        gotoItem.Add($"{procIndex}-{i}-{j}");
                    }
                }
                return gotoItem;
            }

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(Item(context));
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }
        public class SerializableExpandableObjectConverter : ExpandableObjectConverter
        {
            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                if (destinationType == typeof(string))
                {
                    return false;
                }
                else
                {
                    return base.CanConvertTo(context, destinationType);
                }
            }

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string))
                {
                    return false;
                }
                else
                {
                    return base.CanConvertFrom(context, sourceType);
                }
            }
        }
        [Serializable]
        public class CustomList<T> : List<T>
        {
            public override string ToString()
            {
                return "";
            }

        }
        public class ParamListConverter<T> : TypeConverter where T : class
        {
            public static string Name { get; set; }

            public override bool GetPropertiesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
            {
                if (value is CustomList<T> Params)
                {
                    PropertyDescriptorCollection properties = new PropertyDescriptorCollection(null);

                    for (int i = 0; i < Params.Count; i++)
                    {
                        properties.Add(new ParamPropertyDescriptor<T>(Params, i, Name));
                    }

                    return properties;
                }

                return base.GetProperties(context, value, attributes);
            }
        }

        public class ParamPropertyDescriptor<T> : PropertyDescriptor where T : class
        {
            private CustomList<T> _Params;
            private int _index;

            public ParamPropertyDescriptor(CustomList<T> Params, int index, string name)
                : base($"{name}：{index + 1}", null)
            {
                _Params = Params;
                _index = index;
            }

            public override bool CanResetValue(object component)
            {
                return true;
            }

            public override Type ComponentType => _Params.GetType();

            public override object GetValue(object component)
            {
                return _Params[_index];
            }
            public override bool IsReadOnly => false;

            public override Type PropertyType => _Params[_index].GetType();

            public override void ResetValue(object component)
            {
            }

            public override void SetValue(object component, object value)
            {
                _Params[_index] = (T)value;
            }

            public override bool ShouldSerializeValue(object component)
            {
                return false;
            }
        }
        public class StationtItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (GetRuntime(context)?.Stores.Stations?.Items == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return new StandardValuesCollection(GetRuntime(context)?.Stores.Stations.Items.Select(Info => Info.Name).ToList());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class StationHomeType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "所有轴同步回", "轴按优先顺序回" });
            }
        }
        public class SetStationVelItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (GetRuntime(context)?.Stores.Stations?.Items == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return new StandardValuesCollection(GetRuntime(context)?.Stores.Stations.Items.Select(Info => Info.Name).ToList());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class AxisDisableParam : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "有禁用", "无禁用" });
            }
        }
        public class AxisVelParam : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "不改变", "改变速度" });
            }
        }
        public class StationPosDic : StringConverter
        {
            List<string> Item(ITypeDescriptorContext context)
            {
                List<string> posItems = new List<string>();
                if (GetRuntime(context)?.Stores.Stations?.Items == null)
                {
                    return posItems;
                }
                string stationName = null;
                if (GetRuntime(context)?.EditorUi?.CurrentOperationContext is StationRunPos stationRunPos)
                {
                    stationName = stationRunPos.StationName;
                }
                else if (GetRuntime(context)?.EditorUi?.CurrentOperationContext is CreateTray createTray)
                {
                    stationName = createTray.StationName;
                }
                else if (GetRuntime(context)?.EditorUi?.CurrentOperationContext is ModifyStationPos modifyStationPos)
                {
                    stationName = modifyStationPos.StationName;
                }
                else if (GetRuntime(context)?.EditorUi?.CurrentOperationContext is GetStationPos getStationPos)
                {
                    stationName = getStationPos.StationName;
                }
                if (string.IsNullOrEmpty(stationName))
                {
                    return posItems;
                }
                var station = GetRuntime(context)?.Stores.Stations.Items.FirstOrDefault(sc => sc.Name == stationName);

                if (station != null)
                {
                    int stationIndex = GetRuntime(context)?.Stores.Stations.Items.IndexOf(station) ?? -1;
                    if (stationIndex != -1)
                    {
                        var posList = GetRuntime(context)?.Stores.Stations.Items[stationIndex].ListDataPos;
                        if (posList != null)
                        {
                            posItems = posList
                                .Where(info => !string.IsNullOrEmpty(info.Name))
                                .Select(info => info.Name)
                                .ToList();
                        }
                    }
                }
            
                return posItems;
            }
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(Item(context));
            }
        }

        public class StationPosSourceType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "当前位置", "指定点位" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class StationPosSaveType : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "保存到点位", "保存到变量" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class StationPosWithSpecial : StringConverter
        {
            List<string> Item(ITypeDescriptorContext context)
            {
                List<string> posItems = new List<string> { "当前位置", "自定义坐标" };
                if (GetRuntime(context)?.Stores.Stations?.Items == null)
                {
                    return posItems;
                }
                string stationName = null;
                if (GetRuntime(context)?.EditorUi?.CurrentOperationContext is ModifyStationPos modifyStationPos)
                {
                    stationName = modifyStationPos.StationName;
                }
                if (string.IsNullOrEmpty(stationName))
                {
                    return posItems;
                }
                var station = GetRuntime(context)?.Stores.Stations.Items.FirstOrDefault(sc => sc.Name == stationName);
                if (station != null)
                {
                    int stationIndex = GetRuntime(context)?.Stores.Stations.Items.IndexOf(station) ?? -1;
                    if (stationIndex != -1)
                    {
                        var posList = GetRuntime(context)?.Stores.Stations.Items[stationIndex].ListDataPos;
                        if (posList != null)
                        {
                            foreach (var info in posList)
                            {
                                if (info == null || string.IsNullOrEmpty(info.Name))
                                {
                                    continue;
                                }
                                if (!posItems.Contains(info.Name))
                                {
                                    posItems.Add(info.Name);
                                }
                            }
                        }
                    }
                }
                return posItems;
            }
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(Item(context));
            }
        }

        public class StationAixsItem : StringConverter
        {
            List<string> Item(ITypeDescriptorContext context)
            {
                List<string> AxisItems = new List<string>();
                if (GetRuntime(context)?.Stores.Stations?.Items == null)
                {
                    return AxisItems;
                }
                if (!(GetRuntime(context)?.EditorUi?.CurrentOperationContext is SetStationVel setStationVel) || string.IsNullOrEmpty(setStationVel.StationName))
                {
                    return AxisItems;
                }
                var station = GetRuntime(context)?.Stores.Stations.Items.FirstOrDefault(sc => sc.Name == setStationVel.StationName);

                if (station != null)
                {
                    int stationIndex = GetRuntime(context)?.Stores.Stations.Items.IndexOf(station) ?? -1;
                    if (stationIndex != -1)
                    {
                        AxisItems.Add("工站");
                        var axisConfigs = GetRuntime(context)?.Stores.Stations.Items[stationIndex].dataAxis?.axisConfigs;
                        if (axisConfigs == null)
                        {
                            return AxisItems;
                        }
                        for (int j = 0; j < axisConfigs.Count; j++)
                        {
                            ushort index = (ushort)j;
                            if (axisConfigs[j].AxisName != "-1")
                            {
                                AxisItems.Add(axisConfigs[j].AxisName);
                            }
                        }
                    }
                }
                return AxisItems;
            }
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(Item(context));
            }
        }
    }
}
