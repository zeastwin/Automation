using System.Collections.Generic;
// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

using System.ComponentModel;

namespace Automation
{
    public enum AxisEncoderType
    {
        Incremental = 0,
        Absolute = 1
    }

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
        public const string LeiSaiBusCardType = "雷赛总线卡";

        [DisplayName("轴数量"), Category("卡参数"), Description(""), ReadOnly(false)]
        public int AxisCount { get; set; }

        [DisplayName("输入IO数量"), Category("卡参数"), Description(""), ReadOnly(false)]
        public int InputCount { get; set; }

        [DisplayName("输出IO数量"), Category("卡参数"), Description(""), ReadOnly(false)]
        public int OutputCount { get; set; }

        [DisplayName("卡类型"), Category("卡参数"), Description("当前平台固定使用雷赛总线卡。"), ReadOnly(true)]
        public string CardType { get; set; } = LeiSaiBusCardType;
    }

    public sealed class Axis
    {
        [DisplayName("轴名称"), Category("A基本参数"), Description(""), ReadOnly(false)]
        public string AxisName { get; set; }

        [DisplayName("轴号"), Category("A基本参数"), Description(""), ReadOnly(true)]
        public int AxisNum { get; set; }

        [DisplayName("单位毫米脉冲"), Category("A基本参数"), Description(""), ReadOnly(false)]
        public int PulseToMM { get; set; } = 1000;

        [DisplayName("总线回原方法"), Category("B回原参数"), Description("大于0时使用该雷赛总线回原方法；小于等于0时沿用card_0.ini中的回原方法。"), ReadOnly(false)]
        public int HomeMethod { get; set; } = -1;

        [DisplayName("编码器类型"), Category("A基本参数"), Description("增量式编码器或绝对值编码器。"), ReadOnly(false)]
        public AxisEncoderType EncoderType { get; set; } = AxisEncoderType.Incremental;

        [DisplayName("负软限位"), Category("C运动参数"), Description("与正软限位同时为0时沿用card_0.ini；否则必须小于正软限位。"), ReadOnly(false)]
        public double NegativeSoftLimit { get; set; }

        [DisplayName("正软限位"), Category("C运动参数"), Description("与负软限位同时为0时沿用card_0.ini；否则必须大于负软限位。"), ReadOnly(false)]
        public double PositiveSoftLimit { get; set; }

        [DisplayName("回原速度"), Category("B回原参数"), Description(""), ReadOnly(false)]
        public string HomeSpeed { get; set; } = "10";

        [DisplayName("速度说明"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public int SpeedInfo { get; set; }

        [DisplayName("最大速度"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public int SpeedMax { get; set; } = 20;

        [DisplayName("加速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public double AccMax { get; set; } = 40;

        [DisplayName("减速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
        public double DecMax { get; set; } = 40;
    }
}
