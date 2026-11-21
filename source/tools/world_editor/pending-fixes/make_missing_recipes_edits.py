"""Sorts every 1.130 client recipe the world still lacks and writes World Workshop edits for the ones whose product
and ingredients are already in the world (DR-168, 2026-11-21).

    python make_missing_recipes_edits.py <opendaoc.sqlite3.db> <client app folder> [<out prefix>]

Writes, beside this script unless a prefix is given:
  missing-recipes-2026-11-21.edits.json             recipes added with rows the world already has
  missing-recipes-2026-11-21-owner-call.edits.json  recipes whose product is a judgement (see OWNER_CALL)
  missing-recipes-2026-11-21.report.json            every missing client recipe with its group and reason

The client recipe reader, the tier words and the CraftedItem / CraftedXItem row shapes are
make_craft_recipes_130_edits.py's. Skill levels, ingredient names and counts are the client's (tdl.crf); products and
ingredient rows are the world's. No item is built here and no number is made up.

A missing recipe is added when every ingredient is a world item and its product is found by one of:
  name    the client's name (that script's rule: the recipe's realm or realm 0, level within 3, not already carrying
          another ingredient list).
  tier    the client's name with the tier word of its own ingredients in front: the client's "Sigil Harmonious
          Harp" made of duskwood boards is the world's unused "Duskwood Sigil Harmonious Harp".
  realm   an unused row of the client's name filed under another realm while that realm's recipe already has a row
          of its own: "Benthic_Magus_Staff2" is filed under Midgard, Midgard's recipe 9190 uses
          "Benthic_Magus_Staff3", and Hibernia's recipe 16228 has no row. The row's Realm is set to the recipe's.
          Nothing else on the row is touched.

One ingredient name is not a world name: the client's Hibernian legendary bows and staves ask for "duskwood wood".
The world's own recipes for the same client text (16220-16227) take "duskwood wooden boards"; ALIASES says so and
check_aliases() stops the run if the world ever disagrees.
"""
import collections
import json
import sqlite3
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_craft_recipes_130_edits as base  # noqa: E402

NAME = 'missing-recipes-2026-11-21'
ALIASES = {'duskwood wood': 'duskwood wooden boards'}
# The client's "magus staff" in Midgard's and Hibernia's Dragonsworn fletching list. The world has no "Dragonsworn
# Magus Staff"; it has an unused caster "Dragonsworn Staff" (staff type, real numbers) in each realm. Whether that is
# the same item is the owner's call, so these go to their own edits file.
OWNER_CALL = {9279: '2Dragonsworn_Staff_Mid', 16343: '3Dragonsworn_Staff_Hib'}
TRADES = {1: 'Weaponcrafting', 2: 'Armorcrafting', 3: 'Siegecrafting', 4: 'Alchemy', 5: 'Bountycrafting',
          6: 'Metalworking', 7: 'Leathercrafting', 8: 'Clothworking', 9: 'Gemcutting', 10: 'Herbcrafting',
          11: 'Tailoring', 12: 'Fletching', 13: 'Spellcrafting', 14: 'Woodworking', 15: 'Basic Crafting'}
REALMS = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}
TIER_WORDS = sorted(set(base.MATERIALS.values()), key=len, reverse=True)


def family(recipe):
    first = recipe['name'].split()[0]
    if recipe['category'].startswith('Legendary'):
        return f'Legendary: {first}'
    if 'Tinctured' in recipe['category']:
        return 'Tinctured bows'
    if 'Dragonsworn' in recipe['category']:
        return 'Dragonsworn'
    return recipe['category']


