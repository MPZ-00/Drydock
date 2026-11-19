# Changelog

## 0.1.0 (2026-11-20)
First release, a command line tool.

- List your saves, newest first, and pick one by number, part of its name or folder
- List the items the game knows, with names from its localization
- List the inventories of your character, ships and stations by grid, block and slot name
- Set, add and remove items. Items go to the slot that takes them, and tools, guns and power modules stay one per stack like in the game
- Refuse edits over an inventory's weight or stack limit, `--force` writes them anyway
- Refuse to write while the game runs, back up `savegame.vrb` before every write
- Cache what is read from the game's content, rebuilt after a game update
