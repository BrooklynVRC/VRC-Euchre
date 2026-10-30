using TMPro;
using UdonSharp;
using UnityEngine;

/// One context button slot at a seat. The game decides what each slot does (Order Up, Pass, Hearts ...)
/// and hides the slot when there's nothing to do, so only the choices that matter right now are shown.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EuchreButton : UdonSharpBehaviour
{
    public EuchreGame game;
    public int seat;
    public int slot;
    public TMP_Text label;
    public Renderer face;

    string shown = null;

    public override void Interact()
    {
        if (game != null) game.OnActionButton(seat, slot);
    }

    /// Shows the button with this text, or hides it when text is empty.
    public void Show(string text)
    {
        if (text == shown) return;
        shown = text;
        bool visible = !string.IsNullOrEmpty(text);
        if (label != null) label.text = visible ? text : "";
        if (face != null) face.enabled = visible;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = visible;
        if (visible) InteractionText = text;
    }
}
