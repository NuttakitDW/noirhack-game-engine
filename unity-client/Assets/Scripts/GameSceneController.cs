using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Handles phase header and role pill – no waiting overlay.
/// </summary>
public class GameSceneController : MonoBehaviour
{
    [Header("Day/Night Colors")]
    [SerializeField] Camera mainCamera;    // drag Main Camera here
    [SerializeField] Image headerBar;      // drag HeaderBar panel Image
    [SerializeField] Image gameFrame;      // drag GameFrame panel Image

    [Header("Night Colors")]
    [SerializeField] Color nightCamColor = new Color32(0x13, 0x1A, 0x29, 0xFF);
    [SerializeField] Color nightHeaderColor = new Color32(0x13, 0x19, 0x28, 0xFF);
    [SerializeField] Color nightFrameColor = new Color32(0x1F, 0x29, 0x37, 0xFF);

    [Header("Day Colors")]
    [SerializeField] Color dayCamColor = new Color32(0xE8, 0xE8, 0xE8, 0xFF);
    [SerializeField] Color dayHeaderColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    [SerializeField] Color dayFrameColor = new Color32(0xF0, 0xF0, 0xF0, 0xFF);

    [Header("Phase Header")]
    [SerializeField] Image phaseIcon;        // moon / sun Image
    [SerializeField] Sprite moonSprite;       // assign in Inspector
    [SerializeField] Sprite sunSprite;        // assign in Inspector
    [SerializeField] TMP_Text phaseTitle;       // "Night Phase" / "Day Phase"
    [SerializeField] TMP_Text dayBadgeText;     // "Day 1" label inside DayBadge

    [Header("Role Indicator")]
    [SerializeField] TMP_Text roleIndicator;    // pill text, default "Role : N/A"

    [Header("Actions Box")]
    [SerializeField] GameObject actionsBox;      // drag ActionsBox here
    [SerializeField] TMP_Text hintLabel;       // drag HintLabel

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
        actionsBox.SetActive(true);
        bool night = phase == "night";

        // ─── Swap the palette ───────────────────────────────────
        mainCamera.backgroundColor = night ? nightCamColor : dayCamColor;
        headerBar.color = night ? nightHeaderColor : dayHeaderColor;
        gameFrame.color = night ? nightFrameColor : dayFrameColor;

        phaseIcon.sprite = night ? moonSprite : sunSprite;
        phaseTitle.text = night ? "Night Phase" : "Day Phase";
        dayBadgeText.text = $"Day {round}";

        hintLabel.text = night ? "Select a player to perform an action" : "Choose a player to vote";
    }

    void UpdateRolePill(string role)
    {
        roleIndicator.text = $"     Role : {role}";
        roleIndicator.color = Color.red;
    }

    private void HandleGameOver(string winner, Dictionary<string, string> roles)
    {
        Debug.Log("[GameSceneController] Loading ResultsScene");
        SceneManager.LoadScene("ResultScene");
    }

    void OnEnable()
    {
        NetworkManager.OnGameOver += HandleGameOver;
    }
    void OnDisable()
    {
        NetworkManager.OnGameOver -= HandleGameOver;
    }
}
