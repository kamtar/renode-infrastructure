using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Memory;
using System.Collections.Generic;
using Range = Antmicro.Renode.Core.Range;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Cutter.SPIDevices
{
    /// <summary>
    /// Provides basic functionality for spi memory
    /// support commands:
    /// 0x03 - read
    /// 0x02 - write
    /// 0x06 - write enable
    /// 0x04 - write disable
    /// 0x05 - read status register - returns 0
    /// 0x01 - write status register - no effect
    /// 0x9F - read JEDEC ID
    /// 0x4B - read unique ID 
    /// register custom commands via RegisterCustomCommandHandler <see cref="RegisterCustomCommandHandler"/>
    /// </summary>
    public abstract class BaseMemorySpi : ISPIPeripheral
    { 
       
        public BaseMemorySpi(byte[] memory, int pageSize = 0, byte manufactuerId = 0, ushort uniqueId = 0) 
        {
            this.pageSize = pageSize;
            this.memory = memory;
            writeEnable = true;
            this.manufacturerId = manufactuerId;
            this.uniqueId = uniqueId;
            Reset();
        }

        /// <summary>
        /// Registers custom command handler for given command.
        /// <param name="command">Command to register handler for.</param>
        /// <param name="handler">byte f(byte command, byte data_in) handler</param>
        /// </summary>
        public void RegisterCustomCommandHandler (byte command, Func<byte,byte, byte> handler)
        {
            customCommandHandlers[command] = handler;
        }

        public void Reset()
        {
            ResetTransmit();
        }
         
        void ResetTransmit()
        {
            selected = false;
            customHandled = false;
            selectedPage = -1;
            currentCommand = Command.None;
            memoryWriteState = MemoryWriteState.Command;
            memoryReadState = MemoryReadState.Command;
            memoryWriteAddress = 0;
            memoryReadAddress = 0;
            dataCounter = 0;
        } 

        //ISPIPeripheral
        public byte Transmit(byte data)
        {
            dataCounter++;
            if(currentCommand == Command.None && customCommandHandlers.ContainsKey(data) || customHandled)
            {
                if(customHandled == false)
                {
                    customHandledValue = data;
                    customHandled = true;
                }
               
                return customCommandHandlers[customHandledValue](customHandledValue, data);
            }

            if(currentCommand==Command.None)
            {
                if(Enum.IsDefined(typeof(Command), data))
                {
                    currentCommand = (Command)data;
                     this.NoisyLog("Parsing command: " + currentCommand);
                }
                else
                {
                    currentCommand = Command.None;
                    this.Log(LogLevel.Warning, "Invalid command value: 0x" + data.ToString("X"));
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
                        writeEnable = true;
                        return 0xFF;
                    case Command.WriteDisable:
                        writeEnable = false;
                        return 0xFF;
                    case Command.ReadStatusRegister:
                        return 0;
                    case Command.WriteStatusRegister:
                        return 0xFF;
                    case Command.ReadJEDECID:
                        if(dataCounter == 2)
                        {
                            return (byte)manufacturerId;
                        }
                        else if(dataCounter == 3)
                        {
                            return (byte)(uniqueId >> 8);
                        }
                        else if(dataCounter == 4)
                        {
                            return (byte)(uniqueId & 0xFF);
                        }
                        break;
                    case Command.ReadUniqueID:
                        if(dataCounter == 2)
                        {
                            return (byte)(uniqueId >> 8);
                        }
                        else if(dataCounter == 3)
                        {
                            return (byte)(uniqueId & 0xFF);
                        }
                        break;
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
            if(writeEnable == false)
            {
                this.Log(LogLevel.Error, "Write command received while write is disabled");
                return;
            }

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

        private byte manufacturerId;
        private ushort uniqueId;

        private int selectedPage;
        private int pageSize;
        private byte[] memory;
        private bool writeEnable;

        private Command currentCommand;
        private bool selected;
        private bool customHandled;
        private byte customHandledValue;
        private int dataCounter;

        Dictionary<byte, Func<byte, byte, byte>> customCommandHandlers = new Dictionary<byte, Func<byte, byte, byte>>();

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

        enum Command : byte
        {
            None = 0x00,
            Read = 0x03,
            Write = 0x02,
            WriteEnable = 0x06,
            WriteDisable = 0x04,
            ReadStatusRegister = 0x05,
            WriteStatusRegister = 0x01,
            ReadJEDECID = 0x9F,
            ReadUniqueID = 0x4B,
        }

    }
}