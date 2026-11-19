# Drydock
Inventory editor for Space Engineers 2 saves. Works offline on the save files, the game doesn't need to run.

It reads saves with the game's own parser from `VRage.Library.dll` in your install, using the type layout each save stores about itself. Nothing from the game is copied into this repo or shipped with it.

Right now it can:
- list the items the game knows, with names from its localization
- list every inventory in a save: characters, blocks, containers
- set, add and remove items

## Requirements
- Windows, .NET 9 SDK
- Space Engineers 2 installed. Steam libraries are found automatically, otherwise set `SE2_GAME_DIR` to the `Game2` folder

## Usage
```
dotnet run --project src/Drydock.Cli -- inventories "<save>" --owner CompositeCharacter
dotnet run --project src/Drydock.Cli -- items iron
dotnet run --project src/Drydock.Cli -- add "<save>" 310 "Iron Ore" 500 --dry-run
dotnet run --project src/Drydock.Cli -- set "<save>" 310 "Rifle Magazine" 99
```
`<save>` is a folder name in `%AppData%\SpaceEngineers2\AppData\SaveGames` or a full path. The number is the inventory's `#` from `inventories`. Your own character is `CompositeCharacterServer`.

Quit the game before writing. Every write backs up `savegame.vrb` to `%LocalAppData%\Drydock\backups` first.

## How edits work
The parser gives every value its byte range in the decompressed save. An edit splices new bytes into those ranges, fixes the list count, recompresses and recomputes the checksums. The result is parsed again before anything is written. Stacks with extra data (guns with ammo, power modules with a fill ratio) are cloned from an existing stack of the same item when there is one.

## Tests
```
dotnet test
```
The save tests use the most recently written save and only read it. Set `DRYDOCK_TEST_SAVE` to a `savegame.vrb` to pick one.

## Disclaimer
Editing saves can break them. Keep backups. Space Engineers 2 is in alpha and the save format can change with any update. Not affiliated with Keen Software House.

## License
AGPL-3.0, see [LICENSE](LICENSE).
