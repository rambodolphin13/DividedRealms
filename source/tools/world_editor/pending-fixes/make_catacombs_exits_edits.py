"""Plan: the ways out of and between the Catacombs zones, and letting players in at all (read-only generator).

Found by the owner's first real-client visit (2026-11-09):

 1. "This region has been disabled!": the world's `disabled_expansions` is "3;4;5;6". Regions.Expansion 3 is
    Catacombs and 4 is Darkness Rising, so every imported region is disabled. A GM account only sees the message; a
    normal player is refused. The property becomes "5;6".
 2. Doors that lead nowhere: the imports took EoD zone points by their target or source region, but most EoD rows
    have no source region, so exits whose target lies outside the imported regions were skipped. This takes the id of
    every 1.130 client trigger that lies inside an imported zone and adds the EoD rows we lack (one per realm).
 3. A door in Midgard's Burial Grounds ("To Burial AW1", id 310) sends Midgard players to Jordheim: EoD's realm 2 row
    points at region 101 while realms 1 and 3 go to Jarlsberg's Hideout (329). The realm 2 row is set to match.

usage: python -I make_catacombs_exits_edits.py <world.sqlite3.db> <eod-db-public data dir> <reference.sqlite3> <out.edits.json>
"""
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

REGIONS = {58, 59, 63, 65, 66, 67, 68, 69, 92, 93, 94, 95, 96, 97, 98, 99, 148, 149, 162, 188, 189, 195, 197, 243,
           109, 226, 227, 228, 229, 376, 390, 429, 430, 433, 436, 396, 397, 437, 446, 447, 495, 339, 335, 394, 360, 395}
LEFT_OUT_TARGETS = {196}          # Albion's Otherworld: no trustworthy spawn data yet


def main():
    world, eod, ref, out = sys.argv[1:5]
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    rf = sqlite3.connect(f'file:{ref}?mode=ro', uri=True)
    cols = [r[1] for r in db.execute('PRAGMA table_info(ZonePoint)')]
    zone_region = {int(a): int(b) for a, b in db.execute('SELECT ZoneID, RegionID FROM Zones')}
    regions = {int(r[0]) for r in db.execute('SELECT RegionID FROM Regions')}
    ours = defaultdict(dict)
    keys = set()
    for z in db.execute('SELECT * FROM ZonePoint'):
        ours[int(z['Id'])][int(z['Realm'])] = z
        keys.add(z['ZonePoint_ID'])
    known_classes = {r[0] for r in db.execute("SELECT DISTINCT ClassType FROM ZonePoint WHERE ClassType LIKE 'DOL.GS.ServerRules.%'")}
    eod_rows = defaultdict(list)
    for z in json.loads((Path(eod) / 'ZonePoint.json').read_text(encoding='utf-8')):
        eod_rows[int(z['Id'])].append(z)
    inside = {int(pid) for zid, pid in rf.execute('select zone_id, point_id from src_c130_zonejumps')
              if pid is not None and zone_region.get(int(zid)) in REGIONS}

    edits, skipped = [], Counter()
    prop = db.execute("SELECT Value FROM ServerProperty WHERE Key = 'disabled_expansions'").fetchone()
    if prop and prop[0] != '5;6':
        edits.append({'table': 'ServerProperty', 'where': {'Category': 'system', 'Key': 'disabled_expansions'},
                      'set': {'Value': '5;6', 'Description': 'Disabled expansions by Regions.Expansion (0 classic, 1 Shrouded Isles, 2 Atlantis, 3 Catacombs, 4 Darkness Rising). Catacombs and Darkness Rising were opened 2026-11-09.'}})
    added = Counter()
    for pid in sorted(inside):
        for z in eod_rows.get(pid, ()):
            realm, target = int(z['Realm']), int(z.get('TargetRegion') or 0)
            if realm in ours.get(pid, {}):
                continue
            if target in LEFT_OUT_TARGETS:
                skipped['target left out (Albion\'s Otherworld)'] += 1
            elif target == 0 or target not in regions:
                skipped['target region 0 or unknown (code-handled instance in EoD)'] += 1
            else:
                ins = {c: z[c] for c in cols if c in z}
                if ins.get('ClassType') and ins['ClassType'] not in known_classes:
                    ins['ClassType'] = ''
                if ins.get('ZonePoint_ID') in keys:
                    ins['ZonePoint_ID'] = f"eod-zp-{pid}-{realm}"
                keys.add(ins.get('ZonePoint_ID'))
                edits.append({'table': 'ZonePoint', 'insert': ins})
                added[target] += 1
    # 3. id 310, realm 2
    mine, good = ours.get(310, {}).get(2), next((z for z in eod_rows.get(310, ()) if int(z['Realm']) == 1), None)
    if mine is not None and good is not None and int(mine['TargetRegion']) != int(good['TargetRegion']):
        edits.append({'table': 'ZonePoint', 'key': mine['ZonePoint_ID'],
                      'set': {k: good[k] for k in ('TargetRegion', 'TargetX', 'TargetY', 'TargetZ', 'TargetHeading')}})
    no_data = sorted(pid for pid in inside if pid not in eod_rows and pid not in ours)
    Path(out).write_text(json.dumps(edits, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    print(len(edits), 'edits; zone point rows added:', sum(added.values()), 'by target region', sorted(added.items()))
    print('skipped:', dict(skipped))
    print('client triggers with no zone point in any source:', no_data)


if __name__ == '__main__':
    main()
