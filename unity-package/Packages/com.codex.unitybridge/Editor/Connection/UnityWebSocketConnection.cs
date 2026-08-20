using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Codex.UnityBridge.Protocol;
using Codex.UnityBridge.Settings;

namespace Codex.UnityBridge.Connection
{
    internal sealed class UnityWebSocketConnection : IDisposable
    {
        private readonly UnityBridgeSettings settings;
        private readonly string helloMessage;
        private readonly ConcurrentQueue<UnityCommandRequest> incomingCommands =
            new ConcurrentQueue<UnityCommandRequest>();
        private readonly ConcurrentQueue<string> outgoingMessages = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> connectionEvents = new ConcurrentQueue<string>();
        private readonly SemaphoreSlim outgoingSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task connectionTask;
        private int connected;
        private bool disposed;

        public bool IsConnected
        {
            get { return Volatile.Read(ref connected) == 1; }
        }

        public int MaxMessageBytes
        {
            get { return settings.MaxMessageBytes; }
        }

        public UnityWebSocketConnection(UnityBridgeSettings settings, IDictionary<string, object> helloData)
        {
            this.settings = settings;
            helloMessage = Json.Serialize(helloData);
        }

        public void Start()
        {
            if (connectionTask != null)
            {
                return;
            }
            connectionTask = Task.Run(() => ConnectionLoopAsync(lifetime.Token));
        }

        public bool TryDequeueCommand(out UnityCommandRequest request)
        {
            return incomingCommands.TryDequeue(out request);
        }

        public bool TryDequeueConnectionEvent(out string message)
        {
            return connectionEvents.TryDequeue(out message);
        }

        public void EnqueueResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            outgoingMessages.Enqueue(json);
            outgoingSignal.Release();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            lifetime.Cancel();
            outgoingSignal.Release();
            Interlocked.Exchange(ref connected, 0);
        }

        private async Task ConnectionLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (ClientWebSocket socket = new ClientWebSocket())
                using (CancellationTokenSource connectionLifetime =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    try
                    {
                        await socket.ConnectAsync(settings.Endpoint, cancellationToken).ConfigureAwait(false);
                        await SendTextAsync(socket, helloMessage, cancellationToken).ConfigureAwait(false);
                        await ReceiveAndValidateHubHelloAsync(socket, cancellationToken).ConfigureAwait(false);
                        Interlocked.Exchange(ref connected, 1);
                        connectionEvents.Enqueue("connected");

                        Task receiveTask = ReceiveLoopAsync(socket, connectionLifetime.Token);
                        Task sendTask = SendLoopAsync(socket, connectionLifetime.Token);
                        await Task.WhenAny(receiveTask, sendTask).ConfigureAwait(false);
                        connectionLifetime.Cancel();
                        outgoingSignal.Release();

                        try
                        {
                            await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                    catch (Exception exception)
                    {
                        connectionEvents.Enqueue("connection_error:" + exception.Message);
                    }
                    finally
                    {
                        if (Interlocked.Exchange(ref connected, 0) == 1)
                        {
                            connectionEvents.Enqueue("disconnected");
                        }
                        ClearOutgoingMessages();
                    }
                }

                try
                {
                    await Task.Delay(settings.ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using (MemoryStream message = new MemoryStream())
                {
                    WebSocketReceiveResult receiveResult;
                    do
                    {
                        receiveResult = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }
                        if (receiveResult.MessageType != WebSocketMessageType.Text)
                        {
                            throw new ProtocolException("INVALID_ARGUMENT", "Only text WebSocket messages are supported.");
                        }
                        message.Write(buffer, 0, receiveResult.Count);
                        if (message.Length > settings.MaxMessageBytes)
                        {
                            throw new ProtocolException("INVALID_ARGUMENT", "WebSocket message exceeds maxMessageBytes.");
                        }
                    }
                    while (!receiveResult.EndOfMessage);

                    string json = Encoding.UTF8.GetString(message.ToArray());
                    if (IsHubHello(json))
                    {
                        continue;
                    }

                    try
                    {
                        incomingCommands.Enqueue(ProtocolParser.ParseRequest(json));
                    }
                    catch (Exception exception)
                    {
                        connectionEvents.Enqueue("protocol_error:" + exception.Message);
                    }
                }
            }
        }

        private async Task ReceiveAndValidateHubHelloAsync(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using (MemoryStream message = new MemoryStream())
            {
                WebSocketReceiveResult receiveResult;
                do
                {
                    receiveResult = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                        throw new ProtocolException("UNITY_NOT_CONNECTED", "Hub closed before completing the handshake.");
                    if (receiveResult.MessageType != WebSocketMessageType.Text)
                        throw new ProtocolException("INVALID_ARGUMENT", "Hub handshake must be a text JSON message.");
                    message.Write(buffer, 0, receiveResult.Count);
                    if (message.Length > settings.MaxMessageBytes)
                        throw new ProtocolException("INVALID_ARGUMENT", "Hub handshake exceeds maxMessageBytes.");
                }
                while (!receiveResult.EndOfMessage);

                IDictionary<string, object> value = Json.Deserialize(Encoding.UTF8.GetString(message.ToArray()))
                    as IDictionary<string, object>;
                object type;
                object version;
                if (value == null || !value.TryGetValue("type", out type)
                    || !string.Equals(type as string, "hub_hello", StringComparison.Ordinal)
                    || !value.TryGetValue("protocolVersion", out version)
                    || Convert.ToInt32(version) != ProtocolConstants.Version)
                    throw new ProtocolException("INVALID_ARGUMENT", "Hub returned an invalid or incompatible handshake.");
            }
        }

        private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await outgoingSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                string message;
                while (outgoingMessages.TryDequeue(out message))
                {
                    await SendTextAsync(socket, message, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task SendTextAsync(
            ClientWebSocket socket,
            string message,
            CancellationToken cancellationToken)
        {
            byte[] payload = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }

        private static bool IsHubHello(string json)
        {
            IDictionary<string, object> value = Json.Deserialize(json) as IDictionary<string, object>;
            object type;
            return value != null
                && value.TryGetValue("type", out type)
                && string.Equals(type as string, "hub_hello", StringComparison.Ordinal);
        }

        private void ClearOutgoingMessages()
        {
            string ignored;
            while (outgoingMessages.TryDequeue(out ignored))
            {
            }
        }
    }
}
