using System;
using System.Threading;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Analog;
using Antmicro.Renode.Time;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Cutter
{
    public abstract class BaseAnalogSensor : IPeripheral
    {
        protected BaseAnalogSensor(IMachine machine)
        {
            this.machine = machine;
        }

        public abstract uint GetAnalogValue();

        public virtual void Reset()
        {
        }

        protected readonly IMachine machine;
    }
}