// Assets/Scripts/NightActionManager.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NightActionManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] Button peekButton;
    [SerializeField] Button killButton;
    [SerializeField] TMP_Text hintLabel;     // main hint in ActionsBox
    [SerializeField] TMP_Text toastLabel;    // red error banner

    /* ───────────────────────── */
    PlayerCard currentCard;   // the card that is currently selected
    string targetId;      // playerId of that card

    /* ───── Unity lifecycle ─── */
    void OnEnable()
    {
        peekButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(false);
        toastLabel.alpha = 0;

        PlayerCardEvents.OnCardSelected += HandleSelect;
    }
    void OnDisable()
    {
        PlayerCardEvents.OnCardSelected -= HandleSelect;
    }

    /* ───────── selection ────── */
    void HandleSelect(PlayerCard card)
    {
        // Deselect previous
        if (currentCard && currentCard != card)
            currentCard.SetSelected(false);

        currentCard = card;
        targetId = card.PlayerId;

        bool isSelf = targetId == NetworkManager.PlayerState.MyId;

        if (isSelf)
        {
            hintLabel.text = "You cannot target yourself";
            peekButton.gameObject.SetActive(false);
            killButton.gameObject.SetActive(false);
            StartCoroutine(ShowToast("Choose another player"));
            return;
        }

        // Highlight new selection
        card.SetSelected(true);
        hintLabel.text = "Choose Peek or Kill";

        // Wire buttons each time (clears previous listeners)
        peekButton.onClick.RemoveAllListeners();
        killButton.onClick.RemoveAllListeners();

        peekButton.onClick.AddListener(DoPeek);
        killButton.onClick.AddListener(DoKill);

        peekButton.gameObject.SetActive(true);
        killButton.gameObject.SetActive(true);
    }

    /* ───────── actions ──────── */
    void DoPeek()
    {
        if (NetworkManager.PlayerState.Role != "Seer")
        {
            StartCoroutine(ShowToast("You are not the Seer"));
            return;
        }

        SendAction("peek");
        hintLabel.text = "Peek sent…";
    }

    void DoKill()
    {
        if (NetworkManager.PlayerState.Role != "Werewolf")
        {
            StartCoroutine(ShowToast("You are not the Werewolf"));
            return;
        }

        SendAction("kill");
        hintLabel.text = "Kill sent…";
    }

    void SendAction(string action)
    {
        NetworkManager.Instance.SendNightAction(action, targetId);
        peekButton.interactable = false;
        killButton.interactable = false;
    }

    /* ───────── toast helper ─── */
    IEnumerator ShowToast(string msg, float seconds = 2f)
    {
        toastLabel.text = msg;
        toastLabel.alpha = 1;
        yield return new WaitForSeconds(seconds);
        toastLabel.alpha = 0;
    }
}
