"""Mithril store 2026-11-14: store items, merchant lists and the three capital merchants per realm.

Needs server build feature/mithril-store (GameMithrilMerchant, MithrilStoreItem, patterns, omni dyes, mithril_respec_*).
Usage: python -I make_mithril_store_edits.py <world.db> <reference.sqlite3> <out.edits.json>
Reads both databases read-only and writes a World Workshop edits file. Prices are in Mithril (official mount prices;
everything else set by us, see Research/Audits/Mithril-Store-2026-11-14).
"""
import json
import sqlite3
import sys
import uuid

PKG = "DR Mithril store 2026-11-14"
MERCHANT = "DOL.GS.GameMithrilMerchant"
STORE_ITEM = "DOL.GS.MithrilStoreItem"
REALMS = {1: "alb", 2: "mid", 3: "hib"}

# Live merchant spots: Camelot outside the throne room, Jordheim main hall porter exit, Tir na nOg past the main porter.
ANCHOR = {1: (10, 32547, 24320, 8447), 2: (101, 32046, 27428, 8792), 3: (201, 33418, 31180, 7999)}
OFFSETS = [(220, 0), (220, 180), (220, -180)]
# Hibernia: +220 X from Channeler Glasny is inside the alcove wall (no navmesh floor; MattSC/Niebaum report 2026-11-17).
# Use the merchant hall just east of that wall instead, facing east into the room (navmesh-checked, 80-unit clearance).
SPOTS = {3: [(33968, 31230, 8000, 3072), (33968, 31330, 8001, 3072), (33968, 31130, 7999, 3072)]}

# Official Mithril mount prices (darkageofcamelot.com "Mounts: Mithril Mounts"); horsemap id -> price, realm (0 = all).
MOUNTS = [(71, 2100, 0), (72, 2100, 0), (73, 2100, 0), (77, 1385, 1), (75, 1385, 2), (74, 1385, 3), (63, 1385, 0),
          (33, 325, 0), (37, 325, 0), (38, 325, 0), (39, 325, 0), (55, 425, 0),
          (84, 975, 0), (86, 975, 0), (87, 975, 0), (88, 975, 0), (85, 975, 0)]
MOUNT_NAMES = {71: "Wolf Dragon Mount", 72: "Glimmer Dragon Mount", 73: "Stone Dragon Mount", 77: "Great-Lion Mount",
               75: "Great-Wolf Mount", 74: "Great-Elk Mount", 63: "Gryphon Mount", 84: "Enchanted Broom",
               86: "Enchanted Smoke Broom", 87: "Enchanted Witchfire Broom", 88: "Enchanted Fire Broom", 85: "Spectral Mount"}
RESPECS = [("respec_full", 300), ("respec_single", 150), ("respec_realm", 300), ("respec_cl", 300)]

# (id suffix, name, price, model source item or model id)
CONSUMABLES = [
    ("xp_potion", "Potion of Experience", 100), ("rp_potion", "Potion of Valor", 150), ("bp_potion", "Potion of Bounty", 100),
    ("xp_off", "Draught of Stillness (experience on/off)", 50), ("rp_off", "Draught of Peace (realm points on/off)", 50),
    ("cl15_token", "Champion Level 15 Token", 1000), ("ml10_token", "Master Level 10 Token", 1500),
    ("boost_token", "Character Boost Token", 3500), ("lgm_all", "Legendary Grandmaster Token (all trades)", 5000),
    ("lgm_weaponcrafting", "Legendary Weaponcrafting Token", 1000), ("lgm_armorcrafting", "Legendary Armorcrafting Token", 1000),
    ("lgm_tailoring", "Legendary Tailoring Token", 1000), ("lgm_fletching", "Legendary Fletching Token", 1000),
    ("lgm_spellcrafting", "Legendary Spellcrafting Token", 1000), ("lgm_alchemy", "Legendary Alchemy Token", 1000),
    ("lgm_siegecrafting", "Legendary Siegecrafting Token", 1000), ("repair_kit", "Mithril Repair Kit", 250),
    ("name_change", "Name Change Token", 500), ("gender_change", "Gender Change Token", 500), ("race_change", "Race Change Token", 1000),
]

