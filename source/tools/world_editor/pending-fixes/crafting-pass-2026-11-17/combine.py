"""Combine the 2026-11-17 crafting-pass correctness edits into one edits file (one plan, one undo id).

    python -I combine.py <Crafting-Pass-2026-11-17 audit folder> <out.edits.json>

Owner-decision files (legendary bonuses, Dragonsworn materials, furniture sizes, odd models, dye ingredients,
tailoring OWNER-DECISION) are left out. secondary-furniture duplicates basiccrafting/furniture-placeable and is
left out. An edit identical to one already taken (same table, key and change) is dropped; a different change to
the same row stops the script.
"""
import json, sys
from pathlib import Path

FILES = [
    'armorcrafting/armorcraft-stats-2026-11-17', 'tailoring/tailoring-stats',
    'weaponcrafting/weaponcraft-2026-11-17-fixes', 'fletching/fletching-ammo-2026-11-17',
    'spellcrafting/spellcraft-stat-gems-2026-11-17', 'spellcrafting/spellcraft-spectral-guard-2026-11-17',
    'spellcrafting/spellcraft-orphan-recipes-2026-11-17',
    'armorcrafting/armorcraft-recipes-2026-11-17', 'tailoring/tailoring-recipes',
    'weaponcrafting/weaponcraft-2026-11-17-recipes', 'fletching/fletching-recipes-2026-11-17',
    'bountycrafting/bountycraft-fixes-2026-11-17', 'bountycrafting/bountycraft-world-items-2026-11-17',
    'secondary/secondary-conversions-2026-11-17', 'secondary/secondary-horse-armor-2026-11-17',
    'secondary/secondary-bardings-2026-11-17', 'basiccrafting/furniture-placeable',
    'alchemy/alchemy-potion-use-levels', 'alchemy/alchemy-costume-realm',
    'siegecrafting/siege-materials-mid-hib', 'siegecrafting/siege-hib-ammo-rows',
    'shared/nf-material-merchants', 'basiccrafting/smiths-hammer-price',
    'secondary/secondary-material-prices-2026-11-17',
]

def main(root, out):
    taken, seen, counts = [], {}, []
    for name in FILES:
        edits = json.loads((Path(root) / f'{name}.edits.json').read_text(encoding='utf-8'))
        kept = dropped = 0
        for e in edits:
            sig = json.dumps(e, sort_keys=True).lower()
            row = (e.get('table'), str(e.get('key') or e.get('delete') or '').lower()) if (e.get('key') or e.get('delete')) else None
            if sig in seen:
                dropped += 1
                continue
            if row and row in seen and seen[row] != name and 'copy' not in e:
                raise SystemExit(f'{name}: {row} already changed differently by {seen[row]}')
            seen[sig] = name
            if row:
                seen[row] = name
            taken.append(e)
            kept += 1
        counts.append((name, kept, dropped))
    Path(out).write_text(json.dumps(taken, indent=1), encoding='utf-8')
    for c in counts:
        print(f'{c[1]:6} kept {c[2]:4} dropped  {c[0]}')
    print(len(taken), 'edits ->', out)

if __name__ == '__main__':
    main(*sys.argv[1:])
