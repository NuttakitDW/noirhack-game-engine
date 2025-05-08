using UnityEngine;

public class CardPicker : MonoBehaviour
{
    void OnEnable() => NetworkManager.OnCardTaken += HandleCardTaken;
    void OnDisable() => NetworkManager.OnCardTaken -= HandleCardTaken;

    void HandleCardTaken(CardTakenPayload p)
    {
        if (p.status == "denied")
        {
            // choose another free index and send pickCard...
        }
        else
        {
            Debug.Log($"Card {p.card} successfully reserved for me!");
            // update UI or proceed to next phase
        }
    }
}
