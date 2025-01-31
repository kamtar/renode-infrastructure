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
    public static class SPIMemoryTesterExtensions
	{
		public static void CreateSPIMemoryTester( string name, BaseMemorySpi spiMemory)
		{
			var tester = new SPIMemoryTester(spiMemory);
			EmulationManager.Instance.CurrentEmulation.ExternalsManager.AddExternal(tester, name);
		}
	}

	public class SPIMemoryTester : IExternal
	{
		public SPIMemoryTester(BaseMemorySpi spiMemory)
		{
			this.spiMemory = spiMemory;
		}
		public byte[] Read(int address, int count)
		{
			var result = new byte[count];
			for(var i = 0; i < count; i++)
			{
				if((address + i) >= spiMemory.MemoryValue.Length)
				{
					throw new InvalidOperationException("Address out of range");
				}

				result[i] = spiMemory.MemoryValue[address + i];
			}
			return result;
		}

		public bool CompareMemory(int address, byte[] data)
		{
			if((address + data.Length) > spiMemory.MemoryValue.Length)
			{
				return false;
			}

			for(var i = 0; i < data.Length; i++)
			{
				if(spiMemory.MemoryValue[address + i] != data[i])
				{
					return false;
				}
			}
			return true;
		}

		BaseMemorySpi spiMemory;
	}

}