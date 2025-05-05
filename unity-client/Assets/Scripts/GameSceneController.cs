using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameSceneController : MonoBehaviour
{
    [Header("Waiting Overlay")]
    [SerializeField] GameObject waitingPanel;
    [SerializeField] TMP_Text waitingLabel;

    [Header("Phase Header")]
    [SerializeField] Image phaseIcon;
    [SerializeField] Sprite moonSprite;
    [SerializeField] Sprite sunSprite;
    [SerializeField] TMP_Text phaseTitle;
    [SerializeField] TMP_Text dayBadgeText;

    [Header("Role Pill")]
    [SerializeField] TMP_Text roleIndicator;   // “Role : N/A” at start

    /* -------------------------------------------------------------- */
    void Start()
    {
        UpdateWaitingLabel();                        // initial 1 / 4
        NetworkManager.OnRoomUpdate += UpdateWaitingLabel;
        NetworkManager.OnPhaseChange += OnPhaseChange;   // (phase, round)
        NetworkManager.OnRole += UpdateRolePill;  // (string role)
    }
    void OnDestroy()
    {
        NetworkManager.OnRoomUpdate -= UpdateWaitingLabel;
        NetworkManager.OnPhaseChange -= OnPhaseChange;
        NetworkManager.OnRole -= UpdateRolePill;
    }

    /* -------- waiting banner -------- */
    void UpdateWaitingLabel()
    {
        int total = NetworkManager.RoomState.Players.Count;
        waitingLabel.text = $"Players in room: {total} / 4";
    }

    /* -------- phase changes -------- */
    void OnPhaseChange(string phase, int round)
    {
        waitingPanel.SetActive(false);               // hide overlay on first phase

        bool isNight = phase == "night";
        phaseIcon.sprite = isNight ? moonSprite : sunSprite;
        phaseTitle.text = isNight ? "Night Phase" : "Day Phase";
        dayBadgeText.text = $"Day {round}";
    }

    /* -------- role pill update -------- */
    void UpdateRolePill(string role)
    {
        roleIndicator.text = $"Role : {role}";
        // optional colour tweak
        // roleIndicator.color = role == "Werewolf" ? wolfRed : defaultWhite;
    }
}
