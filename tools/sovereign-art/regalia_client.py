"""Shared helpers for the Sovereign Regalia client art installers (tara/, camelot/): catalog rows, DDS encoders,
the client-closed check and rollback. Moved here from the Stormforged concept kit (install_test_copy.py) so the
installers run from the repository; the archive and encoders come from tools/pet-art and tools/asset-tool."""
from __future__ import annotations

import csv
import hashlib
import io
import json
import sys
from pathlib import Path

PET = Path(__file__).resolve().parents[1] / "pet-art"
sys.path.insert(0, str(PET))
from daoc_catalog import archive  # noqa: E402,F401
from install_pet_art import encode_dds  # noqa: E402,F401
from install_sluagh_armor import encode_dxt3  # noqa: E402,F401

# client folders the installers refuse; the first is the owner's modern test client, allowed only with --modern-test
REAL = [Path(r"C:\Users\natex\Desktop\DAoC Offline\Source\artifacts\modern-client-test\runtime\client-modern\app"),
        Path(r"C:\Users\natex\Desktop\DAoC Offline\Stormforged\runtime\client-opendaoc\app")]
MODERN_TEST = REAL[0]


def need(ok, msg):
    if not ok:
        raise SystemExit("REFUSED: " + msg)


def table(data):
    return {r[0]: r for r in csv.reader(io.StringIO(data.decode("latin1"))) if r and r[0].isdigit()}


def line_of(row):
    s = io.StringIO(); csv.writer(s, lineterminator="").writerow(row); return s.getvalue()


def add_row(data, row):
    """Insert a new catalog row in ID order; refuses an ID that is already used."""
    lines = data.decode("latin1").split("\r\n")
    numeric = [(i, int(l.split(",", 1)[0])) for i, l in enumerate(lines) if l.split(",", 1)[0].strip().isdigit()]
    need(all(n != int(row[0]) for _, n in numeric), f"ID {row[0]} already used")
    at = next((i for i, n in numeric if n > int(row[0])), numeric[-1][0] + 1)
    return "\r\n".join(lines[:at] + [line_of(row)] + lines[at:]).encode("latin1")


def client_closed():
    import subprocess
    out = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
    need("connect-local1130" not in out and "camelot.exe" not in out, "close the game client first")


def rollback(client: Path, folder: Path):
    """Restore gamedata.mpk from an install backup and remove the archives that install added."""
    client_closed()
    m = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    game = client / "gamedata.mpk"
    need(hashlib.sha256(game.read_bytes()).hexdigest() in (m["gamedata_before"], m["gamedata_after"]),
         "later catalog changes detected; refuse to overwrite them")
    game.write_bytes((folder / "gamedata.mpk").read_bytes())
    for rel in m["new_files"]:
        (client / rel).unlink(missing_ok=True)
    print("rolled back", client)
