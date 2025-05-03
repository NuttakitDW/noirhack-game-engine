using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private InputField urlInput;   // <-- new field
    [SerializeField] private Button joinButton;

    private const string DEV_URL = "ws://localhost:8080/ws";

    void Start() => joinButton.onClick.AddListener(OnJoin);

    void OnJoin()
    {
        // 1. grab text, or fall back
        var url = urlInput.text.Trim();
        if (string.IsNullOrEmpty(url)) url = DEV_URL;

        // 2. spawn NetworkManager if absent
        if (NetworkManager.Instance == null)
        {
            var prefab = Resources.Load<GameObject>("NetworkManager");
            Instantiate(prefab);
        }

        // 3. connect
        NetworkManager.Instance.Connect(url);
    }
}
