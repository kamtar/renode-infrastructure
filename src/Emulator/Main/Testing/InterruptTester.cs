using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals;
using System.Threading;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.Time;
using System.Runtime.CompilerServices;
using Antmicro.Renode.Peripherals.Cutter.SPIDevices;

namespace Antmicro.Renode.Testing
{
    public static class InterruptTesterExtensions
	{
		public static void CreateInterruptTester( string name, IPeripheral periph, string interruptName = "IRQ")
		{
			var tester = new InterruptTester(periph, interruptName);
			EmulationManager.Instance.CurrentEmulation.ExternalsManager.AddExternal(tester, name);
		}
	}

	public class InterruptTester : IExternal
	{
		public InterruptTester(IPeripheral periph, string interruptName)
		{
			this.periph = periph;
            this.interruptName = interruptName;
		}

        public bool SetInterrupt(bool level)
        {
            if (periph.HasGPIO() == false)
            {
                return false;
            }

            foreach (var gpio in periph.GetGPIOs())
            {
                if(gpio.Item1 == interruptName)
                {
                    gpio.Item2.Set(level);
                     return true;
                }
            }
            return false;
        }

        public bool GenerateInterruptPulse(bool level, uint us)
        {
            if (periph.HasGPIO() == false)
            {
                return false;
            }

            foreach (var gpio in periph.GetGPIOs())
            {
                if(gpio.Item1 == interruptName)
                {
                    gpio.Item2.Set(level);
                    periph.GetMachine().ScheduleAction(TimeInterval.FromMicroseconds(us), _ => gpio.Item2.Set(!level));
                    return true;
                }
            }
            return false;
        }


		IPeripheral periph;
        string interruptName;
	}

}