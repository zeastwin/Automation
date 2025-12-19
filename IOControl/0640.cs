using csLTDMC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automation.IOControl
{
    public class IOC0640
    {
        public bool SetIO(IO io, bool isOpen)
        {
            try
            {
                ushort b = (ushort)(isOpen == true ? 0 : 1);
                LTDMC.dmc_write_outbit((ushort)io.CardNum, ushort.Parse(io.IOIndex), b);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        public bool GetOutIO(IO io, ref bool value)
        {
            try
            {
                value = LTDMC.dmc_read_outbit((ushort)io.CardNum, ushort.Parse(io.IOIndex)) == 1 ? false : true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }
        public bool GetInIO(IO io, ref bool value)
        {
            try
            {
                value = LTDMC.dmc_read_inbit((ushort)io.CardNum, ushort.Parse(io.IOIndex)) == 1 ? false : true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

    }
}