def main(db_path, app_folder, prefix=None):
    prefix = prefix or str(Path(__file__).resolve().parent / NAME)
    recipes = base.read_crf(app_folder)
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    world = {str(r[0]): r[1] for r in db.execute('SELECT CraftedItemID, Id_nb FROM CraftedItem')}
    used = collections.defaultdict(list)  # product -> recipe ids
    for rid, id_nb in world.items():
        used[id_nb.lower()].append(rid)
    as_ingredient = collections.Counter(r[0].lower() for r in db.execute('SELECT IngredientId_nb FROM CraftedXItem'))
    sold = collections.Counter(r[0].lower() for r in db.execute('SELECT ItemTemplateID FROM MerchantItem'))
    attached = collections.defaultdict(list)
    for product, ingredient, count in db.execute('SELECT CraftedItemId_nb, IngredientId_nb, Count FROM CraftedXItem'):
        attached[product.lower()].append((ingredient.lower(), count))
    templates = collections.defaultdict(list)
    for id_nb, name, realm, level in db.execute('SELECT Id_nb, Name, Realm, Level FROM ItemTemplate'):
        templates[name.lower()].append((id_nb, realm, level))
    by_id = {t[0].lower(): (t, name) for name, rows in templates.items() for t in rows}

    def rows_named(name):
        for n in (name, name[:-1] if name.endswith('s') else name + 's'):
            if templates[n.lower()]:
                return templates[n.lower()]
        return []

    def lookup(name, realm):
        return [t for t in rows_named(ALIASES.get(name.lower(), name)) if t[1] in (realm, 0)]

    def pick_ingredient(name, realm):
        return sorted(lookup(name, realm), key=lambda t: (
            -as_ingredient[t[0].lower()], -sold[t[0].lower()], t[1] != realm, t[0]))[0][0]

    def fits(t, recipe):
        return not recipe['level'] or not t[2] or abs(t[2] - recipe['level']) <= 3

    def check_aliases():
        for r in recipes:
            for name, count in r['ingredients']:
                alias = ALIASES.get(name.lower())
                if alias and str(r['id']) in world:
                    want = {t[0].lower() for t in templates[alias]}
                    have = attached[world[str(r['id'])].lower()]
                    if not any(i in want and c == count for i, c in have):
                        raise SystemExit(f'recipe {r["id"]}: the world does not use "{alias}" for "{name}"')

    check_aliases()
    edits, owner_edits, report = [], [], []
    for r in recipes:
        rid = str(r['id'])
        if rid in world:
            continue
        missing = [n for n, _ in r['ingredients'] if not lookup(n, r['realm'])]
        product, rule, repair = None, None, None
        if r['id'] in OWNER_CALL:
            t, _ = by_id.get(OWNER_CALL[r['id']].lower(), (None, None))
            if t and t[1] == r['realm'] and fits(t, r) and not used[t[0].lower()]:
                product, rule = t[0], 'owner call'
        if product is None:
            names = [(n, 'name') for n in base.product_names(r)]
            tiers = {w for n, _ in r['ingredients'] for w in TIER_WORDS if n.lower().startswith(w + ' ')}
            names += [(f'{w} {r["name"]}', 'tier') for w in sorted(tiers)]
            for candidate, how in names:
                free = [t for t in lookup(candidate, r['realm']) if fits(t, r) and not used[t[0].lower()]
                        and t[0].lower() not in attached]
                if free:
                    product, rule = sorted(free, key=lambda t: (t[1] != r['realm'], t[0]))[0][0], how
                    break
        if product is None:
            rows = [t for t in rows_named(r['name']) if fits(t, r)]
            spare = [t for t in rows if not used[t[0].lower()] and t[0].lower() not in attached]
            # another realm's spare row, when that realm's own recipe is already served by a different row
            for t in spare:
                served = [o for o in rows if o[1] == t[1] and o is not t and used[o[0].lower()]]
                if t[1] not in (0, r['realm']) and served and len(spare) == 1:
                    product, rule, repair = t[0], 'realm', t[1]
        if not r['ingredients']:
            group, why = 'not importable', 'no ingredients in the client'
        elif product and not missing:
            group = 'owner call' if rule == 'owner call' else 'importable'
            why = {'name': 'product found by name', 'tier': 'product found with its tier word in front',
                   'realm': f'product row filed under {REALMS[repair]}; Realm set to {REALMS[r["realm"]]}' if repair else '',
                   'owner call': 'product is the realm\'s unused "Dragonsworn Staff"; is that the client\'s Dragonsworn "magus staff"?'}[rule]
        elif product:
            group, why = 'needs ingredients', 'missing ingredient: ' + ', '.join(missing)
        elif missing:
            group, why = 'needs product and ingredients', 'no product item; missing ingredient: ' + ', '.join(missing)
        else:
            group, why = 'needs product', 'no product item'
        entry = {**r, 'trade_name': TRADES[r['trade']], 'family': family(r), 'group': group, 'why': why}
        if group in ('importable', 'owner call'):
            out = owner_edits if group == 'owner call' else edits
            entry['product'] = product
            if repair:
                out.append({'table': 'ItemTemplate', 'key': product, 'set': {'Realm': r['realm']}})
            out.append({'table': 'CraftedItem', 'insert': {
                'CraftedItemID': rid, 'CraftedItem_ID': rid, 'Id_nb': product, 'CraftingLevel': r['skill'],
                'CraftingSkillType': r['trade'], 'MakeTemplated': 0}})
            entry['rows'] = []
            for i, (name, count) in enumerate(r['ingredients']):
                choice = pick_ingredient(name, r['realm'])
                entry['rows'].append([choice, count])
                out.append({'table': 'CraftedXItem', 'insert': {
                    'CraftedXItem_ID': str(uuid.uuid5(base.NAMESPACE, f'{rid}-{i}')), 'CraftedItemId_nb': product,
                    'IngredientId_nb': choice, 'Count': count}})
            used[product.lower()].append(rid)
            attached[product.lower()] = [(c.lower(), n) for c, n in entry['rows']]
        report.append(entry)

    Path(prefix + '.edits.json').write_text(json.dumps(edits, indent=1) + '\n', encoding='utf-8')
    Path(prefix + '-owner-call.edits.json').write_text(json.dumps(owner_edits, indent=1) + '\n', encoding='utf-8')
    Path(prefix + '.report.json').write_text(json.dumps(report, indent=1) + '\n', encoding='utf-8')

    print(f'client recipes {len(recipes)}, in the world {len(recipes) - len(report)}, missing {len(report)}')
    table = collections.Counter((r['trade_name'], REALMS[r['realm']], r['group']) for r in report)
    for key in sorted(table):
        print(f'  {key[0]:15} {key[1]:9} {key[2]:30} {table[key]}')
    families = collections.Counter((r['group'], r['family'].split(':')[0]) for r in report)
    for key in sorted(families):
        print(f'  {key[0]:30} {key[1]:20} {families[key]}')
    print(f'{len(edits)} rows in {NAME}.edits.json, {len(owner_edits)} rows in {NAME}-owner-call.edits.json')


if __name__ == '__main__':
    main(*sys.argv[1:4])
