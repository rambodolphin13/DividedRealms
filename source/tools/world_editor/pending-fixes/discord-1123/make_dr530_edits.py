r"""DR-530 (first part): Serf's Folly entrance and Demon's Breach (the 1-4 / 5-9 starter battleground dungeon).

  * ZonePoint 405 Muire Tomb -> Serf's Folly (443): the wing has 77 spawns and a navmesh; its exits 380/381 exist.
  * Demon's Breach (region 489): the 851 spawns of the EoD db-public archive (src_eodb_Mob, every one on the new
    zone489.nav), the 28 NPC templates they use that our world lacks (src_eodb_NpcTemplate), and the six entrances
    704-709 from the realm home zones (Nergal's Breach 1-4, Balban's Breach 5-9, DemonsBreachJumpPoint level rules).
    Its exits 710-721 already exist.

Read-only on the world and reference DB; writes dr530-serfs-folly-demons-breach-2026-11-23.edits.json next to itself.

    python -I make_dr530_edits.py <world.db> <reference.sqlite3>
"""
import json, sqlite3, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
world = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
ref = sqlite3.connect(f'file:{sys.argv[2]}?mode=ro', uri=True)


def columns(con, table):
    return [c[1] for c in con.execute(f"PRAGMA table_info({table})")]


def rows(con, sql, args=()):
    cur = con.execute(sql, args)
    names = [d[0] for d in cur.description]
    return [dict(zip(names, r)) for r in cur]


edits = []
have_points = {i for i, in world.execute("SELECT Id FROM ZonePoint")}

# --- zone points -------------------------------------------------------------------------------------------
POINTS = [  # id, realm, source region, source xyz (client trigger), class
    (405, 0, 221, (28249, 32470, 0), ''),
    (704, 3, 200, None, 'DOL.GS.ServerRules.BalbansBreachJumpPoint'),
    (705, 3, 200, None, 'DOL.GS.ServerRules.NergalsBreachJumpPoint'),
    (706, 1, 1, None, 'DOL.GS.ServerRules.BalbansBreachJumpPoint'),
    (707, 1, 1, None, 'DOL.GS.ServerRules.NergalsBreachJumpPoint'),
    (708, 2, 100, None, 'DOL.GS.ServerRules.NergalsBreachJumpPoint'),
    (709, 2, 100, None, 'DOL.GS.ServerRules.BalbansBreachJumpPoint'),
]
for point, realm, src_region, src, cls in POINTS:
    if point in have_points:
        print(f"zone point {point} already present, skipped")
        continue
    a = rows(ref, "SELECT * FROM src_eodb_ZonePoint WHERE Id=? AND TargetRegion>0 AND TargetX>0 "
                  "ORDER BY (Realm=?) DESC, (Realm=0) DESC LIMIT 1", (point, realm))[0]
    if src is None:
        c = ref.execute("SELECT client_x, client_y, z1 FROM ref_client_zonepoint_compare WHERE point_id=? AND region_id=?",
                        (point, src_region)).fetchone()
        src = (int(c[0]), int(c[1]), int(c[2] or 0)) if c else (a['SourceX'], a['SourceY'], a['SourceZ'])
    edits.append({"table": "ZonePoint", "insert": {
        "ZonePoint_ID": f"dr530-{point}-{realm}", "Id": point, "Realm": realm, "SourceRegion": src_region,
        "SourceX": src[0], "SourceY": src[1], "SourceZ": src[2],
        "TargetRegion": a['TargetRegion'], "TargetX": a['TargetX'], "TargetY": a['TargetY'], "TargetZ": a['TargetZ'],
        "TargetHeading": a['TargetHeading'], "ClassType": cls}})

# --- Demon's Breach spawns and their templates ---------------------------------------------------------------
mob_cols = set(columns(world, "Mob"))
spawns = rows(ref, "SELECT * FROM src_eodb_Mob WHERE Region=489 ORDER BY Mob_ID")
have_templates = {i for i, in world.execute("SELECT TemplateId FROM NpcTemplate")}
needed = sorted({m['NPCTemplateID'] for m in spawns if m['NPCTemplateID'] not in (None, 0, -1)} - have_templates)
tmpl_cols = set(columns(world, "NpcTemplate"))
for t in rows(ref, f"SELECT * FROM src_eodb_NpcTemplate WHERE TemplateId IN ({','.join('?' * len(needed))})", needed):
    edits.append({"table": "NpcTemplate", "insert": {k: v for k, v in t.items() if k in tmpl_cols}})
missing = set(needed) - {e['insert']['TemplateId'] for e in edits if e['table'] == 'NpcTemplate'}
assert not missing, f"templates not in the archive: {sorted(missing)}"
assert not world.execute("SELECT count(*) FROM Mob WHERE Region=489").fetchone()[0], "region 489 already has spawns"
for m in spawns:
    row = {k: v for k, v in m.items() if k in mob_cols}
    row['PackageID'] = 'dr530-demons-breach'
    edits.append({"table": "Mob", "insert": row})

out = HERE / "dr530-serfs-folly-demons-breach-2026-11-23.edits.json"
out.write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
kinds = {}
for e in edits:
    kinds[e['table']] = kinds.get(e['table'], 0) + 1
print(f"{len(edits)} edits -> {out.name}: {kinds}")
