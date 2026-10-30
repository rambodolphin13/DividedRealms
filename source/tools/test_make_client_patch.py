"""Checks of the modern client patch (make_client_patch.py): synthetic edits, and the real pristine 1.130 client when
this machine has one (OFFLINE_DAOC_PRISTINE_1130, or the developer's OpenDAoC copy)."""
import base64
import hashlib
import os
import re
import struct
import sys
import unittest
import zlib
from pathlib import Path
import make_client_patch as m

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools' / 'asset-tool'))
import archive  # noqa: E402

PRISTINE = Path(os.environ.get('OFFLINE_DAOC_PRISTINE_1130', Path.home() / 'Desktop/OpenDAoC/DAoC Full Client/Dark Age of Camelot'))
CLASSIC = Path(os.environ.get('OFFLINE_DAOC_CLASSIC', Path(__file__).resolve().parents[3] / 'Classic/runtime/client-opendaoc/app'))
MODERN_TEST = Path(__file__).resolve().parents[2] / 'artifacts/modern-client-test/runtime/client-modern/app'


def entry(original, patched):
    return dict(Path='x', InputSha256=m.sha(original), OutputSha256=m.sha(patched), OutputSize=len(patched),
                Edits=[dict(At=at, Remove=remove, Insert=base64.b64encode(block).decode('ascii'))
                       for at, remove, block in m.edits(original, patched)])


def single_file_archive(data, stamp=1234):
    return archive.write(b'zones.mpk', [archive.Entry('zones.dat', data, stamp)])


def sections(table):
    found, current = {}, None
    for line in table.decode('latin1').splitlines():
        head = re.match(r'\s*\[(\w+)\]', line)
        if head:
            current = found.setdefault(head.group(1).lower(), {})
        elif current is not None and '=' in line and not line.lstrip().startswith(';'):
            key, value = line.split('=', 1)
            current[key.strip().lower()] = value.split(';')[0].strip()
    return found


STOCK_TABLE = b''.join(
    b'[zone%d] ;20-24 Level PVP Zone\r\nenabled=1\r\nregion=%d\r\nname=Caledonia\r\nregion_offset_x=1\r\n\r\n' % pair
    for pair in [(250, 166), (251, 167), (252, 168)]) + b'[zone260]\r\nregion=9\r\nname=Other'


