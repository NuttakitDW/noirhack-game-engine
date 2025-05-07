using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Listens for the startShuffle event from NetworkManager, calls the /prove API,
/// and returns a shuffleDone frame with encrypted_deck, public_inputs, and proof.
/// Attach this script to any persistent GameObject (e.g. the one that hosts NetworkManager).
/// </summary>
public class ShuffleManager : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        NetworkManager.OnStartShuffle += HandleStartShuffle;
    }

    void OnDestroy()
    {
        NetworkManager.OnStartShuffle -= HandleStartShuffle;
    }

    private void HandleStartShuffle(NetworkManager.StartShufflePayload payload)
    {
        Debug.Log("StartShuffle received – beginning /prove call");
        StartCoroutine(PerformShuffleCoroutine(payload));
    }

    private IEnumerator PerformShuffleCoroutine(NetworkManager.StartShufflePayload p)
    {
        if (p.deck == null || p.deck.Count == 0)
        {
            Debug.LogError("Shuffle payload missing deck data -> abort");
            yield break;
        }

        int n = p.deck.Count;

        // 1. Generate rand – one random 6-digit string per card
        List<string> rand = Enumerable.Range(0, n)
            .Select(_ => UnityEngine.Random.Range(100000, 1000000).ToString())
            .ToList();

        // 2. Generate a random n×n permutation matrix with exactly one "1" per row/col
        var perm = new List<List<string>>();
        var available = Enumerable.Range(0, n).ToList();
        for (int row = 0; row < n; row++)
        {
            int colPick = available[UnityEngine.Random.Range(0, available.Count)];
            available.Remove(colPick);

            var rowList = new List<string>(new string[n]);
            for (int c = 0; c < n; c++) rowList[c] = (c == colPick ? "1" : "0");
            perm.Add(rowList);
        }

        // 3. Build the /prove request body (using an anonymous type + JsonUtility helper)
        var proveReq = new ProveRequest
        {
            circuit_name = "shuffle4",
            data = new ProveData
            {
                g = "3",
                agg_pk = p.agg_pk,
                deck = p.deck,
                rand = rand,
                perm = perm
            }
        };
        string bodyJson = JsonUtility.ToJson(proveReq);

        // 4. POST to the prover service
        using var www = UnityWebRequest.PostWwwForm("http://localhost:3000/prove", "");
        www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(bodyJson));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("/prove call failed: " + www.error);
            yield break;
        }

        // 5. Parse the prover response
        var resp = JsonUtility.FromJson<ProveResponse>(www.downloadHandler.text);
        if (!resp.ok)
        {
            Debug.LogError("/prove responded ok=false");
            yield break;
        }

        // 6. Build shuffleDone frame
        var doneFrame = new ShuffleDoneFrame
        {
            type = 1,
            target = "shuffleDone",
            arguments = new[]
            {
                new ShuffleDonePayload
                {
                    encrypted_deck = resp.data.outputs.shuffledDeck,
                    public_inputs  = resp.data.public_inputs,
                    proof          = resp.data.proof
                }
            }
        };
        string doneJson = JsonUtility.ToJson(doneFrame);
        Debug.Log("WS → " + doneJson);

        // 7. Send over WebSocket
        awaitSend(doneJson);
    }

    /// <summary>
    /// Helper to send text over the existing WebSocket (fire & forget).
    /// </summary>
    private async void awaitSend(string msg)
    {
        WebSocket ws = NetworkManager.Instance.ws;
        if (ws != null && ws.State == WebSocketState.Open)
        {
            await ws.SendText(msg);
        }
        else
        {
            Debug.LogError("WebSocket not open – couldn’t send shuffleDone");
        }
    }

    #region DTOs for JSON serialization / deserialization

    [Serializable]
    private class ProveRequest
    {
        public string circuit_name;
        public ProveData data;
    }

    [Serializable]
    private class ProveData
    {
        public string g;
        public string agg_pk;
        public List<string[]> deck;
        public List<string> rand;
        public List<List<string>> perm;
    }

    [Serializable]
    private class ProveResponse
    {
        public bool ok;
        public int code;
        public ProveRespData data;
    }

    [Serializable]
    private class ProveRespData
    {
        public ProveOutputs outputs;
        public List<string> public_inputs;
        public string proof;
    }

    [Serializable]
    private class ProveOutputs
    {
        public List<List<string>> shuffledDeck;
    }

    [Serializable]
    private class ShuffleDoneFrame
    {
        public int type;
        public string target;
        public ShuffleDonePayload[] arguments;
    }

    #endregion
}
