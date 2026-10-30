"""Make the modern client patch that players apply to their own 1.130 client (no retail file ships, see Marfach below).

    python make_client_patch.py <pristine 1.130 client folder> <classic client folder> <client-patch.json>

Runs the hash-guarded class-slot patch (classslots/patch_class_slots.py: game.dll's native class slots 63-67 and
the creation screen's extra class buttons in pregame/character_creation.xml) on the pristine files, then records
only the edits (our hooks, section and added buttons, not Broadsword's bytes) with each file's input and output
SHA256. The launcher's client setup (OfflineDaoc.Launcher/ModernClientSetup.cs) copies the player's client and applies
these edits in order; it refuses any input that isn't the pristine 1.130 file. These two files and the splash below are
the only client files the modern test changes (a full hash comparison of the test client with a pristine 1.130 client,
2026-10-25, before the splash and the zone table below).

The loading screen, pregame/splash.mpk, is replaced whole by the Divided Realms splash (our own art,
OfflineDaoc.Launcher/Assets/divided-realms-client-splash.mpk): one edit that removes the stock archive and inserts ours,
guarded by the stock file's SHA256 like the others.

The stock zone table, zones/zones.mpk, has none of the regions the server uses for the four battlegrounds and the old RvR
dungeons: entering one dropped the player to character select or left them out of bounds. Its zones.dat is compressed, so
a byte edit of the archive would ship Mythic's whole table. Instead an entry with Member edits the file inside a
single-file archive and the launcher rebuilds the archive with stored zlib blocks, which Python and .NET write byte for
byte alike. Only our added sections and changed values ship.

An entry with From makes a new file from another stock file: Abermenai's terrain is Thidranki's with the archive renamed
(add_abermenai_terrain.py).

Marfach Caverns (region 276) is the one exception to shipping nothing retail, by the owner's decision (2026-10-30): 1.130
has no copy of its map, so dat276.mpk and its sounds.dat (about 20 KB, read from the classic client when the patch is
built, never kept in Git) travel inside the patch as whole-file edits. The dungeon models it places are stock.
"""
import argparse
import base64
import difflib
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / 'classslots'))
import patch_class_slots  # noqa: E402
import add_abermenai_terrain  # noqa: E402

FORMAT = 1
CLIENT_VERSION = '1.130'
MERGE_GAP = 32  # in-place changes this close together are kept in one edit
SPLASH = Path(__file__).resolve().parent / 'OfflineDaoc.Launcher/Assets/divided-realms-client-splash.mpk'
STORED_BLOCK = 65535  # the most one stored zlib block holds

# The stock zones the server uses under other regions: (zone, stock region, server region, the name to show).
ZONE_REGIONS = [(250, 166, 250, b'Caledonia'), (251, 167, 251, b'Murdaigean'), (252, 168, 252, b'Thidranki')]
# (new file, the stock file its edit replaces): the classic client's files, see the docstring.
MARFACH_FILES = [('zones/zone276/dat276.mpk', 'zones/zone247/dat247.mpk'), ('zones/zone276/sounds.dat', 'zones/zone247/sounds.dat')]
# The sections the server's regions need and the stock table lacks, as the classic table has them.
ADDED_SECTIONS = '''

[region248]
entry_music=6

[region250]
entry_music=2
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[region251]
entry_music=2
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[region252]
entry_music=2
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[region253]
entry_music=5
grasscsv=grass_alb.csv
grassmap=grass_alb.tga

[zone246]
enabled=1
type=2
region=246
name=Dodens Gruva
region_offset_x=1
region_offset_y=1
width=8
height=8

[zone247]
enabled=1
type=2
region=247
name=Marfach Caverns
region_offset_x=1
region_offset_y=1
width=8
height=8

[zone248]
enabled=1
type=2
region=248
name=Summoner's Hall
region_offset_x=1
region_offset_y=1
width=8
height=8

[zone253]
enabled=1
region=253
name=Abermenai
region_offset_x=1
region_offset_y=1
width=8
height=8
entry_music=3

[region276]
entry_music=6

[zone276]
enabled=1
type=2
region=276
name=Marfach Caverns
region_offset_x=1
region_offset_y=1
width=8
height=8
map_enabled=0
'''.replace('\n', '\r\n').encode('ascii')


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def edits(original: bytes, patched: bytes):
    """(at, remove, insert): at an offset of the original, drop `remove` bytes and put `insert` there. Same-size files
    (game.dll grows only by its appended section) are compared in place; the layout's added buttons are an insertion,
    so it is compared by sequence and only the inserted text is kept."""
    if len(patched) >= len(original) and len(original) > 1 << 20:
        out, start, last = [], None, None
        for offset in range(len(original)):
            if original[offset] == patched[offset]:
                continue
            if start is not None and offset - last <= MERGE_GAP:
                last = offset
                continue
            if start is not None:
                out.append((start, last + 1 - start, patched[start:last + 1]))
            start = last = offset
        if start is not None:
            out.append((start, last + 1 - start, patched[start:last + 1]))
        if len(patched) > len(original):
            out.append((len(original), 0, patched[len(original):]))
        return out
    matcher = difflib.SequenceMatcher(None, original, patched, autojunk=False)
    return [(i1, i2 - i1, patched[j1:j2]) for tag, i1, i2, j1, j2 in matcher.get_opcodes() if tag != 'equal']


