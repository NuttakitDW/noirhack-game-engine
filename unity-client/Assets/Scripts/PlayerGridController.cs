using System.Linq;
using System.Collections.Generic; // Added for Dictionary
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
        NetworkManager.OnVoteUpdate += HandleVoteUpdate;    // ← new
        NetworkManager.OnDayEnd += HandleDayEnd;        // ← new
        Rebuild();            // in case snapshot already exists
    }

    void OnDisable()
    {
        NetworkManager.OnRoomUpdate -= Rebuild;
        NetworkManager.OnPeekResult -= ApplyPeekBadge;
        NetworkManager.OnNightEnd -= HandleNightEnd;
        NetworkManager.OnVoteUpdate -= HandleVoteUpdate;
        NetworkManager.OnDayEnd -= HandleDayEnd;
    }

    /* -------------------------------------------------------------------- */
    void Rebuild()
    {
        // 1. wipe old cards
        foreach (Transform c in content) Destroy(c.gameObject);

        // 2. load pool if not set
        if (avatarPool == null || avatarPool.Length == 0)
            avatarPool = Resources.LoadAll<Sprite>("Avatars");

        var players = NetworkManager.RoomState.Players
                        .OrderBy(p => p.name)    // or p.id
                        .ToList();

        bool hasEnough = avatarPool.Length >= players.Count;

        // 3. instantiate with unique avatars
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var go = Instantiate(cardPrefab, content);
            var card = go.GetComponent<PlayerCard>();

            // pick the i-th sprite if we have it
            Sprite avatar = hasEnough
                ? avatarPool[i]
                : placeholderAvatar;

            card.Init(p.id, p.name, avatar);

            // mark “You”
            if (p.id == NetworkManager.PlayerState.MyId)
                go.transform.Find("YouBadge")?.gameObject.SetActive(true);
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

    // live vote counts
    void HandleVoteUpdate(Dictionary<string, int> tally)
    {
        foreach (Transform t in content)
        {
            var card = t.GetComponent<PlayerCard>();
            int count = tally.ContainsKey(card.PlayerId) ? tally[card.PlayerId] : 0;
            card.SetVoteCount(count);   // new helper in PlayerCard
        }
    }

    // end‐of‐day lynch
    void HandleDayEnd(string lynchedId)
    {
        if (string.IsNullOrEmpty(lynchedId)) return;
        foreach (Transform t in content)
        {
            var card = t.GetComponent<PlayerCard>();
            if (card.PlayerId == lynchedId)
            {
                card.MarkDead();
                break;
            }
        }
    }
}
