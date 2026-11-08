# Missing loot tables outside ToA (2026-11-08)

Made by `../make_eod_missing_loot_tables_edits.py`. **Not applied.** Apply last of the three patches of this pass.

The world has 2,267 MobXLootTemplate rows outside ToA naming LootTemplate tables that were never imported (the
same bug as the ToA named drops). Eve of Darkness db-public (MIT) has 1,766 of those tables; this adds them:
**9,309 LootTemplate rows** (8,835 gear, 474 quest or junk items; chances mostly 10-20%, 18 rows at 100%,
no timed XP rows). 1,171 of the tables belong to monsters spawned in the world, the rest to names nothing spawns
yet. Examples: corpse crawler and dungeon chitin (Flayed Hagbui armor), blodfelag wolfwarrior (Blodsnitt, Pillager
Gauntlets), braen (Stone Cutter, rings), Anpassa Foranst's Composite Bow.

Not covered: 494 tables with no EoD source; 32 EoD rows whose item is not in the world.

This brings back a lot of named classic, SI and dungeon loot: it changes the economy and what bots find.

Checks: planned (9,309 edits), applied on a copy after the other two, undone exactly. Dangling links on the copy
went from 2,645 to 500. Not booted.
