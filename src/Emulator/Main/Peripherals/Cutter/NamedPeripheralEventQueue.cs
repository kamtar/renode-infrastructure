using System;
using System.Collections.Generic;
using System.Globalization;

namespace Antmicro.Renode.Peripherals.Cutter.Peripherals
{
    public class NamedPeripheralEventQueue
    {
        public event Action<string> EventQueued;

        public NamedPeripheralEventQueue(Func<string> timestampProvider = null)
        {
            sync = new object();
            queue = new Queue<string>();
            watchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            watchAll = true;
            this.timestampProvider = timestampProvider;
        }

        public void WatchAll()
        {
            lock(sync)
            {
                watchAll = true;
                watchedKeys.Clear();
            }
        }

        public void ClearWatched()
        {
            lock(sync)
            {
                watchAll = false;
                watchedKeys.Clear();
            }
        }

        public void WatchKey(string key)
        {
            if(string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock(sync)
            {
                watchAll = false;
                watchedKeys.Add(key.Trim());
            }
        }

        public void UnwatchKey(string key)
        {
            if(string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock(sync)
            {
                watchedKeys.Remove(key.Trim());
            }
        }

        public void Enqueue(string key, string eventName, string data)
        {
            if(!IsWatched(key))
            {
                return;
            }

            var safeKey = Escape(key);
            var safeEvent = Escape(eventName ?? string.Empty);
            var safeData = Escape(data ?? string.Empty);
            var safeTimestamp = Escape(GetTimestamp());
            var payload = string.Format("{0}|{1}|{2}|{3}", safeKey, safeEvent, safeData, safeTimestamp);

            lock(sync)
            {
                while(queue.Count >= MaxQueueDepth)
                {
                    queue.Dequeue();
                }
                queue.Enqueue(payload);
            }

            var callback = EventQueued;
            if(callback != null)
            {
                callback(payload);
            }
        }

        public string Dequeue()
        {
            lock(sync)
            {
                if(queue.Count == 0)
                {
                    return string.Empty;
                }
                return queue.Dequeue();
            }
        }

        private bool IsWatched(string key)
        {
            if(string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock(sync)
            {
                if(watchAll)
                {
                    return true;
                }
                return watchedKeys.Contains(key.Trim());
            }
        }

        public static string ResolveVirtualTimestamp(object machine)
        {
            if(machine == null)
            {
                return string.Empty;
            }

            try
            {
                var localTimeSourceProperty = machine.GetType().GetProperty("LocalTimeSource");
                var localTimeSource = localTimeSourceProperty != null ? localTimeSourceProperty.GetValue(machine) : null;
                if(localTimeSource == null)
                {
                    return string.Empty;
                }

                var elapsedProperty = localTimeSource.GetType().GetProperty("ElapsedVirtualTime");
                var elapsedVirtualTime = elapsedProperty != null ? elapsedProperty.GetValue(localTimeSource) : null;
                if(elapsedVirtualTime == null)
                {
                    return string.Empty;
                }

                var totalSecondsProperty = elapsedVirtualTime.GetType().GetProperty("TotalSeconds");
                if(totalSecondsProperty != null)
                {
                    var value = totalSecondsProperty.GetValue(elapsedVirtualTime);
                    if(value != null)
                    {
                        var seconds = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        return seconds.ToString("0.000000000", CultureInfo.InvariantCulture);
                    }
                }

                var toTimeSpanMethod = elapsedVirtualTime.GetType().GetMethod("ToTimeSpan", Type.EmptyTypes);
                if(toTimeSpanMethod != null)
                {
                    var timeSpan = toTimeSpanMethod.Invoke(elapsedVirtualTime, null);
                    if(timeSpan != null)
                    {
                        var totalSecondsPropertyOnTimeSpan = timeSpan.GetType().GetProperty("TotalSeconds");
                        if(totalSecondsPropertyOnTimeSpan != null)
                        {
                            var value = totalSecondsPropertyOnTimeSpan.GetValue(timeSpan);
                            if(value != null)
                            {
                                var seconds = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                                return seconds.ToString("0.000000000", CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }
            }
            catch(Exception)
            {
            }

            return string.Empty;
        }

        private string GetTimestamp()
        {
            if(timestampProvider == null)
            {
                return string.Empty;
            }

            try
            {
                return timestampProvider() ?? string.Empty;
            }
            catch(Exception)
            {
                return string.Empty;
            }
        }

        private static string Escape(string input)
        {
            return input.Replace("\\", "\\\\").Replace("|", "\\|");
        }

        private readonly object sync;
        private readonly Queue<string> queue;
        private readonly HashSet<string> watchedKeys;
        private readonly Func<string> timestampProvider;
        private bool watchAll;
        private const int MaxQueueDepth = 20000;
    }
}
