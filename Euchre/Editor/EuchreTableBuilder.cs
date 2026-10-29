#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;
using UdonSharpEditor;

/// Menu: Tools > Euchre > Build Table
/// Builds a complete, wired-up euchre table at the world origin.
public static class EuchreTableBuilder
{
    static readonly string[] Labels  = { "Pass", "Order Up", "Clubs", "Spades", "Hearts", "Diamonds", "Alone", "Start", "Next Hand", "Reset" };
    static readonly string[] Actions = { "ActPass", "ActOrderUp", "ActCallClubs", "ActCallSpades", "ActCallHearts", "ActCallDiamonds", "ActToggleAlone", "ActStart", "ActNextHand", "ActReset" };

    const float TableY = 0.775f; // top surface height

    [MenuItem("Tools/Euchre/Build Table")]
    public static void Build()
    {
        var root = new GameObject("EuchreTable");
        Undo.RegisterCreatedObjectUndo(root, "Build Euchre Table");
        var game = UdonSharpUndo.AddComponent<EuchreGame>(root);

        // Table
        var top = Prim(PrimitiveType.Cube, "TableTop", root.transform, new Vector3(0, 0.75f, 0), Quaternion.identity, new Vector3(1.6f, 0.05f, 1.6f));
        Tint(top, new Color(0.08f, 0.35f, 0.18f));
        var leg = Prim(PrimitiveType.Cylinder, "Leg", root.transform, new Vector3(0, 0.36f, 0), Quaternion.identity, new Vector3(0.2f, 0.36f, 0.2f));
        Tint(leg, new Color(0.3f, 0.2f, 0.1f));

        // Status board (billboards toward the local player)
        var statusAnchor = new GameObject("StatusAnchor").transform;
        statusAnchor.SetParent(root.transform, false);
        statusAnchor.localPosition = new Vector3(0, 1.6f, 0);
        var status = Text("Status", statusAnchor, Vector3.zero, Quaternion.identity, 1.0f, new Vector2(1.4f, 0.8f), Color.white, true);

        var seatLabels = new TMP_Text[4];
        var handCards = new EuchreCard[24];
        var trickCards = new EuchreCard[4];

        for (int s = 0; s < 4; s++)
        {
            // Each seat is built in local space: player at -Z looking toward +Z.
            var anchor = new GameObject("Seat" + (s + 1)).transform;
            anchor.SetParent(root.transform, false);
            anchor.localRotation = Quaternion.Euler(0, 90 * s, 0);

            // Seat button
            var seatGo = ButtonObject("SitButton", anchor, new Vector3(0.62f, TableY + 0.012f, -0.73f), new Color(0.95f, 0.75f, 0.2f), "Sit");
            var seat = UdonSharpUndo.AddComponent<EuchreSeat>(seatGo);
            seat.game = game; seat.seatIndex = s;
            EditorUtility.SetDirty(seat);

            // Name label, faces the rest of the table
            seatLabels[s] = Text("SeatLabel", anchor, new Vector3(0, TableY + 0.3f, -0.8f), Quaternion.Euler(0, 180, 0), 0.8f, new Vector2(0.8f, 0.3f), Color.white, false);

            // Hand (slot 5 = dealer pickup)
            for (int slot = 0; slot < 6; slot++)
            {
                var pos = new Vector3(-0.25f + slot * 0.1f, TableY + 0.08f, -0.58f);
                handCards[s * 6 + slot] = Card("Card" + slot, anchor, pos, Quaternion.Euler(25, 0, 0), game, s, slot, false);
            }

            // Trick slot
            trickCards[s] = Card("TrickCard", anchor, new Vector3(0, TableY + 0.004f, -0.2f), Quaternion.Euler(90, 0, 0), game, s, 0, true);

            // Action buttons
            for (int b = 0; b < Labels.Length; b++)
            {
                var pos = new Vector3(-0.45f + b * 0.1f, TableY + 0.012f, -0.73f);
                var go = ButtonObject("Btn_" + Labels[b], anchor, pos, new Color(0.85f, 0.85f, 0.85f), Labels[b]);
                var btn = UdonSharpUndo.AddComponent<EuchreButton>(go);
                btn.game = game; btn.action = Actions[b]; btn.label = Labels[b];
                EditorUtility.SetDirty(btn);
            }
        }

        var up = Card("Upcard", root.transform, new Vector3(0, TableY + 0.004f, 0), Quaternion.Euler(90, 0, 0), game, 0, 0, true);

        game.statusText = status;
        game.statusAnchor = statusAnchor;
        game.seatLabels = seatLabels;
        game.handCards = handCards;
        game.trickCards = trickCards;
        game.upcard = up;
        EditorUtility.SetDirty(game);

        Selection.activeGameObject = root;
        Debug.Log("[Euchre] Table built. Tick 'Debug Control All Seats' on EuchreGame to test solo.");
    }

    // ---------------- helpers ----------------
    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        return go;
    }

    static void Tint(GameObject go, Color c)
    {
        var mat = new Material(Shader.Find("Standard")) { color = c };
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static TextMeshPro Text(string name, Transform parent, Vector3 pos, Quaternion rot, float size, Vector2 rect, Color color, bool wrap)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize = size;
        t.rectTransform.sizeDelta = rect;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = wrap;
        t.richText = true;
        t.color = color;
        t.text = name;
        return t;
    }

    static GameObject ButtonObject(string name, Transform parent, Vector3 pos, Color color, string label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.085f, 0.02f, 0.05f);

        var face = Prim(PrimitiveType.Cube, "Face", go.transform, Vector3.zero, Quaternion.identity, new Vector3(0.085f, 0.02f, 0.05f));
        Object.DestroyImmediate(face.GetComponent<Collider>());
        Tint(face, color);

        var t = Text("Label", go.transform, new Vector3(0, 0.0115f, 0), Quaternion.Euler(90, 0, 0), 0.16f, new Vector2(0.085f, 0.05f), Color.black, false);
        t.text = label;
        return go;
    }

    static EuchreCard Card(string name, Transform parent, Vector3 pos, Quaternion rot, EuchreGame game, int seat, int slot, bool displayOnly)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        if (!displayOnly)
        {
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.06f, 0.09f, 0.006f);
        }

        var face = Prim(PrimitiveType.Cube, "Face", go.transform, Vector3.zero, Quaternion.identity, new Vector3(0.06f, 0.09f, 0.004f));
        Object.DestroyImmediate(face.GetComponent<Collider>());
        Tint(face, Color.white);

        var label = Text("Label", go.transform, new Vector3(0, 0, -0.0025f), Quaternion.identity, 0.3f, new Vector2(0.06f, 0.09f), Color.black, false);
        label.text = "A♠";

        var card = UdonSharpUndo.AddComponent<EuchreCard>(go);
        card.game = game; card.seat = seat; card.slot = slot; card.displayOnly = displayOnly;
        card.label = label;
        card.background = face.GetComponent<Renderer>();
        EditorUtility.SetDirty(card);
        return card;
    }
}
#endif
