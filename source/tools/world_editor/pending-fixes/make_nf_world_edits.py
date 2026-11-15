"""New Frontiers world, batch 2 (2026-11-15): spawns, NPCs, objects, doors and zone points from EoD db-public (MIT).

Owner 2026-11-15: NF, Darkness Falls, the battlegrounds, the Labyrinth, Passage of Conflict and Summoner's Hall are
the realm-war areas; get there by teleporter (the home-land "Frontier" gate jumps stay removed from the client).
Batch 1 (make_nf_keeps_edits.py) put the NF keeps in; this fills the land around them.

Same rules as the Catacombs import (make_catacombs_import_edits.py, DR-230):
  - only rows inside a zone box (Summoner's Hall 233 gets its missing Zones row from EoD first);
  - no invisible helpers / "New Mob", no second copy of an NPC we already have (same name, region, within 300);
  - RoamingRange 0 -> 100 on neutral attackable GameNPCs; the 1.130 model remap;
  - merchants with nothing we can sell become plain NPCs;
  - zone point rows only where our world has no row for that (Id, Realm); EoD script classes we lack are blanked.
Specific to NF:
  - zone 163 itself (the centre island) is left empty: EoD's rows there are Agramon-era and the 1.130 client shows
    Ellan Vannin (Research/Scopes/New-Frontiers-Readiness-2026-11-14.md, decision 2);
  - EoD keep guards (DOL.GS.Keeps.*) are skipped: keeps spawn their own garrison from KeepPosition;
  - EoD's realm teleporter NPCs are skipped: the Gate Wardens (make_nf_travel_edits.py) replace them;
  - the three NF return points Gaheris moved clear of the border keep passage (gaheris sql/64) use those positions.
--labyrinth writes the Labyrinth (region 245) as its own pair of files: it only looks right on a stock 1.130 client
(the owner's test client shows Hall of the Corrupt art in 245), so it is applied separately.

usage: python -I make_nf_world_edits.py <world.sqlite3.db> <eod-db-public data dir> <out prefix> [--labyrinth]
writes <prefix>-1-support.edits.json, <prefix>-2-mobs.edits.json, <prefix>.excluded.json, <prefix>.report.txt
"""
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_catacombs_import_edits import HELPER_MODELS, HELPER_NAMES, PEACE, ROAM, ROGUE_TRAINER, load, remap_model  # noqa: E402

NF = {163: 'New Frontiers', 233: "Summoner's Hall", 244: 'Passage of Conflict'}
LAB = {245: 'Labyrinth'}
SKIP_ZONES = {163: 'Ellan Vannin island (EoD rows are Agramon-era)'}
SKIP_CLASS_PREFIX = ('DOL.GS.Keeps.',)
SKIP_CLASSES = {'DOL.GS.HiberniaTeleporter', 'DOL.GS.MidgardTeleporter', 'DOL.GS.AlbionTeleporter',
                'DOL.GS.Scripts.FrontierTeleporter', 'DOL.GS.FrontierTeleporter'}
# EoD class names that are spelled differently here, or that we do not have (buff token shops: plain NPC).
CLASS_MAP = {'DOL.GS.Scripts.Recharger': 'DOL.GS.Recharger', 'DOL.GS.SpellTokenMerchant': 'DOL.GS.GameNPC'}
# gaheris sql/64-move-frontier-returns.sql: three returns landed beside the border keep passage.
RETURN_MOVES = {(421257, 485590): (421281, 483193), (333634, 419960): (332041, 418130), (703994, 737935): (703922, 735941)}
report = []


def say(*a):
    line = ' '.join(str(x) for x in a)
    print(line)
    report.append(line)


def strip(row, columns):
    return {c: row[c] for c in columns if c in row}


