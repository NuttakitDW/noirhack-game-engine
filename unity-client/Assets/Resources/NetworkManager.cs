using System;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single-instance WebSocket manager that lives across scene loads.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    private WebSocket ws;
    private const string WS_URL = "ws://localhost:8080/ws";

    // ──────────────────────────────────────────────────────────────────────────────
    #region Unity lifecycle
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
    #endregion
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens the socket and sends the initial join envelope.</summary>
    public async void Connect()
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            Debug.Log("WebSocket already connected.");
            return;
        }

        ws = new WebSocket(WS_URL);

        ws.OnOpen += () => { Debug.Log("WS → OPEN"); SendJson(new { @event = "join" }); InvokeRepeating(nameof(Ping), 25f, 25f); };
        ws.OnError += err => Debug.LogError($"WS → ERROR  {err}");
        ws.OnClose += (closeCode) => { Debug.Log($"WS → CLOSE  {closeCode}"); CancelInvoke(nameof(Ping)); };
        ws.OnMessage += HandleMessage;

        await ws.Connect();
    }

    // ──────────────────────────────────────────────────────────────────────────────
    #region Message handling
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
                SceneManager.LoadScene("GameScene");          // night/day flow kicks in here
                break;

            case "private_role":
                PlayerState.Role = msg.role;                  // store role for GameScene UI
                break;

                // add more events as needed: vote_result, reveal, etc.
        }
    }
    #endregion
    // ──────────────────────────────────────────────────────────────────────────────

    public async void SendJson(object obj)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        await ws.SendText(JsonUtility.ToJson(obj));
    }

    private void Ping() => SendJson(new { @event = "ping" });

    // ──────────────────────────────────────────────────────────────────────────────
    #region Helper structs
    [Serializable]
    private class MessageEnvelope
    {
        public string @event;
        public string phase;   // used by phase_transition
        public string role;    // used by private_role
    }

    /// <summary>Example static holder for data you need across scenes.</summary>
    public static class PlayerState
    {
        public static string Role;
    }
    #endregion
}
