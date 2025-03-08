using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals;
using System.Threading;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.Time;
using System.Runtime.CompilerServices;
using Antmicro.Renode.Peripherals.Cutter.SPIDevices;
using Antmicro.Renode.Peripherals.UART;
using IronPython.Runtime;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Exceptions;

namespace Antmicro.Renode.Testing
{
    public static class RawUartTesterExtensions
	{
		public static void CreateRawUartTester(string name, IUART uart)
		{
			var tester = new RawUartTester(uart);
			EmulationManager.Instance.CurrentEmulation.ExternalsManager.AddExternal(tester, name);
		}
	}

	public class RawUartTester : IExternal
	{
        
        IUART uart;
        IMachine machine;

        List<byte> readBytes = new List<byte>();

        public RawUartTester(IUART uart)
        {
            uart.TryGetMachine(out machine);
            if(machine == null)
            {
                throw new ArgumentException("Could not find machine for UART");
            }
            this.uart = uart;
            uart.CharReceived += (b) => readBytes.Add(b);
        }

        public void WriteBytes(byte[] bytes)
        {  
            foreach(var b in bytes)
            {
                uart.WriteChar(b);
            }
        }

        public byte[] ReadBytesFor(uint timeoutMs)
        {
            var startTime = machine.ElapsedVirtualTime.TimeElapsed.TotalMilliseconds;
            
            //uart.CharReceived += (b) => startTime = machine.ElapsedVirtualTime.TimeElapsed.TotalMilliseconds;
          
            while(machine.ElapsedVirtualTime.TimeElapsed.TotalMilliseconds - startTime < timeoutMs)
            {
                ;
            }
            var result = readBytes.ToArray();
            readBytes.Clear();
            return result;
        }

        public byte[] ReadNumBytes(uint numBytes, uint timeoutMs=1000)
        {
            var startTime = machine.ElapsedVirtualTime.TimeElapsed.TotalMilliseconds;

            var masterTimeSource = EmulationManager.Instance.CurrentEmulation.MasterTimeSource;
            var timeoutEvent = masterTimeSource.EnqueueTimeoutEvent((uint)(30 * 1000));

            while((machine.ElapsedVirtualTime.TimeElapsed.TotalMilliseconds - startTime) < timeoutMs)
            {
                if(timeoutEvent.IsTriggered)
                {
                    throw new TimeoutException("ReadNumBytes Realtime timeout!");
                }
                
                if(readBytes.Count >= numBytes)
                {
                    break;
                }
            }

            var result = readBytes.ToArray();
            readBytes.Clear();
            return result;
        }

    }
}