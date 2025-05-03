using System;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
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

    /* ────────────────────────────  Public API  ──────────────────────────────── */

    /// <summary>Open the socket to <paramref name="url"/> and send a “join”.</summary>
    public async void Connect(string url, string playerName)
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            Debug.Log("WebSocket already connected.");
            return;
        }

        ws.OnOpen += () =>
    {
        Debug.Log("WS → OPEN");

        var joinMsg = new
        {
            type = 1,
            target = "join",
            arguments = new[] { new { name = playerName } }
        };
        SendJson(joinMsg);

        InvokeRepeating(nameof(Ping), 25f, 25f);
    };

        await ws.Connect();
    }

    public async void SendJson(object obj)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        await ws.SendText(JsonUtility.ToJson(obj));
    }

    private void HandleMessage(byte[] bytes)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        Debug.Log($"WS ← {json}");

        MessageEnvelope msg;
        try { msg = JsonUtility.FromJson<MessageEnvelope>(json); }
        catch { Debug.LogWarning("Bad JSON"); return; }

        switch (msg.@event)
        {
            case "phase_transition":
                SceneManager.LoadScene("GameScene");
                break;

            case "private_role":
                PlayerState.Role = msg.role;
                break;

                // TODO: vote_result, reveal, etc.
        }
    }

    private void Ping() => SendJson(new { @event = "ping" });

    /* ──────────────────────────────  Helpers  ──────────────────────────────── */

    [Serializable]
    private class MessageEnvelope
    {
        public string @event;
        public string phase;
        public string role;
    }

    /// <summary>Quick static holder for per-player state.</summary>
    public static class PlayerState
    {
        public static string Role;
    }
}
