using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

// Assumes NetworkManager has a static event OnStartShuffle and a SendRaw(object) method
public class ShuffleManager : MonoBehaviour
{
    void OnEnable()
    {
        NetworkManager.OnStartShuffle += HandleStartShuffle;
    }

    void OnDisable()
    {
        NetworkManager.OnStartShuffle -= HandleStartShuffle;
    }

    private void HandleStartShuffle(StartShufflePayload payload)
    {
        StartCoroutine(DoShuffle(payload));
    }

    private IEnumerator DoShuffle(StartShufflePayload payload)
    {
        int n = payload.deck.Count;
        // 1) build random 'rand'
        var rand = Enumerable.Range(0, n)
                             .Select(_ => UnityEngine.Random.Range(1, 1000000).ToString())
                             .ToList();
        // 2) build a random permutation matrix n x n
        var perm = new List<List<string>>();
        var cols = Enumerable.Range(0, n).ToList();
        for (int i = 0; i < n; i++)
        {
            var row = new List<string>(new string[n]);
            int pick = cols[UnityEngine.Random.Range(0, cols.Count)];
            cols.Remove(pick);
            for (int j = 0; j < n; j++)
                row[j] = (j == pick ? "1" : "0");
            perm.Add(row);
        }

        // 3) build prove request
        var req = new
        {
            circuit_name = "shuffle4",
            data = new
            {
                g = "3",
                agg_pk = payload.agg_pk,
                deck = payload.deck,
                rand = rand,
                perm = perm
            }
        };
        string body = JsonUtility.ToJson(req);

        // 4) POST to /prove
        using var www = new UnityWebRequest("http://localhost:3000/prove", "POST");
        byte[] br = System.Text.Encoding.UTF8.GetBytes(body);
        www.uploadHandler = new UploadHandlerRaw(br);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Shuffle prove error: " + www.error);
            yield break;
        }

        // 5) parse response
        var resp = JsonUtility.FromJson<ProveResponse>(www.downloadHandler.text);
        if (!resp.ok)
        {
            Debug.LogError("Shuffle circuit returned error code: " + resp.code);
            yield break;
        }
        var shuffledDeck = resp.data.outputs.shuffledDeck;
        var publicInputs = resp.data.public_inputs;
        var proof = resp.data.proof;

        // 6) send shuffleDone
        var payloadDone = new ShuffleDonePayload
        {
            encrypted_deck = shuffledDeck,
            public_inputs = publicInputs,
            proof = proof
        };
        var frame = new HubMessage<ShuffleDonePayload>
        {
            target = "shuffleDone",
            arguments = new[] { payloadDone }
        };
        NetworkManager.Instance.SendRaw(frame);
    }

    [Serializable]
    class ProveResponse
    {
        public bool ok;
        public int code;
        public ProveData data;
    }
    [Serializable]
    class ProveData
    {
        public ProveOutputs outputs;
        public List<string> public_inputs;
        public string proof;
    }
    [Serializable]
    class ProveOutputs
    {
        public List<string[]> shuffledDeck;
    }

    [Serializable]
    private class HubMessage<T>
    {
        public int type = 1;
        public string target;
        public T[] arguments;
    }
}