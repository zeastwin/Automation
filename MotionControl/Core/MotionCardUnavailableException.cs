using System;

namespace Automation.MotionControl
{
    /// <summary>
    /// 表示在尚未接触到物理运动卡时，原生 SDK 不可用或明确未检测到板卡。
    /// </summary>
    public sealed class MotionCardUnavailableException : InvalidOperationException
    {
        public MotionCardUnavailableException(string message)
            : base(message)
        {
        }

        public MotionCardUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
