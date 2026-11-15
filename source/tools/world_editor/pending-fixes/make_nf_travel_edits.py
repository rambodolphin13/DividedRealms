"""Travel catalogue and New Frontiers teleporters (2026-11-15): World Workshop edits file generator.

Owner 2026-11-15: get around by teleporter, as close to Gaheris's travel network as we can, but realm-locked in the
realm-war areas: an Albion player can only port to Albion's side of New Frontiers (and the other war areas), never a
Midgard or Hibernia keep or side; the co-op lands are open to everyone. Code: scripts/teleporters/TravelCatalogue.cs.

Rows written (Teleport table, one row per realm that may use it):
  Type 'Travel'      the player's own lands, neutral places, and its own side of NF, DF, Passage of Conflict and
                     Summoner's Hall
  Type 'TravelCoop'  another realm's lands; the teleporters offer these only while hybrid_coop_pvp is on
Sources, in order (first name wins): Gaheris's catalogue (pactor/gaheris sql 11/14/16, itself taken from EoD's
teleport table), our own realm teleporter rows, EoD db-public teleport rows, and for each Catacombs / Darkness Rising
region the arrival point of an entrance zone point. Each row's home realm is the realm whose teleporter offered it
(Realm 0 = neutral). Battlegrounds stay on the existing [Battlegrounds] choice (level brackets); New Frontiers keeps are
added live by the server (keeps the realm holds). Places in disabled regions or outside every zone box are left out.

NPCs: a realm teleporter (LiveTeleporter) at each of the six New Frontiers border keeps, where EoD's frontier
teleporters stand, and one in each realm's frontier town. In a realm-war area a teleporter serves only its own realm.

usage: python -I make_nf_travel_edits.py <world.sqlite3.db> <eod-db-public data dir> <gaheris sql dir> <out prefix>
writes <prefix>.edits.json, <prefix>-labyrinth.edits.json (the Labyrinth's three realm entrances) and <prefix>.report.txt
"""
import json
import re
import sqlite3
import sys
import uuid
from collections import Counter, defaultdict
from pathlib import Path

WAR = {163, 165, 233, 244, 245, 249, 250, 251, 252, 253} | set(range(234, 243))   # HybridCoopRules.DefaultWarZones
BATTLEGROUNDS = {165, 250, 251, 252, 253} | set(range(234, 243))
NOT_A_PLACE = {'accept', 'sure', 'personal', 'housing', 'shrouded isles', 'towns', 'entrance', 'guild', 'hearth',
               'albthroneexit', 'midthroneexit', 'hibthroneexit', 'albthroneroom', 'midthroneroom', 'hibthroneroom',
               'atlantis', 'darkness falls'}
CATACOMBS = [58, 59, 63, 65, 66, 67, 68, 69, 92, 93, 94, 95, 96, 97, 98, 99, 109, 148, 149, 162, 188, 189, 195, 196, 197,
             226, 227, 228, 229, 243, 335, 339, 341, 376, 390, 429, 430, 433, 435, 436]
REALMS = (1, 2, 3)
# Each realm has its own copy of Atlantis (same layout): a place in Albion's copy is offered in the player's own copy.
ATLANTIS_COPY = {73: {1: 73, 2: 30, 3: 130}, 88: {1: 88, 2: 45, 3: 145}, 89: {1: 89, 2: 46, 3: 146},
                 90: {1: 90, 2: 47, 3: 147}}
ATLANTIS_COPY.update({copy: ATLANTIS_COPY[alb] for alb in list(ATLANTIS_COPY) for copy in ATLANTIS_COPY[alb].values()})
CATACOMBS_REALM = {**{r: 1 for r in (59, 63, 65, 66, 67, 68, 69, 109, 148, 196, 227)},
                   **{r: 2 for r in (58, 149, 162, 188, 189, 195, 226, 243)},
                   **{r: 3 for r in (92, 93, 94, 95, 96, 97, 98, 99, 197, 228, 335, 339, 341)}}
NEUTRAL = {229, 246, 247, 248, 276, 277}       # Burial Grounds, the Old Frontiers dungeons, Summoner's Hall raids
SKIP_REGIONS = {2, 102, 202, 27}               # housing (own [housing] choice), the unbuilt tutorial region


