using UdonSharp;
using UnityEngine;

/// Sit / stand toggle for one seat at the table, or (with botButton) add / remove a bot there.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EuchreSeat : UdonSharpBehaviour
{
    public EuchreGame game;
    public int seatIndex;
    public bool botButton;

    public override void Interact()
    {
        if (game == null) return;
        if (botButton) game.ToggleBot(seatIndex);
        else game.ToggleSeat(seatIndex);
    }
}
