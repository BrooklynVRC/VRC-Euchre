#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;
using UdonSharpEditor;

/// Menu: Tools > Euchre > Build Table
/// Builds a complete, wired-up euchre table at the world origin.
public static class EuchreTableBuilder
{

    const float TableY = 0.775f; // top surface height
    const string MaterialFolder = "Assets/Euchre/Materials";

    static Material tableMat, cardFaceMat, cardBackMat, buttonMat, sitButtonMat;
    static List<Renderer> buttonRenderers, sitButtonRenderers;

    [MenuItem("Tools/Euchre/Build Table")]
    public static void Build()
    {
        var root = new GameObject("EuchreTable");
        Undo.RegisterCreatedObjectUndo(root, "Build Euchre Table");
        var game = UdonSharpUndo.AddComponent<EuchreGame>(root);

        // Shared material assets: edit one and everything using it changes.
        tableMat = GetOrCreateMaterial("EU_Table", new Color(0.08f, 0.35f, 0.18f));
        cardFaceMat = GetOrCreateMaterial("EU_CardFace", Color.white);
        cardBackMat = GetOrCreateMaterial("EU_CardBack", new Color(0.15f, 0.25f, 0.6f));
        buttonMat = GetOrCreateMaterial("EU_Button", new Color(0.85f, 0.85f, 0.85f));
        sitButtonMat = GetOrCreateMaterial("EU_SitButton", new Color(0.95f, 0.75f, 0.2f));
        buttonRenderers = new List<Renderer>();
        sitButtonRenderers = new List<Renderer>();

        // Table
        var top = Prim(PrimitiveType.Cube, "TableTop", root.transform, new Vector3(0, 0.75f, 0), Quaternion.identity, new Vector3(1.6f, 0.05f, 1.6f));
        var leg = Prim(PrimitiveType.Cylinder, "Leg", root.transform, new Vector3(0, 0.36f, 0), Quaternion.identity, new Vector3(0.2f, 0.36f, 0.2f));
        var tableRenderers = new[] { top.GetComponent<Renderer>(), leg.GetComponent<Renderer>() };
        foreach (var r in tableRenderers) r.sharedMaterial = tableMat;

        // Status board (billboards toward the local player)
        var statusAnchor = new GameObject("StatusAnchor").transform;
        statusAnchor.SetParent(root.transform, false);
        statusAnchor.localPosition = new Vector3(0, 1.9f, 0);
        var status = Text("Status", statusAnchor, Vector3.zero, Quaternion.identity, 1.0f, new Vector2(1.6f, 0.8f), Color.white, true);

        var seatLabels = new TMP_Text[4];
        var handCards = new EuchreCard[24];
        var trickCards = new EuchreCard[4];
        var actionButtons = new EuchreButton[4 * EuchreGame.ButtonSlots];
        var trickSpots = new Transform[4];

        for (int s = 0; s < 4; s++)
        {
            // Each seat is built in local space: player at -Z looking toward +Z.
            var anchor = new GameObject("Seat" + (s + 1)).transform;
            anchor.SetParent(root.transform, false);
            anchor.localRotation = Quaternion.Euler(0, 90 * s, 0);

            // Sit and Bot buttons
            var seatGo = ButtonObject("SitButton", anchor, new Vector3(0.6f, TableY + 0.012f, -0.73f), sitButtonMat, "Sit", sitButtonRenderers);
            var seat = UdonSharpUndo.AddComponent<EuchreSeat>(seatGo);
            seat.game = game; seat.seatIndex = s;
            EditorUtility.SetDirty(seat);
            var botGo = ButtonObject("BotButton", anchor, new Vector3(0.7f, TableY + 0.012f, -0.73f), sitButtonMat, "Bot", sitButtonRenderers);
            var bot = UdonSharpUndo.AddComponent<EuchreSeat>(botGo);
            bot.game = game; bot.seatIndex = s; bot.botButton = true;
            EditorUtility.SetDirty(bot);

            // Name tag above and behind the player, facing the table, so it isn't in their own view.
            seatLabels[s] = Text("SeatLabel", anchor, new Vector3(0, TableY + 1.25f, -1.3f), Quaternion.Euler(0, 180, 0), 1.0f, new Vector2(1.4f, 0.35f), Color.white, false);

            // Hand (slot 5 = dealer pickup)
            for (int slot = 0; slot < 6; slot++)
            {
                var pos = new Vector3(-0.25f + slot * 0.1f, TableY + 0.08f, -0.58f);
                handCards[s * 6 + slot] = Card("Card" + slot, anchor, pos, Quaternion.Euler(25, 0, 0), game, s, slot, false);
            }

            // Trick slot
            // Trick slot, under a spot each client turns and tilts toward itself.
            var spot = Spot("TrickSpot", anchor, new Vector3(0, TableY + 0.004f, -0.2f));
            trickSpots[s] = spot;
            trickCards[s] = Card("TrickCard", spot, Vector3.zero, Quaternion.Euler(90, 0, 0), game, s, 0, true);

            // Context buttons: the game fills them left to right with only the choices available right now.
            for (int b = 0; b < EuchreGame.ButtonSlots; b++)
            {
                var pos = new Vector3(-0.3f + b * 0.12f, TableY + 0.012f, -0.73f);
                var go = ButtonObject("Action" + b, anchor, pos, buttonMat, "", buttonRenderers, 0.11f);
                var btn = UdonSharpUndo.AddComponent<EuchreButton>(go);
                btn.game = game; btn.seat = s; btn.slot = b;
                btn.label = go.GetComponentInChildren<TextMeshPro>();
                btn.face = buttonRenderers[buttonRenderers.Count - 1];
                actionButtons[s * EuchreGame.ButtonSlots + b] = btn;
                EditorUtility.SetDirty(btn);
            }
        }

        var upSpot = Spot("UpcardSpot", root.transform, new Vector3(0, TableY + 0.004f, 0));
        var up = Card("Upcard", upSpot, Vector3.zero, Quaternion.Euler(90, 0, 0), game, 0, 0, true);

        // Trump marker beside the centre, readable from every side.
        var trumpSpot = Spot("TrumpSpot", root.transform, new Vector3(0.22f, TableY + 0.003f, 0.22f));
        var trumpText = Text("TrumpMarker", trumpSpot, Vector3.zero, Quaternion.Euler(90, 0, 0), 1.2f, new Vector2(0.2f, 0.2f), Color.black, false);
        trumpText.text = "";

        var sfxGo = new GameObject("Sounds");
        sfxGo.transform.SetParent(root.transform, false);
        sfxGo.transform.localPosition = new Vector3(0, TableY + 0.2f, 0);
        var sfx = sfxGo.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;
        sfx.minDistance = 2f;
        sfx.maxDistance = 12f;
        sfx.rolloffMode = AudioRolloffMode.Linear;

        game.sfxSource = sfx;
        game.statusText = status;
        game.statusAnchor = statusAnchor;
        game.seatLabels = seatLabels;
        game.handCards = handCards;
        game.trickCards = trickCards;
        game.upcard = up;
        game.trumpText = trumpText;
        game.actionButtons = actionButtons;
        game.trickSpots = trickSpots;
        game.upcardSpot = upSpot;
        game.trumpSpot = trumpSpot;
        game.tableRenderers = tableRenderers;
        game.buttonRenderers = buttonRenderers.ToArray();
        game.sitButtonRenderers = sitButtonRenderers.ToArray();
        game.tableMaterial = tableMat;
        game.cardFaceMaterial = cardFaceMat;
        game.cardBackMaterial = cardBackMat;
        game.buttonMaterial = buttonMat;
        game.sitButtonMaterial = sitButtonMat;
        EditorUtility.SetDirty(game);

        Selection.activeGameObject = root;
        Debug.Log("[Euchre] Table built. Tick 'Debug Control All Seats' on EuchreGame to test solo. Materials are in " + MaterialFolder + ".");
    }

