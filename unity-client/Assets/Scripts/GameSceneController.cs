using System.Collections;
using TMPro;
using UnityEngine;

public class GameSceneController : MonoBehaviour
{
    /* Assign these in the Inspector */

    [Header("Waiting banner")]
    [SerializeField] GameObject waitingPanel;   // dark overlay / panel
    [SerializeField] TMP_Text waitingLabel;   // "Players in room: x / 4"

    [Header("Phase panels")]
    [SerializeField] GameObject nightPanel;     // root of Night UI (inactive by default)
    [SerializeField] GameObject dayPanel;       // root of Day UI  (inactive by default)

    [Header("Role popup (optional)")]
    [SerializeField] GameObject rolePopup;      // small panel w/ text, inactive by default
    [SerializeField] TMP_Text roleText;
    [SerializeField] float rolePopupSeconds = 3f;

    void Start()
    {
        // Initial label after my own join
        UpdateWaitingLabel();

        // Subscribe to NetworkManager events
        NetworkManager.OnRoomUpdate += UpdateWaitingLabel;
        NetworkManager.OnPhaseChange += HandlePhaseChange;
        NetworkManager.OnRole += ShowRolePopup;
    }

    void OnDestroy()
    {
        NetworkManager.OnRoomUpdate -= UpdateWaitingLabel;
        NetworkManager.OnPhaseChange -= HandlePhaseChange;
        NetworkManager.OnRole -= ShowRolePopup;
    }

    /* ─────────────────────────────────────────────────────────── */

    void UpdateWaitingLabel()
    {
        int total = NetworkManager.RoomState.Players.Count;
        int needed = NetworkManager.RoomState.MIN_PLAYERS;

        waitingLabel.text = $"Players in room: {total} / {needed}";
    }

    void HandlePhaseChange(string phase)        // "night" or "day"
    {
        // Hide the waiting overlay once the game officially starts
        waitingPanel.SetActive(false);

        nightPanel?.SetActive(phase == "night");
        dayPanel?.SetActive(phase == "day");
    }

    void ShowRolePopup(string role)
    {
        if (rolePopup == null) return;

        roleText.text = $"You are a\n{role}";
        rolePopup.SetActive(true);
        StartCoroutine(HideRoleLater());
    }

    IEnumerator HideRoleLater()
    {
        yield return new WaitForSecondsRealtime(rolePopupSeconds);
        rolePopup.SetActive(false);
    }
}
