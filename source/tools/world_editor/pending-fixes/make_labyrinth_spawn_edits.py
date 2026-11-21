"""Writes the World Workshop edits that put Labyrinth (zone/region 245) spawn spots back on the navmesh (DR-431).

    python make_labyrinth_spawn_edits.py <opendaoc.sqlite3.db> <server navmesh folder> <out.edits.json>

After the 2026-11-15 navmesh install, 51 spots in the Labyrinth are not on a navmesh polygon (World Workshop audit:
49 problems plus 2 flying "large portal" NPCs it ignores). Every one of them stands exactly where the EoD db-public
reference has it (same Mob_ID, same X/Y/Z; the bind points match EoD and DOL public), so none is misplaced: the spots
differ only in how the mesh covers them. Each was sorted by hand from its nearest mesh points and its neighbours:

  nudge   the spot is in the strip the mesh leaves free along walls and pillars, on the same floor as the main
          walkable area: move it to the nearest point of that area (at most MAX_MOVE units, height within MAX_STEP)
  floor   the main walkable area is straight above/below the spot and a neighbour stands on it: change Z only
  leave   ledges, pits, machines and rooms the mesh does not cover, and effect/portal NPCs: listed, not changed

Only the hand-sorted rows in FIX are touched, and only while they still stand at the position noted here, so the
script is safe to run again. Targets are searched outward from the spot on the server's own navmesh and must be on
the main connected area (the one the rest of the Labyrinth walks on); a spot with no such point is reported and
skipped, never pushed further.
"""
import json, math, sqlite3, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import audit  # noqa: E402  World Workshop's navmesh audit

REGION = ZONE = 245
MAX_MOVE = 96      # furthest a spot may move sideways
MAX_STEP = 40      # largest height change for a nudge (same floor)
MARGIN = 8         # extra units past the first on-mesh point, away from the wall
SLOPE = 3          # cost of one unit of height change, in units of sideways move

# key: (table, name, (x, y, z) now, mode, why)
FIX = {
    '96566977-e557-46cb-8559-844c0d856054': ('BindPoint', 'bind point', (63903, 43408, 30372), 'nudge',
                                             'beside the Korazh Priest, 16 units outside the walkable floor'),
    '9da4c2d7-f0d2-4faa-92b7-f096f9b3c59a': ('BindPoint', 'bind point', (63903, 43408, 30372), 'nudge',
                                             'beside the Korazh Priest, 16 units outside the walkable floor'),
    '30828ece-13e1-4e03-b4b9-b3e29586035f': ('Mob', 'drakoran guard', (44602, 57532, 26019), 'nudge',
                                             'on a cut-off scrap of mesh on the raised floor; main floor at the same height 80 away'),
    '73b1ee21-cb40-4eb4-8556-2428671f0bbb': ('Mob', 'wraith spiderling', (31640, 18505, 30273), 'nudge', 'on a mesh edge'),
    '5c6dc823-fc4a-451f-a989-5e38ee0bef0c': ('Mob', 'Spirit Priest of Vartigeth', (26834, 68199, 27981), 'nudge', 'against a wall'),
    '74fa9ad2-e68e-45c5-968a-cf8817328ab6': ('Mob', 'animated corpse', (58451, 13277, 27952), 'nudge', 'on a mesh edge'),
    '1010cf31-9a4e-434f-b0f3-2af8b0a6b4e8': ('Mob', 'bottom feeder larva', (46110, 60965, 21475), 'nudge', 'on a mesh edge'),
    '176cf0d8-2be6-46ae-bcd2-be5ef48f07d3': ('Mob', 'indentured goblin', (36940, 28110, 25897), 'nudge', 'against a wall'),
    'be574d88-abcd-489f-bd58-3c75151c2a9e': ('Mob', 'minotaur sercuritor', (48796, 47080, 27433), 'nudge', 'against a wall'),
    '591fd82e-9d28-4fff-beca-ab69b2bec0c2': ('Mob', 'minotaur fireborn', (46451, 42263, 27440), 'nudge', 'against the wall under a gallery'),
    'cd1f231b-e468-467d-9618-3dfd33dbc784': ('Mob', 'minotaur fireborn', (46450, 42732, 27440), 'nudge', 'against the wall under a gallery'),
    '481284ee-4e96-443e-863f-0cc830bb9c32': ('Mob', 'Hannah', (30356, 60028, 27866), 'floor',
                                             'sunk under the floor Trainer Zobaloo stands on (27944)'),
}