class ClientPatchTests(unittest.TestCase):
    def test_in_place_changes_and_growth(self):
        original = bytes(range(256)) * 5000  # over 1 MB: compared in place
        patched = bytearray(original)
        patched[10:14] = b'ABCD'
        patched[500000] = 0xFF
        patched += b'new section'
        e = entry(original, bytes(patched))
        self.assertEqual(m.apply(original, e), bytes(patched))
        self.assertEqual(len(e['Edits']), 3)

    def test_an_insertion_keeps_only_the_new_text(self):
        original = b''.join(b'<line%d>value %d</line%d>\r\n' % (i, i * 7, i) for i in range(60))
        cut = original.index(b'<line30>')
        patched = original[:cut] + b'<c>added</c>\r\n' + original[cut:]
        e = entry(original, patched)
        self.assertEqual(m.apply(original, e), patched)
        # Only the added line's length ships (the matcher may align it a few bytes either way).
        self.assertEqual(sum(len(base64.b64decode(x['Insert'])) for x in e['Edits']), len(b'<c>added</c>\r\n'))
        self.assertTrue(all(x['Remove'] == 0 for x in e['Edits']))

    def test_refuses_another_input(self):
        original, patched = b'x' * 100, b'y' * 100
        with self.assertRaises(ValueError):
            m.apply(b'z' * 100, entry(original, patched))

    def test_a_whole_file_replacement_is_one_guarded_edit(self):
        stock, ours = b'stock splash archive' * 50, b'our splash'
        e = entry(stock, ours) | dict(Edits=[dict(At=at, Remove=remove, Insert=base64.b64encode(block).decode('ascii'))
                                             for at, remove, block in m.replace_whole(stock, ours)])
        self.assertEqual(len(e['Edits']), 1)
        self.assertEqual(m.apply(stock, e), ours)
        with self.assertRaises(ValueError):
            m.apply(b'a different splash', e)

    def test_stored_zlib_inflates_and_splits_long_files(self):
        data = bytes(range(256)) * 600
        stream = m.stored_zlib(data)
        self.assertEqual(zlib.decompress(stream), data)
        self.assertEqual(struct.unpack_from('<BHH', stream, 2), (0, 65535, 0))

    def test_a_member_entry_rebuilds_a_valid_archive(self):
        stock = single_file_archive(STOCK_TABLE)
        entry = m.zone_table_entry(stock)
        patched = m.apply(stock, entry)
        name, entries = archive.read(patched)
        self.assertEqual((name, entries[0].name, entries[0].timestamp), (b'zones.mpk', 'zones.dat', 1234))
        archive.verify_memory_image(patched)
        with self.assertRaises(ValueError):
            m.apply(single_file_archive(b'a newer table'), entry)

    def test_the_zone_table_gets_the_server_regions_and_nothing_else(self):
        stock = single_file_archive(STOCK_TABLE)
        table = archive.read(m.apply(stock, m.zone_table_entry(stock)))[1][0].data
        found = sections(table)
        for zone, name in [(250, 'Caledonia'), (251, 'Murdaigean'), (252, 'Thidranki'), (253, 'Abermenai'),
                           (246, 'Dodens Gruva'), (247, 'Marfach Caverns'), (248, "Summoner's Hall"), (276, 'Marfach Caverns')]:
            self.assertEqual((found[f'zone{zone}']['region'], found[f'zone{zone}']['name']), (str(zone), name))
        self.assertEqual(found['zone260'], dict(region='9', name='Other'))
        self.assertTrue(table.startswith(b'[zone250] ;20-24 Level PVP Zone\r\nenabled=1\r\nregion=250\r\nname=Caledonia\r\n'))

    def test_a_table_laid_out_differently_is_refused(self):
        with self.assertRaises(ValueError):
            m.zone_table_edits(STOCK_TABLE.replace(b'region=167', b'region=99'))

    def test_a_small_rename_ships_only_the_changed_bytes(self):
        original, patched = b'head' + b'x' * 500 + b'tail', b'head' + b'yy' + b'x' * 499 + b'tail'
        (at, remove, block), = m.changed_middle(original, patched)
        self.assertEqual(original[:at] + block + original[at + remove:], patched)
        self.assertEqual((at, remove, block), (4, 1, b'yy'))

    def test_the_real_client_patch_rebuilds_the_test_client(self):
        if not (PRISTINE / 'game.dll').is_file() or not (CLASSIC / 'zones/zone276').is_dir():
            self.skipTest('No pristine 1.130 and classic clients on this machine')
        patch = m.build(PRISTINE, CLASSIC)
        self.assertEqual([f['Path'] for f in patch['Files']],
                         ['game.dll', 'pregame/character_creation.xml', 'pregame/splash.mpk', 'zones/zones.mpk',
                          'zones/zone253/ter253.mpk', 'zones/zone253/lod253.mpk',
                          'zones/zone276/dat276.mpk', 'zones/zone276/sounds.dat'])
        game = patch['Files'][0]
        self.assertEqual((game['InputSha256'][:8], game['OutputSha256'][:8]), ('3915a495', '39d82bb0'))
        # The stock 1.130 loading screen in, the Divided Realms splash out, in one edit.
        splash = patch['Files'][2]
        self.assertEqual(splash['InputSha256'][:8], '9348c3a6')
        self.assertEqual(splash['OutputSha256'], m.sha(m.SPLASH.read_bytes()))
        self.assertEqual(len(splash['Edits']), 1)
        self.assertEqual(m.apply((PRISTINE / 'pregame/splash.mpk').read_bytes(), splash), m.SPLASH.read_bytes())
        # Every stock section stays, only the three battleground zones change, and only our text ships.
        zones = patch['Files'][3]
        stock_zones = (PRISTINE / 'zones/zones.mpk').read_bytes()
        stock, ours = sections(m.member_of(stock_zones)), sections(m.member_of(m.apply(stock_zones, zones)))
        self.assertLessEqual(set(stock), set(ours))
        self.assertEqual({k for k in stock if stock[k] != ours[k]}, {'zone250', 'zone251', 'zone252'})
        self.assertLess(sum(len(base64.b64decode(e['Insert'])) for e in zones['Edits']), 1200)
        for terrain in patch['Files'][4:6]:
            self.assertLess(sum(len(base64.b64decode(e['Insert'])) for e in terrain['Edits']), 64)
            m.apply((PRISTINE / terrain['From']).read_bytes(), terrain)
        # Marfach's map is the classic client's, unchanged, and is the only retail data the patch adds.
        for marfach in patch['Files'][6:]:
            self.assertEqual(m.apply((PRISTINE / marfach['From']).read_bytes(), marfach), (CLASSIC / marfach['Path']).read_bytes())
        self.assertLess(sum(f['OutputSize'] for f in patch['Files'][6:]), 25000)
        if MODERN_TEST.is_dir():
            # The test client keeps its hand-installed classic zone table until it is set up from this patch.
            for f in [f for f in patch['Files'] if f['Path'] != 'zones/zones.mpk']:
                self.assertEqual(hashlib.sha256((MODERN_TEST / f['Path']).read_bytes()).hexdigest(), f['OutputSha256'], f['Path'])


if __name__ == '__main__':
    unittest.main()
