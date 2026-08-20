using System;
using System.Collections.Generic;

namespace Codex.UnityBridge.Protocol
{
    internal static class ProtocolConstants
    {
        public const int Version = 3;
    }

    internal sealed class UnityCommandRequest
    {
        public string RequestId { get; private set; }
        public string Command { get; private set; }
        public IDictionary<string, object> Parameters { get; private set; }

        public UnityCommandRequest(string requestId, string command, IDictionary<string, object> parameters)
        {
            RequestId = requestId;
            Command = command;
            Parameters = parameters;
        }
    }

    internal sealed class ProtocolException : Exception
    {
        public string Code { get; private set; }
        public object Details { get; private set; }

        public ProtocolException(string code, string message, object details = null)
            : base(message)
        {
            Code = code;
            Details = details;
        }
    }

    internal static class ProtocolResponse
    {
        public static string Success(string requestId, object result)
        {
            return Json.Serialize(new Dictionary<string, object>
            {
                { "requestId", requestId },
                { "success", true },
                { "result", result },
                { "error", null }
            });
        }

        public static string Failure(string requestId, string code, string message, object details = null)
        {
            Dictionary<string, object> error = new Dictionary<string, object>
            {
                { "code", code },
                { "message", message }
            };
            if (details != null)
            {
                error["details"] = details;
            }

            return Json.Serialize(new Dictionary<string, object>
            {
                { "requestId", requestId },
                { "success", false },
                { "result", null },
                { "error", error }
            });
        }
    }
}