def stored_zlib(data: bytes) -> bytes:
    """A zlib stream of stored blocks: unlike real compression, every writer makes exactly these bytes."""
    out = bytearray(b'\x78\x01')
    for start in range(0, len(data), STORED_BLOCK):
        block = data[start:start + STORED_BLOCK]
        last = 1 if start + STORED_BLOCK >= len(data) else 0
        out += struct.pack('<BHH', last, len(block), len(block) ^ 0xFFFF) + block
    return bytes(out + struct.pack('>I', zlib.adler32(data)))


def member_of(archive: bytes) -> bytes:
    """The file inside a single-file MPAK v2 archive."""
    _, directory_size, name_size, count = struct.unpack('<4I', bytes(v ^ i for i, v in enumerate(archive[5:21])))
    if archive[:5] != b'MPAK\x02' or count != 1:
        raise ValueError('not a single-file MPAK v2 archive')
    return zlib.decompress(archive[21 + name_size + directory_size:])


def with_member(archive: bytes, data: bytes) -> bytes:
    """The single-file archive with its file replaced, written with stored blocks; name and timestamp are kept."""
    _, directory_size, name_size, _ = struct.unpack('<4I', bytes(v ^ i for i, v in enumerate(archive[5:21])))
    row = bytearray(zlib.decompress(archive[21 + name_size:21 + name_size + directory_size]))
    payload = stored_zlib(data)
    struct.pack_into('<4I', row, 268, len(data), 0, len(payload), zlib.crc32(payload))
    directory = stored_zlib(bytes(row))
    header = struct.pack('<4I', zlib.crc32(directory), len(directory), name_size, 1)
    return archive[:5] + bytes(v ^ i for i, v in enumerate(header)) + archive[21:21 + name_size] + directory + payload


def apply(original: bytes, entry: dict) -> bytes:
    """The launcher's algorithm, for the tests: check the input, rebuild the output, check it."""
    if sha(original) != entry['InputSha256']:
        raise ValueError(f"{entry.get('From', entry['Path'])}: not the pristine {CLIENT_VERSION} file")
    member = entry.get('Member')
    source = member_of(original) if member else original
    out, position = bytearray(), 0
    for edit in entry['Edits']:
        out += source[position:edit['At']]
        out += base64.b64decode(edit['Insert'])
        position = edit['At'] + edit['Remove']
    out += source[position:]
    if member:
        out = with_member(original, bytes(out))
    if len(out) != entry['OutputSize'] or sha(bytes(out)) != entry['OutputSha256']:
        raise ValueError(f"{entry['Path']}: the patched file doesn't match")
    return bytes(out)


def replace_whole(original: bytes, replacement: bytes):
    """One edit that swaps the whole file (the splash archive shares no bytes worth diffing with the stock one)."""
    return [(0, len(original), replacement)]


def changed_middle(original: bytes, patched: bytes):
    """One edit over the span between the common start and end (a renamed archive differs only in its header)."""
    limit = min(len(original), len(patched))
    start = next((i for i in range(limit) if original[i] != patched[i]), limit)
    end = next((i for i in range(limit - start) if original[-1 - i] != patched[-1 - i]), limit - start)
    return [(start, len(original) - end - start, patched[start:len(patched) - end])]


