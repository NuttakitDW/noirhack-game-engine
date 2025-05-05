// Assets/Resources/NetworkManager.cs   (singleton across scenes)
using System;
using System.Collections.Generic;
using System.Linq;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
    /*───────────────────────── Singleton ─────────────────────────*/
    public static NetworkManager Instance { get; private set; }
    public static Action<string> OnNightEnd;
    WebSocket ws;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    void Update() => ws?.DispatchMessageQueue();
    async void OnApplicationQuit() { if (ws != null) await ws.Close(); }

    /*───────────────────────── Connect / join ────────────────────*/
    string myName;

    public async void Connect(string url, string playerName)
    {
        myName = playerName;

        if (string.IsNullOrWhiteSpace(url) ||
           !(url.StartsWith("ws://") || url.StartsWith("wss://")))
        {
            Debug.LogError($"Bad WS url: {url}"); return;
        }

        ws = new WebSocket(url);

        ws.OnOpen += () =>
        {
            Debug.Log("WS → OPEN");

            SendRaw(new HubMessage<JoinArg>
            {
                target = "join",
                arguments = new[] { new JoinArg { name = playerName } }
            });

            SendRaw(new ReadyFrame(true));

            SceneManager.LoadScene("GameScene");
        };
        ws.OnError += e => Debug.LogError($"WS ERROR {e}");
        ws.OnClose += c => Debug.Log($"WS CLOSE  {c}");
        ws.OnMessage += HandleMessage;

        await ws.Connect();
    }

    /*───────────────────────── Outgoing helpers ──────────────────*/
    public void SendNightAction(string action, string targetId)
    {
        var frame = new HubMessage<NightArg>
        {
            target = "nightAction",
            arguments = new[] { new NightArg { action = action, target = targetId } }
        };
        SendRaw(frame);
    }

    async void SendRaw(object obj)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        string json = JsonUtility.ToJson(obj);
        Debug.Log($"WS → {json}");
        await ws.SendText(json);
    }

    /*───────────────────────── Incoming parsing ──────────────────*/
    void HandleMessage(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"WS ← {json}");

        // Fast path: lobby snapshot comes only once per join/update
        if (json.Contains("\"target\":\"lobby\""))
        {
            var lob = JsonUtility.FromJson<LobbyEnvelope>(json);
            RoomState.Players = lob.arguments[0].players.ToList();

            var me = RoomState.Players.FirstOrDefault(p => p.name == myName);
            if (me != null) PlayerState.MyId = me.id;

            OnRoomUpdate?.Invoke();
            return;
        }

        // Generic target switch
        var env = JsonUtility.FromJson<GenericEnv>(json);

        switch (env.target)
        {
            case "phase":
                var pe = JsonUtility.FromJson<PhaseEnvelope>(json);
                var pa = pe.arguments[0];
                OnPhaseChange?.Invoke(pa.phase, pa.round);
                break;

            case "role":
                var re = JsonUtility.FromJson<RoleEnvelope>(json);
                string role = re.arguments.Length > 0 ? re.arguments[0].role : "N/A";
                PlayerState.Role = role;
                OnRole?.Invoke(role);
                break;

            case "peekResult":
                var pr = JsonUtility.FromJson<PeekResultEnvelope>(json);
                var ra = pr.arguments[0];
                OnPeekResult?.Invoke(ra.target, ra.role);
                break;
            case "nightEnd":
                {
                    var ne = JsonUtility.FromJson<NightEndEnvelope>(json);
                    string victim = ne.arguments[0].killed;
                    OnNightEnd?.Invoke(victim);        // new event
                    break;
                }

        }
    }

    /*───────────────────────── Shared state ─────────────────────*/
    public static class RoomState
    {
        public const int MIN_PLAYERS = 4;
        public static List<PlayerInfo> Players = new();
    }
    public static class PlayerState
    {
        public static string MyId;
        public static string Role;
    }

    /*───────────────────────── Events ────────────────────────────*/
    public static Action OnRoomUpdate;                 // lobby changed
    public static Action<string, int> OnPhaseChange;             // phase, round
    public static Action<string> OnRole;                       // my role
    public static Action<string, string> OnPeekResult;           // targetId, role

    /*───────────────────────── DTOs / envelopes ─────────────────*/
    [Serializable] class HubMessage<T> { public int type = 1; public string target; public T[] arguments; }
    [Serializable] class JoinArg { public string name; }
    [Serializable] class ReadyFrame { public int type = 1; public string target = "ready"; public bool[] arguments; public ReadyFrame(bool v) { arguments = new[] { v }; } }
    [Serializable] class NightArg { public string action; public string target; }

    [Serializable] class LobbyEnvelope { public int type; public string target; public LobbyArgs[] arguments; }
    [Serializable] class LobbyArgs { public PlayerInfo[] players; }
    [Serializable] public class PlayerInfo { public string id; public string name; public bool ready; }

    [Serializable] class GenericEnv { public string target; }

    [Serializable] class PhaseEnvelope { public int type; public string target; public PhaseArg[] arguments; }
    [Serializable] class PhaseArg { public string phase; public int round; }

    [Serializable] class RoleEnvelope { public int type; public string target; public RoleArg[] arguments; }
    [Serializable] class RoleArg { public string role; }

    [Serializable] class PeekResultEnvelope { public int type; public string target; public PeekArg[] arguments; }
    [Serializable] class PeekArg { public string target; public string role; }
    [Serializable] class NightEndEnvelope { public int type; public string target; public NightEndArg[] arguments; }
    [Serializable] class NightEndArg { public string killed; }
}