def main():
    world, eod, prefix = sys.argv[1], sys.argv[2], sys.argv[3]
    labyrinth = '--labyrinth' in sys.argv[4:]
    regions = LAB if labyrinth else NF
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    cols = {t: [r[1] for r in db.execute(f'PRAGMA table_info({t})')]
            for t in ('Mob', 'ZonePoint', 'NpcTemplate', 'MerchantItem', 'Zones', 'Door', 'WorldObject')}
    support = []

    # ------------------------------------------------------------------ zones
    our_zone_ids = {int(r[0]) for r in db.execute('SELECT ZoneID FROM Zones')}
    boxes = defaultdict(list)
    zone_of = []
    for r in db.execute('SELECT ZoneID, RegionID, OffsetX, OffsetY, Width, Height FROM Zones'):
        boxes[int(r['RegionID'])].append((int(r['ZoneID']), int(r['OffsetX']) * 8192, int(r['OffsetY']) * 8192,
                                          int(r['Width']) * 8192, int(r['Height']) * 8192))
    for z in load(eod, 'Zones'):
        if int(z['RegionID']) in regions and int(z['ZoneID']) not in our_zone_ids:
            support.append({'table': 'Zones', 'insert': strip(z, cols['Zones'])})
            boxes[int(z['RegionID'])].append((int(z['ZoneID']), int(z['OffsetX']) * 8192, int(z['OffsetY']) * 8192,
                                              int(z['Width']) * 8192, int(z['Height']) * 8192))
            say('Zones row added:', z['ZoneID'], z['Name'])
    if labyrinth:
        support.append({'table': 'Zones', 'where': {'ZoneID': 245}, 'set': {'Name': 'Labyrinth'}})
        support.append({'table': 'Regions', 'where': {'RegionID': 245}, 'set': {'Description': 'Labyrinth'}})
        say('Zones/Regions 245 renamed Labyrinth (was Hall of the Corrupt; that dungeon lives in 277)')

    def zone_at(reg, x, y):
        for zid, ox, oy, w, h in boxes.get(reg, ()):
            if ox <= x < ox + w and oy <= y < oy + h:
                return zid
        return None

    # ------------------------------------------------------------------ mobs
    have_ids = {r[0] for r in db.execute('SELECT Mob_ID FROM Mob')}
    ours_here = defaultdict(list)
    for r in db.execute(f"SELECT Name, Region, X, Y FROM Mob WHERE Region IN ({','.join(map(str, regions))})"):
        ours_here[(int(r['Region']), (r['Name'] or '').lower())].append((int(r['X']), int(r['Y'])))
    known_classes = {r[0] for r in db.execute('SELECT DISTINCT ClassType FROM Mob')}
    templates = {int(r[0]) for r in db.execute('SELECT TemplateId FROM NpcTemplate')}
    template_keys = {r[0] for r in db.execute('SELECT NpcTemplate_ID FROM NpcTemplate')}
    items = {r[0].lower() for r in db.execute('SELECT Id_nb FROM ItemTemplate')}
    our_lists = defaultdict(list)
    for r in db.execute('SELECT ItemListID, ItemTemplateID FROM MerchantItem'):
        our_lists[r[0]].append((r[1] or '').lower())

    mobs, excluded, new_classes, roam_set, remapped = [], [], Counter(), 0, 0
    for m in load(eod, 'Mob'):
        reg = int(m.get('Region') or 0)
        if reg not in regions:
            continue
        x, y = int(m['X']), int(m['Y'])
        name = (m.get('Name') or '').lower()
        base_model = int(m['Model']) & 0x7FFF
        zid = zone_at(reg, x, y)
        cls = m['ClassType'] or ''
        why = ('invisible helper' if cls == 'DOL.GS.GameNPC' and base_model in HELPER_MODELS else
               'placeholder name' if name in HELPER_NAMES else
               'outside every zone' if zid is None else
               'skipped zone: ' + SKIP_ZONES[zid] if zid in SKIP_ZONES and reg == 163 else
               'keep guard (the keep spawns its own)' if cls.startswith(SKIP_CLASS_PREFIX) else
               'realm teleporter (Gate Wardens replace it)' if cls in SKIP_CLASSES else
               'Mob_ID already used' if m['Mob_ID'] in have_ids else
               'already in our world' if any((px - x) ** 2 + (py - y) ** 2 <= 300 ** 2
                                             for px, py in ours_here.get((reg, name), ())) else None)
        if why:
            excluded.append({'why': why, 'Mob_ID': m['Mob_ID'], 'Name': m['Name'], 'Region': reg, 'Zone': zid,
                             'X': x, 'Y': y, 'Z': m['Z'], 'Level': m.get('Level'), 'ClassType': cls})
            continue
        row = strip(m, cols['Mob'])
        row['ClassType'] = CLASS_MAP.get(row['ClassType'], row['ClassType'])
        if row['ClassType'] == 'DOL.GS.Trainer.RogueTrainer':
            row['ClassType'] = ROGUE_TRAINER.get(int(row.get('Realm') or 0), row['ClassType'])
        new_model = remap_model(row['Model'], row.get('Name'), reg)
        remapped += new_model != row['Model']
        row['Model'] = new_model
        if (int(row.get('Realm') or 0) == 0 and row['ClassType'] == 'DOL.GS.GameNPC'
                and not int(row.get('Flags') or 0) & PEACE and int(row.get('RoamingRange') or 0) == 0):
            row['RoamingRange'] = ROAM
            roam_set += 1
        if row['ClassType'] not in known_classes:
            new_classes[row['ClassType']] += 1
        mobs.append(row)

    # ------------------------------------------------------------------ merchants
    eod_lists = defaultdict(list)
    for r in load(eod, 'MerchantItem'):
        eod_lists[r['ItemListID']].append(r)
    demoted, lists_added = [], 0
    for row in mobs:
        lid = row.get('ItemsListTemplateID')
        if 'Merchant' not in row['ClassType'] or not lid:
            continue
        if any(i in items for i in our_lists.get(lid, ())):
            continue
        sellable = [r for r in eod_lists.get(lid, ()) if (r['ItemTemplateID'] or '').lower() in items]
        if sellable and lid not in our_lists:
            for r in sellable:
                support.append({'table': 'MerchantItem', 'insert': strip(r, cols['MerchantItem'])})
            our_lists[lid] = [(r['ItemTemplateID'] or '').lower() for r in sellable]
            lists_added += 1
        elif not sellable:
            demoted.append((row['Name'], row['Region'], lid))
            row['ClassType'], row['ItemsListTemplateID'] = 'DOL.GS.GameNPC', ''

    # ------------------------------------------------------------------ NPC templates
    used = {int(r['NPCTemplateID']) for r in mobs if int(r.get('NPCTemplateID') or -1) > 0}
    missing = used - templates
    eod_templates = {int(t['TemplateId']): t for t in load(eod, 'NpcTemplate')}
    for tid in sorted(missing):
        t = eod_templates.get(tid)
        if t:
            ins = strip(t, cols['NpcTemplate'])
            if ins.get('NpcTemplate_ID') in template_keys:
                ins['NpcTemplate_ID'] = f'eod-template-{tid}'
            if ins.get('Model') not in (None, ''):
                ins['Model'] = ';'.join(str(remap_model(x, ins.get('Name'), 0)) if x.strip().isdigit() else x
                                        for x in str(ins['Model']).split(';'))
            support.append({'table': 'NpcTemplate', 'insert': ins})
    no_template = sorted(t for t in missing if t not in eod_templates)

    # ------------------------------------------------------------------ doors and world objects
    our_doors = {int(r[0]) for r in db.execute('SELECT InternalID FROM Door')}
    nf_zone_ids = {zid for reg in regions for zid, *_ in boxes.get(reg, ())}
    doors = 0
    for d in load(eod, 'Door'):
        if int(d['InternalID']) // 1_000_000 in nf_zone_ids and int(d['InternalID']) not in our_doors:
            support.append({'table': 'Door', 'insert': strip(d, cols['Door'])})
            doors += 1
    our_wo = [(r['Name'], int(r['Region']), int(r['X']), int(r['Y'])) for r in
              db.execute(f"SELECT Name, Region, X, Y FROM WorldObject WHERE Region IN ({','.join(map(str, regions))})")]
    our_wo_ids = {r[0] for r in db.execute('SELECT WorldObject_ID FROM WorldObject')}
    objects = 0
    for o in load(eod, 'WorldObject'):
        reg = int(o['Region'])
        if reg not in regions or o['WorldObject_ID'] in our_wo_ids:
            continue
        if any(n == o['Name'] and r == reg and (x - int(o['X'])) ** 2 + (y - int(o['Y'])) ** 2 <= 50 ** 2
               for n, r, x, y in our_wo):
            continue
        support.append({'table': 'WorldObject', 'insert': strip(o, cols['WorldObject'])})
        objects += 1

    # ------------------------------------------------------------------ areas (NF realm towns: Catterick Hamlet, Godrborg, Crair Treflan)
    area_cols = [r[1] for r in db.execute('PRAGMA table_info(Area)')]
    our_areas = {(int(r[0]), (r[1] or '').lower()) for r in db.execute('SELECT Region, Description FROM Area')}
    areas = 0
    for a in load(eod, 'Area'):
        if int(a['Region']) in regions and (int(a['Region']), (a['Description'] or '').lower()) not in our_areas:
            support.append({'table': 'Area', 'insert': strip(a, area_cols)})
            areas += 1
    say('areas added:', areas)

    # ------------------------------------------------------------------ zone points
    our_zp = {(int(r['Id']), int(r['Realm'])): r for r in db.execute('SELECT * FROM ZonePoint')}
    zp_classes = {r[0] for r in db.execute("SELECT DISTINCT ClassType FROM ZonePoint WHERE ClassType <> ''")}
    zp_added, zp_same, zp_conflict, moved = 0, 0, [], 0
    lab_ids = set(range(732, 753))
    for z in load(eod, 'ZonePoint'):
        tr, sr, zid = int(z.get('TargetRegion') or 0), int(z.get('SourceRegion') or 0), int(z['Id'])
        if tr not in regions and sr not in regions:
            continue
        # the Labyrinth's own links go with the Labyrinth batch, whichever side they start on
        if (zid in lab_ids or tr in LAB or sr in LAB) != labyrinth:
            continue
        mine = our_zp.get((zid, int(z['Realm'])))
        if mine is None:
            ins = strip(z, cols['ZonePoint'])
            if ins.get('ClassType') and ins['ClassType'] not in zp_classes:
                ins['ClassType'] = ''
            if (int(ins['TargetX']), int(ins['TargetY'])) in RETURN_MOVES:
                ins['TargetX'], ins['TargetY'] = RETURN_MOVES[(int(ins['TargetX']), int(ins['TargetY']))]
                moved += 1
            support.append({'table': 'ZonePoint', 'insert': ins})
            zp_added += 1
        elif (int(mine['TargetRegion']), int(mine['TargetX']), int(mine['TargetY'])) == (tr, int(z['TargetX']), int(z['TargetY'])):
            zp_same += 1
        else:
            zp_conflict.append((zid, f"ours -> region {mine['TargetRegion']}", f"EoD -> region {tr}"))

    # ------------------------------------------------------------------ report and files
    say('Labyrinth import plan' if labyrinth else 'New Frontiers world plan (NF 163, Summoner\'s Hall 233, Passage of Conflict 244)')
    say('mob rows to add:', len(mobs), '| excluded:', len(excluded), dict(Counter(e['why'] for e in excluded)))
    by_zone = Counter((int(r['Region']), zone_at(int(r['Region']), int(r['X']), int(r['Y']))) for r in mobs)
    for (reg, zid), n in sorted(by_zone.items()):
        say(f'  region {reg:>4} zone {zid:>4} {n:>6}')
    say('realm:', dict(Counter(int(r.get('Realm') or 0) for r in mobs)))
    say('classes:', dict(Counter(r['ClassType'] for r in mobs).most_common(40)))
    say('classes our world has never used:', dict(new_classes))
    say('RoamingRange 0 -> 100 on', roam_set, 'rows; model remapped on', remapped, 'rows')
    say('NPC templates added:', sum(1 for e in support if e['table'] == 'NpcTemplate'), '| missing in EoD too:', no_template)
    say('merchant lists added:', lists_added, '| merchant item rows:', sum(1 for e in support if e['table'] == 'MerchantItem'),
        '| merchants made plain NPCs (nothing sellable):', demoted)
    say('doors added:', doors, '| world objects added:', objects)
    say('zone points added:', zp_added, f'({moved} returns moved clear of the border keep passage)',
        '| already ours and the same:', zp_same, '| id used by ours for another place:', zp_conflict)
    out = lambda name, data: Path(f'{prefix}{name}').write_text(json.dumps(data, indent=0, ensure_ascii=False) + '\n',
                                                                 encoding='utf-8', newline='\n')
    out('-1-support.edits.json', support)
    out('-2-mobs.edits.json', [{'table': 'Mob', 'insert': r} for r in mobs])
    out('.excluded.json', excluded)
    Path(f'{prefix}.report.txt').write_text('\n'.join(report) + '\n', encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