# Region numbers do not split cleanly by realm (Hibernia's Shrouded Isles is 181); these are the home lands as the
# bots' AutonomousWorldBotController.ProtectedRealm lists them.
HOME_REGIONS = {**{r: 1 for r in (1, 10, 20, 21, 22, 23, 24, 50, 51, 60, 61, 62)},
                **{r: 2 for r in (100, 101, 125, 126, 127, 128, 129, 150, 151, 160, 161)},
                **{r: 3 for r in (200, 180, 181, 190, 191, 192, 193, 194, 201, 220, 221, 222, 223, 224)}}


def home_realm(region):
    """The realm whose lands a place is in (0 = neutral). Teleport rows' Realm is the offering NPC's, not this."""
    if region in NEUTRAL or region in ATLANTIS_COPY:
        return 0
    return HOME_REGIONS.get(region) or CATACOMBS_REALM.get(region, 0)
NAME = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}


def deg(d):
    return round(d * 4096 / 360) % 4096


# Each realm's own side of the realm-war areas. NF border keeps and towns: EoD's NF teleporter rows and zone points
# 189 (Snowdonia); Darkness Falls: the three realm arrivals Gaheris surveyed in game (sql/131, zone-local + 8192);
# Passage of Conflict: the NF entrance in each realm's territory (zone points 194 Breifine, 195 Jamtland, 196 Pennine);
# Summoner's Hall: its one entrance from Passage of Conflict (zone point 147), the same for every realm.
WAR_STOPS = {
    1: [('Castle Sauvage (New Frontiers)', 163, 652700, 617189, 9560, 2000),
        ('Snowdonia Fortress (New Frontiers)', 163, 616381, 679321, 9560, 2000),
        ('Catterick Hamlet', 163, 677193, 568367, 8088, 0),
        ('Darkness Falls (Albion entrance)', 249, 31236, 27920, 22893, deg(92))],
    2: [('Svasud Faste (New Frontiers)', 163, 651761, 313723, 9432, 1006),
        ('Vindsaul Faste (New Frontiers)', 163, 715347, 364830, 9432, 1006),
        ('Godrborg', 163, 597563, 304676, 8088, 0),
        ('Darkness Falls (Midgard entrance)', 249, 18798, 18667, 22892, deg(269))],
    3: [('Druim Ligen (New Frontiers)', 163, 396557, 618469, 9825, 1966),
        ('Druim Cain (New Frontiers)', 163, 432550, 680367, 9752, 1966),
        ('Crair Treflan', 163, 373944, 572847, 8040, 0),
        ('Darkness Falls (Hibernia entrance)', 249, 46324, 40871, 21357, deg(1))],
}
POC_ENTRANCE = {1: 196, 2: 195, 3: 194}
SUMMONERS_HALL = 147
LABYRINTH = {1: 734, 2: 733, 3: 732}
# EoD's frontier teleporters at the six NF border keeps (skipped by make_nf_world_edits.py) and the realm towns.
NF_TELEPORTER_CLASSES = {'DOL.GS.AlbionTeleporter': 1, 'DOL.GS.MidgardTeleporter': 2, 'DOL.GS.HiberniaTeleporter': 3}
NF_TOWNS = {1: (677343, 568367, 8088), 2: (597713, 304676, 8088), 3: (374094, 572847, 8040)}
report = []


def say(*a):
    line = ' '.join(str(x) for x in a)
    print(line)
    report.append(line)


def load(eod, name):
    rows = []
    for p in sorted(Path(eod).glob(f'{name}.json')) + sorted(Path(eod).glob(f'{name}.*.json')):
        data = json.loads(p.read_text(encoding='utf-8'))
        rows.extend(data if isinstance(data, list) else [])
    return rows


def gaheris_rows(sql_dir):
    pattern = re.compile(r"VALUES \(UUID\(\), '([^']*)', '((?:[^']|'')*)', (\d+), (\d+), (-?\d+), (-?\d+), (-?\d+), (-?\d+)\)")
    rows = []
    for f in ('11-travel-catalogue.sql', '14-atlantis-travel.sql', '16-portals.sql'):
        p = Path(sql_dir) / f
        if p.exists():
            for m in pattern.finditer(p.read_text(encoding='utf-8', errors='replace')):
                rows.append((m.group(2).replace("''", "'"), int(m.group(3)), int(m.group(4)), int(m.group(5)), int(m.group(6)),
                             int(m.group(7)), int(m.group(8)), 'gaheris ' + f))
    return rows