# Client mask objects (objects.csv): design -> (Alb, Mid, Hib) model; Minotaur versions are separate objects.
MASKS = {"eye": ("Eye Mask", 4653, 4654, 4655), "horned": ("Horned Mask", 4657, 4658, 4659),
         "slitted": ("Slitted Mask", 4661, 4662, 4663), "skull": ("Skull Mask", 4665, 4666, 4667),
         "plague": ("Plague Mask", 4669, 4670, 4671), "beast": ("Beast Mask", 4673, 4674, 4675),
         "devour": ("Devourer Mask", 4677, 4678, 4679), "satyr": ("Satyr Mask", 4681, 4682, 4683),
         "death": ("Death Mask", 4685, 4686, 4687), "plague_bird": ("Bird Plague Mask", 4779, 4780, 4781),
         "beast_leopard": ("Leopard Mask", 4783, 4784, 4785), "satyr_tracery": ("Traced Satyr Mask", 4787, 4788, 4789),
         "skull_glyphs": ("Glyph Skull Mask", 4791, 4792, 4793), "tentacled": ("Tentacled Mask", 4818, 4819, 4820),
         "demon": ("Demon Mask", 4829, 4830, 4831), "angel": ("Angel Mask", 4833, 4832, 4834)}
OMNI_TINTS = [0, 6] + list(range(8, 88)) + list(range(120, 144))


def uid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "dr-mithril-store/" + "/".join(map(str, parts))))


