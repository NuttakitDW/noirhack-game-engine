// Assets/Resources/NetworkManager.cs   (singleton across scenes)
using System;
using System.Collections;
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
    public static string LastWinner { get; private set; }
    public WebSocket ws;

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

    private IEnumerator FetchKeysThenEnterGame()
    {
        // 1) Fetch the HTTP keypair
        yield return KeyPairStore.Instance.FetchKeyPair();

        // 2) Register our public key with the server
        var pk = KeyPairStore.Instance.PublicKey;
        var frame = new HubMessage<string>
        {
            target = "registerPublicKey",
            arguments = new[] { pk }   // send the raw string
        };
        // SendRaw is async void, but that's fine here:
        SendRaw(frame);

        // 3) Now load the GameScene
        SceneManager.LoadScene("GameScene");
    }



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

            var joinFrame = new HubMessage<JoinArg>
            {
                target = "join",
                arguments = new[] { new JoinArg { name = playerName } }
            };
            SendRaw(joinFrame);
            SendRaw(new ReadyFrame(true));

            // start our coroutine instead of immediate LoadScene
            StartCoroutine(FetchKeysThenEnterGame());
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

    public void SendVote(string targetId)
    {
        // we reuse our generic HubMessage<T> but T is string here
        var frame = new HubMessage<string>
        {
            target = "vote",
            arguments = new[] { targetId }
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

        if (json.Contains("\"target\":\"startShuffle\""))
        {
            // Parse into our generic frame type
            var frame = JsonUtility.FromJson<IncomingFrame<StartShufflePayload>>(json);
            var payload = frame.arguments[0];
            OnStartShuffle?.Invoke(payload);
            return;  // skip the rest of HandleMessage
        }

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
            case "voteUpdate":
                {
                    var vu = JsonUtility.FromJson<VoteUpdateEnvelope>(json);

                    if (vu.arguments != null
                     && vu.arguments.Length > 0
                     && vu.arguments[0].votes != null)
                    {
                        var tally = new Dictionary<string, int>();
                        foreach (var vc in vu.arguments[0].votes)
                            tally[vc.playerId] = vc.count;
                        OnVoteUpdate?.Invoke(tally);
                    }
                    else
                    {
                        Debug.LogWarning("voteUpdate frame missing arguments or votes");
                    }
                    break;
                }
            case "dayEnd":
                {
                    var de = JsonUtility.FromJson<DayEndEnvelope>(json);
                    string lynchedId = de.arguments[0].lynched;  // may be null or empty
                    OnDayEnd?.Invoke(lynchedId);
                    break;
                }
            case "gameOver":
                {
                    Debug.Log($"WS ← {json}");

                    // 1) Pull out the winner field via JsonUtility (it works for simple types)
                    var goEnv = JsonUtility.FromJson<GameOverEnvelope>(json);
                    if (goEnv.arguments == null || goEnv.arguments.Length == 0)
                    {
                        Debug.LogWarning("gameOver missing arguments");
                        break;
                    }
                    string winner = goEnv.arguments[0].winner;

                    // 2) Manually locate and parse the "roles" object
                    var rolesDict = new Dictionary<string, string>();
                    const string marker = "\"roles\":{";
                    int idx = json.IndexOf(marker, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        idx += marker.Length;
                        int end = json.IndexOf('}', idx);
                        if (end > idx)
                        {
                            string body = json.Substring(idx, end - idx);
                            // body = '"id1":"Villager","id2":"Werewolf",…'
                            foreach (var pair in body.Split(','))
                            {
                                var kv = pair.Split(':');
                                if (kv.Length == 2)
                                {
                                    string key = kv[0].Trim().Trim('"');
                                    string val = kv[1].Trim().Trim('"');
                                    rolesDict[key] = val;
                                }
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning("gameOver frame: no roles object found");
                    }

                    // 3) Fire the event
                    Debug.Log($"[NetworkManager] Parsed gameOver → winner={winner}, roles count={rolesDict.Count}");
                    LastWinner = winner;
                    OnGameOver?.Invoke(winner, rolesDict);
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
    public static Action<Dictionary<string, int>> OnVoteUpdate;
    public static Action<string> OnDayEnd;
    public static Action<string, Dictionary<string, string>> OnGameOver;
    public static Action<StartShufflePayload> OnStartShuffle;

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

    [Serializable]
    private class VoteArg
    {
        public string voter;
        public string target;
    }
    [Serializable] class VoteUpdateEnvelope { public VoteUpdateArg[] arguments; }
    [Serializable]
    class VoteUpdateArg
    {
        // We expect something like { votes:[ { playerId:"...",count:2 }, ... ] }
        public VoteCount[] votes;
    }
    [Serializable]
    class VoteCount
    {
        public string playerId;
        public int count;
    }

    [Serializable] class DayEndEnvelope { public DayEndArg[] arguments; }
    [Serializable] class DayEndArg { public string lynched; }
    [Serializable]
    private class GameOverEnvelope
    {
        public int type;
        public string target;
        public GameOverArg[] arguments;
    }
    [Serializable]
    private class GameOverArg
    {
        public string winner;
    }
    [Serializable]
    private class SerializableDictionary
    {
        // Unity will fill in these two parallel arrays
        public string[] keys;
        public string[] values;

        // helper property to turn it into a real dictionary
        public Dictionary<string, string> ToDictionary()
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < keys.Length; i++)
                dict[keys[i]] = values[i];
            return dict;
        }
    }
    [Serializable]
    class PubKeyArg { public string publicKey; }

    [Serializable]
    public class StartShufflePayload
    {
        public string agg_pk;
        public List<string[]> deck;
    }

    [Serializable]
    public class IncomingFrame<T>
    {
        public int type;
        public string target;
        public T[] arguments;
    }

    [Serializable]
    public class ShuffleDonePayload
    {
        public List<string[]> encrypted_deck;
        public List<string> public_inputs;
        public string proof;
    }
}
