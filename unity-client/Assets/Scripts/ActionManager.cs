// Assets/Scripts/ActionManager.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine.Networking;

public class ActionManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] Button peekButton;
    [SerializeField] Button killButton;
    [SerializeField] Button voteButton;    // new
    [SerializeField] TMP_Text hintLabel;
    [SerializeField] TMP_Text toastLabel;

    PlayerCard currentCard;
    string currentPhase = "lobby";
    string targetId;

    static byte RoleToMessageByte(string role) => role switch
    {
        "WOLF" => 0x0a,
        "SEER" => 0x01,
        _ => 0x02
    };

    void OnEnable()
    {
        // start with everything off
        peekButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(false);
        voteButton.gameObject.SetActive(false);
        toastLabel.alpha = 0;

        PlayerCardEvents.OnCardSelected += HandleSelect;
        NetworkManager.OnPhaseChange += HandlePhaseChange;
        ActionManagerEvents.OnNightAck += HandleNightAck;
    }

    void OnDisable()
    {
        PlayerCardEvents.OnCardSelected -= HandleSelect;
        NetworkManager.OnPhaseChange -= HandlePhaseChange;
        ActionManagerEvents.OnNightAck -= HandleNightAck;
    }
    void HandleNightAck(string status, string reason)
    {
        if (status == "ok")
        {
            StartCoroutine(ShowToast("Action verified ✓"));
        }
        else
        {
            StartCoroutine(ShowToast($"Rejected: {reason}"));

            // Optional: let the player try again
            peekButton.interactable = killButton.interactable = true;
        }
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
        if (NetworkManager.PlayerState.Role != "SEER")
        {
            StartCoroutine(ShowToast("You are not the Seer")); return;
        }
        hintLabel.text = "Generating proof…";
        peekButton.interactable = killButton.interactable = false;
        StartCoroutine(ProveAndSend("peek"));
    }

    void DoKill()
    {
        if (NetworkManager.PlayerState.Role != "WOLF")
        {
            StartCoroutine(ShowToast("You are not the Werewolf")); return;
        }
        hintLabel.text = "Generating proof…";
        peekButton.interactable = killButton.interactable = false;
        StartCoroutine(ProveAndSend("kill"));
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
    IEnumerator ProveAndSend(string action)
    {
        Debug.Log($"ProveAndSend: {action} to {targetId}");
        if (NetworkManager.PlayerState.MyCardIndex is not int idx)
        {
            Debug.LogError("Prove: MyCardIndex is null"); yield break;
        }

        int realCompCount = NetworkManager.PlayerState.DecryptComponents.Count;

        var decryptComponentsPadded = new List<string>(
                NetworkManager.PlayerState.DecryptComponents);

        // fill up to 10 entries with "1"
        while (decryptComponentsPadded.Count < 10)
            decryptComponentsPadded.Add("1");


        var expected = new List<string>(new string[10]);
        for (int i = 0; i < expected.Count; i++)
            expected[i] = "0";
        expected[0] = RoleToMessageByte(NetworkManager.PlayerState.Role).ToString();

        var deckPadded = new List<string[]>(NetworkManager.PlayerState.EncryptedDeck);

        while (deckPadded.Count < 10)
            deckPadded.Add(new[] { "0", "0" });

        // ----- build JSON body -----------------------------------------
        var bodyObj = new
        {
            circuit_name = "verifyCardMessage",
            data = new
            {
                deck = deckPadded,
                deck_size = "4",
                card = NetworkManager.PlayerState.EncryptedDeck[idx],
                decrypt_components = decryptComponentsPadded,
                num_decrypt_components = realCompCount.ToString(),
                expected_messages = expected,
                num_expected_messages = "1",
                nullifier_secret = Random.Range(1, int.MaxValue).ToString()
            }
        };
        string json = JsonConvert.SerializeObject(bodyObj);
        Debug.Log($"Prove JSON: {json}");

        // ----- POST /prove ---------------------------------------------
        using var www = new UnityWebRequest("http://localhost:3000/prove", "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        www.SetRequestHeader("Content-Type", "application/json");
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Prove HTTP error: {www.error}"); yield break;
        }

        VerifyResp resp = JsonConvert.DeserializeObject<VerifyResp>(www.downloadHandler.text);
        if (!resp.ok)
        {
            Debug.LogError($"Prover returned ok=false (code {resp.code})"); yield break;
        }

        // ----- proof OK → send nightAction with proof ------------------
        NetworkManager.Instance.SendNightActionProof(
            action, targetId, resp.data.proof, resp.data.public_inputs);

        Debug.Log($"✓ Sent nightAction '{action}' with ZK proof");
    }

}

[System.Serializable]
class VerifyResp
{
    public bool ok;
    public int code;
    public RespData data;

    [System.Serializable]
    public class RespData
    {
        public string outputs;
        public string[] public_inputs;
        public string proof;
    }
}

public static class ActionManagerEvents
{
    public static System.Action<string, string> OnNightAck;
}