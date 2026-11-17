"""CL15 Darkness Falls weapon buff (DR-463, owner 2026-11-17): every Bonus1..Bonus10 value on the
weapons sold by the cl15_blood_weapons_alb/mid/hib merchant lists (Bountycraft makes the same items)
goes up by 50%, rounded half up. Focus levels (types 120-162, 165) stay at 50 (they cap there).
Type, DPS, procs and everything else stay as live.

    python make_df_cl15_weapon_buff_edits.py <world.sqlite3.db> > df-cl15-weapon-buff-2026-11-17.edits.json
"""
import json
import sqlite3
import sys

FACTOR = 1.5


def is_focus(bonus_type):
    return 120 <= bonus_type <= 162 and bonus_type not in (145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156) or bonus_type == 165


def scale(value, bonus_type):
    return value if is_focus(bonus_type) else int(value * FACTOR + 0.5)


def main(db):
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    ids = [r[0] for r in con.execute(
        "SELECT DISTINCT ItemTemplateID FROM MerchantItem WHERE ItemListID LIKE 'cl15_blood_weapons_%' ORDER BY 1")]
    cols = [f"Bonus{n}" for n in range(1, 11)]
    edits = []
    for item_id in ids:
        types = [f"{c}Type" for c in cols]
        row = con.execute(f"SELECT {','.join(cols + types)} FROM ItemTemplate WHERE Id_nb=?", (item_id,)).fetchone()
        if row is None:
            continue
        changes = {c: scale(v, t) for c, v, t in zip(cols, row[:10], row[10:]) if v and scale(v, t) != v}
        if changes:
            edits.append({"table": "ItemTemplate", "key": item_id, "set": changes})
    json.dump(edits, sys.stdout, indent=1)
    print(f"{len(edits)} items", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1])
