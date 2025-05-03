using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private InputField urlInput;    // wss://…
    [SerializeField] private InputField nameInput;   // YourName
    [SerializeField] private Button joinButton;

    private const string DEV_URL = "ws://localhost:8080/ws";

    void Start() => joinButton.onClick.AddListener(OnJoin);

    void OnJoin()
    {
        var url = urlInput.text.Trim();
        if (string.IsNullOrEmpty(url)) url = DEV_URL;

        var playerName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(playerName))
            playerName = "Player" + Random.Range(1000, 9999);

        if (NetworkManager.Instance == null)
            Instantiate(Resources.Load<GameObject>("NetworkManager"));

        NetworkManager.Instance.Connect(url, playerName);
    }
}
