using UdonSharp;
using UnityEngine;

/// Sit / stand toggle for one seat at the table.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EuchreSeat : UdonSharpBehaviour
{
    public EuchreGame game;
    public int seatIndex;

    public override void Interact()
    {
        if (game == null) return;
        game.ToggleSeat(seatIndex);
    }
}
