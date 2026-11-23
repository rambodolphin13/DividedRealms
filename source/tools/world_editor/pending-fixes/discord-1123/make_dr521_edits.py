r"""DR-521: move the lv_store_hub0..hub8 'Gear levels' Quartermasters from the capitals to outside each realm's
frontier gate keep (Castle Sauvage / Svasud Faste / Druim Ligen), in two tidy rows either side of the road out of
the realm-side gate, each NPC facing the road. Read-only on the world; writes dr521-...edits.json next to itself.

    python -I make_dr521_edits.py <world.db> <runtime\server
avmesh>
"""
import json, math, sqlite3, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))  # world_editor (audit.py)
import audit

db, nav = sys.argv[1], Path(sys.argv[2])
con = sqlite3.connect(f'file:{db}?mode=ro', uri=True)
con.row_factory = sqlite3.Row
world = audit.World(con, nav)

# realm -> (region, road centre x, first y outside the gate wall, ground Z of the NPCs just outside, label)
# gate x / wall y read from the gate guards, wall-top archers and the navmesh gaps; Z from the stable master outside.
KEEPS = {
    1: (1, 583944, 488330, 2186, 'Castle Sauvage'),    # realm door 583944,487847; outer wall ~488160; Uliam Z 2175
    2: (100, 765080, 675200, 5736, 'Svasud Faste'),    # realm door 765130,674609; wall ~675050; Vorgar Z 5733
    3: (200, 334800, 422280, 5186, 'Druim Ligen'),     # realm door 334810,421338; outer wall ~422080; Ullios Z 5227
}
SIDE = 260     # each row this far from the road centre
STEP = 160     # spacing along the road
MIN_GAP = 90


def heading_to(x, y, tx, ty):
    return int(round(math.atan2(-(tx - x), (ty - y)) * 2048 / math.pi)) & 0xFFF


edits, report = [], []
for realm, (region, cx, y0, z0, label) in KEEPS.items():
    occupied = [(r['X'], r['Y']) for r in con.execute(
        "SELECT X, Y FROM Mob WHERE Region=? AND abs(X-?)<3000 AND abs(Y-?)<3000", (region, cx, y0))]
    npcs = con.execute("SELECT Mob_ID, Name, ItemsListTemplateID, X, Y, Z FROM Mob WHERE ItemsListTemplateID LIKE ? "
                       "ORDER BY ItemsListTemplateID", (f'lv_store_hub%_{ {1: "alb", 2: "mid", 3: "hib"}[realm] }',)).fetchall()
    road = (cx, y0, z0)
    slot = {(-1): 0, 1: 0}
    for i, n in enumerate(npcs):
        side = -1 if i % 2 == 0 else 1
        k = slot[side]
        while True:
            x, y = cx + side * SIDE, y0 + 80 + k * STEP
            k += 1
            if k > 30:
                raise RuntimeError(f'no spot for {n["Name"]} at {label}')
            if any((x - ox) ** 2 + (y - oy) ** 2 < MIN_GAP ** 2 for ox, oy in occupied):
                continue
            st, fl = world.classify(region, x, y, z0)
            if st != 'ok' or abs(fl - z0) > 150:
                continue
            fl = int(round(fl))
            # walkable from the road beside it
            rs, rf = world.classify(region, cx, y, z0)
            if rs != 'ok' or not world.leg_clear(region, (cx, y, int(round(rf))), (x, y, fl)):
                continue
            break
        slot[side] = k
        occupied.append((x, y))
        hd = heading_to(x, y, cx, y)  # face the road
        edits.append({'table': 'Mob', 'key': n['Mob_ID'],
                      'set': {'Region': region, 'X': x, 'Y': y, 'Z': fl, 'Heading': hd}})
        report.append((label, n['Name'], n['ItemsListTemplateID'], x, y, fl, hd))

out = HERE / 'dr521-hub-quartermasters-frontier-keeps-2026-11-23.edits.json'
out.write_text(json.dumps(edits, indent=1))
for r in report:
    print(*r)
print(len(edits), 'edits ->', out)
