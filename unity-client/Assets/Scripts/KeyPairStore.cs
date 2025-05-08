using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class KeyPairStore : MonoBehaviour
{
    public static KeyPairStore Instance { get; private set; }
    public string PublicKey { get; private set; }
    private string _secretKey;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }

    public string GetSecretKey() => _secretKey;

    /// <summary>
    /// Fetches sk/pk from the HTTP API and stores them in memory.
    /// </summary>
    public IEnumerator FetchKeyPair()
    {
        // 1. six-digit random r (keeps leading zeros)
        string r = UnityEngine.Random.Range(0, 1_000_000).ToString("D6");

        // 2. build a strongly-typed payload
        var payload = new KeyPairRequest
        {
            circuit_name = "genElgamalKeyPair",
            data = new KeyPairData { g = "3", r = r }
        };
        string bodyJson = JsonUtility.ToJson(payload);

        // 3. POST
        using var www = new UnityWebRequest("http://localhost:3000/execute", "POST");
        www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(bodyJson));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"KeyPairStore.FetchKeyPair failed: {www.error}");
            yield break;
        }

        var resp = JsonUtility.FromJson<KeyPairResponse>(www.downloadHandler.text);
        PublicKey = resp.data.outputs.pk;
        _secretKey = resp.data.outputs.sk;
        Debug.Log($"[KeyPairStore] pk={PublicKey} sk={_secretKey}");
    }

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

    #region DTOs
    [Serializable]
    private class KeyPairRequest
    {
        public string circuit_name;
        public KeyPairData data;
    }
    [Serializable]
    private class KeyPairData
    {
        public string g;
        public string r;
    }
    #endregion
}
