// Assets/Scripts/ResultSceneController.cs
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ResultSceneController : MonoBehaviour
{
    [Header("Backgrounds")]
    [SerializeField] private Image backgroundImage;       // Full-screen panel’s Image
    [SerializeField] private Sprite villagersBackground;   // assign your Villagers win bg
    [SerializeField] private Sprite werewolvesBackground;  // assign your Werewolves win bg

    [Header("Result UI")]
    [SerializeField] private TMP_Text winnerLabel;         // “Villagers Win!” / “Werewolves Win!”
    [SerializeField] private RectTransform rolesContent;   // Content container of RolesList
    [SerializeField] private GameObject roleEntryPrefab;   // prefab of a TMP_Text entry

    [Header("Play Again Button (optional)")]
    [SerializeField] private Button playAgainButton;      // assign if you have one

    private void Awake()
    {
        // Hide play‐again until we know game is over
        if (playAgainButton != null)
            playAgainButton.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        NetworkManager.OnGameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        NetworkManager.OnGameOver -= HandleGameOver;
    }

    void HandleGameOver(string winner, Dictionary<string, string> roles)
    {
        // 1. Swap the background
        if (winner == "villagers")
            backgroundImage.sprite = villagersBackground;
        else
            backgroundImage.sprite = werewolvesBackground;

        // 2. Show winner text
        winnerLabel.text =
            winner == "werewolves"
              ? "🐺 Werewolves Win!"
              : "🛡️ Villagers Win!";

        // 3. List roles
        foreach (var kv in roles)
        {
            var go = Instantiate(roleEntryPrefab, rolesContent);
            go.GetComponent<TMP_Text>().text = $"{kv.Key}: {kv.Value}";
        }
        Debug.Log("Loading ResultScene");
        SceneManager.LoadScene("ResultScene");
    }
}
