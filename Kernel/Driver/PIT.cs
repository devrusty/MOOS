using MOOS.Driver;
using MOOS.Misc;

namespace MOOS
{
    public class PIT
    {
        public static void Initialise()
        {
            ushort timerCount = 1193182 / 1000;

            Native.Out8(0x43, 0x36);
            Native.Out8(0x40, (byte)(timerCount & 0xFF));
            Native.Out8(0x40, (byte)((timerCount & 0xFF00) >> 8));

            Interrupts.EnableInterrupt(0x20);
        }
    }
}