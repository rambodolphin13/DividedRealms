r"""DR-530: fill an empty Catacombs adventure wing with spawns (owner 2026-11-23: build them).

No source has these wings' spawn positions; Allakhazam lists which monsters lived there and their levels. This places
those monsters on the wing's own navmesh:
  * floor points every 192 units over the zone, kept when a full Detour path joins them to the entrance (the wing's
    ZonePoint target), spaced at least SPACING apart;
  * points ordered by walking distance from the entrance; the listed monsters, by level, fill them from the door
    inwards (low levels first), each monster on several points;
  * the named boss(es) with no world template stand at the farthest points;
  * every spawn copies a real spawn row of the same monster (any region; its NpcTemplate sets the stats) or, failing
    that, its NpcTemplate; a boss copies the strongest listed monster's row with its own name, level and 1.25x size,
    and no template (a template would overwrite the name).
Read-only on the world and reference DB; writes dr530-wing-<region>-2026-11-23.edits.json and a report next to itself.

    python -I make_wing_spawns_edits.py <world.db> <reference.sqlite3> <zone.nav> <region> "<zone name>" x,y,z [per_monster]
"""
import json, math, random, sqlite3, sys, uuid
from pathlib import Path

sys.argv_saved = list(sys.argv)
HERE = Path(__file__).resolve().parent
PROBE = Path(r"C:\Users\natex\Desktop\DAoC Offline\Source\artifacts\atlantis-navmesh")
sys.path.insert(0, str(PROBE))
db, ref, nav, region, zone_name, entrance = sys.argv[1:7]
per_monster = int(sys.argv[7]) if len(sys.argv) > 7 else 4
region = int(region)
ex, ey, ez = (float(v) for v in entrance.split(','))
sys.argv = ['x']
import detour_probe as dp  # noqa: E402

SPACING = 420
STEP = 192
random.seed(region)

world = sqlite3.connect(f'file:{db}?mode=ro', uri=True)
world.row_factory = sqlite3.Row
reference = sqlite3.connect(f'file:{ref}?mode=ro', uri=True)
listed = reference.execute("SELECT name, level_min, level_max, type FROM src_zam_zone_mobs WHERE zone=? ORDER BY level_min, name",
                           (zone_name,)).fetchall()
assert listed, f"Allakhazam lists no monsters for {zone_name}"
assert not world.execute("SELECT count(*) FROM Mob WHERE Region=?", (region,)).fetchone()[0], "region already has spawns"

mesh = dp.load(str(nav))
q = dp.query(mesh)
st, start = dp.closest(q, (ex, ey, ez), (200, 200, 400))
assert start, "entrance is not on the mesh"

# floor points, reachable from the entrance, with walking distance
cells = {}
for gx in range(0, 65536, STEP):
    for gy in range(0, 65536, STEP):
        s, f = dp.closest(q, (gx, gy, ez), (STEP / 2, STEP / 2, 4000))
        if f:
            cells[(int(f[0] // 96), int(f[1] // 96), int(f[2] // 128))] = f
points = []
for f in cells.values():
    s, nodes = dp.path(q, start, f)
    if (s & dp.SUCCESS) and not (s & dp.PARTIAL) and nodes:
        points.append((sum(math.dist(nodes[i], nodes[i + 1]) for i in range(len(nodes) - 1)), f))
points.sort()
spots = []
for d, f in points:
    if d < 300:
        continue  # leave the entrance clear
    if all(math.dist(f[:2], g[:2]) >= SPACING or abs(f[2] - g[2]) > 200 for _, g in spots):
        spots.append((d, f))


def source_row(name):
    row = world.execute("SELECT * FROM Mob WHERE lower(Name)=lower(?) AND NPCTemplateID>0 ORDER BY Region LIMIT 1", (name,)).fetchone()
    if row:
        return dict(row)
    t = world.execute("SELECT * FROM NpcTemplate WHERE lower(Name)=lower(?) LIMIT 1", (name,)).fetchone()
    if not t:
        return None
    base = dict(world.execute("SELECT * FROM Mob WHERE NPCTemplateID>0 AND Realm=0 LIMIT 1").fetchone())
    model = str(t['Model']).split(';')[0].split('-')[0]
    size = str(t['Size']).split('-')[0] if t['Size'] else '50'
    base.update(Name=name, NPCTemplateID=t['TemplateId'], Model=int(model), Size=int(size), Level=0,
                Race=t['Race'], BodyType=t['BodyType'], AggroLevel=t['AggroLevel'], AggroRange=t['AggroRange'])
    return base


regular, bosses, missing = [], [], []
for name, lo, hi, kind in listed:
    row = source_row(name)
    if row:
        regular.append((name, lo, hi, row))
    elif name[0].isupper() and len(name.split()) >= 1:
        bosses.append((name, lo, hi))
    else:
        missing.append(name)
need = len(regular) * per_monster
boss_spots = spots[-len(bosses):] if bosses else []
field = spots[:len(spots) - len(boss_spots)]
if len(field) > need:  # spread evenly over the wing
    field = [field[int(i * len(field) / need)] for i in range(need)]

edits, report = [], []
mob_cols = [c[1] for c in world.execute("PRAGMA table_info(Mob)")]


def mob(row, name, level, x, y, z, size=None, template=None):
    r = {k: row[k] for k in mob_cols if k in row and k not in ('LastTimeRowUpdated',)}
    r.update(Name=name, Region=region, X=int(x), Y=int(y), Z=int(round(z)), Heading=random.randrange(4096), Level=level,
             PackageID=f'dr530-wing-{region}', RoamingRange=-1, PathID=None)
    if size: r['Size'] = size
    if template is not None: r['NPCTemplateID'] = template
    r['Mob_ID'] = str(uuid.uuid5(uuid.NAMESPACE_URL, f'dr530/{region}/{name}/{int(x)}/{int(y)}/{int(z)}'))
    return r


for i, (d, f) in enumerate(field):
    band = min(len(regular) - 1, int(i * len(regular) / max(1, len(field))))
    name, lo, hi, row = regular[band]
    level = 0 if row.get('NPCTemplateID', 0) > 0 and row.get('Level', 0) == 0 else random.randint(lo, max(lo, hi))
    edits.append({"table": "Mob", "insert": mob(row, name, level, *f)})
    report.append({"name": name, "at": [int(v) for v in f], "walk": int(d)})
strongest = max(regular, key=lambda r: r[2])[3] if regular else None
for (name, lo, hi), (d, f) in zip(bosses, boss_spots):
    size = int(min(255, (strongest.get('Size') or 50) * 1.25))
    edits.append({"table": "Mob", "insert": mob(strongest, name, lo, *f, size=size, template=0)})
    report.append({"name": name, "boss": True, "level": lo, "at": [int(v) for v in f], "walk": int(d)})

out = HERE / f"dr530-wing-{region}-2026-11-23.edits.json"
out.write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
(HERE / f"dr530-wing-{region}-2026-11-23.report.json").write_text(json.dumps(
    {"zone": zone_name, "floor_points": len(points), "spots": len(spots), "spawns": len(edits), "bosses": [b[0] for b in bosses],
     "unplaced": missing, "deepest_walk": int(spots[-1][0]) if spots else 0, "spawn_list": report}, indent=1) + "\n", encoding="utf-8")
print(f"{zone_name}: {len(points)} reachable floor points, {len(spots)} spaced spots, {len(edits)} spawns "
      f"({len(regular)} monsters x up to {per_monster}, bosses {[b[0] for b in bosses]}, unplaced {missing}) -> {out.name}")
