r"""DR-168: build the product items of the last 1.130 client recipes no source has stats for (owner 2026-11-23: build
them as copies of their siblings). After these items exist, make_missing_recipes_edits.py finds them by the client's
name and writes the recipes.

  Entropic / Somatic Legendary  copy of the Lithic Legendary of the same weapon (same speed, DPS, bonuses, model).
                                The six Legendaries follow their defining ingredient: Torch (Pyroclasmic, heat),
                                Cauldron (Benthic, cold), Vial of Breezes (Tempestuous, energy), Dust of Creation
                                (Lithic, matter); Pound of Flesh (Somatic) is Body and Orb of Entropy (Entropic) is
                                Spirit, each at the Legendary proc strength (95). Spirit 95 exists (92127, unused);
                                Body 95 is a new spell copied from it (DamageType 10).
  Tinctured bows                copy of the untinctured bow of the same name and realm, with the proc of the volatile
                                tincture the recipe uses (the tincture item's own ProcSpellID).
  Dragonsworn shields           copy of the world's Duskwood shield of the same name (e.g. "Duskwood Rune Tempered
                                Adroit Buckler" for "Tempered Adroit Buckler").
  Mandolins and harps           copy of the realm's Duskwood Sigil harp of the same name (Albion), else the realm's
                                crafted Duskwood Harp (Hibernia).

Read-only on the world; reads missing-recipes-2026-11-21.report.json (bot-ai-trips pending-fixes) for the list.

    python -I make_dr168_items_edits.py <world.db> <missing-recipes report.json>
"""
import json, re, sqlite3, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
BODY_SPELL = 92128
SPIRIT_95 = 92127
LEGENDARY_PROC = {'Legendary: Entropic': SPIRIT_95, 'Legendary: Somatic': BODY_SPELL}
LEGENDARY_SIBLINGS = ['Lithic', 'Benthic', 'Pyroclasmic', 'Tempestuous']
ELEMENTS = {'fiery', 'icy', 'conductive'}

con = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
con.row_factory = sqlite3.Row
report = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
have_recipes = {str(r[0]) for r in con.execute("SELECT CraftedItemID FROM CraftedItem")}
todo = [x for x in report if x['group'] == 'needs product' and str(x['id']) not in have_recipes]


def item_named(name, realm, level=None):
    rows = con.execute("SELECT * FROM ItemTemplate WHERE lower(Name)=lower(?) AND Realm IN (?,0) ORDER BY Realm DESC, Level DESC",
                       (name, realm)).fetchall()
    return rows[0] if rows else None


def unused_id(base):
    candidate, n = base, 2
    while con.execute("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", (candidate,)).fetchone() or candidate in made:
        candidate, n = f"{base}{n}", n + 1
    return candidate


def sibling(x):
    name, realm, fam = x['name'], x['realm'], x['family']
    if fam.startswith('Legendary'):
        rest = name.split(' ', 1)[1]
        for stem in LEGENDARY_SIBLINGS:
            row = item_named(f"{stem} {rest}", realm)
            if row: return row, {"ProcSpellID": LEGENDARY_PROC[fam]}
        return None, f"no Legendary sibling for {rest}"
    if fam == 'Tinctured bows':
        # The client names tinctured bows by grade ("superior fiery long bow"); the world names the plain bows by wood
        # and grade ("dyrwood exceptional long bow"), and the plain recipe has the same boards and thread without the
        # tincture. superior = exceptional, durable = fortified; the wood comes from the recipe's boards.
        words = name.split()
        grade = {'superior': 'exceptional', 'durable': 'fortified'}.get(words[0].lower(), words[0])
        kind = ' '.join(w for w in words[1:] if w.lower() not in ELEMENTS)
        wood = next((i.split()[0] for i, _ in x['ingredients'] if 'wooden boards' in i.lower()), '')
        base = f"{wood} {grade} {kind}"
        tincture = next((i for i, _ in x['ingredients'] if 'tincture' in i.lower()), None)
        trow = item_named(tincture, 0) if tincture else None
        trow = trow or (con.execute("SELECT * FROM ItemTemplate WHERE lower(Name)=lower(?)", (tincture,)).fetchone() if tincture else None)
        row = item_named(base, realm)
        if not row: return None, f"no untinctured '{base}'"
        if not trow or not trow['ProcSpellID']: return None, f"tincture '{tincture}' has no proc"
        return row, {"ProcSpellID": trow['ProcSpellID']}
    if fam == 'Dragonsworn' and ('Buckler' in name or 'Shield' in name):
        rows = con.execute("SELECT * FROM ItemTemplate WHERE lower(Name) LIKE lower(?) AND Realm=? AND Level>=50 "
                           "ORDER BY (Name LIKE 'Duskwood %') DESC, Price", ('%' + name, realm)).fetchall()
        return (rows[0], {}) if rows else (None, f"no shield ending '{name}'")
    if 'Mandolin' in name or 'Harp' in name:
        tune = name.replace('Spell Marked ', '').replace(' Mandolin', '').replace(' Harp', '')
        row = item_named(f"Duskwood Sigil {tune} Harp", realm) or item_named("Duskwood Harp", realm)
        return (row, {}) if row else (None, "no Duskwood harp")
    return None, "no rule"


edits, made, report_rows = [], set(), []
if not con.execute("SELECT 1 FROM Spell WHERE SpellID=?", (BODY_SPELL,)).fetchone():
    spirit = dict(con.execute("SELECT * FROM Spell WHERE SpellID=?", (SPIRIT_95,)).fetchone())
    assert spirit['DamageType'] == 15 and spirit['Damage'] == 95, spirit
    spell = {k: v for k, v in spirit.items() if k not in ('Spell_ID', 'LastTimeRowUpdated')}
    spell.update(SpellID=BODY_SPELL, Name='ProcDirectDamageBody47', DamageType=10)
    edits.append({"table": "Spell", "insert": spell})
for x in todo:
    row, extra = sibling(x)
    if row is None:
        report_rows.append({"recipe": x['id'], "name": x['name'], "realm": x['realm'], "skipped": extra})
        continue
    new_id = unused_id(re.sub(r"[^A-Za-z0-9]+", "_", x['name']).strip('_') + f"_dr168_r{x['realm']}")
    made.add(new_id)
    sets = {"Name": x['name'], "Realm": x['realm'], "Level": x['level'], **extra}
    edits.append({"table": "ItemTemplate", "copy": row['Id_nb'], "key": new_id, "set": sets})
    report_rows.append({"recipe": x['id'], "name": x['name'], "realm": x['realm'], "item": new_id, "from": row['Id_nb'], **extra})

out = HERE / "dr168-built-items-2026-11-23.edits.json"
out.write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
(HERE / "dr168-built-items-2026-11-23.report.json").write_text(json.dumps(report_rows, indent=1) + "\n", encoding="utf-8")
skipped = [r for r in report_rows if 'skipped' in r]
print(f"{len(edits)} edits ({len(todo) - len(skipped)} items) -> {out.name}; skipped {len(skipped)}")
for r in skipped[:30]: print("  skip", r)
