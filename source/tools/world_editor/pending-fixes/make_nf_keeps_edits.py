"""New Frontiers keeps, batch 1 (2026-11-14): World Workshop edits file generator.

Owner decisions 2026-11-14: keep both frontiers, Old Frontiers stays as it is, New Frontiers uses the newer
component keeps (destructible walls), and the 21 OF keeps whose numbers clash with NF take new internal numbers.

What it writes:
  * the 21 clashing OF Keep rows (50-56, 75-81, 100-106) copied to 175-195, and the old rows overwritten with
    the NF keeps of the same number; nothing else
    about them changes (no component, position or hook point row refers to an OF keep)
  * every EoD db-public Keep row in region 163 (21 keeps + 84 towers) with its stock number; the client's
    /realmwar map finds keeps and towers by number, so NF keeps must keep theirs
  * only the new-style KeepComponent rows (Skin > 20) of those keeps, KeepPosition rows with ComponentSkin > 20
    that are not already in the world, and KeepHookPoint rows for skins > 20
  * use_new_keeps = 1 (the server then loads only Skin > 20 components; OF keeps have none)
  * Zones 163 renamed "Ellan Vannin" (stock 1.130 name; Agramon was replaced in 1.119)

Region 163 has no zone points in yet and IsFrontier stays 0, so players and bots can't reach these keeps and they
stay off the war map until the entrances batch.

    python make_nf_keeps_edits.py --world <opendaoc.sqlite3.db> --eod <eod-db-public/data> --out <edits.json>
"""
import argparse
import json
import sqlite3
from pathlib import Path

NF_REGION = 163
OF_RENUMBER = {**{50 + i: 175 + i for i in range(7)},
               **{75 + i: 182 + i for i in range(7)},
               **{100 + i: 189 + i for i in range(7)}}
NEW_SKIN = 20  # KeepManager.IsNewKeepComponent: skin > 20


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def strip(row):
    return {k: v for k, v in row.items() if k != "LastTimeRowUpdated"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--eod", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    db = sqlite3.connect(f"file:{a.world}?mode=ro", uri=True)
    eod = Path(a.eod)
    edits = []

    # 1. Old Frontiers keeps move out of the way.
    of_rows = {r[0]: r for r in db.execute("SELECT KeepID, Keep_ID, Region FROM Keep")}
    used = set(of_rows)
    for old, new in OF_RENUMBER.items():
        keep_id, keep_pk, region = of_rows[old]
        assert region in (1, 100, 200), (old, region)
        assert new not in used, new
        edits.append({"table": "Keep", "copy": str(old), "key": str(new), "set": {"Keep_ID": str(new)}})

    # 2. New Frontiers keeps and towers.
    keeps = [k for k in load(eod / "Keep.json") if k["Region"] == NF_REGION]
    nf_ids = {k["KeepID"] for k in keeps}
    remaining = used - set(OF_RENUMBER) | set(OF_RENUMBER.values())
    assert not nf_ids & remaining, sorted(nf_ids & remaining)
    for k in sorted(keeps, key=lambda k: k["KeepID"]):
        if k["KeepID"] in OF_RENUMBER:
            # the OF row was copied above; overwrite it with the NF keep (one edit per key per patch)
            edits.append({"table": "Keep", "key": str(k["KeepID"]),
                          "set": {c: v for c, v in strip(k).items() if c != "KeepID"}})
        else:
            edits.append({"table": "Keep", "insert": strip(k)})

    comps = [c for c in load(eod / "KeepComponent.json") if c["KeepID"] in nf_ids and c["Skin"] > NEW_SKIN]
    for c in sorted(comps, key=lambda c: (c["KeepID"], c["ID"])):
        edits.append({"table": "KeepComponent", "insert": strip(c)})

    have_pos = {r[0] for r in db.execute("SELECT KeepPosition_ID FROM KeepPosition")}
    pos = [p for p in load(eod / "KeepPosition.json")
           if p["ComponentSkin"] > NEW_SKIN and p["KeepPosition_ID"] not in have_pos]
    for p in pos:
        edits.append({"table": "KeepPosition", "insert": strip(p)})

    have_hp = {r[0] for r in db.execute("SELECT KeepHookPoint_ID FROM KeepHookPoint")}
    hps = [h for h in load(eod / "KeepHookPoint.json")
           if h["KeepComponentSkinID"] > NEW_SKIN and h["KeepHookPoint_ID"] not in have_hp]
    for h in hps:
        edits.append({"table": "KeepHookPoint", "insert": strip(h)})

    # 3. Settings and names.
    edits.append({"table": "ServerProperty", "where": {"Key": "use_new_keeps"}, "set": {"Value": "1"}})
    edits.append({"table": "Zones", "where": {"ZoneID": 163}, "set": {"Name": "Ellan Vannin"}})

    Path(a.out).write_text(json.dumps(edits, indent=1), encoding="utf-8")
    print(f"OF renumbered {len(OF_RENUMBER)}, NF keeps {len(keeps)}, components {len(comps)}, "
          f"positions {len(pos)}, hook points {len(hps)}, edits {len(edits)}")


if __name__ == "__main__":
    main()
