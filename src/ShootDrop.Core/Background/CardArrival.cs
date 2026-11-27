using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;

namespace ShootDrop.Core.Background;

/// <summary>What to do after the list of plugged-in cards changed.</summary>
public enum ArrivalDecision
{
    /// <summary>No new card, or the user chose to do nothing.</summary>
    None,
    ShowReadyWindow,
    StartCopy,

    /// <summary>Autopilot: show the one-screen "new sessions" view.</summary>
    ShowAutopilot,
}

/// <summary>
/// Decides what happens when cards are plugged in. Kept in Core (and tested) because "copy automatically"
/// must never start a copy the user wouldn't expect.
/// </summary>
public static class CardArrival
{
    /// <summary>Cards in <paramref name="now"/> that were not in <paramref name="before"/>.</summary>
    public static IReadOnlyList<DetectedCard> NewCards(IReadOnlyList<DetectedCard> before, IReadOnlyList<DetectedCard> now) =>
        now.Where(c => !before.Any(b => b.Volume.Path == c.Volume.Path)).ToList();

    /// <summary>
    /// First step, before scanning: is a new card here at all, and does the user want anything to happen?
    /// </summary>
    public static ArrivalDecision OnCardsChanged(
        IReadOnlyList<DetectedCard> before, IReadOnlyList<DetectedCard> now, CardArrivalAction action, bool copyRunning)
    {
        if (copyRunning || action == CardArrivalAction.DoNothing) return ArrivalDecision.None;
        if (NewCards(before, now).Count == 0) return ArrivalDecision.None;
        return action switch
        {
            CardArrivalAction.CopyAutomatically => ArrivalDecision.StartCopy,
            CardArrivalAction.AskOnce => ArrivalDecision.ShowAutopilot,
            _ => ArrivalDecision.ShowReadyWindow,
        };
    }

    /// <summary>
    /// Second step for automatic copies, once the last setup has been planned. Copy only when every plugged-in
    /// card is a known device and the plan has something to copy and nothing waiting for a folder choice.
    /// Otherwise fall back to showing the Ready window, so the user decides.
    /// </summary>
    public static ArrivalDecision ConfirmAutoCopy(IReadOnlyList<DetectedCard> cards, CopyPlan plan)
    {
        if (cards.Count == 0) return ArrivalDecision.None;
        if (cards.Any(c => !c.IsKnown)) return ArrivalDecision.ShowReadyWindow;
        if (!plan.CanRun || plan.Items.Count == 0) return ArrivalDecision.ShowReadyWindow;
        return ArrivalDecision.StartCopy;
    }
}
