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

            // send join
            var join = new
            {
                type = 1,
                target = "join",
                arguments = new[] { new { name = playerName } }
            };
            SendRaw(join);

            var ready = new
            {
                type = 1,
                target = "ready",
                arguments = new[] { new { ready = true } }
            };
            SendRaw(ready);

            // jump straight to GameScene
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
        await ws.SendText(JsonUtility.ToJson(obj));
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
            case "phase":         // night / day
                OnPhaseChange?.Invoke(env.phase);
                break;

            case "role":          // private role
                PlayerState.Role = env.role;
                OnRole?.Invoke(env.role);
                break;
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

    /* ───────────────  Simple events for UI  ─────────────── */

    public static Action OnRoomUpdate;           // call to refresh counts
    public static Action<string> OnPhaseChange;  // arg: "night"/"day"
    public static Action<string> OnRole;         // arg: role name
}
