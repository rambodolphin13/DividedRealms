r"""DR-225 (first batch): add the client zone crossings whose destination exists and has content.

The client sends a crossing's point id (zonejump.csv column 9); the server looks it up by Id and realm only
(RegionChangeRequestHandler), so a missing Id means walking into that crossing does nothing. Of the 107 point ids
the 1.130 client has and we lack (Research\Reference ref_client_zonepoint_compare, region 3 ghost world left out),
this batch adds only those whose destination is known from the EoD / DOL public archives AND leads somewhere that
exists and is populated:
  80        Snowdonia -> Darkness Falls (third Albion DF entrance; 79/81 already exist, same target)
  106       Krondon second exit -> Avalon (same target as Krondon exit 151)
  642..659  Catacombs Foothills tunnels <-> Black Mtns North / West Svealand / Vale of Mularn (regions 330/334 have spawns)
Left out: adventure wings and task dungeons (every target region, also the six already wired, has no spawns),
the mini battleground to Demon's Breach (489, empty), the epic quest instances, tutorial, Labyrinth and instance
exits (no archived destination), and the Foothills tunnels into region 348 (empty).

Source coordinates come from the archive where it has them, else from the client trigger. Read-only on the world
and reference DB; writes dr225-zone-crossings-batch1-2026-11-23.edits.json next to itself.

    python -I make_dr225_edits.py <world.db> <reference.sqlite3>
"""
import json, sqlite3, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
# point id -> realm the crossing belongs to (0 = everyone); the archive's realm for the Midgard tunnels is wrong (1)
POINTS = {80: 1, 106: 0,
          642: 2, 643: 2, 648: 2, 649: 2,      # Midgard: West Svealand / Vale of Mularn <-> Foothills of Midgard (334)
          644: 1, 645: 1, 658: 1, 659: 1}      # Albion: Black Mtns North <-> Foothills of Albion (330)

world = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
ref = sqlite3.connect(f'file:{sys.argv[2]}?mode=ro', uri=True)
have = {i for i, in world.execute("SELECT Id FROM ZonePoint")}
regions = {i for i, in world.execute("SELECT RegionID FROM Regions")}

edits = []
for point, realm in POINTS.items():
    if point in have:
        print(f"{point}: already present, skipped")
        continue
    arch = ref.execute("SELECT SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, "
                       "TargetHeading, ClassType FROM src_eodb_ZonePoint WHERE Id=? AND TargetRegion>0 "
                       "ORDER BY SourceX DESC LIMIT 1", (point,)).fetchone()
    client = ref.execute("SELECT region_id, client_x, client_y, z1, zone_name, name FROM ref_client_zonepoint_compare "
                         "WHERE point_id=? AND region_id<>3 LIMIT 1", (point,)).fetchone()
    src_region, sx, sy, sz, tr, tx, ty, tz, th, cls = arch
    c_region, cx, cy, cz, zone, name = client
    if not sx or src_region != c_region:
        src_region, sx, sy, sz = c_region, int(cx), int(cy), int(cz or 0)
    assert tr in regions, (point, tr)
    edits.append({"table": "ZonePoint", "insert": {
        "ZonePoint_ID": f"dr225-{point}-{realm}", "Id": point, "Realm": realm,
        "SourceRegion": src_region, "SourceX": sx, "SourceY": sy, "SourceZ": sz,
        "TargetRegion": tr, "TargetX": tx, "TargetY": ty, "TargetZ": tz, "TargetHeading": th,
        "ClassType": cls or ""}})
    print(f"{point} realm {realm}: {zone} '{name}' ({src_region} {sx},{sy}) -> region {tr} {tx},{ty},{tz}")

out = HERE / "dr225-zone-crossings-batch1-2026-11-23.edits.json"
out.write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
print(f"{len(edits)} edits -> {out.name}")
