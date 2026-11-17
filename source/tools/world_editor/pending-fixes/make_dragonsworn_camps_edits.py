"""Writes the World Workshop edits that spawn the Dragonsworn camps (crafting pass 2026-11-17, owner choice).

    python -I make_dragonsworn_camps_edits.py <reference.sqlite3> <opendaoc.sqlite3.db> <out.edits.json>

Broken Dragonsworn Armors / Weapons drop only from the 27 dragonsworn loot tables, and the world had no Dragonsworn
mob, so ~235 Dragonsworn armorcraft, tailoring and weaponcraft recipes could never be made. The EoD db-public reference
(src_eodb_Mob) has 206 spawns around the three dragons: Dartmoor and Cornwall (Golestandt), Malmohus (Gjalpinulva) and
Sheeroe Hills (Cuuldurach). Every NpcTemplate and NPCEquipment row they name already exists in our world.

Each spawn is copied with its own columns; the Mob_ID gets an "eod-" prefix only if it is taken. PackageID marks the
rows so they can be found again. Spawns already present by name and position (within 50 units) are skipped.
"""
import json, sqlite3, sys

PACKAGE = 'OfflineDragonswornCamps'


def main(ref_path, world_path, out_path):
    ref = sqlite3.connect(f'file:{ref_path}?mode=ro', uri=True)
    ref.row_factory = sqlite3.Row
    world = sqlite3.connect(f'file:{world_path}?mode=ro', uri=True)
    columns = [r[1] for r in world.execute('PRAGMA table_info(Mob)')]
    taken = {r[0].lower() for r in world.execute('SELECT Mob_ID FROM Mob')}
    present = world.execute("SELECT Name, Region, X, Y FROM Mob WHERE lower(Name) LIKE 'dragonsworn%'").fetchall()
    edits = []
    for row in ref.execute("SELECT * FROM src_eodb_Mob WHERE lower(Name) LIKE 'dragonsworn%' ORDER BY Region, Name, Mob_ID"):
        if any(p[0].lower() == row['Name'].lower() and p[1] == row['Region'] and abs(p[2] - row['X']) < 50
               and abs(p[3] - row['Y']) < 50 for p in present):
            continue
        values = {c: row[c] for c in columns if c in row.keys() and row[c] is not None and c != 'LastTimeRowUpdated'}
        mob_id = row['Mob_ID'] if row['Mob_ID'].lower() not in taken else f"eod-{row['Mob_ID']}"
        values['Mob_ID'] = mob_id
        values['PackageID'] = PACKAGE
        edits.append({'table': 'Mob', 'insert': values})
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(edits, f, indent=1)
    print(len(edits), 'Mob inserts ->', out_path)


if __name__ == '__main__':
    main(*sys.argv[1:])
