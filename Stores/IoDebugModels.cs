using System.Collections.Generic;

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
