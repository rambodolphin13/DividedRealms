"""Plan: put back the New Frontiers Breifine spawns the 1.130 world never got (DR-523, read-only generator).

Discord 2026-11-21 (Vonwar): "the fin wolves are not at the normal spot in Breifine". Region 163 zone 172 (Breifine) has
every mob the DOL public database places there except these, which it has none of:
finliath (7), grand pooka (4), wanshee (3), field lich (1), Idol of Crom Cruiach (1).
Evern is left out: its one spawn is still in the old Breifine (region 200) and a second copy of a unique boss is the
owner's call.

Rows come from the reference database's src_dolpub_mob copy (the same source as the rest of NF Breifine, PackageID
Public_DB), with a fresh Mob_ID, only where we have no spawn of that name within RADIUS. A template id our world lacks
(grand pooka's 60161556) is replaced by our template of the same name (21001).

usage: python -I make_nf_breifine_spawns_edits.py <world.sqlite3.db> <reference.sqlite3> <out.edits.json>
"""
import json
import sqlite3
import sys
import uuid

REGION, ZONE = 163, 172
NAMES = ('finliath', 'grand pooka', 'wanshee', 'field lich', 'idol of crom cruiach')
RADIUS = 350
# Fixed namespace so the generator gives the same ids on every run.
ID_NAMESPACE = uuid.UUID('6f1b3c52-5d4e-4a8e-9a43-523000000172')


def build(world, reference):
    db = sqlite3.connect(f'file:{world}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    ref = sqlite3.connect(f'file:{reference}?mode=ro', uri=True)
    ref.row_factory = sqlite3.Row
    ox, oy, w, h = db.execute('SELECT OffsetX, OffsetY, Width, Height FROM Zones WHERE ZoneID = ? AND RegionID = ?',
                              (ZONE, REGION)).fetchone()
    x0, x1, y0, y1 = ox * 8192, (ox + w) * 8192, oy * 8192, (oy + h) * 8192
    info = list(db.execute('PRAGMA table_info(Mob)'))
    mob_cols = [r[1] for r in info]
    # The archive leaves some NOT NULL columns empty (Race); ours default them to 0 like the other Public_DB rows.
    not_null = {r[1]: (r[4].strip("'") if r[4] is not None else (0 if 'INT' in (r[2] or '').upper() else ''))
                for r in info if r[3]}
    templates = {str(r[0]) for r in db.execute('SELECT TemplateId FROM NpcTemplate')}
    # A template id the archive names but our world lacks: use our template of the same name, if there is one.
    by_name = {}
    for tid, tname in db.execute('SELECT TemplateId, Name FROM NpcTemplate ORDER BY TemplateId'):
        by_name.setdefault((tname or '').lower(), tid)
    marks = ','.join('?' * len(NAMES))
    ours = [(r['Name'].lower(), r['X'], r['Y']) for r in db.execute(
        f'SELECT Name, X, Y FROM Mob WHERE Region = ? AND lower(Name) IN ({marks})', (REGION, *NAMES))]
    src = ref.execute(f'SELECT * FROM src_dolpub_mob WHERE Region = ? AND X BETWEEN ? AND ? AND Y BETWEEN ? AND ? '
                      f'AND lower(Name) IN ({marks}) ORDER BY Mob_ID', (REGION, x0, x1, y0, y1, *NAMES)).fetchall()
    edits, skipped = [], []
    for r in src:
        name = r['Name'].lower()
        if any(n == name and (x - r['X']) ** 2 + (y - r['Y']) ** 2 <= RADIUS ** 2 for n, x, y in ours):
            skipped.append(f"{r['Name']} at {r['X']},{r['Y']}: we already have one there")
            continue
        template = r['NPCTemplateID']
        if template not in (None, '', 0, -1) and str(template) not in templates:
            if name not in by_name:
                skipped.append(f"{r['Name']}: NPC template {template} missing and none of that name")
                continue
            template = by_name[name]
        row = {c: r[c] for c in mob_cols if c in r.keys()}
        row['NPCTemplateID'] = template
        for c, default in not_null.items():
            if row.get(c) is None and c != 'Mob_ID':
                row[c] = default
        row['Mob_ID'] = str(uuid.uuid5(ID_NAMESPACE, f"dolpub:{r['Mob_ID']}"))
        row['LastTimeRowUpdated'] = '2000-01-01 00:00:00'
        edits.append({'table': 'Mob', 'insert': row})
    return edits, skipped


def main():
    world, reference, out = sys.argv[1], sys.argv[2], sys.argv[3]
    edits, skipped = build(world, reference)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(edits, f, indent=1, ensure_ascii=False)
        f.write('\n')
    counts = {}
    for e in edits:
        counts[e['insert']['Name']] = counts.get(e['insert']['Name'], 0) + 1
    print(f'{len(edits)} spawns to restore in NF Breifine: {counts}')
    for s in skipped:
        print('  skipped:', s)


if __name__ == '__main__':
    main()
