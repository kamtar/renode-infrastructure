using System;
using System.Collections.Generic;

namespace Antmicro.Renode.Peripherals.Cutter.Peripherals
{
    public class NamedPeripheralEventQueue
    {
        public event Action<string> EventQueued;

        public NamedPeripheralEventQueue()
        {
            sync = new object();
            queue = new Queue<string>();
            watchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            watchAll = true;
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
            var payload = string.Format("{0}|{1}|{2}", safeKey, safeEvent, safeData);

            lock(sync)
            {
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

        private static string Escape(string input)
        {
            return input.Replace("\\", "\\\\").Replace("|", "\\|");
        }

        private readonly object sync;
        private readonly Queue<string> queue;
        private readonly HashSet<string> watchedKeys;
        private bool watchAll;
    }
}
