using TMPro;
using UdonSharp;
using UnityEngine;

/// One card visual on the table. Hand cards are clickable; trick/upcard slots are display only.
/// Card ids are 0..23: suit = id / 6 (Clubs, Spades, Hearts, Diamonds), rank = id % 6 (9, 10, J, Q, K, A).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EuchreCard : UdonSharpBehaviour
{
    public EuchreGame game;
    public int seat;
    public int slot;
    public bool displayOnly;
    public TMP_Text label;
    public Renderer background;

    Color faceColor = new Color(1f, 1f, 1f);
    Color highlightColor = new Color(1f, 0.95f, 0.55f);
    Color backColor = new Color(0.15f, 0.25f, 0.6f);
    Color redInk = new Color(0.8f, 0.1f, 0.1f);
    Color blackInk = new Color(0.05f, 0.05f, 0.05f);

    public override void Interact()
    {
        if (displayOnly || game == null) return;
        game.OnCardClicked(seat, slot);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void ShowFace(int cardId, bool highlight)
    {
        SetVisible(true);
        int suit = cardId / 6;
        if (label != null)
        {
            label.text = RankName(cardId % 6) + SuitSymbol(suit);
            label.color = (suit == 2 || suit == 3) ? redInk : blackInk;
        }
        if (background != null) background.material.color = highlight ? highlightColor : faceColor;
    }

    public void ShowBack()
    {
        SetVisible(true);
        if (label != null) label.text = "";
        if (background != null) background.material.color = backColor;
    }

    void SetVisible(bool visible)
    {
        if (background != null) background.enabled = visible;
        if (label != null) label.enabled = visible;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = visible && !displayOnly;
    }

    string RankName(int rank)
    {
        if (rank == 0) return "9";
        if (rank == 1) return "10";
        if (rank == 2) return "J";
        if (rank == 3) return "Q";
        if (rank == 4) return "K";
        return "A";
    }

    string SuitSymbol(int suit)
    {
        if (suit == 0) return "♣";
        if (suit == 1) return "♠";
        if (suit == 2) return "♥";
        return "♦";
    }
}
