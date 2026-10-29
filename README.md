# VRChat Euchre

A networked 4-player Euchre table for VRChat worlds, written in UdonSharp.

## Requirements

- Unity 2022.3 (the VRChat-supported version)
- VRChat Worlds SDK 3.x
- UdonSharp 1.x
- TextMeshPro

## Install

1. Copy the `Euchre` folder (and `Euchre.meta`) into your project's `Assets` folder.
2. Open your world scene and run **Tools → Euchre → Build Table**.
3. Players click a seat to sit down, and then start the game from the table.

## Files

- `EuchreGame.cs`: the game logic and network sync. The instance owner runs the game.
- `EuchreSeat.cs`, `EuchreCard.cs`, `EuchreButton.cs`: the seat, card and button interactables.
- `Editor/EuchreTableBuilder.cs`: builds and wires up the whole table in the scene.

## License

MIT, see [LICENSE](LICENSE).
