using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kjs
{
    /// <summary>
    /// Desktop WebSocket transport. No Unity API is used on an async worker.
    /// Read messages and parse JsonUtility from the Unity main thread.
    /// Create a new instance for every connection attempt.
    /// </summary>
    public sealed class WorldClient : IDisposable
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
        private int queued;
        private int disposed;

        public bool IsOpen => Volatile.Read(ref disposed) == 0 && socket.State == WebSocketState.Open;
        public Task ReceiveTask { get; private set; } = Task.CompletedTask;

        public WorldClient()
        {
            token = lifetime.Token;
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        }

        public bool TryDequeue(out string json)
        {
            if (!incoming.TryDequeue(out json)) return false;
            Interlocked.Decrement(ref queued);
            return true;
        }

        public bool TryDequeueEvent(out string text) => events.TryDequeue(out text);

        public async Task ConnectAsync(string endpoint, string joinJson)
        {
            try
            {
                var uri = new Uri(endpoint);
                if (uri.Scheme != "ws" && uri.Scheme != "wss")
                    throw new ArgumentException("Use a ws:// or wss:// address.");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                }
                // Join is the first packet. Start receiving only after it is sent.
                if (await SendAsync(joinJson).ConfigureAwait(false))
                {
                    events.Enqueue("Connected. Waiting for welcome...");
                    ReceiveTask = ReceiveLoopAsync();
                }
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested) events.Enqueue("Connection timed out.");
                Abort();
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) events.Enqueue("Connection failed: " + error.Message);
                Abort();
            }
        }

        public async Task<bool> SendAsync(string json)
        {
            bool entered = false;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    await sendGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                    entered = true;
                    if (!IsOpen) return false;
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                                           true, timeout.Token).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) events.Enqueue("Send failed: " + error.Message);
                Abort();
                return false;
            }
            finally
            {
                if (entered) sendGate.Release();
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested && IsOpen)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token)
                                                 .ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                events.Enqueue("Disconnected: " + result.CloseStatusDescription);
                                return;
                            }
                            if (result.MessageType != WebSocketMessageType.Text)
                                throw new InvalidDataException("Server sent a non-text frame.");
                            if (message.Length + result.Count > 262144)
                                throw new InvalidDataException("Server message exceeds 256 KiB.");
                            message.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (Interlocked.Increment(ref queued) > 64)
                        {
                            Interlocked.Decrement(ref queued);
                            throw new InvalidDataException("Receive queue overflow. Reconnect to resume.");
                        }
                        incoming.Enqueue(Encoding.UTF8.GetString(message.ToArray()));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) events.Enqueue("Receive failed: " + error.Message);
            }
            finally { Abort(); }
        }

        private void Abort()
        {
            if (Volatile.Read(ref disposed) != 0) return;
            try { socket.Abort(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            socket.Abort();
            socket.Dispose();
            lifetime.Dispose();
        }
    }
}
