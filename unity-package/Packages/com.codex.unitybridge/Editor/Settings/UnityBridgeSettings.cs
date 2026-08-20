using System;
using System.IO;
using UnityEngine;

namespace Codex.UnityBridge.Settings
{
    [Serializable]
    internal sealed class UnityBridgeSettingsData
    {
        public string host = "127.0.0.1";
        public int port = 17891;
        public int reconnectDelayMs = 1000;
        public int maxMessageBytes = 1048576;
    }

    internal sealed class UnityBridgeSettings
    {
        private const string RelativeSettingsPath = "ProjectSettings/UnityCodexHub.json";

        public string Host { get; private set; }
        public int Port { get; private set; }
        public int ReconnectDelayMs { get; private set; }
        public int MaxMessageBytes { get; private set; }

        public Uri Endpoint
        {
            get { return new Uri(string.Format("ws://{0}:{1}", Host, Port)); }
        }

        private UnityBridgeSettings(UnityBridgeSettingsData data)
        {
            Host = data.host;
            Port = data.port;
            ReconnectDelayMs = data.reconnectDelayMs;
            MaxMessageBytes = data.maxMessageBytes;
        }

        public static UnityBridgeSettings Load()
        {
            UnityBridgeSettingsData data = new UnityBridgeSettingsData();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string settingsPath = Path.Combine(projectRoot, RelativeSettingsPath);

            if (File.Exists(settingsPath))
            {
                try
                {
                    data = JsonUtility.FromJson<UnityBridgeSettingsData>(File.ReadAllText(settingsPath));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Unity Codex Bridge could not read " + settingsPath + ": " + exception.Message);
                }
            }

            Validate(data, settingsPath);
            return new UnityBridgeSettings(data);
        }

        private static void Validate(UnityBridgeSettingsData data, string settingsPath)
        {
            if (!string.Equals(data.host, "127.0.0.1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Unity Codex Bridge only permits host 127.0.0.1. Check " + settingsPath);
            }

            if (data.port < 1024 || data.port > 65535)
            {
                throw new InvalidOperationException("Unity Codex Bridge port must be between 1024 and 65535.");
            }

            if (data.reconnectDelayMs < 100 || data.reconnectDelayMs > 60000)
            {
                throw new InvalidOperationException("Unity Codex Bridge reconnectDelayMs must be between 100 and 60000.");
            }

            if (data.maxMessageBytes < 1024 || data.maxMessageBytes > 16777216)
            {
                throw new InvalidOperationException("Unity Codex Bridge maxMessageBytes is outside the allowed range.");
            }
        }
    }
}
