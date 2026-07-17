using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Time;
using  Antmicro.Renode.Peripherals.Network;
namespace Antmicro.Renode.Peripherals.Wireless
{
    public static class UwbMediumExtensions
    {
        public static void CreateUwbMedium(this Emulation emulation, string name)
        {
            emulation.ExternalsManager.AddExternal(new UwbMedium(), name);
        }
    }

    public sealed class UwbMedium : IHasChildren<IMediumFunction>, IExternal, IConnectable<IRadio>, INetworkLogWireless, INetworkInterface 
    {
        public UwbMedium()
        {
            radioHooks = new Dictionary<IRadio, Action<byte[]>>();
            radios = new Dictionary<IRadio, Position>();
            mediumFunction = SimpleMediumFunction.Instance;
            additionalDelayNanoseconds = 0m;
            propagationSpeedMetersPerSecond = SpeedOfLightMetersPerSecond;
        }

        public void AttachTo(IRadio radio)
        {
            if(radios.ContainsKey(radio))
            {
                throw new RecoverableException("Cannot attach to the provided radio as it is already registered in this UWB medium.");
            }

            radios.Add(radio, new Position());
            radio.FrameSent += FrameSentHandler;
        }

        public void DetachFrom(IRadio radio)
        {
            radios.Remove(radio);
            radioHooks.Remove(radio);
            radio.FrameSent -= FrameSentHandler;
        }

        public void SetMediumFunction(IMediumFunction function)
        {
            mediumFunction = function ?? SimpleMediumFunction.Instance;
        }

        public void SetPosition(IRadio radio, decimal x, decimal y, decimal z)
        {
            if(!radios.ContainsKey(radio))
            {
                EmulationManager.Instance.CurrentEmulation.TryGetEmulationElementName(radio, out string name);
                this.Log(LogLevel.Error, "Cannot set position for {0} as it is not registered in this UWB medium.", name);
                return;
            }

            radios[radio] = new Position(x, y, z);
        }

        public void SetPropagationSpeedMetersPerSecond(decimal value)
        {
            if(value <= 0)
            {
                throw new RecoverableException("Propagation speed must be greater than zero.");
            }

            propagationSpeedMetersPerSecond = value;
        }

        public void SetAdditionalDelayNanoseconds(decimal value)
        {
            if(value < 0)
            {
                throw new RecoverableException("Additional delay cannot be negative.");
            }

            additionalDelayNanoseconds = value;
        }

        public decimal GetDistanceMeters(IRadio sender, IRadio receiver)
        {
            if(!radios.TryGetValue(sender, out var senderPosition) || !radios.TryGetValue(receiver, out var receiverPosition))
            {
                return 0m;
            }

            return CalculateDistanceMeters(senderPosition, receiverPosition);
        }

        public IEnumerable<string> GetNames()
        {
            return new[] { mediumFunction.FunctionName };
        }

        public IEnumerable<string> GetAttachedRadiosNames()
        {
            var result = new List<string>();
            var currentEmulation = EmulationManager.Instance.CurrentEmulation;
            foreach(var radio in radios)
            {
                currentEmulation.TryGetEmulationElementName(radio.Key, out var name);
                result.Add(name);
            }
            return result;
        }

        public void AttachHookToRadio(IRadio radio, Action<byte[]> hook)
        {
            radioHooks[radio] = hook;
        }

        public IMediumFunction TryGetByName(string name, out bool success)
        {
            if(mediumFunction.FunctionName == name)
            {
                success = true;
                return mediumFunction;
            }

            success = false;
            return null;
        }

        public event Action<IExternal, IRadio, IRadio, byte[]> FrameTransmitted;

        public event Action<IExternal, IRadio, byte[]> FrameProcessed;