def zone_table_edits(table: bytes):
    """(at, remove, insert) over the stock zones.dat: the battleground zones move to the server's regions, each with its
    own name, and the missing sections are added at the end."""
    found = []
    for zone, stock_region, region, name in ZONE_REGIONS:
        start = table.index(b'[zone%d]' % zone)
        for key, old, new in [(b'region', b'%d' % stock_region, b'%d' % region), (b'name', b'Caledonia', name)]:
            at = table.index(b'\r\n%s=%s\r\n' % (key, old), start)
            if at - start > 200:
                raise ValueError(f'zone{zone}: no {key.decode()}={old.decode()} in its section')
            if old != new:
                found.append((at + len(key) + 3, len(old), new))
    found.append((len(table), 0, ADDED_SECTIONS))
    return sorted(found)


def encoded(changes) -> list:
    return [dict(At=at, Remove=remove, Insert=base64.b64encode(block).decode('ascii')) for at, remove, block in changes]


def zone_table_entry(stock: bytes) -> dict:
    table = member_of(stock)
    changes = zone_table_edits(table)
    patched_table, position = bytearray(), 0
    for at, remove, block in changes:
        patched_table += table[position:at] + block
        position = at + remove
    patched = with_member(stock, bytes(patched_table + table[position:]))
    entry = dict(Path='zones/zones.mpk', Member='zones.dat', InputSha256=sha(stock), OutputSha256=sha(patched),
                 OutputSize=len(patched), Edits=encoded(changes))
    assert apply(stock, entry) == patched
    return entry


def abermenai_entry(client: Path, kind: str) -> dict:
    # The stock dat252 is a later Thidranki than Abermenai's dat253, so plan()'s same-maps check would refuse; these terrain
    # archives are the ones the classic client has too (add_abermenai_terrain.py), and they fit Abermenai there.
    source, target = add_abermenai_terrain.SOURCE_ZONE, add_abermenai_terrain.TARGET_ZONE
    stock_path = f'zones/zone{source}/{kind}{source}.mpk'
    name = f'{kind}{target}.mpk'
    stock = (client / stock_path).read_bytes()
    patched = add_abermenai_terrain.renamed(stock, name.encode('ascii'))
    entry = dict(Path=f'zones/zone{target}/{name}', From=stock_path, InputSha256=sha(stock), OutputSha256=sha(patched),
                 OutputSize=len(patched), Edits=encoded(changed_middle(stock, patched)))
    assert apply(stock, entry) == patched
    return entry


def marfach_entry(client: Path, classic: Path, path: str, stock_path: str) -> dict:
    stock, ours = (client / stock_path).read_bytes(), (classic / path).read_bytes()
    entry = dict(Path=path, From=stock_path, InputSha256=sha(stock), OutputSha256=sha(ours), OutputSize=len(ours),
                 Edits=encoded(replace_whole(stock, ours)))
    assert apply(stock, entry) == ours
    return entry


def build(client: Path, classic: Path, splash: Path = SPLASH) -> dict:
    game = (client / 'game.dll').read_bytes()
    layout = (client / 'pregame/character_creation.xml').read_bytes()
    stock_splash = (client / 'pregame/splash.mpk').read_bytes()
    patched_game, _ = patch_class_slots.patch(game)
    patched_layout = patch_class_slots.patch_layout(layout)
    our_splash = splash.read_bytes()
    files = []
    for path, original, patched, changes in [
            ('game.dll', game, patched_game, edits),
            ('pregame/character_creation.xml', layout, patched_layout, edits),
            ('pregame/splash.mpk', stock_splash, our_splash, replace_whole)]:
        entry = dict(Path=path, InputSha256=sha(original), OutputSha256=sha(patched), OutputSize=len(patched),
                     Edits=encoded(changes(original, patched)))
        assert apply(original, entry) == patched
        files.append(entry)
    files.append(zone_table_entry((client / 'zones/zones.mpk').read_bytes()))
    files.extend(abermenai_entry(client, kind) for kind in add_abermenai_terrain.KINDS)
    files.extend(marfach_entry(client, classic, path, stock_path) for path, stock_path in MARFACH_FILES)
    return dict(Format=FORMAT, ClientVersion=CLIENT_VERSION, Files=files)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('client', type=Path, help='pristine 1.130 client folder (only read)')
    parser.add_argument('classic', type=Path, help='classic client folder with zones/zone276 (only read)')
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    patch = build(args.client, args.classic)
    args.output.write_text(json.dumps(patch, indent=1), encoding='utf-8')
    for entry in patch['Files']:
        print(entry['Path'], entry['InputSha256'][:12], '->', entry['OutputSha256'][:12],
              len(entry['Edits']), 'edits,', sum(len(base64.b64decode(e['Insert'])) for e in entry['Edits']), 'bytes')
