using System;
using System.Collections.Generic;
using System.Linq;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
    /* ───────────────  Singleton  ─────────────── */
    public static NetworkManager Instance { get; private set; }
    private WebSocket ws;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update() => ws?.DispatchMessageQueue();

    async void OnApplicationQuit()
    {
        if (ws != null) await ws.Close();
    }

    /* ───────────────  Public API  ─────────────── */

    private string myName;

    public async void Connect(string url, string playerName)
    {
        myName = playerName;

        if (string.IsNullOrWhiteSpace(url) ||
            (!url.StartsWith("ws://") && !url.StartsWith("wss://")))
        {
            Debug.LogError($"Invalid WebSocket URL: {url}");
            return;
        }

        ws = new WebSocket(url);

        ws.OnOpen += () =>
        {
            Debug.Log("WS → OPEN");

            // --- join ------------------------------------------------------------
            var join = new HubMessage<JoinArg>
            {
                target = "join",
                arguments = new[] { new JoinArg { name = playerName } }
            };
            SendRaw(join);   // will log JSON inside SendRaw

            // --- auto-ready ------------------------------------------------------
            var ready = new ReadyFrame(true);
            SendRaw(ready);

            // --- load scene ------------------------------------------------------
            SceneManager.LoadScene("GameScene");
        };

        ws.OnError += e => Debug.LogError($"WS ERROR  {e}");
        ws.OnClose += (e) => Debug.Log($"WS CLOSE   {e}");
        ws.OnMessage += HandleMessage;

        await ws.Connect();
    }

    public async void SendRaw(object obj)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;

        string json = JsonUtility.ToJson(obj);
        Debug.Log($"WS → {json}");

        await ws.SendText(json);
    }


    /* ───────────────  Message handling  ─────────────── */

    private void HandleMessage(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"WS ← {json}");

        /* 1. Lobby snapshot ---------------------------------------- */
        if (json.Contains("\"target\":\"lobby\""))
        {
            var lob = JsonUtility.FromJson<LobbyEnvelope>(json);
            RoomState.Players = lob.arguments[0].players.ToList();

            // catch my server-generated id
            var me = RoomState.Players.FirstOrDefault(p => p.name == myName);
            if (me != null) PlayerState.MyId = me.id;

            OnRoomUpdate?.Invoke();
            return;
        }

        /* 2. Generic envelope -------------------------------------- */
        var env = JsonUtility.FromJson<GenericEnv>(json);

        switch (env.target)
        {
            case "phase":          // night / day, plus round #
                {
                    var pe = JsonUtility.FromJson<PhaseEnvelope>(json);
                    var arg = pe.arguments[0];
                    OnPhaseChange?.Invoke(arg.phase, arg.round);
                    break;
                }

            case "role":          // private role
                {
                    var re = JsonUtility.FromJson<RoleEnvelope>(json);
                    var role = re.arguments.Length > 0 ? re.arguments[0].role : "N/A";

                    Debug.Log($"Parsed role = {role}");

                    PlayerState.Role = role;
                    OnRole?.Invoke(role);
                    break;
                }
        }
    }

    /* ───────────────  Models & shared state  ─────────────── */

    [Serializable]
    private class LobbyEnvelope
    {
        public int type;
        public string target;
        public LobbyArgs[] arguments;
    }
    [Serializable]
    private class LobbyArgs
    {
        public PlayerInfo[] players;
    }
    [Serializable]
    private class GenericEnv
    {
        public string target;
        public string phase;  // night / day
        public string role;   // Werewolf, Seer, ...
    }
    [Serializable]
    public class PlayerInfo
    {
        public string id;
        public string name;
        public bool ready;  // server still sends it but we ignore
    }

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

    [Serializable]
    private class HubMessage<T>
    {
        public int type = 1;      // 1 = Invocation frame
        public string target;
        public T[] arguments;
    }

    [Serializable] private class JoinArg { public string name; }
    [Serializable] private class ReadyArg { public bool ready; }

    [Serializable]
    private class ReadyFrame
    {
        public int type = 1;
        public string target = "ready";
        public bool[] arguments;

        public ReadyFrame(bool value)
        {
            arguments = new[] { value };
        }
    }

    [Serializable]
    private class PhaseEnvelope
    {
        public int type;
        public string target;
        public PhaseArg[] arguments;
    }
    [Serializable]
    private class PhaseArg
    {
        public string phase;
        public int round;
        public int duration;
    }

    [Serializable]
    private class RoleEnvelope
    {
        public int type;
        public string target;      // "role"
        public RoleArg[] arguments;   // array length 1
    }

    [Serializable]
    private class RoleArg
    {
        public string role;
    }

    /* ───────────────  Simple events for UI  ─────────────── */

    public static Action OnRoomUpdate;           // call to refresh counts
    public static Action<string, int> OnPhaseChange;
    public static Action<string> OnRole;         // arg: role name
}
