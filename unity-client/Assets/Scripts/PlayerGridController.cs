using System.Linq;
using UnityEngine;

/// <summary>
/// Spawns a PlayerCard for every player in RoomState
/// and assigns a random avatar sprite from a pool.
/// </summary>
public class PlayerGridController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform content;      // PlayerGrid (Grid Layout Group)
    [SerializeField] GameObject cardPrefab;   // PlayerCard prefab

    [Header("Avatar settings")]
    [Tooltip("If empty, sprites will be loaded from Resources/Avatars at runtime")]
    [SerializeField] Sprite[] avatarPool;
    [Tooltip("Used when avatarPool is empty or pool has fewer sprites than players")]
    [SerializeField] Sprite placeholderAvatar;

    /* -------------------------------------------------------------------- */
    void OnEnable()
    {
        // Fallback: auto-load any sprites placed in Resources/Avatars
        if (avatarPool == null || avatarPool.Length == 0)
            avatarPool = Resources.LoadAll<Sprite>("Avatars");

        NetworkManager.OnRoomUpdate += Rebuild;
        NetworkManager.OnPeekResult += ApplyPeekBadge;
        NetworkManager.OnNightEnd += HandleNightEnd;
        Rebuild();            // in case snapshot already exists
    }

    void OnDisable()
    {
        NetworkManager.OnRoomUpdate -= Rebuild;
        NetworkManager.OnPeekResult -= ApplyPeekBadge;
        NetworkManager.OnNightEnd -= HandleNightEnd;
    }

    /* -------------------------------------------------------------------- */
    void Rebuild()
    {
        // Clear previous cards
        foreach (Transform c in content) Destroy(c.gameObject);

        // Create one card per player (sorted by name for stability)
        foreach (var p in NetworkManager.RoomState.Players.OrderBy(pl => pl.name))
        {
            var go = Instantiate(cardPrefab, content);
            var card = go.GetComponent<PlayerCard>();

            // Pick a deterministic avatar based on player id
            card.Init(p.id, p.name, avatarPool);

            // Mark local player
            if (p.id == NetworkManager.PlayerState.MyId)
            {
                var badge = go.transform.Find("YouBadge");
                if (badge) badge.gameObject.SetActive(true);
            }
        }
    }

    void ApplyPeekBadge(string playerId, string role)
    {
        foreach (Transform child in content)
        {
            var card = child.GetComponent<PlayerCard>();
            if (card && card.PlayerId == playerId)
            {
                card.ShowRole(role);              // red RoleBadge appears
                break;
            }
        }
    }

    void HandleNightEnd(string victimId)
    {
        if (string.IsNullOrEmpty(victimId)) return;   // no kill tonight

        foreach (Transform child in content)
        {
            var card = child.GetComponent<PlayerCard>();
            if (card && card.PlayerId == victimId)
            {
                card.MarkDead();
                break;
            }
        }
    }


    /* -------------------------------------------------------------------- */
    Sprite PickAvatar(string seed)
    {
        if (avatarPool != null && avatarPool.Length > 0)
        {
            int idx = Mathf.Abs(seed.GetHashCode()) % avatarPool.Length;
            return avatarPool[idx];
        }
        return placeholderAvatar;
    }
}
