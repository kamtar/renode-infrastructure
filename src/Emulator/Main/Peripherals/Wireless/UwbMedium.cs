//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Time;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Wireless
{
    public static class UwbMediumExtensions
    {
        public static void CreateUwbMedium(this Emulation emulation, string name)
        {
            emulation.ExternalsManager.AddExternal(new UwbMedium(), name);
        }
    }

    public sealed class UwbMedium : IHasChildren<IMediumFunction>, IExternal, IConnectable<IRadio>, INetworkLogWireless
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

            foreach(var radioAndPosition in radios.Where(x => x.Key != sender))
            {
                var receiver = radioAndPosition.Key;
                currentEmulation.TryGetEmulationElementName(receiver, out var receiverName);

                if(!mediumFunction.CanReach(senderPosition, radioAndPosition.Value) || receiver.Channel != sender.Channel)
                {
                    this.NoisyLog("Packet {0}:chan{1} -> {2}:chan{3} NOT delivered, size {4}.", senderName, sender.Channel, receiverName, receiver.Channel, packet.Length);
                    continue;
                }

                var deliveryTime = ApplyPropagationDelay(TimeDomainsManager.Instance.GetEffectiveVirtualTimeStamp(), senderPosition, radioAndPosition.Value);
                var distanceMeters = CalculateDistanceMeters(senderPosition, radioAndPosition.Value);

                var packetCopy = packet.ToArray();
                if(radioHooks.TryGetValue(receiver, out var hook))
                {
                    hook(packetCopy);
                }

                receiver.GetMachine().HandleTimeDomainEvent(receiver.ReceiveFrame, packetCopy, sender, deliveryTime, () =>
                {
                    this.NoisyLog("Packet {0}:chan{1} -> {2}:chan{3} delivered, size {4}, distance {5:F3}m, at {6}.", senderName, sender.Channel, receiverName, receiver.Channel, packet.Length, distanceMeters, deliveryTime.TimeElapsed);
                    FrameTransmitted?.Invoke(this, sender, receiver, packetCopy);
                });
            }
        }

        private TimeStamp ApplyPropagationDelay(TimeStamp currentTime, Position senderPosition, Position receiverPosition)
        {
            var distanceMeters = CalculateDistanceMeters(senderPosition, receiverPosition);
            var propagationDelayNanoseconds = additionalDelayNanoseconds;
            if(distanceMeters > 0)
            {
                propagationDelayNanoseconds += distanceMeters / propagationSpeedMetersPerSecond * NanosecondsPerSecond;
            }

            var roundedNanoseconds = propagationDelayNanoseconds <= 0 ? 0UL : (ulong)Math.Ceiling((double)propagationDelayNanoseconds);
            this.NoisyLog("Applying propagation delay of {0}ns for distance {1:F3}m.", roundedNanoseconds, distanceMeters);
            return new TimeStamp(currentTime.TimeElapsed + TimeInterval.FromNanoseconds(roundedNanoseconds), currentTime.Domain);
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
    }
}