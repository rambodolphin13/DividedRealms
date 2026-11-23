r"""DR-530 / DR-225: entrance crossings into the adventure wings filled today. Each wing's exits back out already exist;
17 wings had no way in. The destination of each entrance is the EoD archive's realm-0 Adventure Wing row (the
wing instance's arrival point); the trigger is where the 1.130 client's zonejump.csv puts that point id.

    python -I make_wing_entrances_edits.py <world.db> <reference-test.sqlite3> <wing region> ...
"""
import json, sqlite3, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
world = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
ref = sqlite3.connect(f'file:{sys.argv[2]}?mode=ro', uri=True)
have = {i for i, in world.execute("SELECT Id FROM ZonePoint")}
edits, report = [], []
for wing in (int(a) for a in sys.argv[3:]):
    if world.execute("SELECT 1 FROM ZonePoint WHERE TargetRegion=?", (wing,)).fetchone():
        report.append(f"{wing}: already has an entrance")
        continue
    row = ref.execute("SELECT Id, TargetX, TargetY, TargetZ, TargetHeading FROM src_eodb_ZonePoint WHERE TargetRegion=? AND TargetX>0 "
                      "ORDER BY (Realm=0) DESC LIMIT 1", (wing,)).fetchone()
    if not row:
        report.append(f"{wing}: no archived entrance"); continue
    point, tx, ty, tz, th = row
    if point in have:
        report.append(f"{wing}: point {point} already used"); continue
    src = ref.execute("SELECT region_id, client_x, client_y, z1, zone_name, name FROM ref_client_zonepoint_compare "
                      "WHERE point_id=? AND region_id<>3 LIMIT 1", (point,)).fetchone()
    if not src:
        report.append(f"{wing}: point {point} has no client trigger"); continue
    region, sx, sy, sz, zone, name = src
    edits.append({"table": "ZonePoint", "insert": {
        "ZonePoint_ID": f"dr530-entrance-{point}", "Id": point, "Realm": 0,
        "SourceRegion": region, "SourceX": int(sx), "SourceY": int(sy), "SourceZ": int(sz or 0),
        "TargetRegion": wing, "TargetX": tx, "TargetY": ty, "TargetZ": tz, "TargetHeading": th,
        "ClassType": "DOL.GS.ServerRules.AdventureWingJumpPoint"}})
    report.append(f"{wing}: point {point} from {zone} '{name}' (region {region}) -> {tx},{ty},{tz}")
out = HERE / "dr530-wing-entrances-2026-11-23.edits.json"
out.write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
print("\n".join(report)); print(f"{len(edits)} entrances -> {out.name}")
