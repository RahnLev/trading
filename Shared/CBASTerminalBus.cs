using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using NinjaTrader.Gui;

namespace NinjaTrader.Custom.CBASTerminal
{
    public class CBASTerminalLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string InstanceId { get; set; }
        public string Instrument { get; set; }
        public string Message { get; set; }
        public string Level { get; set; } = "INFO"; // INFO/WARN/ERROR
        public int Bar { get; set; }
    }

    // Registry of live indicator instances with weak references
    public static class CBASTerminalRegistry
    {
        private static readonly ConcurrentDictionary<string, WeakReference<object>> instances =
            new ConcurrentDictionary<string, WeakReference<object>>();

        public static event Action RegistryChanged;

        public static void Register(string id, object indicator)
        {
            instances[id] = new WeakReference<object>(indicator);
            RegistryChanged?.Invoke();
        }

        public static void Unregister(string id)
        {
            instances.TryRemove(id, out _);
            RegistryChanged?.Invoke();
        }

        public static IReadOnlyList<string> GetActiveIds()
        {
            var dead = new List<string>();
            var ids = new List<string>();
            foreach (var kv in instances)
            {
                if (kv.Value.TryGetTarget(out var _))
                    ids.Add(kv.Key);
                else
                    dead.Add(kv.Key);
            }
            foreach (var d in dead) instances.TryRemove(d, out _);
            return ids;
        }

        public static bool TryGet(string id, out object indicator)
        {
            indicator = null;
            if (instances.TryGetValue(id, out var wr) && wr.TryGetTarget(out var obj))
            {
                indicator = obj;
                return true;
            }
            return false;
        }
    }

    // Lightweight pub/sub bus
    public static class CBASTerminalBus
    {
        public static event Action<CBASTerminalLogEntry> OnLog;
        public static void Publish(CBASTerminalLogEntry e)
        {
            // Thread-safe: fire event without blocking caller
            // Subscribers (UI) use queues to avoid cross-thread issues
            try { OnLog?.Invoke(e); } catch { /* swallow to not crash indicator */ }
        }

        // Command routing from terminal -> indicator
        public static event Action<string, string> OnCommand; // (instanceId, commandText)
        public static void SendCommand(string instanceId, string command)
        {
            try { OnCommand?.Invoke(instanceId, command); } catch { }
        }
    }

    // Simple command parser helpers
    public static class CBASTerminalCommands
    {
        // Example commands:
        // set Sensitivity 3.2
        // toggle EmaEnergy
        // set KeltnerLength 20
        // help
        public static (string verb, string[] args) Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return (null, Array.Empty<string>());
            var parts = input.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (null, Array.Empty<string>());
            var verb = parts[0].ToLowerInvariant();
            var args = parts.Skip(1).ToArray();
            return (verb, args);
        }
}
}
