using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles phase header and role pill – no waiting overlay.
/// </summary>
public class GameSceneController : MonoBehaviour
{
    [Header("Phase Header")]
    [SerializeField] Image phaseIcon;        // moon / sun Image
    [SerializeField] Sprite moonSprite;       // assign in Inspector
    [SerializeField] Sprite sunSprite;        // assign in Inspector
    [SerializeField] TMP_Text phaseTitle;       // "Night Phase" / "Day Phase"
    [SerializeField] TMP_Text dayBadgeText;     // "Day 1" label inside DayBadge

    [Header("Role Indicator")]
    [SerializeField] TMP_Text roleIndicator;    // pill text, default "Role : N/A"

    /* ─────────────────────────────────────────────────────────────── */

    void Start()
    {
        // subscribe to network events
        NetworkManager.OnPhaseChange += OnPhaseChange; // (phase, round)
        NetworkManager.OnRole += UpdateRolePill;
    }

    void OnDestroy()
    {
        NetworkManager.OnPhaseChange -= OnPhaseChange;
        NetworkManager.OnRole -= UpdateRolePill;
    }

    /* ─────────────────────────────────────────────────────────────── */

    void OnPhaseChange(string phase, int round)
    {
        bool night = phase == "night";

        phaseIcon.sprite = night ? moonSprite : sunSprite;
        phaseTitle.text = night ? "Night Phase" : "Day Phase";
        dayBadgeText.text = $"Day {round}";
    }

    void UpdateRolePill(string role)
    {
        roleIndicator.text = $"Role : {role}";
        // You can tint by role colour here if you like
    }
}