def main():
    world, eod, gsql, prefix = sys.argv[1:5]
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    disabled_exp = {int(x) for x in (db.execute("SELECT Value FROM ServerProperty WHERE [Key]='disabled_expansions'").fetchone() or ['']
                                     )[0].replace(',', ';').split(';') if x.strip().isdigit()}
    regions = {int(r['RegionID']): r for r in db.execute('SELECT RegionID, Description, Expansion FROM Regions')}
    boxes = defaultdict(list)
    for r in db.execute('SELECT RegionID, OffsetX, OffsetY, Width, Height FROM Zones'):
        boxes[int(r['RegionID'])].append((int(r['OffsetX']) * 8192, int(r['OffsetY']) * 8192, int(r['Width']) * 8192, int(r['Height']) * 8192))
    for z in load(eod, 'Zones'):   # Summoner's Hall 233 has no Zones row until make_nf_world_edits.py adds it
        if int(z['RegionID']) == 233 and not boxes.get(233):
            boxes[233].append((int(z['OffsetX']) * 8192, int(z['OffsetY']) * 8192, int(z['Width']) * 8192, int(z['Height']) * 8192))
    in_zone = lambda reg, x, y: any(ox <= x < ox + w and oy <= y < oy + h for ox, oy, w, h in boxes.get(reg, ()))
    usable = lambda reg: reg in regions and int(regions[reg]['Expansion'] or 0) not in disabled_exp
    existing = {(r['Type'], r['TeleportID'].lower(), int(r['Realm'])) for r in db.execute('SELECT Type, TeleportID, Realm FROM Teleport')}

    candidates = gaheris_rows(gsql)
    candidates += [(r['TeleportID'], int(r['Realm']), int(r['RegionID']), int(r['X']), int(r['Y']), int(r['Z']), int(r['Heading']), 'ours')
                   for r in db.execute("SELECT * FROM Teleport WHERE Type = ''")]
    candidates += [(r['TeleportID'], int(r['Realm']), int(r['RegionID']), int(r['X']), int(r['Y']), int(r['Z']), int(r['Heading']), 'eod')
                   for r in load(eod, 'Teleport') if (r['Type'] or '') == '']
    zp = list(db.execute('SELECT * FROM ZonePoint'))
    for reg in CATACOMBS:
        if not usable(reg):
            continue
        entrances = [z for z in zp if int(z['TargetRegion']) == reg and int(z['SourceRegion']) not in CATACOMBS
                     and int(z['SourceRegion']) not in (0, reg)]
        by_realm = {}
        for z in entrances:
            src = int(z['SourceRegion'])
            home = 1 if src < 100 else 2 if src < 200 else 3 if src < 300 else 0
            by_realm.setdefault(home, z)
        for home, z in sorted(by_realm.items()):
            name = regions[reg]['Description'] + ('' if len(by_realm) == 1 or home not in NAME else f' ({NAME[home]} entrance)')
            candidates.append((name, home if len(by_realm) > 1 else home, reg, int(z['TargetX']), int(z['TargetY']),
                               int(z['TargetZ']), int(z['TargetHeading']), 'zone point'))

    edits, skipped, taken = [], Counter(), Counter()
    seen = set()

    def row(kind, name, realm, reg, x, y, z, h):
        key = (kind, name.lower(), realm)
        if key in existing or key in seen:
            return
        seen.add(key)
        edits.append({'table': 'Teleport', 'insert': {'Teleport_ID': f'travel-{uuid.uuid5(uuid.NAMESPACE_URL, f"{kind}|{name}|{realm}")}',
                                                       'Type': kind, 'TeleportID': name, 'Realm': realm, 'RegionID': reg,
                                                       'X': x, 'Y': y, 'Z': z, 'Heading': h}})

    names, spots = set(), []
    for name, home, reg, x, y, z, h, src in candidates:
        lname = name.strip().lower()
        why = ('not a place' if lname in NOT_A_PLACE or not lname else
               'already listed' if lname in names else
               'same spot as a listed place' if any(r == reg and (px - x) ** 2 + (py - y) ** 2 < 600 ** 2 for r, px, py in spots) else
               'housing or tutorial region' if reg in SKIP_REGIONS else
               'battleground (level brackets)' if reg in BATTLEGROUNDS else
               'realm-war area (own-side stops only)' if reg in WAR else
               'region disabled or missing' if not usable(reg) else
               'outside every zone' if not in_zone(reg, x, y) else None)
        if why:
            skipped[why] += 1
            continue
        names.add(lname)
        spots.append((reg, x, y))
        taken[src] += 1
        # one entrance per realm: that realm's; otherwise the land the place is in
        if not (src == 'zone point' and lname.endswith(' entrance)')):
            home = home_realm(reg)
        for realm in REALMS:
            own_reg = ATLANTIS_COPY.get(reg, {}).get(realm, reg)
            if own_reg != reg and not in_zone(own_reg, x, y):
                continue
            row('Travel' if home in (0, realm) else 'TravelCoop', name.strip(), realm, own_reg, x, y, z, h)

    by_id = defaultdict(list)
    for z in zp:
        by_id[int(z['Id'])].append(z)
    eod_zp = defaultdict(list)
    for z in load(eod, 'ZonePoint'):
        eod_zp[int(z['Id'])].append(z)
    target = lambda zid: (by_id.get(zid) or eod_zp.get(zid) or [None])[0]
    war_rows = 0
    for realm in REALMS:
        stops = list(WAR_STOPS[realm])
        poc = target(POC_ENTRANCE[realm])
        stops.append(('Passage of Conflict', 244, int(poc['TargetX']), int(poc['TargetY']), int(poc['TargetZ']), int(poc['TargetHeading'])))
        sh = target(SUMMONERS_HALL)
        stops.append(("Summoner's Hall (New Frontiers)", 233, int(sh['TargetX']), int(sh['TargetY']), int(sh['TargetZ']), int(sh['TargetHeading'])))
        for name, reg, x, y, z, h in stops:
            assert in_zone(reg, x, y), (name, reg, x, y)
            row('Travel', name, realm, reg, x, y, z, h)
            war_rows += 1

    # ---- NPCs: realm teleporters at the NF border keeps and towns
    npcs = 0
    for m in load(eod, 'Mob'):
        realm = NF_TELEPORTER_CLASSES.get(m['ClassType'])
        if int(m.get('Region') or 0) != 163 or realm is None:
            continue
        edits.append({'table': 'Mob', 'insert': {'Mob_ID': f'travel-nf-teleporter-{m["Mob_ID"]}', 'ClassType': 'DOL.GS.Scripts.LiveTeleporter',
                                                  'Name': 'Teleporter', 'Region': 163, 'X': int(m['X']), 'Y': int(m['Y']), 'Z': int(m['Z']),
                                                  'Heading': int(m['Heading']), 'Realm': realm, 'Level': 60, 'Model': 61, 'Size': 50,
                                                  'Flags': 16, 'RespawnInterval': -1, 'PackageID': 'nf-travel'}})
        npcs += 1
    for realm, (x, y, z) in NF_TOWNS.items():
        edits.append({'table': 'Mob', 'insert': {'Mob_ID': f'travel-nf-town-teleporter-{realm}', 'ClassType': 'DOL.GS.Scripts.LiveTeleporter',
                                                  'Name': 'Teleporter', 'Region': 163, 'X': x, 'Y': y, 'Z': z, 'Heading': 0, 'Realm': realm,
                                                  'Level': 60, 'Model': 61, 'Size': 50, 'Flags': 16, 'RespawnInterval': -1, 'PackageID': 'nf-travel'}})
        npcs += 1

    lab = []
    for realm, zid in LABYRINTH.items():
        z = target(zid)
        lab.append({'table': 'Teleport', 'insert': {'Teleport_ID': f'travel-labyrinth-{realm}', 'Type': 'Travel', 'TeleportID': 'Labyrinth',
                                                     'Realm': realm, 'RegionID': 245, 'X': int(z['TargetX']), 'Y': int(z['TargetY']),
                                                     'Z': int(z['TargetZ']), 'Heading': int(z['TargetHeading'])}})

    kinds = Counter((e['insert']['Type'], e['insert']['Realm']) for e in edits if e['table'] == 'Teleport')
    say('travel catalogue: places taken by source', dict(taken), '| skipped', dict(skipped))
    say('teleport rows', dict(kinds), f'| own-side realm-war rows {war_rows}')
    say('families per realm (Travel + TravelCoop):')
    fam = Counter()
    for e in edits:
        if e['table'] == 'Teleport':
            fam[(e['insert']['Realm'], e['insert']['Type'], e['insert']['RegionID'] // 100)] += 1
    say(' ', dict(sorted(fam.items())))
    say('NF teleporter NPCs', npcs, '| Labyrinth entrance rows (separate file)', len(lab))
    Path(f'{prefix}.edits.json').write_text(json.dumps(edits, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    Path(f'{prefix}-labyrinth.edits.json').write_text(json.dumps(lab, indent=0, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    Path(f'{prefix}.report.txt').write_text('\n'.join(report) + '\n', encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
