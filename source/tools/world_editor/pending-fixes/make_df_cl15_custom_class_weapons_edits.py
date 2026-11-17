"""CL15 Darkness Falls weapons for the custom classes (DR-463, owner 2026-11-17).

Live has no CL15 weapon for Stormforged (64), Pendragon Sovereign (65), Death Knight (66) or Tuatha Sovereign (67).
Each new weapon copies the closest live CL15 weapon of its realm (model, type, speed, procs, price, Bountycraft
recipe) and gets bonuses for the class. Values are already at the +50% buff level of
df-cl15-weapon-buff-2026-11-17 (focus levels stay 50). They go on the realm's Diamond Seal merchant list, so the
DF drop generator rolls them too, and get a Bountycraft recipe like the others.

    python make_df_cl15_custom_class_weapons_edits.py <world.sqlite3.db> > df-cl15-custom-class-weapons-2026-11-17.edits.json
"""
import json
import sqlite3
import sys
import uuid

FACTOR = 1.5

# STR / PIE / CON hybrids that also cast (Sovereigns, Stormforged).
SOVEREIGN = [(164, 4), (1, 15), (6, 13), (3, 13), (201, 6), (206, 6), (173, 4), (200, 4), (155, 4), (198, 4)]
# Death Knight: INT caster with Necromancer focus lines (all focus 50) and Dex/Qui second.
DEATH_KNIGHT = [(163, 4), (156, 15), (2, 15), (209, 6), (202, 6), (198, 4), (191, 4), (199, 5), (254, 10), (165, 50)]

# new id, name, class id, template (closest live CL15 weapon), list, bonuses
WEAPONS = [
    ("bounty_glacial_stormforged_axe_mid", "Glacial Stormforged Axe", 64, "bounty_glacial_thane_axe_mid", "mid", SOVEREIGN),
    ("bounty_glacial_stormforged_hammer_mid", "Glacial Stormforged Hammer", 64, "bounty_glacial_thane_hammer_mid", "mid", SOVEREIGN),
    ("bounty_blighted_stormforged_heavy_axe_mid", "Blighted Stormforged Heavy Axe", 64, "bounty_blighted_thane_heavy_axe_mid", "mid", SOVEREIGN),
    ("bounty_blighted_stormforged_heavy_hammer_mid", "Blighted Stormforged Heavy Hammer", 64, "bounty_blighted_thane_heavy_hammer_mid", "mid", SOVEREIGN),
    ("bounty_darkflame_pendragon_blade_alb", "Darkflame Pendragon Blade", 65, "bounty_darkflame_armsman_blade_alb", "alb", SOVEREIGN),
    ("bounty_chthonian_pendragon_halberd_alb", "Chthonian Pendragon Halberd", 65, "bounty_chthonian_armsman_halberd_alb", "alb", SOVEREIGN),
    ("bounty_venomous_tuatha_blade_hib", "Venomous Tuatha Blade", 67, "bounty_venomous_blademaster_blade_hib", "hib", SOVEREIGN),
    ("bounty_malignant_tuatha_scythe_hib", "Malignant Tuatha Scythe", 67, "bounty_malignant_valewalker_scythe_hib", "hib", SOVEREIGN),
    ("bounty_abyssal_death_knight_staff_alb", "Abyssal Death Knight Staff", 66, "bounty_abyssal_necromancer_staff_alb", "alb", DEATH_KNIGHT),
]

CLASS_NAMES = {64: "Stormforged", 65: "Pendragon Sovereign", 66: "Death Knight", 67: "Tuatha Sovereign"}


def buffed(bonus_type, value):
    return value if bonus_type == 165 or 120 <= bonus_type <= 144 else int(value * FACTOR + 0.5)


def main(db):
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    edits = []
    next_slot = {}
    for list_suffix in ("alb", "mid", "hib"):
        list_id = f"cl15_blood_weapons_{list_suffix}"
        page, slot = con.execute(
            "SELECT PageNumber, MAX(SlotPosition) FROM MerchantItem WHERE ItemListID=? "
            "GROUP BY PageNumber ORDER BY PageNumber DESC LIMIT 1", (list_id,)).fetchone()
        next_slot[list_suffix] = [page, slot + 1]
    next_recipe = con.execute("SELECT MAX(CAST(CraftedItemID AS INTEGER)) FROM CraftedItem").fetchone()[0] + 1

    for new_id, name, class_id, template, list_suffix, bonuses in WEAPONS:
        if con.execute("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", (new_id,)).fetchone():
            raise SystemExit(f"{new_id} already exists")
        changes = {
            "Name": name,
            "AllowedClasses": str(class_id),
            "Description": f"Divided Realms CL15 Darkness Falls weapon for the {CLASS_NAMES[class_id]} (DR-463)",
        }
        for n in range(1, 11):
            bonus_type, value = bonuses[n - 1] if n <= len(bonuses) else (0, 0)
            changes[f"Bonus{n}Type"] = bonus_type
            changes[f"Bonus{n}"] = buffed(bonus_type, value) if bonus_type else 0
        edits.append({"table": "ItemTemplate", "copy": template, "key": new_id, "set": changes})

        list_id = f"cl15_blood_weapons_{list_suffix}"
        page, slot = next_slot[list_suffix]
        if slot > 29:
            page, slot = page + 1, 0
        next_slot[list_suffix] = [page, slot + 1]
        edits.append({"table": "MerchantItem", "insert": {
            "MerchantItem_ID": f"{list_id}-dr463-{new_id}", "ItemListID": list_id,
            "ItemTemplateID": new_id, "PageNumber": page, "SlotPosition": slot}})

        recipe = con.execute("SELECT CraftingLevel, CraftingSkillType, MakeTemplated FROM CraftedItem WHERE Id_nb=?",
                             (template,)).fetchone()
        if recipe:
            recipe_id = str(next_recipe)
            next_recipe += 1
            edits.append({"table": "CraftedItem", "insert": {
                "CraftedItem_ID": recipe_id, "CraftedItemID": recipe_id, "Id_nb": new_id,
                "CraftingLevel": recipe[0], "CraftingSkillType": recipe[1], "MakeTemplated": recipe[2]}})
            for ingredient, count in con.execute(
                    "SELECT IngredientId_nb, Count FROM CraftedXItem WHERE CraftedItemId_nb=? ORDER BY IngredientId_nb",
                    (template,)):
                edits.append({"table": "CraftedXItem", "insert": {
                    "CraftedXItem_ID": str(uuid.uuid5(uuid.NAMESPACE_URL, f"dr463/{new_id}/{ingredient}")),
                    "CraftedItemId_nb": new_id, "IngredientId_nb": ingredient, "Count": count}})

    json.dump(edits, sys.stdout, indent=1)
    print(f"{len(WEAPONS)} weapons, {len(edits)} edits", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1])
