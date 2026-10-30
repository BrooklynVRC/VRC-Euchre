# VRChat Euchre

A networked 4-player Euchre table for VRChat worlds, written in UdonSharp. Empty seats can be filled with bots.

## Features

- Standard euchre: ordering up, the dealer's pickup and discard, naming trump on round two, going alone, and bowers.
- **Bots:** they bid on hand strength (bowers, trump, off-suit aces, voids) and can go alone. In play they track which cards are gone, so they pull trump, cash winners, don't waste trump on a trick their partner is winning, and throw off low cards. If a player leaves mid-game, a bot takes over their seat, and a player can sit in a bot's seat to take it back.
- Owner-run networking: the instance owner runs the game and everyone else sends their moves to them, so two people clicking at once can't put the game out of sync.
- Options: stick the dealer, points to win, bot speed, how long a finished trick stays visible, and automatic next hand.
- Context buttons: each seat shows only the choices you have right now, such as Order Up / Go Alone / Pass, or the three suits you can name. Nothing shows when you just need to play a card.
- Readable table: on your screen, other players' played cards, the upcard and the trump marker turn to face you, tilt up and grow 1.5x. Your own played card stays flat. It's visual only, so each player sees the table turned toward themselves.
- Playable cards lift and glow on your turn, the winning card of each trick is highlighted, and a trump marker sits on the table.
- Name tags float above and behind each player, so your own tag isn't in your face.
- Material slots on the game object let you restyle the table, card faces, card backs and buttons.

## Requirements

- Unity 2022.3 (the VRChat-supported version)
- VRChat Worlds SDK 3.8 or newer (it uses networked events with parameters)
- UdonSharp 1.x
- TextMeshPro

## Install

1. Copy the `Euchre` folder (and `Euchre.meta`) into your project's `Assets` folder.
2. Run **Tools → Euchre → Build Table**. This also creates the shared materials in `Euchre/Materials`.
3. Optional: select the table and run **Tools → Euchre → Save Selected Table As Prefab**.
4. In game, click **Sit** to take a seat or **Bot** to add a bot, then press **Start**.

To restyle the table, drop materials into the slots on `EuchreGame` and run **Tools → Euchre → Apply Materials**. The game also applies the slots when the world loads.

## Files

- `EuchreGame.cs`: rules, networking and bots.
- `EuchreSeat.cs`, `EuchreCard.cs`, `EuchreButton.cs`: the seat, card and button interactables.
- `Editor/EuchreTableBuilder.cs`: builds and wires up the whole table.

## License

MIT, see [LICENSE](LICENSE).
