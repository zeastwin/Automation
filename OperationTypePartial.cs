using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.ComponentModel.TypeConverter;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Automation
{
    public class OperationTypePartial
    {
        public class IoOutItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(SF.frmIO.IoOutItems);
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
                return new StandardValuesCollection(SF.frmIO.IoInItems);
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class IoItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(SF.frmIO.IoItems);
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
                return new StandardValuesCollection(SF.customFunc.funcName);
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class IOCountItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "1", "2", "3", "4", "5", "6" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
            }
        }

        public class PauseCountItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "0", "1", "2", "3", "4", "5", "6" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
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
                if (SF.alarmInfoStore == null)
                {
                    return new StandardValuesCollection(new List<int>());
                }
                return new StandardValuesCollection(SF.alarmInfoStore.GetValidIndices());
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
                return new StandardValuesCollection(SF.dataStructStore.GetStructNames());
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }
        public class ProcItemCount : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(SF.frmProc.procListItemCount);
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
                return new StandardValuesCollection(SF.frmProc.procListItem);
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
                if (context?.Instance is SendReceoveCommMsg commMsg)
                {
                    if (string.Equals(commMsg.CommType, "TCP", StringComparison.OrdinalIgnoreCase))
                    {
                        return new StandardValuesCollection(SF.frmComunication.socketInfos.Select(item => item.Name).ToList());
                    }
                    if (string.Equals(commMsg.CommType, "串口", StringComparison.OrdinalIgnoreCase))
                    {
                        return new StandardValuesCollection(SF.frmComunication.serialPortInfos.Select(item => item.Name).ToList());
                    }
                }

                List<string> all = new List<string>();
                all.AddRange(SF.frmComunication.socketInfos.Select(item => item.Name));
                all.AddRange(SF.frmComunication.serialPortInfos.Select(item => item.Name));
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
                return new StandardValuesCollection(SF.valueStore.GetValueNames());
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
                return new StandardValuesCollection(SF.frmComunication.socketInfos.Select(socketInfo => socketInfo.Name).ToList());
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
                return new StandardValuesCollection(SF.frmComunication.serialPortInfos.Select(Info => Info.Name).ToList());
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
        public class MathcCount : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "0","1", "2", "3", "4", "5", "6" });
            }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return false;
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
        public class Count : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new List<string>() { "1", "2", "3", "4", "5", "6" });
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
                if (SF.plcStore == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return new StandardValuesCollection(SF.plcStore.Devices.Select(device => device.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList());
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class PlcDataTypeItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(PlcConstants.DataTypes.ToList());
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }
        }

        public class PlcDirectionItem : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(PlcConstants.Directions.ToList());
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
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
            List<string> Item()
            {
                List<string> gotoItem = new List<string>();
                if (SF.frmProc == null || SF.frmProc.procsList == null)
                {
                    return gotoItem;
                }
                int procIndex = SF.frmProc.SelectedProcNum;
                if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
                {
                    return gotoItem;
                }
                if (SF.frmProc.procsList[procIndex]?.steps == null)
                {
                    return gotoItem;
                }
                for (int i = 0; i < SF.frmProc.procsList[procIndex].steps.Count; i++)
                {
                    var ops = SF.frmProc.procsList[procIndex].steps[i]?.Ops;
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
                return new StandardValuesCollection(Item());
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
                if (SF.frmCard == null || SF.frmCard.dataStation == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return new StandardValuesCollection(SF.frmCard.dataStation.Select(Info => Info.Name).ToList());
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
                if (SF.frmCard == null || SF.frmCard.dataStation == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return new StandardValuesCollection(SF.frmCard.dataStation.Select(Info => Info.Name).ToList());
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
            List<string> Item()
            {
                List<string> posItems = new List<string>();
                if (SF.frmCard == null || SF.frmCard.dataStation == null)
                {
                    return posItems;
                }
                string stationName = null;
                if (SF.frmDataGrid?.OperationTemp is StationRunPos stationRunPos)
                {
                    stationName = stationRunPos.StationName;
                }
                else if (SF.frmDataGrid?.OperationTemp is CreateTray createTray)
                {
                    stationName = createTray.StationName;
                }
                else if (SF.frmDataGrid?.OperationTemp is ModifyStationPos modifyStationPos)
                {
                    stationName = modifyStationPos.StationName;
                }
                else if (SF.frmDataGrid?.OperationTemp is GetStationPos getStationPos)
                {
                    stationName = getStationPos.StationName;
                }
                if (string.IsNullOrEmpty(stationName))
                {
                    return posItems;
                }
                var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == stationName);

                if (station != null)
                {
                    int stationIndex = SF.frmCard.dataStation.IndexOf(station);
                    if (stationIndex != -1)
                    {
                        var posList = SF.frmCard.dataStation[stationIndex].ListDataPos;
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
                return new StandardValuesCollection(Item());
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
            List<string> Item()
            {
                List<string> posItems = new List<string> { "当前位置", "自定义坐标" };
                if (SF.frmCard == null || SF.frmCard.dataStation == null)
                {
                    return posItems;
                }
                string stationName = null;
                if (SF.frmDataGrid?.OperationTemp is ModifyStationPos modifyStationPos)
                {
                    stationName = modifyStationPos.StationName;
                }
                if (string.IsNullOrEmpty(stationName))
                {
                    return posItems;
                }
                var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == stationName);
                if (station != null)
                {
                    int stationIndex = SF.frmCard.dataStation.IndexOf(station);
                    if (stationIndex != -1)
                    {
                        var posList = SF.frmCard.dataStation[stationIndex].ListDataPos;
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
                return new StandardValuesCollection(Item());
            }
        }

        public class StationAixsItem : StringConverter
        {
            List<string> Item()
            {
                List<string> AxisItems = new List<string>();
                if (SF.frmCard == null || SF.frmCard.dataStation == null)
                {
                    return AxisItems;
                }
                if (!(SF.frmDataGrid?.OperationTemp is SetStationVel setStationVel) || string.IsNullOrEmpty(setStationVel.StationName))
                {
                    return AxisItems;
                }
                var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == setStationVel.StationName);

                if (station != null)
                {
                    int stationIndex = SF.frmCard.dataStation.IndexOf(station);
                    if (stationIndex != -1)
                    {
                        AxisItems.Add("工站");
                        var axisConfigs = SF.frmCard.dataStation[stationIndex].dataAxis?.axisConfigs;
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
                return new StandardValuesCollection(Item());
            }
        }
        #region
        //public class ExPropertyGrid : PropertyGrid
        //{
        //    protected override void OnHandleCreated(EventArgs e)
        //    {
        //        base.OnHandleCreated(e);
        //        var grid = this.Controls[2];
        //        grid.MouseClick += grid_MouseClick;
        //    }
        //    void grid_MouseClick(object sender, MouseEventArgs e)
        //    {
        //        var grid = this.Controls[2];
        //        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        //        var invalidPoint = new Point(-2147483648, -2147483648);
        //        var FindPosition = grid.GetType().GetMethod("FindPosition", flags);
        //        var p = (Point)FindPosition.Invoke(grid, new object[] { e.X, e.Y });
        //        GridItem entry = null;
        //        if (p != invalidPoint)
        //        {
        //            var GetGridEntryFromRow = grid.GetType()
        //                                          .GetMethod("GetGridEntryFromRow", flags);
        //            entry = (GridItem)GetGridEntryFromRow.Invoke(grid, new object[] { p.Y });
        //        }
        //        if (entry != null && entry.Value != null)
        //        {
        //            object parent;
        //            if (entry.Parent != null && entry.Parent.Value != null)
        //                parent = entry.Parent.Value;
        //            else
        //                parent = this.SelectedObject;
        //            if (entry.Value != null && entry.Value is bool)
        //            {
        //                entry.PropertyDescriptor.SetValue(parent, !(bool)entry.Value);
        //                this.Refresh();
        //            }
        //        }
        //    }
        //}
        //public class MyBoolEditor : UITypeEditor
        //{
        //    public override bool GetPaintValueSupported
        //        (System.ComponentModel.ITypeDescriptorContext context)
        //    { return true; }
        //    public override void PaintValue(PaintValueEventArgs e)
        //    {
        //        var rect = e.Bounds;
        //        rect.Inflate(1, 1);
        //        ControlPaint.DrawCheckBox(e.Graphics, rect, ButtonState.Flat |
        //            (((bool)e.Value) ? ButtonState.Checked : ButtonState.Normal));
        //    }
        //}

        //public class Model
        //{
        //    [Editor(typeof(MyBoolEditor), typeof(UITypeEditor))]
        //    public bool Property2 { get; set; }
        //}
        #endregion
    }
}
