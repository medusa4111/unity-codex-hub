using System;
using System.Collections.Generic;
using System.IO;
using Codex.UnityBridge.Commands;
using Codex.UnityBridge.Protocol;
using Codex.UnityBridge.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codex.UnityBridge.Connection
{
    [InitializeOnLoad]
    internal static class UnityBridgeBootstrap
    {
        private const int MaxCommandsPerUpdate = 16;
        private static UnityWebSocketConnection connection;
        private static UnityCommandExecutor executor;
        private static UnityConsoleBuffer consoleBuffer;
        private static string lastConnectionError;
        private static bool initialized;

        static UnityBridgeBootstrap()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            try
            {
                UnityBridgeSettings settings = UnityBridgeSettings.Load();
                consoleBuffer = new UnityConsoleBuffer();
                connection = new UnityWebSocketConnection(settings, CreateHello());
                executor = new UnityCommandExecutor(connection, consoleBuffer);
                EditorApplication.update += OnEditorUpdate;
                AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
                EditorApplication.quitting += Shutdown;
                connection.Start();
                initialized = true;
            }
            catch (Exception exception)
            {
                Debug.LogError("Unity Codex Bridge failed to initialize: " + exception.Message);
            }
        }

        private static void OnEditorUpdate()
        {
            DrainConnectionEvents();

            UnityCommandRequest request;
            int processed = 0;
            while (processed < MaxCommandsPerUpdate && connection.TryDequeueCommand(out request))
            {
                // This update callback is the only path from network messages to Unity APIs.
                executor.Execute(request);
                processed++;
            }
        }

        private static void DrainConnectionEvents()
        {
            string message;
            while (connection.TryDequeueConnectionEvent(out message))
            {
                if (message == "connected")
                {
                    lastConnectionError = null;
                    Debug.Log("Unity Codex Bridge connected to local Hub.");
                }
                else if (message == "disconnected")
                {
                    Debug.LogWarning("Unity Codex Bridge disconnected; reconnecting automatically.");
                }
                else if (message.StartsWith("protocol_error:", StringComparison.Ordinal))
                {
                    Debug.LogWarning("Unity Codex Bridge rejected a protocol message: " + message.Substring(15));
                }
                else if (message.StartsWith("connection_error:", StringComparison.Ordinal))
                {
                    string error = message.Substring(17);
                    if (!string.Equals(lastConnectionError, error, StringComparison.Ordinal))
                    {
                        lastConnectionError = error;
                        Debug.LogWarning("Unity Codex Bridge is waiting for Hub: " + error);
                    }
                }
            }
        }

        private static IDictionary<string, object> CreateHello()
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            Scene scene = SceneManager.GetActiveScene();
            return new Dictionary<string, object>
            {
                { "type", "unity_hello" },
                { "protocolVersion", ProtocolConstants.Version },
                { "unityVersion", Application.unityVersion },
                { "projectName", new DirectoryInfo(projectPath).Name },
                { "projectPath", projectPath },
                { "currentScene", scene.name }
            };
        }

        private static void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            initialized = false;
            EditorApplication.update -= OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            connection.Dispose();
            consoleBuffer.Dispose();
            connection = null;
            executor = null;
            consoleBuffer = null;
        }
    }
}
