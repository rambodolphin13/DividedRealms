r"""Install the Regalia of Tara art (concept A, emerald court, with the stag roundel cloak) into the modern 1.130 client.
Owner picks 2026-11-06: armor A, cloak v1.

  plan    : python -B install_tara_set.py plan     <client app folder> [--modern-test]
  install : python -B install_tara_set.py install  <client app folder> [--modern-test]
  rollback: python -B install_tara_set.py rollback <client app folder> <backup folder>

Refuses the real client folders; the owner's modern test client only with --modern-test. No meshes are added: the
pieces draw the stock Celtic scale meshes with private skins. Private data under new IDs (free on 2026-11-06):
  items\\pskins\\pskin062.mpk      NEW  pskins textures (cata armor, cata cloak, classic DXT3 cloak, helm)
  figures\\Mskins\\mskin071.mpk    NEW  mskins textures (cata armor BaseTex, read by /hood)
  mskins.csv 2750-2755, pskins.csv 5810-5818, objects.csv 4950-4956: NEW rows (no stock row changes)
objects: 4950 hauberk (cloned from 428 Celtic Scaled 3 Vest; the server item uses Extension 5 = Body05, as the Storm
hauberk), 4951 legs (429), 4952 sleeves (430), 4953 gloves (431), 4954 boots (432), 4955 helm (840 Celtic scale helm 3,
HibHelm03 meshes), 4956 cloak (326, Cloak# 1).
Then point the server items at them: source/tools/sovereign/tara_regalia_edits.py --update (World Workshop)."""
from __future__ import annotations

import hashlib
import json
import sys
import time
from datetime import datetime
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import regalia_client as S  # noqa: E402  (need, table, add_row, client_closed, rollback, archive, encoders, REAL, MODERN_TEST)

HERE = Path(__file__).resolve().parent
TEX = HERE / "textures"
PSKIN_ARC, MSKIN_ARC = 62, 71
need = S.need


def img(name, size):
    return Image.open(TEX / name).convert("RGB").resize(size, Image.LANCZOS)


# mskins: id, texture, source, size (stock cata sizes)
MSKINS = [(2750, "tara_mbody_m.dds", "body_m.png", (256, 512)), (2751, "tara_mbody_f.dds", "body_f.png", (256, 512)),
          (2752, "tara_marms.dds", "arms.png", (256, 256)), (2753, "tara_mlegs.dds", "legs.png", (256, 256)),
          (2754, "tara_mgloves.dds", "gloves.png", (512, 256)), (2755, "tara_mboots.dds", "boots.png", (512, 256))]
# pskins: id, texture, source, size, stock row cloned, BaseTex (our mskins row), dxt3?
PSKINS = [(5810, "tara_body_m.dds", "body_m.png", (256, 512), "4330", 2750, False),
          (5811, "tara_body_f.dds", "body_f.png", (256, 512), "4331", 2751, False),
          (5812, "tara_arms.dds", "arms.png", (256, 256), "4332", 2752, False),
          (5813, "tara_legs.dds", "legs.png", (256, 256), "4334", 2753, False),
          (5814, "tara_gloves.dds", "gloves.png", (512, 256), "4333", 2754, False),
          (5815, "tara_boots.dds", "boots.png", (512, 256), "4335", 2755, False),
          (5816, "tara_cloak.dds", "cloak.png", (512, 1024), "4972", 0, False),      # cata cloak, UV set 1
          (5817, "tara_cloak256.dds", "cloak.png", (256, 256), "618", 0, True),      # classic col 11 (hooded mantle), DXT3
          (5818, "tara_helm.dds", "helm.png", (256, 256), "615", 0, False)]          # helm (classic cols 12 and 22)
# objects: id, stock row cloned, name, column changes (every inherited skin column is decided)
OBJECTS = [(4950, "428", "Regalia of Tara Hauberk", {66: "5810", 67: "5811"}),
           (4951, "429", "Regalia of Tara Greaves", {70: "5813"}),
           (4952, "430", "Regalia of Tara Sleeves", {68: "5812"}),
           (4953, "431", "Regalia of Tara Gauntlets", {69: "5814"}),
           (4954, "432", "Regalia of Tara Boots", {71: "5815"}),
           (4955, "840", "Regalia of Tara Crown", {12: "5818", 22: "5818"}),
           (4956, "326", "Regalia of Tara Mantle", {11: "5817", 16: "1", 72: "5816"})]


