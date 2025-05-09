// Assets/Resources/NetworkManager.cs   (singleton across scenes)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using System.Text;


public class NetworkManager : MonoBehaviour
{
    /*───────────────────────── Singleton ─────────────────────────*/
    public static NetworkManager Instance { get; private set; }
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

    IEnumerator DoFinalDecrypt(string[] cipher, Action<string[]> onDone = null)
    {
        // 1) build request body
        string mySk = KeyPairStore.Instance.GetSecretKey();

        var bodyObj = new
        {
            circuit_name = "decryptOneLayer",
            data = new
            {
                g = "3",
                card = cipher,   // ["123456789", "987654321"]
                sk = mySk
            }
        };
        string bodyJson = JsonConvert.SerializeObject(bodyObj);
        Debug.Log("[Decrypt] /execute body: " + bodyJson);

        // 2) POST to http://localhost:3000/execute
        using var www = new UnityEngine.Networking.UnityWebRequest(
                           "http://localhost:3000/execute",
                           UnityEngine.Networking.UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(
                                Encoding.UTF8.GetBytes(bodyJson)),
            downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer()
        };
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();

        if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            Debug.LogError($"/execute decrypt failed: {www.error}");
            yield break;
        }

        // 3) parse response
        var resp = JsonConvert.DeserializeObject<DecryptOneLayerResp>(
                       www.downloadHandler.text);
        if (resp == null || !resp.ok)
        {
            Debug.LogError("/execute decrypt responded ok=false");
            yield break;
        }

        string[] decrypted = resp.data.outputs.decryptedCard;   // [c1, message]
        string comp = resp.data.outputs.decryptComponent;

        // optional: stash the component in PlayerState
        PlayerState.DecryptComponents.Add(comp);

        Debug.Log($"[Decrypt] FINAL -> msg='{decrypted[1]}' comp='{comp}'");

