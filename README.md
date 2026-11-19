# Drydock
Inventory editor for Space Engineers 2 saves. Works offline on the save files, the game doesn't need to run.

It reads saves with the game's own parser from `VRage.Library.dll` in your install, using the type layout each save stores about itself. Nothing from the game is copied into this repo or shipped with it.

Right now it can:
- list your saves and the items the game knows, with names from its localization
- list the inventories of your character, ships and stations by name
- set, add and remove items

## Requirements
- Windows, .NET 9 SDK
- Space Engineers 2 installed. Steam libraries are found automatically, otherwise set `SE2_GAME_DIR` to the `Game2` folder

## Usage
```
drydock saves
drydock inventories 1
drydock inventories 1 Aquila
drydock items iron
drydock add 1 player "Iron Ore" 500 --dry-run
drydock set 1 player "Rifle Magazine" 99
drydock add 1 "Aquila/Smelter:input" "Iron Ore" 1000
```
Run it with `dotnet run --project src/Drydock.Cli --` in place of `drydock`, or build it and run `drydock.exe`.

`<save>` is the number from `saves` (1 is the newest), a unique part of the save's name, or a folder path.

The target is `player`, a ship or station name, or `name/block`, with an optional `:slot` such as `player:datapads` or `Smelter:input`. Consumables and datapads go to their own slot when you only say `player`. Blocks with the same name are told apart by the `#number` from `inventories`, but that number changes whenever the game saves. Renaming the block in game is the stable way.

Tools, guns and power modules take one stack per unit, like the game keeps them. New ones copy their extra data (ammo, charge) from one already in the save.

Writing refuses while the game runs. Every write backs up `savegame.vrb` to `%LocalAppData%\Drydock\backups` first.

## How edits work
The parser gives every value its byte range in the decompressed save. An edit splices new bytes into those ranges, fixes the list count, recompresses and recomputes the checksums. The result is parsed again before anything is written.

## Tests
```
dotnet test
```
The save tests use the most recently written save and only read it. Set `DRYDOCK_TEST_SAVE` to a `savegame.vrb` to pick one.

## Disclaimer
Editing saves can break them. Keep backups. Space Engineers 2 is in alpha and the save format can change with any update. Not affiliated with Keen Software House.

## License
AGPL-3.0, see [LICENSE](LICENSE).
