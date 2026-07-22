using System.Collections.Generic;

// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

namespace Automation
{
    public class IODebugMap
    {
        public List<IO> inputs = new List<IO>();
        public List<IO> outputs = new List<IO>();
        public List<IOConnect> iOConnects = new List<IOConnect>();
        public List<IOConnect> iOConnects2 = new List<IOConnect>();
        public List<IOConnect> iOConnects3 = new List<IOConnect>();
    }

    public class IOConnect
    {
        public IO Output = new IO();
        public IO Output2 = new IO();
        public IO Intput1 = new IO();
        public IO Intput2 = new IO();
    }
}
