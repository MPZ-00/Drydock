# Changelog

## 0.3.0 (2026-11-20)
Polishes the app.

- Rows show the item's picture from your game install where Drydock finds one (most ores, components and consumables), its name with the item key smaller underneath, amount and mass side by side, and a status of edited, removed or new
- Right-click a row to copy its name, item key, id or the whole row, Ctrl+C copies the selected rows
- App icon and a Help > About window with the version, links to the source, bug reports and license, the game version this release was tested with next to yours, and buttons to open the settings and backup folders
- Saving or restoring while the game runs now explains why that is risky and asks once instead of refusing. Nothing remembers the answer. The command line tool still refuses, and now says why
- Tested with game version 4.0.0.3169

## 0.2.0 (2026-11-20)
Adds a Windows app next to the command line tool.

- Portable app: unzip and run `Drydock.exe`, nothing gets installed
- Inventories in a tree by ship or station, block and slot, with a filter and "show all" for other grids and empty inventories
- Edit totals in place, remove single stacks and add items. Changes stay pending until you save, with weight and stack bars showing where the inventory ends up
- Power cells, modules and guns get a row per stack. New ones start new, as a copy of a stack you pick, empty, or at a charge or ammo count you type
- Amounts take either decimal mark, so `1,5` and `1.5` both work
- Save refuses while the game runs, offers a reload when the game saved the world in the meantime, and lists every change past the game's limits before writing anyway
- Backups now hold all files at the top of the save folder, not only `savegame.vrb`, and the newest 20 per save are kept. This applies to the command line tool too
- Backups window to put a backup back, which backs up the current state first. Works for a save that no longer loads
- Light, dark or system theme, settings in `%LocalAppData%\Drydock\settings.json` with an optional `settings.json` next to the exe that overrides it
- Command line: over-capacity messages for block inventories no longer crash

## 0.1.0 (2026-11-20)
First release, a command line tool.

- List your saves, newest first, and pick one by number, part of its name or folder
- List the items the game knows, with names from its localization
- List the inventories of your character, ships and stations by grid, block and slot name
- Set, add and remove items. Items go to the slot that takes them, and tools, guns and power modules stay one per stack like in the game
- Refuse edits over an inventory's weight or stack limit, `--force` writes them anyway
- Refuse to write while the game runs, back up `savegame.vrb` before every write
- Cache what is read from the game's content, rebuilt after a game update
