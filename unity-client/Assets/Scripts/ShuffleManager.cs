using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class ShuffleManager : MonoBehaviour
{
    public static ShuffleManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        NetworkManager.OnStartShuffle += HandleStartShuffle;
    }

    void OnDestroy() =>
        NetworkManager.OnStartShuffle -= HandleStartShuffle;

    /* ───────────── event entry point ───────────── */
    void HandleStartShuffle(NetworkManager.StartShufflePayload p)
    {
        Debug.Log("[Shuffle] startShuffle received, posting to /prove");
        StartCoroutine(ShuffleCoroutine(p));
    }

    /* ───────────── coroutine that calls /prove ───────────── */
    IEnumerator ShuffleCoroutine(NetworkManager.StartShufflePayload p)
    {
        /* 1. Build rand (6-digit) & identity perm */
        int n = p.deck.Count;
        var rand = Enumerable.Range(0, n)
            .Select(_ => UnityEngine.Random.Range(0, 1_000_000).ToString("D6"))
            .ToList();

        var perm = new List<string[]>();
        for (int r = 0; r < n; r++)
        {
            var row = new string[n];
            for (int c = 0; c < n; c++) row[c] = (c == r ? "1" : "0");
            perm.Add(row);
        }

        var deck = p.deck;

        /* 3. Pack request object */
        var req = new ShuffleRequest
        {
            circuit_name = "shuffle4",
            data = new ShuffleData
            {
                g = "3",
                agg_pk = p.agg_pk,
                deck = deck,
                rand = rand,
                perm = perm
            }
        };
        string bodyJson = JsonConvert.SerializeObject(req, Formatting.Indented);
        Debug.Log("[Shuffle] ► /prove\n" + bodyJson);
        string pretty = JsonUtility.ToJson(req, /*prettyPrint*/ true);
        Debug.Log("[DEBUG] Pretty JSON:\n" + pretty);

        /* 4. POST */
        using var www = new UnityWebRequest("http://localhost:3000/prove", "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();

        /* 5. Print response */
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[Shuffle] ◄ /prove HTTP-error: " + www.error);
            yield break;
        }
        string respText = www.downloadHandler.text;
        var resp = JsonConvert.DeserializeObject<ProverResp>(respText);
        if (!resp.ok)
        {
            Debug.LogError($"[Shuffle] prover returned ok=false  code={resp.code}");
            yield break;
        }
        string proofHead = resp.data.proof.Length >= 10
                   ? resp.data.proof.Substring(0, 10) + "..."
                   : resp.data.proof;
        Debug.Log($"[Shuffle] ✔ proof generated (public_inputs={resp.data.public_inputs.Count}, " +
          $"proof={proofHead})");
        var done = new ShuffleDoneFrame
        {
            type = 1,
            target = "shuffleDone",
            arguments = new[] {
        new ShuffleDoneArg {
            encrypted_deck = resp.data.outputs.shuffledDeck,
            public_inputs  = resp.data.public_inputs,
            proof          = resp.data.proof
        }
    }
        };
        string doneJson = JsonConvert.SerializeObject(done);
        StartCoroutine(SendWs(doneJson));
        Debug.Log("[Shuffle] ► shuffleDone sent");
    }

    IEnumerator SendWs(string json)
    {
        var ws = NetworkManager.Instance.ws;
        if (ws == null || ws.State != NativeWebSocket.WebSocketState.Open)
        {
            Debug.LogError("WebSocket not open – shuffleDone not sent");
            yield break;
        }
        var task = ws.SendText(json);
        while (!task.IsCompleted) yield return null;
        if (task.Exception != null) Debug.LogError(task.Exception);
    }

    /* ───────── DTOs for JsonUtility ───────── */
    [Serializable]
    class ShuffleRequest
    {
        public string circuit_name;
        public ShuffleData data;
    }

    [Serializable]
    class ShuffleData
    {
        public string g;
        public string agg_pk;
        public List<string[]> deck;
        public List<string> rand;
        public List<string[]> perm;
    }
    [Serializable] public class DeckRow { public string[] row; }
    [Serializable] public class PermRow { public string[] row; }

    [Serializable]
    class ProverResp
    {
        public bool ok;
        public int code;
        public RespData data;

        [Serializable]
        public class RespData
        {
            public string proof;
            public List<string> public_inputs;
            public Outputs outputs;
        }
        [Serializable]
        public class Outputs
        {
            public List<string[]> shuffledDeck;
        }
    }
    [Serializable]
    class ShuffleDoneFrame
    {
        public int type;
        public string target;
        public ShuffleDoneArg[] arguments;
    }
    [Serializable]
    class ShuffleDoneArg
    {
        public List<string[]> encrypted_deck;
        public List<string> public_inputs;
        public string proof;
    }
}
