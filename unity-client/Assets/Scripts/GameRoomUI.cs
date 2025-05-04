using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameRoomUI : MonoBehaviour
{
    [SerializeField] Transform content;      // ScrollView Content
    [SerializeField] GameObject rowPrefab;    // PlayerRow prefab
    [SerializeField] TMP_Text readyCounter; // top label
    [SerializeField] Button readyButton;
    [SerializeField] TMP_Text readyLabel;

    bool localReady;

    void Start()
    {
        Populate();
        UpdateCounter();
        readyButton.onClick.AddListener(ToggleReady);
        NetworkManager.OnRoomUpdate += Refresh;
        SetButtonVisual();
    }
    void OnDestroy() => NetworkManager.OnRoomUpdate -= Refresh;

    void Refresh() { Populate(); UpdateCounter(); }

    /* -------------------------------------------------------------------- */
    void Populate()
    {
        foreach (Transform c in content) Destroy(c.gameObject);

        int index = 0;
        foreach (var p in NetworkManager.RoomState.Players)
        {
            var row = Instantiate(rowPrefab, content);
            row.transform.GetChild(0).GetComponent<TMP_Text>().text = p.name;
            row.transform.GetChild(1).GetComponent<TMP_Text>().text = p.ready ? "✓" : "";
            row.GetComponent<Image>().color = (index % 2 == 0) ? new Color(1, 1, 1, 0.04f) : new Color(1, 1, 1, 0f);
            index++;
        }
    }

    void ToggleReady()
    {
        SetButtonVisual();
        StartCoroutine(Pulse());
        if (NetworkManager.RoomState.Players.Count < NetworkManager.RoomState.MIN_PLAYERS) return;

        localReady = !localReady;
        NetworkManager.Instance.SendJson(new
        {
            type = 1,
            target = "ready",
            arguments = new[] { new { ready = localReady } }
        });

        /* optimistic UI until server echoes */
        var me = NetworkManager.RoomState.Players
                 .First(p => p.id == NetworkManager.PlayerState.MyId);
        me.ready = localReady;
        Refresh();
    }

    void SetButtonVisual()
    {
        var colors = readyButton.colors;
        if (localReady)
        {
            colors.normalColor = new Color(0.22f, 0.60f, 0.25f);   // green
            colors.highlightedColor = new Color(0.27f, 0.70f, 0.30f);
            colors.pressedColor = new Color(0.18f, 0.48f, 0.20f);
            readyButton.GetComponentInChildren<TMP_Text>().text = "Un-Ready";
        }
        else
        {
            colors.normalColor = new Color(0.40f, 0.40f, 0.40f);   // grey
            colors.highlightedColor = new Color(0.50f, 0.50f, 0.50f);
            colors.pressedColor = new Color(0.25f, 0.25f, 0.25f);
            readyLabel.text = localReady ? "Un-Ready" : "Ready";
        }
        readyButton.colors = colors;
    }

    IEnumerator Pulse()
    {
        Vector3 baseScale = readyButton.transform.localScale;
        Vector3 big = baseScale * 1.1f;
        float t = 0;
        while (t < 0.15f)
        {
            t += Time.unscaledDeltaTime;
            readyButton.transform.localScale =
                Vector3.Lerp(baseScale, big, t / 0.15f);
            yield return null;
        }
        t = 0;
        while (t < 0.15f)
        {
            t += Time.unscaledDeltaTime;
            readyButton.transform.localScale =
                Vector3.Lerp(big, baseScale, t / 0.15f);
            yield return null;
        }
    }


    void UpdateCounter()
    {
        int total = NetworkManager.RoomState.Players.Count;
        int ok = NetworkManager.RoomState.Players.Count(p => p.ready);

        readyButton.interactable = total >= NetworkManager.RoomState.MIN_PLAYERS;

        readyCounter.text = total < NetworkManager.RoomState.MIN_PLAYERS
            ? $"Waiting for players ({total}/4)"
            : $"{ok} / {total} ready";
    }
}