def build(client: Path):
    game = client / "gamedata.mpk"
    name, entries = S.archive.read(game.read_bytes())
    files = {e.name.lower(): e.data for e in entries}
    T = {k: S.table(files[k]) for k in ("pskins.csv", "mskins.csv", "objects.csv")}
    upd = {k: files[k] for k in ("pskins.csv", "mskins.csv", "objects.csv")}
    for mid, tex, _, _ in MSKINS:
        row = list(T["mskins.csv"]["2327"]); row[0], row[1], row[2], row[4], row[5] = str(mid), tex, tex, str(MSKIN_ARC), "0"
        upd["mskins.csv"] = S.add_row(upd["mskins.csv"], row)
    for pid, tex, _, _, stock, base, _ in PSKINS:
        row = list(T["pskins.csv"][stock])
        row += [""] * max(0, 7 - len(row))
        row[0], row[1], row[2], row[4], row[5], row[6] = str(pid), "Tara " + tex[:-4], tex, str(PSKIN_ARC), "0", str(base)
        upd["pskins.csv"] = S.add_row(upd["pskins.csv"], row)
    for oid, stock, label, changes in OBJECTS:
        row = list(T["objects.csv"][stock]); row += [""] * (98 - len(row))
        row[0], row[1] = str(oid), label
        for c, v in changes.items():
            row[c] = v
        upd["objects.csv"] = S.add_row(upd["objects.csv"], row)
    blob = S.archive.write(name, [S.archive.Entry(e.name, upd.get(e.name.lower(), e.data), e.timestamp, e.flags) for e in entries])
    S.archive.verify_memory_image(blob)
    _, dec = S.archive.read(blob)
    for old, new in zip(entries, dec):
        if old.name.lower() not in upd:
            need(old.data == new.data, f"unrelated entry changed: {old.name}")
    for k in upd:
        old_rows = files[k].decode("latin1").split("\r\n")
        new_rows = upd[k].decode("latin1").split("\r\n")
        need([l for l in new_rows if l in set(old_rows)] == old_rows, f"stock {k} line changed")
        need(len(new_rows) - len(old_rows) == {"mskins.csv": 6, "pskins.csv": 9, "objects.csv": 7}[k], f"{k}: unexpected row count")
    ref = next(e.data for e in S.archive.read((client / "items/pskins/pskin008.mpk").read_bytes())[1]
               if e.name.lower().endswith(".dds") and e.data[84:88] == b"DXT1")
    ref3 = next(e.data for e in S.archive.read((client / "items/pskins/pskin001.mpk").read_bytes())[1] if e.name.lower() == "cloak.dds")
    stamp = int(time.time())
    ptex = sorted(((t, S.encode_dxt3(img(src, size), ref3) if dxt3 else S.encode_dds(img(src, size), ref))
                   for _, t, src, size, _, _, dxt3 in PSKINS), key=lambda x: x[0].lower())
    mtex = sorted(((t, S.encode_dds(img(src, size), ref)) for _, t, src, size in MSKINS), key=lambda x: x[0].lower())
    pak = S.archive.write(f"pskin{PSKIN_ARC:03d}.mpk".encode(), [S.archive.Entry(n, d, stamp, 4) for n, d in ptex])
    mpak = S.archive.write(f"mskin{MSKIN_ARC:03d}.mpk".encode(), [S.archive.Entry(n, d, stamp, 4) for n, d in mtex])
    for b in (pak, mpak):
        S.archive.verify_memory_image(b)
    return blob, pak, mpak


def targets(client):
    return client / f"items/pskins/pskin{PSKIN_ARC:03d}.mpk", client / f"figures/Mskins/mskin{MSKIN_ARC:03d}.mpk"


def main():
    if len(sys.argv) >= 4 and sys.argv[1] == "rollback":
        S.rollback(Path(sys.argv[2]).resolve(), Path(sys.argv[3]).resolve()); return
    if len(sys.argv) < 3 or sys.argv[1] not in ("plan", "install"):
        print(__doc__); return
    client = Path(sys.argv[2]).resolve()
    real_ok = "--modern-test" in sys.argv and client == S.MODERN_TEST.resolve()
    need(real_ok or all(client != r.resolve() for r in S.REAL), "this is a real client folder; only the modern test client with --modern-test")
    need((client / "gamedata.mpk").exists() and (client / "game.dll").exists(), "not a client folder")
    if real_ok:
        S.client_closed()
    for t in targets(client):
        need(not t.exists(), f"{t.name} already exists")
    blob, pak, mpak = build(client)
    print(f"plan ok: gamedata {len(blob)} bytes, pskin{PSKIN_ARC:03d} {len(pak)}, mskin{MSKIN_ARC:03d} {len(mpak)}; "
          f"{len(MSKINS)} mskins, {len(PSKINS)} pskins, {len(OBJECTS)} objects rows")
    if sys.argv[1] != "install":
        return
    game = client / "gamedata.mpk"
    manifest = {"when": datetime.now().isoformat(timespec="seconds"), "client": str(client),
                "gamedata_before": hashlib.sha256(game.read_bytes()).hexdigest(), "gamedata_after": hashlib.sha256(blob).hexdigest()}
    backup = client.parents[1] / "backups" / ("client-tara-set-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    (backup / "gamedata.mpk").write_bytes(game.read_bytes())
    manifest["new_files"] = [str(t.relative_to(client)) for t in targets(client)]
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for t, b in zip(targets(client), (pak, mpak)):
        t.write_bytes(b)
    game.write_bytes(blob)
    need(hashlib.sha256(game.read_bytes()).hexdigest() == manifest["gamedata_after"], "installed catalog differs")
    (client / "tara-set-install.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("installed into", client)
    print(f'rollback: python -B "{Path(__file__).resolve()}" rollback "{client}" "{backup}"')


if __name__ == "__main__":
    main()