        private void FrameSentHandler(IRadio sender, byte[] packet)
        {
            var senderPosition = radios[sender];
            var currentEmulation = EmulationManager.Instance.CurrentEmulation;
            currentEmulation.TryGetEmulationElementName(sender, out var senderName);

            FrameProcessed?.Invoke(this, sender, packet);

            if(!mediumFunction.CanTransmit(senderPosition))
            {
                this.NoisyLog("Packet from {0} can't be transmitted, size {1}.", senderName, packet.Length);
                return;
            }

            var transmitTime = TimeDomainsManager.Instance.VirtualTimeStamp;
            var setTransmitTime = sender.GetType().GetMethod("SetTransmitTimeFromMedium");
            if(setTransmitTime != null)
            {
                // This timestamp is authoritative for the over-the-air event.
                // DW1000.cs and UwbMedium.cs are compiled by Renode as separate
                // script assemblies, so a concrete DW1000 cast cannot be used.
                setTransmitTime.Invoke(sender, new object[] { transmitTime });
            }

            ulong? dwTransmitTimestamp = null;
            var getTransmitTimestamp = sender.GetType().GetMethod("GetTransmitTimestampForMedium");
            if(getTransmitTimestamp != null)
            {
                dwTransmitTimestamp = (ulong)getTransmitTimestamp.Invoke(sender, null);
            }
            else
            {
                this.WarningLog("Radio type {0} does not expose SetTransmitTimeFromMedium; check which DW1000.cs Renode loaded.", sender.GetType().AssemblyQualifiedName);
            }

            foreach(var radioAndPosition in radios.Where(x => x.Key != sender))
            {
                var receiver = radioAndPosition.Key;
                currentEmulation.TryGetEmulationElementName(receiver, out var receiverName);

                if(!mediumFunction.CanReach(senderPosition, radioAndPosition.Value) || receiver.Channel != sender.Channel)
                {
                    this.NoisyLog("Packet {0}:chan{1} -> {2}:chan{3} NOT delivered, size {4}.", senderName, sender.Channel, receiverName, receiver.Channel, packet.Length);
                    continue;
                }

                var deliveryTime = ApplyPropagationDelay(transmitTime, senderPosition, radioAndPosition.Value);
                var distanceMeters = CalculateDistanceMeters(senderPosition, radioAndPosition.Value);
                var propagationNanoseconds = CalculatePropagationDelayNanoseconds(senderPosition, radioAndPosition.Value);

                var packetCopy = packet.ToArray();
                if(radioHooks.TryGetValue(receiver, out var hook))
                {
                    hook(packetCopy);
                }

                Action<byte[], IRadio> receiveAction = receiver.ReceiveFrame;
                var receiveAtDwTimestamp = receiver.GetType().GetMethod("ReceiveFrameAtDwTimestamp");
                if(receiveAtDwTimestamp != null && dwTransmitTimestamp.HasValue)
                {
                    var propagationTicks = (ulong)Math.Round(propagationNanoseconds * DwTicksPerNanosecond);
                    var receiveTimestamp = (dwTransmitTimestamp.Value + propagationTicks) & DwTimeMask;
                    receiveAction = (receivedPacket, frameSender) => receiveAtDwTimestamp.Invoke(receiver, new object[] { receivedPacket, frameSender, receiveTimestamp });
                }
                else
                {
                    var receiveAtMediumTime = receiver.GetType().GetMethod("ReceiveFrameAtMediumTime");
                    if(receiveAtMediumTime != null)
                    {
                        receiveAction = (receivedPacket, frameSender) => receiveAtMediumTime.Invoke(receiver, new object[] { receivedPacket, frameSender, deliveryTime });
                    }
                }

                receiver.GetMachine().HandleTimeDomainEvent(receiveAction, packetCopy, sender, deliveryTime, () =>
                {
                    this.NoisyLog("Packet {0}:chan{1} -> {2}:chan{3} delivered, size {4}, distance {5:F3}m, at {6}.", senderName, sender.Channel, receiverName, receiver.Channel, packet.Length, distanceMeters, deliveryTime.TimeElapsed);
                    FrameTransmitted?.Invoke(this, sender, receiver, packetCopy);
                });
            }
        }

        private TimeStamp ApplyPropagationDelay(TimeStamp currentTime, Position senderPosition, Position receiverPosition)
        {
            var distanceMeters = CalculateDistanceMeters(senderPosition, receiverPosition);
            var propagationDelayNanoseconds = CalculatePropagationDelayNanoseconds(senderPosition, receiverPosition);
            var roundedNanoseconds = propagationDelayNanoseconds <= 0 ? 0UL : (ulong)Math.Ceiling((double)propagationDelayNanoseconds);
            this.NoisyLog("Applying propagation delay of {0}ns for distance {1:F3}m.", roundedNanoseconds, distanceMeters);
            return new TimeStamp(currentTime.TimeElapsed + TimeInterval.FromNanoseconds(roundedNanoseconds), currentTime.Domain);
        }

        private decimal CalculatePropagationDelayNanoseconds(Position senderPosition, Position receiverPosition)
        {
            var distanceMeters = CalculateDistanceMeters(senderPosition, receiverPosition);
            return additionalDelayNanoseconds + (distanceMeters <= 0 ? 0 : distanceMeters / propagationSpeedMetersPerSecond * NanosecondsPerSecond);
        }

        private static decimal CalculateDistanceMeters(Position senderPosition, Position receiverPosition)
        {
            var dx = (double)(senderPosition.X - receiverPosition.X);
            var dy = (double)(senderPosition.Y - receiverPosition.Y);
            var dz = (double)(senderPosition.Z - receiverPosition.Z);
            return (decimal)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private IMediumFunction mediumFunction;
        private readonly Dictionary<IRadio, Action<byte[]>> radioHooks;
        private readonly Dictionary<IRadio, Position> radios;
        private decimal propagationSpeedMetersPerSecond;
        private decimal additionalDelayNanoseconds;

        private const decimal SpeedOfLightMetersPerSecond = 299792458m;
        private const decimal NanosecondsPerSecond = 1000000000m;
        private const decimal DwTicksPerNanosecond = 63.8976m;
        private const ulong DwTimeMask = 0xFFFFFFFFFFul;
    }
}
