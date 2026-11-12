"""Make the CL15 Darkness Falls weapon merchant edits (DR-296).

Live 1.116+ sold the ~150 class-specific CL15 weapons for Daemon Blood Seals in Darkness Falls, and Bountycraft
(Recipe: Darkness Falls) makes them too. Our world has the templates and the Bountycraft recipes but no merchant.
This adds one Diamond Seal merchant per realm beside that realm's Diamond Weapon Merchant in Darkness Falls
(region 249), selling the realm's weapons plus the all-realm ones. GameDiamondSealsMerchant reads
ItemTemplate.Price as the seal count, and these templates have Price 0, so Price is set to SEAL_PRICE.
Mythirians (Freezing ... Mythirian) stay Bountycraft only. No Champion Level requirement is added.

    python -I make_cl15_blood_weapons_edits.py <world db> > cl15-blood-weapons-<date>.edits.json
"""
import json
import sqlite3
import sys

SEAL_PRICE = 25  # Diamond Weapon Merchant Avernal weapons cost 15-22 seals
SLOTS_PER_PAGE = 30
PREFIXES = ("Abyssal", "Blighted", "Chthonian", "Darkflame", "Flamescarred", "Frostshard", "Glacial",
            "Icecursed", "Infectious", "Malignant", "Miasmic", "Venomous", "Hallowed")
# Diamond Weapon Merchant to stand beside, per realm, and the new NPC's name.
ANCHORS = {1: ("4d420d76-a77a-4c4a-9042-504867d35850", "Cruo"),
           2: ("c04446e5-56c9-4b44-949d-04715ba90de6", "Blodo"),
           3: ("78b4ad11-b7c7-4684-b7c3-749a8c6c386b", "Fuilo")}
REALM_TAG = {1: "alb", 2: "mid", 3: "hib"}


def main(db):
    c = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    where = " or ".join("Name like ?" for _ in PREFIXES)
    rows = c.execute(
        f"select Id_nb, Name, Realm, Item_Type from ItemTemplate where PackageID='OfflineBountycraft' "
        f"and Id_nb like 'bounty_%' and Name not like '%Mythirian' and ({where}) "
        f"and Id_nb in (select Id_nb from CraftedItem) order by Realm, Name",
        [p + " %" for p in PREFIXES]).fetchall()
    edits = []
    for id_nb, *_ in rows:
        edits.append({"table": "ItemTemplate", "key": id_nb, "set": {"Price": SEAL_PRICE}})
    for realm, (anchor, name) in ANCHORS.items():
        x, y = c.execute("select X, Y from Mob where Mob_ID=?", (anchor,)).fetchone()
        list_id = f"cl15_blood_weapons_{REALM_TAG[realm]}"
        edits.append({"table": "Mob", "copy": anchor, "key": f"cl15-blood-weapons-{REALM_TAG[realm]}",
                      "set": {"Name": name, "Guild": "Blood Weapon Merchant", "ItemsListTemplateID": list_id,
                              "X": x + 120, "Y": y + 120, "PackageID": "OfflineCL15BloodWeapons"}})
        items = [r[0] for r in rows if r[2] in (realm, 0)]
        for i, id_nb in enumerate(items):
            edits.append({"table": "MerchantItem", "insert": {
                "ItemListID": list_id, "ItemTemplateID": id_nb,
                "PageNumber": i // SLOTS_PER_PAGE, "SlotPosition": i % SLOTS_PER_PAGE,
                "MerchantItem_ID": f"{list_id}-{i:03d}"}})
        print(f"realm {realm}: {len(items)} items", file=sys.stderr)
    print(f"{len(rows)} weapons", file=sys.stderr)
    json.dump(edits, sys.stdout, indent=1)


if __name__ == "__main__":
    main(sys.argv[1])
