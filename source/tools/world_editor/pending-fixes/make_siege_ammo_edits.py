"""Writes the World Workshop edits that give crafted siege ammunition its numbers (owner design 2026-11-17).

    python -I make_siege_ammo_edits.py <opendaoc.sqlite3.db> <out.edits.json>

The server reads them from the item row (SiegeAmmo.cs): DPS_AF = damage % of the engine's plain shot, SPD_ABS = blast
area % (catapult and trebuchet), Type_Damage = damage type. Live gives no numbers; the trade-offs follow the 1.55 notes
(a high-damage small-area shot, cold ice balls) and the Grab Bag (ammo carries crush, thrust, matter... damage types).
Image of Nefastus stays out: its Runed Demon Seal Fragment has no source.
"""
import json
import sqlite3
import sys

CRUSH, SLASH, THRUST, BODY, COLD, HEAT, SPIRIT = 1, 2, 3, 10, 11, 13, 15

# name: (damage %, area %, damage type)
AMMO = {
    'stone shot': (100, 100, CRUSH),
    'boulder': (110, 100, CRUSH),
    'dense stone shot': (125, 75, CRUSH),
    'flawless boulder': (130, 110, CRUSH),
    'lava stone': (100, 100, HEAT),
    'incandescent lava stone': (125, 100, HEAT),
    'ice ball': (100, 100, COLD),
    'arctic ice ball': (120, 100, COLD),
    'demon seal boulder': (150, 100, SPIRIT),
    'demon seal barrage': (120, 150, SPIRIT),
    'field bolt': (100, 100, THRUST),
    'perfect field bolt': (120, 100, THRUST),
    'serrated bolt': (110, 100, SLASH),
    'scintillating serrated bolt': (130, 100, SLASH),
    'heavy bolt': (125, 100, CRUSH),
    'honed heavy bolt': (145, 100, CRUSH),
    'toxic bolt': (110, 100, BODY),
    'lethal bolt': (150, 100, BODY),
}


def main(db_path, out):
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    edits, seen = [], set()
    for id_nb, name in db.execute('SELECT Id_nb, Name FROM ItemTemplate WHERE Model IN (1681, 1682, 2621) '
                                  'AND Object_Type = 0 ORDER BY Id_nb'):
        values = AMMO.get(name.lower())
        if values is None:
            continue
        damage, area, damage_type = values
        edits.append({'table': 'ItemTemplate', 'key': id_nb,
                      'set': {'DPS_AF': damage, 'SPD_ABS': area, 'Type_Damage': damage_type}})
        seen.add(name.lower())
    missing = sorted(set(AMMO) - seen)
    with open(out, 'w', encoding='utf-8') as f:
        json.dump(edits, f, indent=1)
    print(len(edits), 'ammo rows ->', out, '; not found:', missing or 'none')


if __name__ == '__main__':
    main(*sys.argv[1:])
