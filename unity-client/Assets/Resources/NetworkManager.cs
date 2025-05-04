using System;
using System.Collections.Generic;
using System.Linq;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
    /* ──────────────────────────  SINGLETON  ────────────────────────── */
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
        CancelInvoke(nameof(Ping));
        if (ws != null) await ws.Close();
    }

    /* ──────────────────────────  PUBLIC API  ───────────────────────── */

    private string playerNameCached;

    /// Opens the socket, sends the join payload.
    public async void Connect(string url, string playerName)
    {
        playerNameCached = playerName;

        if (string.IsNullOrWhiteSpace(url) ||
            (!url.StartsWith("ws://") && !url.StartsWith("wss://")))
        {
            Debug.LogError($"Invalid URL: {url}");
            return;
        }

        ws = new WebSocket(url);

        ws.OnOpen += () =>
        {
            Debug.Log("WS → OPEN");

            var join = new JoinPayload
            {
                arguments = new[] { new NameArg { name = playerName } }
            };
            SendJson(join);

            InvokeRepeating(nameof(Ping), 25f, 25f);
        };

        ws.OnError += e => Debug.LogError($"WS ERROR {e}");
        ws.OnClose += (e) => Debug.Log($"WS CLOSE {e}");
        ws.OnMessage += HandleMessage;

        await ws.Connect();
    }

    public async void SendJson(object obj)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        await ws.SendText(JsonUtility.ToJson(obj));
    }

    /* ──────────────────────────  INCOMING  ────────────────────────── */

    private void HandleMessage(byte[] bytes)
    {
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        Debug.Log($"WS ← {json}");

        /* 1. Lobby snapshot → switch to GameRoom */
        if (json.Contains("\"target\":\"lobby\""))
        {
            var lob = JsonUtility.FromJson<LobbyEnvelope>(json);
            RoomState.Players = lob.arguments[0].players.ToList();

            var me = RoomState.Players.FirstOrDefault(p => p.name == playerNameCached);
            if (me != null) PlayerState.MyId = me.id;

            SceneManager.LoadScene("GameRoom");
            OnRoomUpdate?.Invoke();
            return;
        }

        /* 2. Other envelopes parsed to lightweight struct */
        var env = JsonUtility.FromJson<GenericEnvelope>(json);

        switch (env.target)
        {
            case "readyUpdate":               // one player toggled
                var pl = RoomState.Players.First(p => p.id == env.id);
                pl.ready = env.ready;
                OnRoomUpdate?.Invoke();
                break;

            case "phase":                     // server changed phase
                if (env.phase == "night" || env.phase == "day")
                    SceneManager.LoadScene("GameScene");   // later you’ll branch here
                break;

            case "role":                      // personal role
                PlayerState.Role = env.role;
                break;
        }
    }

    private void Ping() => SendJson(new { @event = "ping" });

    /* ──────────────────────  MODELS / STATE  ─────────────────────── */

    [Serializable]
    private class JoinPayload
    {
        public int type = 1;
        public string target = "join";
        public NameArg[] arguments;
    }
    [Serializable] private class NameArg { public string name; }

    /* lobby snapshot */
    [Serializable]
    private class LobbyEnvelope
    {
        public int type;
        public string target;
        public LobbyArg[] arguments;
    }
    [Serializable] private class LobbyArg { public PlayerInfo[] players; }

    /* generic small envelope for simple events */
    [Serializable]
    private class GenericEnvelope
    {
        public string target;
        public string id;
        public bool ready;
        public string phase;
        public string role;
    }

    [Serializable]
    public class PlayerInfo
    {
        public string id;
        public string name;
        public bool ready;
    }

    /* shared runtime state */
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

    /* one-liner pub-sub for UI */
    public static Action OnRoomUpdate;
}
