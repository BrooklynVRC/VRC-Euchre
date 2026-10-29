using UdonSharp;
using UnityEngine;

/// Table button that forwards a named action (e.g. "ActPass") to the game.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EuchreButton : UdonSharpBehaviour
{
    public EuchreGame game;
    public string action;
    public string label;

    void Start()
    {
        if (!string.IsNullOrEmpty(label)) InteractionText = label;
    }

    public override void Interact()
    {
        if (game == null || string.IsNullOrEmpty(action)) return;
        game.SendCustomEvent(action);
    }
}
