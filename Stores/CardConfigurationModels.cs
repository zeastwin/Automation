using System.Collections.Generic;
using System.ComponentModel;

namespace Automation
{
    public sealed class Card
    {
        public List<ControlCard> controlCards = new List<ControlCard>();
    }

    public sealed class ControlCard
    {
        public CardHead cardHead = new CardHead();
        public List<Axis> axis = new List<Axis>();
    }

    public sealed class CardHead
    {
        [DisplayName("轴数量"), Category("卡参数"), Description(""), ReadOnly(false)]
        public int AxisCount { get; set; }

        [DisplayName("输入IO数量"), Category("卡参数"), Description(""), ReadOnly(false)]
        public int InputCount { get; set; }

        [DisplayName("输出IO数量"), Category("卡参数"), Description(""), ReadOnly(false)]
        public int OutputCount { get; set; }

        [DisplayName("卡类型"), Category("卡参数"), Description(""), ReadOnly(false)]
        public string CardType { get; set; }
    }

    public sealed class Axis
    {
        [DisplayName("轴名称"), Category("A基本参数"), Description(""), ReadOnly(false)]
        public string AxisName { get; set; }

        [DisplayName("轴号"), Category("A基本参数"), Description(""), ReadOnly(true)]
        public int AxisNum { get; set; }

        [DisplayName("单位毫米脉冲"), Category("A基本参数"), Description(""), ReadOnly(false)]
        public int PulseToMM { get; set; }

        [DisplayName("回原搜索方向"), Category("A基本参数"), Description("控制卡搜索原点的方向；回原模式固定为一次回零加回找。"), ReadOnly(false), TypeConverter(typeof(HomeDirectionItem))]
        public string HomeDirection { get; set; }

        [DisplayName("回原速度"), Category("B回原参数"), Description(""), ReadOnly(false)]
        public string HomeSpeed { get; set; }

        [DisplayName("速度说明"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public int SpeedInfo { get; set; }

        [DisplayName("最大速度"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public int SpeedMax { get; set; }

        [DisplayName("加速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public double AccMax { get; set; }

        [DisplayName("减速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public double DecMax { get; set; }
    }
}
