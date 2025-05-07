using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

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

        var perm = new List<List<string>>();
        for (int r = 0; r < n; r++)
        {
            var row = new List<string>(Enumerable.Repeat("0", n));
            row[r] = "1";                 // identity – change later if you need shuffle
            perm.Add(row);
        }

        /* 2. Unwrap StringRow -> string[] */
        var deckRows = p.deck.Select(r => r.row).ToList();

        /* 3. Pack request object */
        var req = new ShuffleRequest
        {
            circuit_name = "shuffle4",
            data = new ShuffleData
            {
                g = "3",
                agg_pk = p.agg_pk,
                deck = deckRows,
                rand = rand,
                perm = perm
            }
        };
        string bodyJson = JsonUtility.ToJson(req);
        Debug.Log("[Shuffle] ► /prove\n" + bodyJson);

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
        Debug.Log("[Shuffle] ◄ /prove response:\n" + www.downloadHandler.text);
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
        public List<List<string>> perm;
    }
}
