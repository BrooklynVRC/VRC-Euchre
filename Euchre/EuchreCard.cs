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

    [Header("Materials (the game's material slots override these)")]
    public Material faceMaterial;
    [Tooltip("Optional. If empty, the face material is tinted blue for card backs.")]
    public Material backMaterial;

    // Tints multiply over the material, so white shows the material exactly as authored.
    Color faceColor = new Color(1f, 1f, 1f);
    Color highlightColor = new Color(1f, 0.92f, 0.45f);
    Color backColor = new Color(0.15f, 0.25f, 0.6f);
    Color redInk = new Color(0.8f, 0.1f, 0.1f);
    Color blackInk = new Color(0.05f, 0.05f, 0.05f);

    const float LiftHeight = 0.015f;
    Vector3 basePos;
    bool hasBasePos = false;
    MaterialPropertyBlock block;

    public override void Interact()
    {
        if (displayOnly || game == null) return;
        game.OnCardClicked(seat, slot);
    }

    /// Called by the game to apply its material slots.
    public void SetMaterials(Material face, Material back)
    {
        if (face != null) faceMaterial = face;
        backMaterial = back;
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
            label.text = RankName(cardId % 6) + "\n" + SuitSymbol(suit);
            label.color = (suit == 2 || suit == 3) ? redInk : blackInk;
        }
        UseMaterial(faceMaterial);
        Tint(highlight ? highlightColor : faceColor);
        SetLifted(highlight && !displayOnly);
    }

    public void ShowBack()
    {
        SetVisible(true);
        if (label != null) label.text = "";
        if (backMaterial != null)
        {
            UseMaterial(backMaterial);
            Tint(faceColor);
        }
        else
        {
            UseMaterial(faceMaterial);
            Tint(backColor);
        }
        SetLifted(false);
    }

    void UseMaterial(Material m)
    {
        if (background != null && m != null && background.sharedMaterial != m) background.sharedMaterial = m;
    }

    /// Tints through a property block so every card keeps sharing one material.
    void Tint(Color c)
    {
        if (background == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_Color", c);
        block.SetColor("_BaseColor", c);
        background.SetPropertyBlock(block);
    }

    /// Raises playable cards slightly so they stand out.
    void SetLifted(bool lifted)
    {
        if (!hasBasePos)
        {
            basePos = transform.localPosition;
            hasBasePos = true;
        }
        transform.localPosition = lifted ? basePos + transform.localRotation * new Vector3(0f, LiftHeight, 0f) : basePos;
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
