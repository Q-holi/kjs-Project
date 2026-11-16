using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Kjs
{
    /// <summary>Entry point attached to Assets/Scenes/SharedWorld.unity.</summary>
    public sealed class KjsDemo : MonoBehaviour
    {
        private WorldView view;
        private WorldClient client;
        private Task connectTask = Task.CompletedTask;
        private Task<bool> inputTask = Task.FromResult(true);
        private Task<bool> chatTask = Task.FromResult(true);
        private readonly Queue<string> history = new Queue<string>();
        private readonly InputRequest input = new InputRequest();
        private string endpoint = "ws://127.0.0.1:7777/world";
        private string playerName = "Player";
        private string chat = "";
        private string localId;
        private string status = "Start the server, then connect.";
        private int tick;
        private int maxPlayers = 64;
        private float nextInput;
        private float lastSnapshot;
        private bool typing;
        private bool started;
        private bool automatic;
        private bool releaseFocus;
        private Vector2 chatScroll;
        private Font uiFont;
        private GUIStyle titleStyle, bodyStyle, statusStyle, chatStyle;

        public WorldView View => view;

        private void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            view = new GameObject("Shared World", typeof(WorldView)).GetComponent<WorldView>();
            view.transform.SetParent(transform, false);
            view.Initialize();
            ParseCommandLine();
        }

        private void Start()
        {
            started = true;
            if (automatic) Connect();
        }

        private void ParseCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--server" && i + 1 < args.Length) endpoint = args[++i];
                else if (args[i] == "--name" && i + 1 < args.Length) playerName = args[++i];
                else if (args[i] == "--connect") automatic = true;
            }
        }

        private void Connect()
        {
            if (!connectTask.IsCompleted || string.IsNullOrWhiteSpace(playerName)) return;
            Disconnect();
            input.seq = 0;
            client = new WorldClient();
            status = "Connecting...";
            var join = new JoinRequest { name = playerName.Trim() };
            connectTask = client.ConnectAsync(endpoint.Trim(), JsonUtility.ToJson(join));
        }

        private void Disconnect()
        {
            client?.Dispose();
            client = null;
            localId = null;
            view?.Clear();
            tick = 0;
            status = "Disconnected.";
            releaseFocus = true;
            typing = false;
        }

        private void Update()
        {
            if (client == null) return;
            while (client.TryDequeueEvent(out var message)) status = message;
            while (client.TryDequeue(out var json))
            {
                try { HandlePacket(JsonUtility.FromJson<ServerPacket>(json)); }
                catch (Exception error) { status = "Protocol error: " + error.Message; }
            }
            if (!client.IsOpen)
            {
                if (!string.IsNullOrEmpty(localId))
                {
                    localId = null;
                    view.Clear();
                }
                return;
            }
            if (string.IsNullOrEmpty(localId)) return;
            if (Time.unscaledTime - lastSnapshot > 5)
            {
                Disconnect();
                status = "No world update for 5 seconds. Please reconnect.";
                return;
            }
            if (Time.unscaledTime < nextInput || !inputTask.IsCompleted) return;
            nextInput = Time.unscaledTime + 0.1f;
            // Losing focus or editing any text field sends a stop input.
            Vector2 direction = Vector2.zero;
            if (Application.isFocused && !typing)
                direction = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"),
                                                                Input.GetAxisRaw("Vertical")), 1);
            input.dx = direction.x;
            input.dz = direction.y;
            input.seq++;
            inputTask = client.SendAsync(JsonUtility.ToJson(input));
        }

        private void HandlePacket(ServerPacket packet)
        {
            if (packet == null) return;
            switch (packet.type)
            {
                case "welcome":
                    if (packet.version != 1)
                    {
                        Disconnect();
                        status = "Unsupported server protocol version.";
                        return;
                    }
                    localId = packet.id;
                    maxPlayers = packet.maxPlayers;
                    view.SetLocalId(localId);
                    lastSnapshot = Time.unscaledTime;
                    status = "Connected | " + packet.tickRate + " Hz server";
                    AddHistory("Joined as " + playerName + ".");
                    releaseFocus = true;
                    typing = false;
                    break;
                case "snapshot":
                    if (string.IsNullOrEmpty(localId) || packet.tick < tick) return;
                    tick = packet.tick;
                    lastSnapshot = Time.unscaledTime;
                    view.Apply(packet.players);
                    break;
                case "chat": AddHistory(packet.name + ": " + packet.text); break;
                case "left": view.Remove(packet.id); break;
                case "error": status = "Server: " + packet.message; break;
            }
        }

        private void AddHistory(string text)
        {
            history.Enqueue(text);
            while (history.Count > 8) history.Dequeue();
            chatScroll.y = float.MaxValue;
        }

        private void SendChat()
        {
            if (client == null || !client.IsOpen || string.IsNullOrEmpty(localId)
                || !chatTask.IsCompleted || string.IsNullOrWhiteSpace(chat)) return;
            chatTask = client.SendAsync(JsonUtility.ToJson(new ChatRequest { text = chat }));
            chat = "";
            releaseFocus = true;
            typing = false;
        }

        private void CreateStyles()
        {
            if (titleStyle != null) return;
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 16);
            titleStyle = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 25,
                fontStyle = FontStyle.Bold, richText = false };
            bodyStyle = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 15, richText = false };
            statusStyle = new GUIStyle(bodyStyle) { wordWrap = true, fontSize = 13 };
            chatStyle = new GUIStyle(bodyStyle) { wordWrap = true, fontSize = 13 };
            GUI.skin.textField.font = uiFont;
            GUI.skin.button.font = uiFont;
        }

        private void OnGUI()
        {
            if (!started) return;
            CreateStyles();
            view.DrawNames(uiFont);
            float scale = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.Box(new Rect(18, 18, 386, 267), "");
            GUI.Label(new Rect(36, 30, 330, 40), "kjs / Shared World", titleStyle);
            GUI.Label(new Rect(36, 76, 330, 24), "3D multiplayer starter", bodyStyle);
            bool open = client != null && client.IsOpen;
            GUI.Label(new Rect(36, 110, 68, 24), "Server", bodyStyle);
            GUI.enabled = !open && connectTask.IsCompleted;
            GUI.SetNextControlName("ServerAddress");
            endpoint = GUI.TextField(new Rect(102, 110, 282, 26), endpoint, 200);
            GUI.Label(new Rect(36, 145, 68, 24), "Name", bodyStyle);
            GUI.SetNextControlName("PlayerName");
            playerName = GUI.TextField(new Rect(102, 145, 162, 26), playerName, 20);
            if (GUI.Button(new Rect(274, 145, 110, 26), "Connect")) Connect();
            GUI.enabled = client != null;
            if (GUI.Button(new Rect(274, 181, 110, 26), "Disconnect")) Disconnect();
            GUI.enabled = true;
            GUI.Label(new Rect(36, 184, 230, 24), "Players " + view.PlayerCount + " / " + maxPlayers, bodyStyle);
            GUI.Label(new Rect(36, 218, 345, 53), status, statusStyle);

            GUI.Box(new Rect(18, height - 249, 420, 231), "");
            GUI.Label(new Rect(34, height - 242, 300, 24), "WORLD CHAT", bodyStyle);
            string chatLines = string.Join("\n", history.ToArray());
            float chatHeight = Mathf.Max(143, chatStyle.CalcHeight(new GUIContent(chatLines), 360));
            chatScroll = GUI.BeginScrollView(new Rect(34, height - 211, 386, 143), chatScroll,
                                           new Rect(0, 0, 360, chatHeight));
            GUI.Label(new Rect(0, 0, 360, chatHeight), chatLines, chatStyle);
            GUI.EndScrollView();
            GUI.enabled = open && !string.IsNullOrEmpty(localId);
            bool submit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                          && GUI.GetNameOfFocusedControl() == "ChatInput";
            GUI.SetNextControlName("ChatInput");
            chat = GUI.TextField(new Rect(34, height - 57, 302, 27), chat, 140);
            if (GUI.Button(new Rect(344, height - 57, 76, 27), "Send") || submit)
            {
                SendChat();
                if (submit) Event.current.Use();
            }
            GUI.enabled = true;
            GUI.Label(new Rect(width - 370, 25, 350, 65),
                      "WASD / Arrow keys: move\nClick the field to leave text input\nTick " + tick, statusStyle);
            // Clicking outside controls releases typing focus and resumes movement.
            if (releaseFocus || (Event.current.type == EventType.MouseDown && GUIUtility.hotControl == 0))
            {
                GUI.FocusControl(null);
                releaseFocus = false;
            }
            string focus = GUI.GetNameOfFocusedControl();
            typing = focus == "ChatInput" || focus == "ServerAddress" || focus == "PlayerName";
            GUI.matrix = oldMatrix;
        }

        private void OnDestroy()
        {
            client?.Dispose();
            if (uiFont != null) Destroy(uiFont);
        }
    }
}