def main(world, ref, out):
    w = sqlite3.connect(f"file:{world}?mode=ro", uri=True)
    r = sqlite3.connect(f"file:{ref}?mode=ro", uri=True)
    cols = [x[1] for x in w.execute("pragma table_info(ItemTemplate)")]
    have = {x[0].lower() for x in w.execute("select Id_nb from ItemTemplate")}
    tints = {int(a): (b or "").strip() for a, b in r.execute("select ID, Textual_Name from src_client_tints") if str(a).isdigit()}

    def row(id_nb):
        values = w.execute(f"select * from ItemTemplate where Id_nb = ?", (id_nb,)).fetchone()
        assert values, id_nb
        d = dict(zip(cols, values))
        d.pop("ItemTemplate_ID", None)
        d.pop("LastTimeRowUpdated", None)
        return d

    def model_of(name_like, fallback):
        x = w.execute("select Model from ItemTemplate where Name like ? and Model > 0 limit 1", (name_like,)).fetchone()
        return x[0] if x else fallback

    potion_model = model_of("%Potion of%", 2050)
    token_model = model_of("Bonus Level Token", 485)
    edits, lists = [], {}

    def item(id_nb, **fields):
        assert id_nb.lower() not in have, id_nb
        base = {"Id_nb": id_nb, "Name": id_nb, "Level": 1, "Object_Type": 0, "Item_Type": 40, "Model": token_model,
                "MaxCount": 1, "PackSize": 1, "Quality": 100, "Condition": 50000, "MaxCondition": 50000,
                "Durability": 50000, "MaxDurability": 50000, "Weight": 1, "IsPickable": 1, "IsDropable": 1,
                "CanDropAsLoot": 0, "IsTradable": 0, "Realm": 0, "PackageID": PKG}
        base.update(fields)
        edits.append({"table": "ItemTemplate", "insert": base})
        have.add(id_nb.lower())
        return id_nb

    def copy(src, id_nb, **fields):
        d = row(src)
        d.update({"Id_nb": id_nb, "PackageID": PKG, "IsTradable": 0, "CanDropAsLoot": 0})
        d.update(fields)
        return item(id_nb, **d)

    general = []
    for suffix, name, price in CONSUMABLES:
        model = potion_model if suffix.endswith("potion") or suffix.endswith("_off") else token_model
        general.append(item(f"mithril_{suffix}", Name=name, Price=price, Model=model, ClassType=STORE_ITEM,
                            MaxCount=20 if suffix.endswith("potion") else 1))
    for src, price in RESPECS:
        general.append(copy(src, f"mithril_{src}", Price=price))
    mounts = {}
    for horse, price, realm in MOUNTS:
        mounts[horse] = (copy(f"mount_{horse}", f"mithril_mount_{horse}", Price=price, Level=1,
                              Name=MOUNT_NAMES.get(horse, row(f"mount_{horse}")["Name"])), realm)

    patterns = {}
    blank = item("mithril_pattern_blank", Name="Blank Pattern", Price=100, Model=498)
    eraser = item("mithril_pattern_eraser", Name="Pattern Eraser", Price=25, Model=499, MaxCount=20)
    for realm, short in REALMS.items():
        masks = [item(f"mithril_mask_{key}_{short}", Name=name, Price=300, Model=models[realm - 1], Realm=realm)
                 for key, (name, *models) in MASKS.items()]
        patterns[realm] = [blank, eraser] + masks

    dyes = []
    for tint in OMNI_TINTS:
        label = tints.get(tint, "").replace(" - crafter only", "").replace(" Cloth", "").strip()
        if not label:
            continue
        dyes.append(item(f"mithril_omni_dye_{tint}", Name=f"{label} Omni Dye", Price=50, Model=229, Color=tint, MaxCount=50))

    for realm, short in REALMS.items():
        lists[f"mithril_store_{short}"] = general + [m for m, (mid, rr) in mounts.items() if rr in (0, realm) for m in [mid]]
        lists[f"mithril_patterns_{short}"] = patterns[realm]
    lists["mithril_dyes"] = dyes

    for list_id, ids in lists.items():
        assert len(ids) <= 150, (list_id, len(ids))
        for n, id_nb in enumerate(ids):
            edits.append({"table": "MerchantItem", "insert": {
                "MerchantItem_ID": uid("list", list_id, id_nb), "ItemListID": list_id, "ItemTemplateID": id_nb,
                "PageNumber": n // 30, "SlotPosition": n % 30}})

    for realm, short in REALMS.items():
        region, x, y, z = ANCHOR[realm]
        models = [m for (m,) in w.execute("select Model from Mob where Region = ? and Realm = ? and ClassType like '%Merchant%' "
                                          "and Model > 0 limit 3", (region, realm))] or [32, 32, 32]
        for k, (name, list_id) in enumerate([("Mithril Merchant", f"mithril_store_{short}"),
                                              ("Pattern Merchant", f"mithril_patterns_{short}"),
                                              ("Dye Merchant", "mithril_dyes")]):
            dx, dy = OFFSETS[k]
            px, py, pz, heading = SPOTS[realm][k] if realm in SPOTS else (x + dx, y + dy, z, 0)
            edits.append({"table": "Mob", "insert": {
                "Mob_ID": uid("npc", realm, name), "ClassType": MERCHANT, "Name": name, "Guild": "Mithril Store",
                "Realm": realm, "Region": region, "X": px, "Y": py, "Z": pz, "Heading": heading,
                "Model": models[k % len(models)], "Size": 50, "Level": 50, "Speed": 0, "RespawnInterval": 0, "RoamingRange": 0,
                "AggroLevel": 0, "AggroRange": 0, "Flags": 16, "MeleeDamageType": 1, "Strength": 30, "Constitution": 30,
                "Dexterity": 30, "Quickness": 30, "Intelligence": 30, "Piety": 30, "Empathy": 30, "Charisma": 30,
                "ItemsListTemplateID": list_id, "PackageID": PKG, "BodyType": 0, "Race": 0, "Gender": 0}})

    json.dump(edits, open(out, "w"), indent=1)
    counts = {}
    for e in edits:
        counts[e["table"]] = counts.get(e["table"], 0) + 1
    print(counts, {k: len(v) for k, v in lists.items()})


if __name__ == "__main__":
    main(*sys.argv[1:4])
