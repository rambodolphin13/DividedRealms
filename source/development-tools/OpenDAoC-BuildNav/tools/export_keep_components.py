r"""Export server-placed keep components (New Frontiers keeps, towers) for BuildNav (DR-035).

The 1.130 client draws a keep component from the KeepComponentInfo packet: skin (fr_tiers.csv row), the keep's
height (tier column), its grid offset from the keep and its quarter-turn heading. BuildNav only reads client zone
data, so without this file the frontier meshes have no keep walls, gates or door polys. This writes, per
component, the client model to load and the world placement the server uses (GameKeepComponent.LoadFromDatabase),
plus the server's door positions so the build can be checked against them.

    python -I export_keep_components.py <world.db> <client app folder> <out keeps.json> [region ...]

Read-only on the database and client.
"""
import json, math, os, re, sqlite3, struct, sys
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', 'tools', 'asset-tool'))
import archive  # noqa: E402  (the repository's MPK/NPK reader)


def mpk_files(path):
    _, entries = archive.read(open(path, 'rb').read())
    return {e.name.lower(): e.data for e in entries}


def logged_doors(server_log):
    """Server door positions from DoorMgr's boot lines (New Frontiers keep doors are created from the components)."""
    pattern = re.compile(r'\(Id: (\d+)\) \(Name: ([^)]*)\) \(Loc: (-?\d+),(-?\d+),(-?\d+)\) \(ZoneId: (\d+)\) \(RegionId: (\d+)\)')
    doors = {}
    for line in Path(server_log).read_text(encoding='utf-8', errors='replace').splitlines():
        m = pattern.search(line)
        if m:
            doors[int(m[1])] = {"id": int(m[1]), "name": m[2], "x": int(m[3]), "y": int(m[4]), "z": int(m[5]),
                                "zone": int(m[6]), "region": int(m[7])}
    return list(doors.values())


def main():
    db, app, out = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    regions = [int(r) for r in sys.argv[4:]] or [163]
    tables = mpk_files(app / 'frontiers' / 'frontiers.mpk')
    tables = {k: v for k, v in tables.items()}
    pieces = {}
    for line in tables['fr_pieces.csv'].decode('latin1').splitlines()[2:]:
        cells = line.split(',')
        if cells and cells[0].strip().isdigit() and len(cells) > 2 and cells[2].strip():
            pieces[int(cells[0])] = cells[2].strip()
    tiers = {}
    for line in tables['fr_tiers.csv'].decode('latin1').splitlines()[2:]:
        cells = line.split(',')
        if cells and cells[0].strip().isdigit():
            tiers[int(cells[0])] = [int(c) if c.strip().lstrip('-').isdigit() else None for c in cells[2:7]]
    nifs = {p.stem.lower(): p.name for p in (app / 'frontiers' / 'NIFS').glob('*.npk')}

    con = sqlite3.connect(f'file:{db}?mode=ro', uri=True)
    con.row_factory = sqlite3.Row
    components, missing = [], {}
    for region in regions:
        keeps = {k['KeepID']: k for k in con.execute("SELECT * FROM Keep WHERE Region=?", (region,))}
        for c in con.execute(f"SELECT * FROM KeepComponent WHERE KeepID IN ({','.join('?' * len(keeps))}) ORDER BY KeepID, ID",
                             list(keeps)):
            keep = keeps[c['KeepID']]
            # GameKeepComponent.LoadFromDatabase (server position for every component)
            angle = keep['Heading'] * (math.pi * 2) / 360
            cx, cy = struct.unpack('b', struct.pack('B', c['X'] & 0xFF))[0], struct.unpack('b', struct.pack('B', c['Y'] & 0xFF))[0]
            x = int(keep['X'] + (cx * 148 * math.cos(angle) + cy * 148 * math.sin(angle)))
            y = int(keep['Y'] - (cy * 148 * math.cos(angle) - cx * 148 * math.sin(angle)))
            heading = (c['Heading'] * 90 + keep['Heading']) % 360
            tier_row = tiers.get(c['Skin'])
            tier = max(1, min(4, keep['Level'] // 3 + 1 if keep['Level'] else 1))   # client tier column; new-style rows are equal
            piece_index = tier_row[tier - 1] if tier_row else None
            model = pieces.get(piece_index + 1) if piece_index is not None else None
            npk = nifs.get(model.lower()) if model else None
            if npk is None:
                missing[c['Skin']] = missing.get(c['Skin'], 0) + 1
                continue
            components.append({"region": region, "keep": keep['KeepID'], "component": c['ID'], "skin": c['Skin'],
                               "model": f"frontiers/NIFS/{npk}/{Path(npk).stem}.nif", "x": x, "y": y, "z": keep['Z'],
                               "heading": heading})
    log = os.environ.get('KEEP_DOOR_LOG')
    doors = [d for d in logged_doors(log) if d['region'] in regions] if log else []
    out.write_text(json.dumps({"components": components, "serverDoors": doors}, indent=1), encoding='utf-8')
    print(f"{len(components)} components in regions {regions} -> {out}; skins without a client model: {missing}; "
          f"{len(doors)} keep doors listed")


if __name__ == '__main__':
    main()
