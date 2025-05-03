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
        Debug.Log($"[Connect] url  = '{url}'");
        Debug.Log($"[Connect] name = '{playerName}'");

        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogError("[Connect] URL is empty — abort");
            return;
        }

        if (!url.StartsWith("ws://") && !url.StartsWith("wss://"))
        {
            Debug.LogError($"[Connect] Invalid URL scheme  {url}");
            return;
        }

        //--- create socket --------------------------------------------------------
        ws = new WebSocket(url);
        Debug.Log($"[Connect] new WebSocket() returned {(ws == null ? "NULL" : "OK")}");

        //--- OnOpen handler -------------------------------------------------------
        ws.OnOpen += () =>
        {
            Debug.Log("[Connect] OnOpen fired");

            // build join payload
            var joinMsg = new JoinPayload
            {
                arguments = new[] { new NameArg { name = playerName } }
            };

            var json = JsonUtility.ToJson(joinMsg);
            Debug.Log($"[Connect] join json = '{json ?? "NULL"}'");

            if (json == null)
            {
                Debug.LogError("[Connect] JsonUtility returned null!");
                return;
            }

            SendJson(joinMsg); // will log inside SendJson
            InvokeRepeating(nameof(Ping), 25f, 25f);
        };

        ws.OnError += e => Debug.LogError($"WS ERROR {e}");
        ws.OnClose += (e) => Debug.Log($"WS CLOSE {e}");
        ws.OnMessage += HandleMessage;

        Debug.Log("[Connect] Awaiting Connect()");
        await ws.Connect();                    // <-- if this throws we’ll see stack trace
        Debug.Log("[Connect] await ws.Connect finished");
    }

    public async void SendJson(object obj)
    {
        if (ws == null) { Debug.LogError("[SendJson] ws is null"); return; }
        if (ws.State != WebSocketState.Open) { Debug.LogError($"[SendJson] bad state {ws.State}"); return; }

        var json = JsonUtility.ToJson(obj);
        Debug.Log($"[SendJson] about to send '{json ?? "NULL"}'");

        await ws.SendText(json);
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

    [Serializable]
    private class JoinPayload
    {
        public int type = 1;
        public string target = "join";
        public NameArg[] arguments;
    }

    [Serializable]
    private class NameArg
    {
        public string name;
    }
    public static class PlayerState
    {
        public static string Role;
    }
}
