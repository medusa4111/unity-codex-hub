using System;
using System.Collections.Generic;
using Codex.UnityBridge.Protocol;
using UnityEngine;

namespace Codex.UnityBridge.Commands
{
    internal sealed class UnityConsoleBuffer : IDisposable
    {
        private const int MaximumEntries = 2000;
        private readonly object sync = new object();
        private readonly Queue<ConsoleEntry> entries = new Queue<ConsoleEntry>();
        private long sequence;
        private bool disposed;

        public UnityConsoleBuffer()
        {
            Application.logMessageReceivedThreaded += OnLogMessage;
        }

        public IDictionary<string, object> Read(IDictionary<string, object> parameters)
        {
            bool legacyErrorsOnly = CommandArguments.OptionalBool(parameters, "errorsOnly", false);
            IList<object> severityValues = CommandArguments.OptionalArray(parameters, "severities");
            HashSet<string> severities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (legacyErrorsOnly) severities.Add("Error");
            else if (severityValues.Count == 0)
            {
                severities.Add("Error"); severities.Add("Warning"); severities.Add("Log");
            }
            else
            {
                foreach (object value in severityValues)
                {
                    string severity = value as string;
                    if (severity != "Error" && severity != "Warning" && severity != "Log")
                        throw new ProtocolException("INVALID_ARGUMENT", "Unknown Console severity.");
                    severities.Add(severity);
                }
            }
            string search = CommandArguments.OptionalString(parameters, "search");
            long since = CommandArguments.OptionalLong(parameters, "sinceSequence", 0);
            int maxResults = CommandArguments.OptionalInt(parameters, "maxResults", 200);
            bool includeStack = CommandArguments.OptionalBool(parameters, "includeStackTrace", true);
            if (maxResults < 1 || maxResults > 1000)
                throw new ProtocolException("INVALID_ARGUMENT", "maxResults must be between 1 and 1000.");

            List<object> messages = new List<object>();
            long latest;
            lock (sync)
            {
                latest = sequence;
                foreach (ConsoleEntry entry in entries)
                {
                    if (entry.Sequence <= since || !severities.Contains(entry.Type)) continue;
                    if (search != null && entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                        && entry.StackTrace.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (messages.Count >= maxResults) break;
                    Dictionary<string, object> message = new Dictionary<string, object>
                    {
                        { "sequence", entry.Sequence }, { "timestamp", entry.Timestamp },
                        { "type", entry.Type }, { "message", entry.Message }
                    };
                    if (includeStack) message["stackTrace"] = entry.StackTrace;
                    messages.Add(message);
                }
            }
            return new Dictionary<string, object>
            {
                { "count", messages.Count }, { "messages", messages }, { "latestSequence", latest },
                { "truncated", messages.Count >= maxResults },
                { "captureScope", "Bridge-captured messages since initialization; this is not Unity Console UI history." }
            };
        }

        public IDictionary<string, object> Clear()
        {
            int removed;
            lock (sync) { removed = entries.Count; entries.Clear(); }
            return new Dictionary<string, object>
            {
                { "cleared", true }, { "removed", removed }, { "latestSequence", sequence },
                { "scope", "Bridge buffer only; Unity Console UI was not modified." }
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Application.logMessageReceivedThreaded -= OnLogMessage;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType logType)
        {
            ConsoleEntry entry = new ConsoleEntry
            {
                Sequence = System.Threading.Interlocked.Increment(ref sequence),
                Timestamp = DateTime.UtcNow.ToString("o"),
                Type = ToProtocolType(logType), Message = condition ?? string.Empty,
                StackTrace = stackTrace ?? string.Empty
            };
            lock (sync)
            {
                entries.Enqueue(entry);
                while (entries.Count > MaximumEntries) entries.Dequeue();
            }
        }

        private static string ToProtocolType(LogType logType)
        {
            switch (logType)
            {
                case LogType.Error: case LogType.Assert: case LogType.Exception: return "Error";
                case LogType.Warning: return "Warning";
                default: return "Log";
            }
        }

        private sealed class ConsoleEntry
        {
            public long Sequence;
            public string Timestamp;
            public string Type;
            public string Message;
            public string StackTrace;
        }
    }
}
