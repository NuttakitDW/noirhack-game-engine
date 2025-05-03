using NativeWebSocket;
using UnityEngine;
using System.Threading.Tasks;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    private WebSocket ws;
    private const string WS_URL = "ws://localhost:8080";   // change to match your server

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async void Connect(string roomCode)
    {
        ws = new WebSocket($"{WS_URL}?room={roomCode}");

        ws.OnOpen += () => Debug.Log("WS - OPEN");
        ws.OnError += e => Debug.LogError($"WS ERROR: {e}");
        ws.OnClose += (closeCode) => Debug.Log($"WS CLOSE [{closeCode}]");
        ws.OnMessage += bytes =>
        {
            var msg = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"WS ⇐ {msg}");
            // here you’ll dispatch to a message handler later
        };

        await ws.Connect();

        // send the initial join envelope
        SendJson(new { @event = "join", room = roomCode });

        // start heartbeat
        InvokeRepeating(nameof(Ping), 25f, 25f);
    }

    public async void SendJson(object obj)
        => await ws.SendText(JsonUtility.ToJson(obj));

    void Ping() => SendJson(new { @event = "ping" });

    void Update() => ws?.DispatchMessageQueue();

    async void OnApplicationQuit() => await ws?.Close();
}