        onDone?.Invoke(decrypted);
    }
    IEnumerator DoPartialDecrypt(NeedDecryptPayload p)
    {
        string mySk = KeyPairStore.Instance.GetSecretKey();

        // 1) build JSON body for decryptOneLayer
        var bodyObj = new
        {
            circuit_name = "decryptOneLayer",
            data = new
            {
                g = "3",
                card = p.cipher,
                sk = mySk
            }
        };
        string bodyJson = JsonConvert.SerializeObject(bodyObj);
        Debug.Log("[Decrypt] /prove body: " + bodyJson);

        // 2) POST to prover
        using var www = new UnityEngine.Networking.UnityWebRequest("http://localhost:3000/prove", UnityEngine.Networking.UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson)),
            downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer()
        };
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();

        if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            Debug.LogError($"/prove decrypt failed: {www.error}");
            yield break;
        }

        // 3) parse response
        var resp = JsonConvert.DeserializeObject<DecryptOneLayerResp>(
                       www.downloadHandler.text);
        if (resp == null || !resp.ok)
        {
            Debug.LogError("/prove decrypt responded ok=false");
            yield break;
        }

        string[] partial = resp.data.outputs.decryptedCard;
        string comp = resp.data.outputs.decryptComponent;

        PlayerState.DecryptComponents.Add(comp);

        var frame = new DecryptCardFrame
        {
            arguments = new[] { new DecryptCardArg {
        @for      = p.@for,
        card      = p.card,
        partial   = partial,
        component = comp
    }}
        };
        Instance.SendRaw(frame);
        Debug.Log($"[Decrypt] sent decryptCard for {p.@for} (card {p.card})");
    }

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

    void SendPickCard(int index)
    {
        var frame = new PickCardFrame
        {
            arguments = new[] { new PickCardArg { card = index } }
        };
        SendRaw(frame);   // now JsonUtility can handle it
    }

    /*───────────────────────── Incoming parsing ──────────────────*/

    void HandleShuffleComplete(ShuffleCompletePayload p)
    {
        PlayerState.EncryptedDeck = p.deck;
        int idx = UnityEngine.Random.Range(0, 4);
        PlayerState.MyCardIndex = idx;
        SendPickCard(idx);
    }
    void HandleMessage(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"WS ← {json}");

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
            case "startShuffle":
                {
                    var shufEnv = JsonConvert.DeserializeObject<
                                     IncomingFrame<StartShufflePayload>>(json);

                    var payload = shufEnv?.arguments?[0];
                    if (payload == null || payload.deck == null)
                    {
                        Debug.LogError("startShuffle deck is null after JSON parse");
                        break;
                    }

                    Debug.Log($"[Network] startShuffle: deck rows={payload.deck.Count}");
                    OnStartShuffle?.Invoke(payload);
                    break;
                }
            case "shuffleComplete":
                {
                    var compEnv = JsonConvert.DeserializeObject<
                                      IncomingFrame<ShuffleCompletePayload>>(json);

                    if (compEnv?.arguments == null || compEnv.arguments.Length == 0)
                    {
                        Debug.LogWarning("shuffleComplete frame missing arguments");
                        break;
                    }

                    var payload = compEnv.arguments[0];

                    PlayerState.EncryptedDeck = payload.deck;
                    Debug.Log($"[Deck] stored {PlayerState.EncryptedDeck.Count} encrypted rows");

                    Debug.Log($"[Network] shuffleComplete: deck rows = {payload.deck.Count}");
                    HandleShuffleComplete(payload);
                    break;
                }
            case "cardTaken":
                {
                    var cardEnv = JsonConvert.DeserializeObject<
                                      IncomingFrame<CardTakenPayload>>(json);
                    var pay = cardEnv?.arguments?[0];
                    if (pay == null) break;

                    Debug.Log($"[Pick] server replied {pay.status} for card {pay.card}");

                    if (pay.status == "ok")
                    {
                        PlayerState.MyCardIndex = pay.card;   // success – nothing more to do
                    }
                    else            // "denied"  → pick another free index
                    {
                        PlayerState.TakenIndices.Add(pay.card);

                        // choose first free index 0-3 not yet denied
                        int idx = Enumerable.Range(0, 4)
                                  .First(i => !PlayerState.TakenIndices.Contains(i));

                        Debug.Log($"[Pick] retrying with card {idx}");
                        SendPickCard(idx);
                    }

                    break;
                }
            case "needDecrypt":
                {
                    var needDeEnv = JsonConvert.DeserializeObject<
                                 IncomingFrame<NeedDecryptPayload>>(json);
                    var p = needDeEnv?.arguments?[0];
                    if (p == null) break;

                    Debug.Log($"[Decrypt] needDecrypt → helper for {p.@for} card {p.card}");
                    StartCoroutine(DoPartialDecrypt(p));
                    break;
                }
            case "partialReady":
                {
                    // Strong-typed parse of the frame
                    var prEnv = JsonConvert.DeserializeObject<
                        IncomingFrame<PartialReadyPayload>>(json);
                    var p = prEnv?.arguments?[0];
                    if (p == null)
                    {
                        Debug.LogWarning("partialReady frame missing payload");
                        break;
                    }

                    // 1) Cache the cipher (["cx","cy"]) for the final decrypt
                    PlayerState.PendingCipher = p.partial;

                    // 2) Stash the decrypt component for completeness / auditing
                    PlayerState.DecryptComponents.Add(p.component);

                    // 3) Log so we can confirm in the Console
                    Debug.Log($"[Decrypt] partialReady – saved cipher (card {p.card})");

                    break;
                }
            case "allPartsReady":
                {
                    // Parse (mainly for the 'card' field; optional)
                    var arEnv = JsonConvert.DeserializeObject<
                        IncomingFrame<AllPartsReadyPayload>>(json);
                    int cardIdx = arEnv?.arguments?[0]?.card ?? -1;

                    if (PlayerState.PendingCipher == null)
                    {
                        Debug.LogError("allPartsReady received but PendingCipher is null!");
                        break;
                    }

                    // Kick off the last decrypt layer
                    StartCoroutine(DoFinalDecrypt(PlayerState.PendingCipher, decrypted =>
                    {
                        string hex = decrypted[1];                    // e.g. "0x01"
                        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            hex = hex[2..];                           // drop "0x"

                        if (!byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                           null, out byte code))
                        {
                            Debug.LogError($"Could not parse rôle byte: {decrypted[1]}");
                            return;                                   // abort gracefully
                        }

                        string roleText = RoleLookup.TryGetValue(code, out var name)
                      ? name
                      : $"UNKNOWN({code:X2})";

                        // ───── 3. Store, log, broadcast ─────
                        PlayerState.Role = roleText;

                        Debug.Log($"🂠 FINAL ROLE for card {cardIdx} → {roleText}");

                        OnRole?.Invoke(roleText);
                    }));

                    // Clear the stash to avoid accidental reuse
                    PlayerState.PendingCipher = null;
                    break;
                }
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
        public static List<string[]> EncryptedDeck = new();
        public static int? MyCardIndex;
        public static readonly HashSet<int> TakenIndices = new();
        public static readonly List<string> DecryptComponents = new();
        public static string[] PendingCipher;

    }
    public static string LastWinner { get; private set; }

    static readonly Dictionary<byte, string> RoleLookup = new()
    {
        { 0x00, "WOLF"      },
        { 0x01, "SEER"      },
        { 0x02, "VILLAGER"  },
    };


    /*───────────────────────── Events ────────────────────────────*/
    public static Action OnRoomUpdate;
    public static event Action<StartShufflePayload> OnStartShuffle;
    public static Action<string, int> OnPhaseChange;
    public static Action<string> OnRole;
    public static Action<string, string> OnPeekResult;
    public static Action<Dictionary<string, int>> OnVoteUpdate;
    public static Action<string> OnDayEnd;
    public static Action<string, Dictionary<string, string>> OnGameOver;
    public static Action<string> OnNightEnd;
    public static string[] PendingCipher;

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
    public class IncomingFrame<T>
    {
        public int type;
        public string target;
        public T[] arguments;
    }

    [Serializable]
    public class StringRow   // wrapper for one row
    {
        public string[] row;
    }
    [Serializable]
    public class StartShufflePayload
    {
        public string agg_pk;
        public List<string[]> deck;
    }
    [Serializable]
    class ProveRequest
    {
        public string circuit_name;
        public ProveData data;
    }

    [Serializable]
    class ProveData
    {
        public string g;
        public string agg_pk;
        public List<string[]> deck;
        public List<string> rand;
        public List<List<string>> perm;
    }

    [Serializable]
    public class ShuffleCompletePayload
    {
        public List<string[]> deck;
    }
    [Serializable]
    public class CardTakenPayload
    {
        public string status;   // "ok" or "denied"
        public int card;     // index 0-3
    }
    [Serializable] class PickCardArg { public int card; }
    [Serializable]
    class PickCardFrame
    {
        public int type = 1;
        public string target = "pickCard";
        public PickCardArg[] arguments;
    }
    [Serializable]
    public class NeedDecryptPayload
    {
        public string @for;      // requester’s player-id
        public int card;      // index 0-3
        public string[] cipher;  // ["x","y"] at this stage
    }

    [Serializable]
    public class DecryptOneLayerResp   // shape of /prove response (partial)
    {
        public bool ok;
        public int code;
        public RespData data;

        [Serializable]
        public class RespData
        {
            public Outputs outputs;
            [Serializable]
            public class Outputs
            {
                public string[] decryptedCard;      // two-element array
                public string decryptComponent;   // cᵢ
            }
        }
    }
    [Serializable]
    class DecryptCardArg
    {
        public string @for;
        public int card;
        public string[] partial;
        public string component;
    }

    [Serializable]
    class DecryptCardFrame
    {
        public int type = 1;
        public string target = "decryptCard";
        public DecryptCardArg[] arguments;
    }

    [Serializable]
    class PartialReadyPayload
    {
        public int card;            // index 0-3 (handy for logging)
        public string[] partial;    // 2-element cipher after helpers finished
        public string component;    // helper’s decrypt component cᵢ
    }

    [Serializable]
    class AllPartsReadyPayload
    {
        public int card;            // only used for logging / UI (optional)
    }
}
