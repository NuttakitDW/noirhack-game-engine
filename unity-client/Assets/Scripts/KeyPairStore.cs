using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class KeyPairStore : MonoBehaviour
{
    public static KeyPairStore Instance { get; private set; }
    public string PublicKey { get; private set; }
    private string _secretKey;  // stays in memory only

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }

    /// <summary>
    /// Fetches sk/pk from the HTTP API and stores them in memory.
    /// </summary>
    public IEnumerator FetchKeyPair()
    {
        // 1. generate random 6-digit r
        var r = UnityEngine.Random.Range(100000, 1000000).ToString();

        // 2. build JSON payload
        var payload = JsonUtility.ToJson(new
        {
            circuit_name = "genElgamalKeyPair",
            data = new { g = "3", r = r }
        });

        // 3. POST to API
        using var www = new UnityWebRequest("http://localhost:3000/execute", "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(payload);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"KeyPairStore.FetchKeyPair failed: {www.error}");
            yield break;
        }

        // 4. parse
        var resp = JsonUtility.FromJson<KeyPairResponse>(www.downloadHandler.text);
        PublicKey = resp.data.outputs.pk;
        _secretKey = resp.data.outputs.sk;
        Debug.Log($"[KeyPairStore] Received pk={PublicKey} sk={_secretKey}");
    }

    /// <summary>Retrieve the secret for proofs.</summary>
    public string GetSecretKey() => _secretKey;

    [Serializable]
    private class KeyPairResponse
    {
        public bool ok;
        public int code;
        public ResponseData data;
    }

    [Serializable]
    private class ResponseData
    {
        public Outputs outputs;
        public string witness;
    }

    [Serializable]
    private class Outputs
    {
        public string sk;
        public string pk;
    }
}
