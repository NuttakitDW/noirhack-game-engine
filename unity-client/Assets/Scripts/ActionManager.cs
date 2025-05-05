// Assets/Scripts/ActionManager.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ActionManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] Button peekButton;
    [SerializeField] Button killButton;
    [SerializeField] Button voteButton;    // new
    [SerializeField] TMP_Text hintLabel;
    [SerializeField] TMP_Text toastLabel;

    PlayerCard currentCard;
    string currentPhase = "lobby";      // tracks night vs day
    string targetId;

    void OnEnable()
    {
        // start with everything off
        peekButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(false);
        voteButton.gameObject.SetActive(false);
        toastLabel.alpha = 0;

        PlayerCardEvents.OnCardSelected += HandleSelect;
        NetworkManager.OnPhaseChange += HandlePhaseChange;
    }

    void OnDisable()
    {
        PlayerCardEvents.OnCardSelected -= HandleSelect;
        NetworkManager.OnPhaseChange -= HandlePhaseChange;
    }

    void HandlePhaseChange(string phase, int round)
    {
        currentPhase = phase;

        // clear any prior selection
        if (currentCard) currentCard.SetSelected(false);
        peekButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(false);
        voteButton.gameObject.SetActive(false);

        // update hint text
        if (phase == "night")
        {
            hintLabel.text = "Select a player to perform an action";
        }
        else    // day
        {
            hintLabel.text = "Select a player to vote";
        }
    }

    void HandleSelect(PlayerCard card)
    {
        // deselect old
        if (currentCard && currentCard != card)
            currentCard.SetSelected(false);

        currentCard = card;
        targetId = card.PlayerId;

        bool isSelf = targetId == NetworkManager.PlayerState.MyId;
        bool isNight = currentPhase == "night";

        // clear all buttons
        peekButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(false);
        voteButton.gameObject.SetActive(false);

        if (isSelf)
        {
            hintLabel.text = isNight
                ? "You cannot target yourself"
                : "You cannot vote for yourself";
            StartCoroutine(ShowToast("Choose another player"));
            return;
        }

        // Non-self selection
        currentCard.SetSelected(true);

        if (isNight)
        {
            // Night: show Peek/Kill
            hintLabel.text = "Choose Peek or Kill";

            peekButton.onClick.RemoveAllListeners();
            killButton.onClick.RemoveAllListeners();

            peekButton.onClick.AddListener(DoPeek);
            killButton.onClick.AddListener(DoKill);

            peekButton.gameObject.SetActive(true);
            killButton.gameObject.SetActive(true);
        }
        else
        {
            // Day: show Vote
            hintLabel.text = "Choose Vote";

            voteButton.onClick.RemoveAllListeners();
            voteButton.onClick.AddListener(DoVote);

            voteButton.gameObject.SetActive(true);
        }
    }

    void DoPeek()
    {
        if (NetworkManager.PlayerState.Role != "Seer")
        {
            StartCoroutine(ShowToast("You are not the Seer"));
            return;
        }
        hintLabel.text = "Peek sent…";
        NetworkManager.Instance.SendNightAction("peek", targetId);
        peekButton.interactable = killButton.interactable = false;
    }

    void DoKill()
    {
        if (NetworkManager.PlayerState.Role != "Werewolf")
        {
            StartCoroutine(ShowToast("You are not the Werewolf"));
            return;
        }
        hintLabel.text = "Kill sent…";
        NetworkManager.Instance.SendNightAction("kill", targetId);
        peekButton.interactable = killButton.interactable = false;
    }

    void DoVote()
    {
        hintLabel.text = "Vote sent…";
        // you’ll implement SendVote in NetworkManager later
        NetworkManager.Instance.SendVote(targetId);
        voteButton.interactable = false;
    }

    IEnumerator ShowToast(string msg, float duration = 2f)
    {
        toastLabel.text = msg;
        toastLabel.alpha = 1f;
        yield return new WaitForSeconds(duration);
        toastLabel.alpha = 0f;
    }
}
