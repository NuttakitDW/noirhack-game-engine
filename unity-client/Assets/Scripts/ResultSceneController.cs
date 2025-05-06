using UnityEngine;
using UnityEngine.UI;

public class ResultSceneController : MonoBehaviour
{
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite villagersWinSprite;
    [SerializeField] private Sprite werewolvesWinSprite;

    void Start()
    {
        // read the stored winner
        string winner = NetworkManager.LastWinner;
        if (winner == "villagers")
            backgroundImage.sprite = villagersWinSprite;
        else
            backgroundImage.sprite = werewolvesWinSprite;
    }
}
