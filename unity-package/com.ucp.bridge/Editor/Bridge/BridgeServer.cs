using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UCP.Bridge
{
    /// <summary>
    /// WebSocket server that runs inside the Unity Editor.
    /// Uses raw TcpListener + manual WebSocket upgrade for maximum compatibility.
    /// Starts automatically via [InitializeOnLoad] and handles JSON-RPC commands.
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeServer
    {
        private const int DefaultPort = 21342;
        private const int MaxPort = 21352;
        private const int MaxConnections = 4;
        private const string ProtocolVersion = "0.5.0";

        private static TcpListener s_listener;
        private static CancellationTokenSource s_cts;
        private static readonly List<WebSocket> s_clients = new();
        private static readonly object s_clientLock = new();
        private static int s_port;
        private static string s_token;
        private static bool s_running;

        // Main-thread action queue
        private static readonly ConcurrentQueue<Action> s_mainThreadQueue = new();

        // Command router
        private static readonly CommandRouter s_router = new();

        // Log subscribers
        private static readonly HashSet<WebSocket> s_logSubscribers = new();

        static BridgeServer()
        {
            // Use delayCall + update fallback to ensure reliable startup after domain reload
            EditorApplication.delayCall += Initialize;
            EditorApplication.update += EnsureRunning;
        }

        private static void EnsureRunning()
        {
            if (!s_running)
            {
                EditorApplication.update -= EnsureRunning;
                Initialize();
            }
            else
            {
                EditorApplication.update -= EnsureRunning;
            }
        }

        private static void Initialize()
        {
            if (s_running) return;

            try
            {
                RegisterHandlers();

                EditorApplication.update += PumpMainThread;
                EditorApplication.quitting += Shutdown;
                AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
                Application.logMessageReceived += OnLogMessage;

                s_token = Guid.NewGuid().ToString("N").Substring(0, 16);
                StartServer();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UCP] Failed to initialize bridge: {ex}");
            }
        }

        private static void RegisterHandlers()
        {
            // Handshake
            s_router.Register("handshake", (paramsJson) =>
            {
                return new
                {
                    serverVersion = ProtocolVersion,
                    protocolVersion = ProtocolVersion,
                    unityVersion = Application.unityVersion,
                    projectName = Application.productName,
                    projectPath = Path.GetDirectoryName(Application.dataPath)
                };
            });

            // Play mode
            PlayModeController.Register(s_router);

            // Compilation
            CompilationController.Register(s_router);

            // Editor lifecycle
            EditorController.Register(s_router);

            // Scenes
            SceneController.Register(s_router);

            // Snapshots
            SnapshotController.Register(s_router);

            // Screenshots
            ScreenshotController.Register(s_router);

            // Logs
            LogsController.Register(s_router);

            // Tests
            TestRunnerController.Register(s_router);

            // Profiler
            ProfilerController.Register(s_router);

            // Scripts (exec)
            ScriptController.Register(s_router);

            // Files
            FileController.Register(s_router);

            // Version Control (Unity VCS / Plastic SCM)
            VcsController.Register(s_router);

            // Object Properties
            PropertyController.Register(s_router);

            // Hierarchy Operations
            HierarchyController.Register(s_router);

            // Asset Management
            AssetController.Register(s_router);
            ImporterController.Register(s_router);

            // Editor Settings (Player, Quality, Physics, Lighting, Tags/Layers)
            EditorSettingsController.Register(s_router);

            // Material Properties
            MaterialController.Register(s_router);

            // Prefab Operations
            PrefabController.Register(s_router);

            // Build Pipeline
            BuildController.Register(s_router);

            // Package management
            PackagesController.Register(s_router);

            // Reference search (bridge fallback)
            ReferenceController.Register(s_router);
        }

        private static void StartServer()
        {
            s_cts = new CancellationTokenSource();

            for (int port = DefaultPort; port <= MaxPort; port++)
            {
                try
                {
                    s_listener = new TcpListener(IPAddress.Loopback, port);
                    s_listener.Start();
                    s_port = port;
                    s_running = true;
                    Debug.Log($"[UCP] Bridge server started on port {port}");
                    break;
                }
                catch (Exception)
                {
                    s_listener?.Stop();
                    s_listener = null;
                }
            }

            if (!s_running)
            {
                Debug.LogError("[UCP] Failed to start bridge server - all ports in use");
                return;
            }

            WriteLockFile();
            Task.Run(() => AcceptLoop(s_cts.Token));
        }

        private static async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && s_running)
            {
                try
                {
                    var tcp = await s_listener.AcceptTcpClientAsync();
                    var stream = tcp.GetStream();

                    // Read HTTP upgrade request (may arrive in multiple segments)
                    var requestBuilder = new StringBuilder();
                    var buffer = new byte[4096];
                    do
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                        if (read == 0) { tcp.Close(); continue; }
                        requestBuilder.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    }
                    while (!requestBuilder.ToString().Contains("\r\n\r\n") && stream.DataAvailable);

                    var request = requestBuilder.ToString();

                    // Check if it's a WebSocket upgrade
                    if (!request.Contains("Upgrade: websocket", StringComparison.OrdinalIgnoreCase))
                    {
                        var resp = "HTTP/1.1 426 Upgrade Required\r\nContent-Length: 0\r\n\r\n";
                        var respBytes = Encoding.UTF8.GetBytes(resp);
                        await stream.WriteAsync(respBytes, 0, respBytes.Length, ct);
                        tcp.Close();
                        continue;
                    }

                    // Extract Sec-WebSocket-Key
                    var keyMatch = Regex.Match(request, @"Sec-WebSocket-Key:\s*(\S+)",
                        RegexOptions.IgnoreCase);
                    if (!keyMatch.Success)
                    {
                        Debug.LogError("[UCP] No Sec-WebSocket-Key found in request");
                        tcp.Close();
                        continue;
                    }

                    var wsKey = keyMatch.Groups[1].Value.Trim();
                    var acceptKey = ComputeWebSocketAcceptKey(wsKey);

                    // Send upgrade response
                    var upgradeResponse =
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
                    var upgradeBytes = Encoding.UTF8.GetBytes(upgradeResponse);
                    await stream.WriteAsync(upgradeBytes, 0, upgradeBytes.Length, ct);

                    // Create WebSocket from stream
                    var ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));

                    lock (s_clientLock)
                    {
                        if (s_clients.Count >= MaxConnections)
                        {
                            _ = ws.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                                "Max connections reached", CancellationToken.None);
                            continue;
                        }
                        s_clients.Add(ws);
                    }

                    _ = Task.Run(() => HandleClient(ws, ct));
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Debug.LogError($"[UCP] Accept error: {ex.Message}");
                }
            }
        }

        private static string ComputeWebSocketAcceptKey(string key)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-5AB5DC85B11B";
            var combined = key + magic;
            var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash);
        }

        private static async Task HandleClient(WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[64 * 1024]; // 64KB buffer

            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult received;
                    var messageBuilder = new StringBuilder();

                    do
                    {
                        received = await ws.ReceiveAsync(segment, ct);
                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            return;
                        }
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                    }
                    while (!received.EndOfMessage);

                    var message = messageBuilder.ToString();
                    ProcessMessage(ws, message);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[UCP] Client error: {ex.Message}");
            }
            finally
            {
                lock (s_clientLock)
                {
                    s_clients.Remove(ws);
                    s_logSubscribers.Remove(ws);
                }
            }
        }

        private static void ProcessMessage(WebSocket ws, string message)
        {
            // Parse JSON-RPC request
            long id = 0;
            string method = null;
            string paramsJson = "{}";

            try
            {
                // Minimal JSON parsing using Unity's JsonUtility won't work well for dynamic JSON.
                // Use a simple manual parse for the top-level fields.
                var json = MiniJson.Deserialize(message) as Dictionary<string, object>;
                if (json == null)
                {
                    SendError(ws, 0, ErrorCodes.ParseError, "Invalid JSON");
                    return;
                }

                if (json.TryGetValue("id", out var idVal))
                    id = Convert.ToInt64(idVal);

                if (json.TryGetValue("method", out var methodVal))
                    method = methodVal as string;

                if (json.TryGetValue("params", out var paramsVal))
                    paramsJson = MiniJson.Serialize(paramsVal);
            }
            catch (Exception ex)
            {
                SendError(ws, 0, ErrorCodes.ParseError, $"Parse error: {ex.Message}");
                return;
            }

            if (string.IsNullOrEmpty(method))
            {
                SendError(ws, id, ErrorCodes.InvalidRequest, "Missing 'method' field");
                return;
            }

            // Handle log subscription specially
            if (method == "logs/subscribe")
            {
                lock (s_clientLock) { s_logSubscribers.Add(ws); }
            }
            else if (method == "logs/unsubscribe")
            {
                lock (s_clientLock) { s_logSubscribers.Remove(ws); }
            }

            // Dispatch on main thread
            var capturedId = id;
            var capturedMethod = method;
            var capturedParams = paramsJson;
            var capturedWs = ws;

            s_mainThreadQueue.Enqueue(() =>
            {
                var response = s_router.Dispatch(capturedMethod, capturedId, capturedParams);
                SendResponse(capturedWs, response);
            });
        }

        private static void SendResponse(WebSocket ws, JsonRpcResponse response)
        {
            try
            {
                var json = MiniJson.Serialize(ResponseToDict(response));
                var bytes = Encoding.UTF8.GetBytes(json);
                _ = ws.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UCP] Send error: {ex.Message}");
            }
        }

        private static void SendError(WebSocket ws, long id, int code, string message)
        {
            SendResponse(ws, JsonRpcResponse.Error(id, code, message));
        }

        /// <summary>
        /// Send a notification to log subscribers only.
        /// </summary>
        public static void SendNotification(string method, object data)
        {
            SendNotificationTo(method, data, false);
        }

        /// <summary>
        /// Broadcast a notification to ALL connected clients.
        /// Use for test results and other non-log notifications.
        /// </summary>
        public static void BroadcastNotification(string method, object data)
        {
            SendNotificationTo(method, data, true);
        }

        private static void SendNotificationTo(string method, object data, bool toAll)
        {
            var dict = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = data
            };
            var json = MiniJson.Serialize(dict);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            List<WebSocket> targets;
            lock (s_clientLock)
            {
                targets = toAll
                    ? new List<WebSocket>(s_clients)
                    : new List<WebSocket>(s_logSubscribers);
            }

            foreach (var ws in targets)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        _ = ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch { /* ignore send failures for notifications */ }
            }
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            // Don't forward our own log messages to avoid infinite recursion
            if (message.StartsWith("[UCP]")) return;

            SendNotification("log", LogsController.RecordLog(message, stackTrace, type));
        }

        private static void PumpMainThread()
        {
            int processed = 0;
            while (s_mainThreadQueue.TryDequeue(out var action) && processed < 50)
            {
                try { action(); }
                catch (Exception ex) { Debug.LogError($"[UCP] Main thread error: {ex}"); }
                processed++;
            }
        }

        private static void WriteLockFile()
        {
            try
            {
                var projectPath = Path.GetDirectoryName(Application.dataPath);
                var ucpDir = Path.Combine(projectPath, ".ucp");
                Directory.CreateDirectory(ucpDir);

                var lockData = new Dictionary<string, object>
                {
                    ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["port"] = s_port,
                    ["protocolVersion"] = ProtocolVersion,
                    ["unityVersion"] = Application.unityVersion,
                    ["projectPath"] = projectPath,
                    ["startedAt"] = DateTime.UtcNow.ToString("o"),
                    ["token"] = s_token
                };

                File.WriteAllText(
                    Path.Combine(ucpDir, "bridge.lock"),
                    MiniJson.Serialize(lockData)
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UCP] Failed to write lock file: {ex.Message}");
            }
        }

        private static void CleanLockFile()
        {
            try
            {
                var projectPath = Path.GetDirectoryName(Application.dataPath);
                var lockPath = Path.Combine(projectPath, ".ucp", "bridge.lock");
                if (File.Exists(lockPath))
                    File.Delete(lockPath);
            }
            catch { }
        }

        private static void Shutdown()
        {
            if (!s_running) return;
            s_running = false;

            Debug.Log("[UCP] Bridge server shutting down");

            s_cts?.Cancel();

            // Stop listener first to release port immediately
            try { s_listener?.Stop(); }
            catch { }
            s_listener = null;

            lock (s_clientLock)
            {
                foreach (var ws in s_clients)
                {
                    try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None); }
                    catch { }
                }
                s_clients.Clear();
                s_logSubscribers.Clear();
            }

            s_cts?.Dispose();
            s_cts = null;

            CleanLockFile();

            EditorApplication.update -= PumpMainThread;
            Application.logMessageReceived -= OnLogMessage;
        }

        private static Dictionary<string, object> ResponseToDict(JsonRpcResponse r)
        {
            var dict = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = r.id
            };

            if (r.error != null)
            {
                dict["error"] = new Dictionary<string, object>
                {
                    ["code"] = r.error.code,
                    ["message"] = r.error.message,
                    ["data"] = r.error.data
                };
            }
            else
            {
                dict["result"] = r.result;
            }

            return dict;
        }
    }
}
