using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private Button joinButton;

    void Start() => joinButton.onClick.AddListener(OnJoin);

    void OnJoin()
    {
        if (NetworkManager.Instance == null)
        {
            var prefab = Resources.Load<GameObject>("NetworkManager");
            if (prefab == null) { Debug.LogError("NetworkManager prefab missing!"); return; }
            Instantiate(prefab);                          // Awake() sets Instance
        }

        NetworkManager.Instance.Connect();
    }

}
