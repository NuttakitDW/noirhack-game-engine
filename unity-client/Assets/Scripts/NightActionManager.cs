using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NightActionManager : MonoBehaviour
{
    [SerializeField] Button peekButton;
    [SerializeField] Button killButton;
    [SerializeField] TMP_Text hintLabel;    // "Select a player…"
    [SerializeField] TMP_Text toastLabel;   // red error message

    PlayerCard current;                   // last selected card

    /* ---------------------------------------------------------- */
    void OnEnable()
    {
        peekButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(false);
        toastLabel.alpha = 0;

        PlayerCardEvents.OnCardSelected += HandleSelect;
    }
    void OnDisable() => PlayerCardEvents.OnCardSelected -= HandleSelect;

    /* ---------------------------------------------------------- */
    void HandleSelect(PlayerCard card)
    {
        // deselect previously highlighted card
        if (current && current != card) current.SetSelected(false);
        current = card;

        bool isMe = card.PlayerId == NetworkManager.PlayerState.MyId;

        if (isMe)
        {
            // show toast + disable buttons
            peekButton.gameObject.SetActive(false);
            killButton.gameObject.SetActive(false);
            hintLabel.text = "You cannot target yourself";
            StartCoroutine(Toast());
        }
        else
        {
            card.SetSelected(true);
            hintLabel.text = "Choose Peek or Kill";
            peekButton.gameObject.SetActive(true);
            killButton.gameObject.SetActive(true);
        }
    }

    /* ---------------------------------------------------------- */
    IEnumerator Toast()
    {
        toastLabel.alpha = 1;
        yield return new WaitForSeconds(2);
        toastLabel.alpha = 0;
        hintLabel.text = "Select a player to perform an action";
        if (current) current.SetSelected(false);
    }
}
