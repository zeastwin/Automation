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
                return new StandardValuesCollection( SF.frmAlarmConfig.alarmInfos.Where(sc => !string.IsNullOrEmpty(sc.Name)).Select(sc => sc.Index).ToList());
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
        public class GotoItem : StringConverter
        {
            List<string> Item()
            {
                List<string> gotoItem = new List<string>();
                for (int i = 0; i < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count; i++)
                {
                    for (int j = 0; j < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[i].Ops.Count; j++)
                    {
                        gotoItem.Add($"{SF.frmProc.SelectedProcNum}-{i}-{j}");
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
                var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == ((StationRunPos)SF.frmDataGrid.OperationTemp).StationName);

                if (station != null)
                {
                    int stationIndex = SF.frmCard.dataStation.IndexOf(station);
                    if (stationIndex != -1)
                    {
                        posItems =  SF.frmCard.dataStation[stationIndex].ListDataPos
                            .Where(info => !string.IsNullOrEmpty(info.Name))
                            .Select(info => info.Name)
                            .ToList();
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
                var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == ((SetStationVel)SF.frmDataGrid.OperationTemp).StationName);

                if (station != null)
                {
                    int stationIndex = SF.frmCard.dataStation.IndexOf(station);
                    if (stationIndex != -1)
                    {
                        AxisItems.Add("工站");
                        for (int j = 0; j < SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs.Count; j++)
                        {
                            ushort index = (ushort)j;
                            if (SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                            {
                                AxisItems.Add(SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName);
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