    /// Menu: Tools > Euchre > Apply Materials
    /// Pushes each EuchreGame's material slots onto its cards, buttons and table in edit mode
    /// (the game also does this itself when the world starts).
    [MenuItem("Tools/Euchre/Apply Materials")]
    public static void ApplyMaterials()
    {
        int count = 0;
        foreach (var game in Object.FindObjectsOfType<EuchreGame>())
        {
            ApplyCards(game.handCards, game);
            ApplyCards(game.trickCards, game);
            ApplyCards(new[] { game.upcard }, game);
            ApplyRenderers(game.tableRenderers, game.tableMaterial);
            ApplyRenderers(game.buttonRenderers, game.buttonMaterial);
            ApplyRenderers(game.sitButtonRenderers, game.sitButtonMaterial);
            count++;
        }
        Debug.Log("[Euchre] Applied materials to " + count + " table(s).");
    }

    /// Menu: Tools > Euchre > Save Selected Table As Prefab
    [MenuItem("Tools/Euchre/Save Selected Table As Prefab")]
    public static void SaveAsPrefab()
    {
        var go = Selection.activeGameObject;
        if (go == null || go.GetComponent<EuchreGame>() == null)
        {
            EditorUtility.DisplayDialog("Euchre", "Select the EuchreTable object in the scene first.", "OK");
            return;
        }
        string path = EditorUtility.SaveFilePanelInProject("Save Euchre Table Prefab", go.name, "prefab", "Where to save the table prefab", "Assets/Euchre");
        if (string.IsNullOrEmpty(path)) return;
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("[Euchre] Saved " + path + ". To share it, right-click the Euchre folder > Export Package.");
        }
    }

    static void ApplyCards(EuchreCard[] cards, EuchreGame game)
    {
        if (cards == null) return;
        foreach (var card in cards)
        {
            if (card == null) continue;
            Undo.RecordObject(card, "Apply Euchre Materials");
            if (game.cardFaceMaterial != null) card.faceMaterial = game.cardFaceMaterial;
            card.backMaterial = game.cardBackMaterial;
            EditorUtility.SetDirty(card);
            if (card.background != null && game.cardFaceMaterial != null)
            {
                Undo.RecordObject(card.background, "Apply Euchre Materials");
                card.background.sharedMaterial = game.cardFaceMaterial;
            }
        }
    }

    static void ApplyRenderers(Renderer[] renderers, Material m)
    {
        if (renderers == null || m == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            Undo.RecordObject(r, "Apply Euchre Materials");
            r.sharedMaterial = m;
        }
    }

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Euchre", "Materials");
        var mat = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ---------------- helpers ----------------
    static Transform Spot(string name, Transform parent, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        return t;
    }

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

    static GameObject ButtonObject(string name, Transform parent, Vector3 pos, Material mat, string label, List<Renderer> renderers, float width = 0.085f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(width, 0.02f, 0.05f);

        var face = Prim(PrimitiveType.Cube, "Face", go.transform, Vector3.zero, Quaternion.identity, new Vector3(width, 0.02f, 0.05f));
        Object.DestroyImmediate(face.GetComponent<Collider>());
        var r = face.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        renderers.Add(r);

        var t = Text("Label", go.transform, new Vector3(0, 0.0115f, 0), Quaternion.Euler(90, 0, 0), 0.16f, new Vector2(width - 0.006f, 0.045f), Color.black, false);
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.06f;
        t.fontSizeMax = 0.16f;
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
        face.GetComponent<Renderer>().sharedMaterial = cardFaceMat;

        var label = Text("Label", go.transform, new Vector3(0, 0, -0.0025f), Quaternion.identity, 0.3f, new Vector2(0.06f, 0.09f), Color.black, false);
        label.text = "A\n♠";

        var card = UdonSharpUndo.AddComponent<EuchreCard>(go);
        card.game = game; card.seat = seat; card.slot = slot; card.displayOnly = displayOnly;
        card.label = label;
        card.background = face.GetComponent<Renderer>();
        card.faceMaterial = cardFaceMat;
        card.backMaterial = cardBackMat;
        EditorUtility.SetDirty(card);
        return card;
    }
}
#endif
