"""Candidate zone road graph for the server's long-walk fallback (AutonomousZoneRoads). Read only.

Detour's search stops at a dead end when the only way to a far point first leads away from it
(Sheeroe Hills: the plateau reaches the Bog of Cullen border only through the western valley,
104,000 units for a 54,000-unit chord). The server then falls back to a graph of short, proven
legs. This script picks the graph's candidate anchors and legs from each zone mesh: one anchor
per CELL-unit cell for every connected walkable area (navcomp) with ground in that cell (the
centre of its largest polygon), and a candidate leg to the same area's anchor in each of the
eight neighbouring cells. Tiny areas (pockets) get no anchors.

The legs are only candidates: UT_ZoneRoadBuild proves every leg with the real Detour corridor
check and writes navmesh/roads.json. Regenerate both after rebuilding meshes.

python roadgraph.py <db> <navdir> <out.json> [regions, default 1,100,200,51,151,181] [zone ids] [--step=N] [--band=N]
--step: anchor spacing (default CELL); --band: also one anchor per height slice of this size (stacked floors).
Labyrinth (2026-11-15): python roadgraph.py <db> <navdir> lab.json 245 245 --step=512 --band=400
"""
import json, os, sqlite3, sys, time
from collections import defaultdict
from navcomp import NavAreas, _area

CELL = 2048
MIN_AREA_SHARE = 0.002      # of the zone's walkable ground
MIN_AREA_UNITS = 4_000_000  # or this many square units (2,000 x 2,000), whichever is smaller


def zone_candidates(mesh, step=CELL, band=0):
    """One anchor per step-unit square per area (and per band-unit height slice when band > 0), with candidate legs
    to the neighbouring squares (and slices). The defaults reproduce the overland graph; mazes with stacked floors
    (the Labyrinth, zone 245) use a finer step and height bands. The server's lookup cell stays CELL: it keeps several
    anchors per cell and weighs height (AutonomousZoneRoads.Nearby)."""
    total = sum(mesh.area.values()) or 1
    keep = {r for r, a in mesh.area.items() if a >= min(MIN_AREA_UNITS, MIN_AREA_SHARE * total)}
    best = {}  # (cell x, cell y, height slice, area) -> (polygon area, x, y, z)
    for i, (_, pts, _, _, om) in enumerate(mesh.polys):
        root = mesh.root[i]
        if om or root not in keep or len(pts) < 3:
            continue
        x = sum(p[0] for p in pts) / len(pts)
        y = sum(p[1] for p in pts) / len(pts)
        z = mesh.height(i, x, y)
        key = (int(x // step), int(y // step), int(z // band) if band else 0, root)
        size = _area(pts)
        if key not in best or size > best[key][0]:
            best[key] = (size, x, y, z)
    keys = sorted(best)
    index = {k: n for n, k in enumerate(keys)}
    anchors = [[round(best[k][1]), round(best[k][2]), round(best[k][3]), k[3]] for k in keys]
    edges = []
    slices = (-1, 0, 1) if band else (0,)
    for (cx, cy, cz, root), n in index.items():
        for dx, dy in ((1, -1), (1, 0), (1, 1), (0, 1), (0, 0)):   # each undirected pair once
            for dz in slices:
                if (dx, dy) == (0, 0) and dz <= 0:
                    continue
                m = index.get((cx + dx, cy + dy, cz + dz, root))
                if m is not None:
                    edges.append([n, m])
    return anchors, edges


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    opts = dict(a[2:].split("=", 1) for a in sys.argv[1:] if a.startswith("--") and "=" in a)
    step, band = int(opts.get("step", CELL)), int(opts.get("band", 0))
    db, navdir, out = args[0], args[1], args[2]
    regions = [int(r) for r in (args[3] if len(args) > 3 else "1,100,200,51,151,181").split(",")]
    only = {int(z) for z in args[4].split(",")} if len(args) > 4 else None
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    zones = {}
    for zid, rid, ox, oy, w, h in con.execute("select ZoneID, RegionID, OffsetX, OffsetY, Width, Height from Zones"):
        path = os.path.join(navdir, f"zone{zid:03d}.nav")
        if rid in regions and os.path.exists(path) and (only is None or zid in only):
            zones[zid] = (rid, ox * 8192, oy * 8192, w * 8192, h * 8192, path)
    result = {"version": 1, "cell": CELL, "zones": {}}
    for zid in sorted(zones):
        rid, x, y, w, h, path = zones[zid]
        started = time.time()
        anchors, edges = zone_candidates(NavAreas(path), step, band)
        result["zones"][str(zid)] = {"region": rid, "x": x, "y": y, "w": w, "h": h, "anchors": anchors, "edges": edges}
        print(f"zone {zid}: {len(anchors)} anchors, {len(edges)} candidate legs, {time.time() - started:.0f}s", flush=True)
    with open(out, "w") as f:
        json.dump(result, f, separators=(",", ":"))
    print(out)


if __name__ == "__main__":
    main()
