using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Memory;

using Range = Antmicro.Renode.Core.Range;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Peripherals.SPI
{
    public abstract class BASE_EEPROM_SPI : ISPIPeripheral
    { 
        public BASE_EEPROM_SPI(byte[] memory, int pageSize = 0)
        {
            this.pageSize = pageSize;
            this.memory = memory;
            Reset();
        }

        public void Reset()
        {
            ResetTransmit();
        }
         
        void ResetTransmit()
        {
            selected = false;
            selectedPage = -1;
            currentCommand = Command.None;
            memoryWriteState = MemoryWriteState.Command;
            memoryReadState = MemoryReadState.Command;
            memoryWriteAddress = 0;
            memoryReadAddress = 0;
        } 

        //ISPIPeripheral
        public byte Transmit(byte data)
        {
            if(currentCommand==Command.None)
            {
                if(data == (byte)Command.Read || data == (byte)Command.Write || data == (byte)Command.WriteEnable || data == (byte)Command.WriteDisable || data == (byte)Command.ReadStatusRegister || data == (byte)Command.WriteStatusRegister)
                {
                    currentCommand = (Command)data;
                     this.NoisyLog("Parsing command: " + currentCommand);
                }
                else
                {
                    currentCommand = Command.None;
                    this.Log(LogLevel.Warning, "BASE_EEPROM_SPI: Invalid command");
                }
            }
            else
            {
                switch(currentCommand)
                {
                    case Command.Read:
                        return ReadFromMemory(data);
                        break;
                    case Command.Write:
                        WriteIntoMemory(data);
                        break;
                    case Command.WriteEnable:
                        return 0xFF;
                    case Command.WriteDisable:
                        return 0xFF;
                    case Command.ReadStatusRegister:
                        return 0;
                    case Command.WriteStatusRegister:
                        return 0xFF;
                }
            }

            return 0xFF;//high impedance
        }

        public void FinishTransmission()
        {
            ResetTransmit();
            this.NoisyLog("Memory deasserted");
        }

        void WriteIntoMemory(byte value)
        {
            switch (memoryWriteState)
            {
                case MemoryWriteState.Command:
                case MemoryWriteState.AddressLow:                  
                    memoryWriteAddress |= (ushort)(value << 8);
                    memoryWriteState = MemoryWriteState.AddressHigh;
                    break;
                case MemoryWriteState.AddressHigh:
                    memoryWriteAddress = value;
                    memoryWriteState = MemoryWriteState.Data;
                    this.NoisyLog("Write to memory at "+ memoryWriteAddress + " initiated");
                    break;
                case MemoryWriteState.Data:
                    if(SanityAddressCheck(memoryWriteAddress, true) == false)
                    {
                        return;
                    }
                    this.NoisyLog("Written: " + memory[memoryReadAddress]);
                    memory[memoryWriteAddress++] = value;
                    break;
            }
        }

        byte ReadFromMemory(byte value)
        {
            switch (memoryReadState)
            {
                case MemoryReadState.Command:
                case MemoryReadState.AddressLow:
                    memoryReadAddress |= (ushort)(value << 8);                   
                    memoryReadState = MemoryReadState.AddressHigh;
                    break;
                case MemoryReadState.AddressHigh:
                    memoryReadAddress = value;
                    memoryReadState = MemoryReadState.Data;
                    this.NoisyLog("Read from memory at "+ memoryReadAddress + " initiated");
                    break;
                case MemoryReadState.Data:
                    if(SanityAddressCheck(memoryReadAddress) == false)
                    {
                        return 0xFF;
                    }
                    this.NoisyLog("Read: " + memory[memoryReadAddress]);
                    return memory[memoryReadAddress++];
            }

            return 0xFF;
        }

        bool SanityAddressCheck(ushort address, bool isWrite = false)
        {
            if(address >= memory.Length)
            {
                this.Log(LogLevel.Error, "Address out of range: " + address.ToString("X"));
                return false;
            }

            if(pageSize == 0 || isWrite == false)
            {
                return true;
            }
            if(selectedPage == -1)
            {
                selectedPage = address / pageSize;
                return true;
            }

            if(selectedPage != address / pageSize)
            {
                this.Log(LogLevel.Error, "Address out of page: " + address.ToString("X"));
                return false;
            }

            return true;
        }

        public byte[] MemoryValue
        {
            get
            {
                return memory;
            }
        }

        private int selectedPage;
        private int pageSize;
        private byte[] memory;

        private Command currentCommand;
        private bool selected;

        enum MemoryWriteState
        {
            Command,
            AddressLow,
            AddressHigh,
            Data
        }
        MemoryWriteState memoryWriteState;
        ushort memoryWriteAddress;

        enum MemoryReadState
        {
            Command,
            AddressLow,
            AddressHigh,
            Data
        }
        MemoryReadState memoryReadState;
        ushort memoryReadAddress;

        enum Command{
            None = 0x00,
            Read = 0x03,
            Write = 0x02,
            WriteEnable = 0x06,
            WriteDisable = 0x04,
            ReadStatusRegister = 0x05,
            WriteStatusRegister = 0x01
        }

    }
}