# Left alone (39): name, count, why. Kept here so the next reader sees they were looked at.
LEFT = (
    ('Relic Locked Effect', 4, 'effect NPCs on the relic doors; never walk, 31 units from the mesh'),
    ('large portal', 2, 'flying flag, level 99 portal objects; the audit ignores them'),
    ('ambient chaosian / ambient succubus / succubus', 9, 'ledge near (64800,39500) about 1000 above the meshed floor; no mesh there'),
    ('drakoran guardian', 11, 'drakoran pit (42200-46000, 58900-62700): ledges at many heights and a room south of y=62909 the mesh does not cover'),
    ('guardian demon', 6, 'upper levels round the Obelisk of Nurizane (56700-57600, 31300-32100); mesh only on the floor 230-700 away'),
    ('corrupt mechanic x2 / developing rat', 3, 'on two sloped mesh islands (17000,49000) and (20100,49200) not joined to the main floor'),
    ('developing rat', 1, '(38464,65331,30247): nearest mesh is an island 157 away'),
    ('emaciated maggot', 2, '(42451,29254,23094) and 19 away: east of a straight mesh edge at x=42406; the nearest floor is 60-80 away '
                            'and 13-39 higher, the nearest at their own height about 100 away'),
    ('inactive stoneguard', 1, '(52822,52083,25904): 115 from the floor, more than a small move; may stand in a wall niche'),
)

PLACE = {'Mob': ('Mob_ID', 'Region', 'X', 'Y', 'Z'), 'BindPoint': ('BindPoint_ID', 'Region', 'X', 'Y', 'Z')}


def main(db_path, navmesh, out_path):
    con = sqlite3.connect(Path(db_path).resolve().as_uri() + '?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    world = audit.World(con, Path(navmesh))
    mesh = world.mesh(ZONE)
    if mesh is None:
        sys.exit(f'no zone{ZONE:03d}.nav in {navmesh}')
    main_area = max(mesh.area, key=mesh.area.get)

    def on_main(x, y, z, tolerance):
        """Height of the main walkable area at (x, y) within tolerance of z, or None."""
        poly = mesh.locate(x, y, z, tolerance=tolerance)
        return mesh.height(poly, x, y) if poly is not None and mesh.root[poly] == main_area else None

    def nudge(x, y, z):
        """Nearest point of the main area, counting each unit of height change as SLOPE units of distance, so a
        spawn on a step or dais stays on it instead of sliding to a closer, lower floor."""
        hits = []
        for r in range(0, MAX_MOVE + 1, 2):
            for k in range(1 if r == 0 else 32):
                a = k * math.pi / 16
                for rr in (r + MARGIN, r):               # prefer the point a little further from the wall
                    px, py = round(x + rr * math.cos(a)), round(y + rr * math.sin(a))
                    h = on_main(px, py, z, MAX_STEP)
                    if h is not None and on_main(px, py, math.ceil(h), audit.NEAR) is not None:
                        hits.append((rr + SLOPE * abs(h - z), px, py, math.ceil(h)))
                        break
        return min(hits)[1:] if hits else None

    edits, skipped = [], []
    for key, (table, name, at, mode, why) in FIX.items():
        id_col, region, cx, cy, cz = PLACE[table]
        row = con.execute(f'SELECT {region}, {cx}, {cy}, {cz} FROM {table} WHERE {id_col}=?', (key,)).fetchone()
        if row is None or tuple(row) != (REGION, *at):
            skipped.append(f'{name} [{key}]: not at {at} any more ({None if row is None else tuple(row)})')
            continue
        if world.classify(REGION, *at)[0] == 'ok':
            skipped.append(f'{name} [{key}]: already on the navmesh')
            continue
        if mode == 'floor':
            h = on_main(at[0], at[1], at[2], 200)
            to = None if h is None else (at[0], at[1], math.ceil(h))
        else:
            to = nudge(*at)
        if to is None or world.classify(REGION, *to)[0] != 'ok':
            skipped.append(f'{name} [{key}]: no point of the main floor within {MAX_MOVE} of {at}')
            continue
        change = {c: v for c, v, old in zip((cx, cy, cz), to, at) if v != old}
        moved = round(math.dist(at[:2], to[:2]))
        edits.append({'table': table, 'key': key, 'set': change,
                      'why': f'{name} {why}: {at} -> {to}, {moved} sideways, {to[2] - at[2]:+d} up '
                             f'(DR-431, Labyrinth spawns off the navmesh)'})
        print(f'{name:<28} {at} -> {to}  {moved} sideways, {to[2] - at[2]:+d} up')
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(edits, f, indent=1)
    print(len(edits), 'edits ->', out_path)
    for s in skipped:
        print('skipped:', s)
    print('left alone:', sum(n for _, n, _ in LEFT), 'spots')
    for name, n, why in LEFT:
        print(f'  {n:>2} {name}: {why}')


if __name__ == '__main__':
    main(*sys.argv[1:])
