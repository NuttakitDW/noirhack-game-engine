// File: PlayerCardEvents.cs   (place in Assets/Scripts)

using System;

/// <summary>
/// Simple global event hub for PlayerCard clicks.
/// Phase managers (NightActionManager, VotingManager, etc.)
/// can subscribe to get notified when a card is selected.
/// </summary>
public static class PlayerCardEvents
{
    /// <summary>Fired whenever a PlayerCard is clicked.</summary>
    public static event Action<PlayerCard> OnCardSelected;

    /// <summary>Called by PlayerCard.OnClick()</summary>
    public static void Select(PlayerCard card)
    {
        OnCardSelected?.Invoke(card);
    }
}
