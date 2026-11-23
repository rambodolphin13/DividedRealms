r"""DR-197: live's fixed zone bonuses instead of the OpenDAoC/Atlas leftovers, and enable_zone_bonuses on.

Live rules (Research\Audits\Game-Rules-2026-11-08\patchnotes):
  1.87  "all Classic and Shrouded Isles zones will have a 150% bonus to experience and a 200% bonus to coin ...
         Double coin and experience will remain in all RvR areas."   -> classic/SI +50% XP +100% coin, RvR +100%/+100%
  1.116 Darkness Falls XP and coin bonus 100% -> 150%, RP bonus 25% -> 50%      -> DF +150% XP, +150% coin, +50% RP
Every other zone (ToA, Catacombs, Darkness Rising, Labyrinth's neighbours, instances, housing, cities) gets no bonus.
The Zones columns are percent added on top (GamePlayer.GainExperience: baseXp * BonusExperience / 100).

Read-only on the world; writes dr197-live-zone-bonuses-2026-11-23.edits.json next to itself.

    python -I make_dr197_edits.py <world.db>
"""
import json, sqlite3, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

CLASSIC = {1, 100, 200,                       # outdoor realms (old frontier zones there are PvE since New Frontiers)
           20, 21, 22, 23, 24,                # Albion classic dungeons
           125, 126, 127, 128, 129,           # Midgard classic dungeons
           220, 221, 222, 223, 224}           # Hibernia classic dungeons
SHROUDED_ISLES = {50, 51, 60, 61, 62,         # Avalon City, Avalon, Caer Sidi, Krondon, Crystal Cave
                  150, 151, 160, 161,         # Trollheim, Aegir, Tuscaran Glacier, Iarnvidiur's Lair
                  180, 181, 190, 191}         # Fomor City, Hy Brasil, Tur Suil, Galladoria
RVR = {163,                                   # New Frontiers
       165, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 248, 250, 251, 252, 253,  # battlegrounds
       244, 245}                              # Passage of Conflict, Labyrinth
DARKNESS_FALLS = {249}


def bonus(region):
    """(Experience, Realmpoints, Bountypoints, Coin) percent for a region's zones."""
    if region in DARKNESS_FALLS:
        return 150, 50, 0, 150
    if region in RVR:
        return 100, 0, 0, 100
    if region in CLASSIC or region in SHROUDED_ISLES:
        return 50, 0, 0, 100
    return 0, 0, 0, 0


con = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
edits, counts = [], {}
for zone_id, region, name, xp, rp, bp, coin in con.execute(
        "SELECT ZoneID, RegionID, Name, Experience, Realmpoints, Bountypoints, Coin FROM Zones ORDER BY ZoneID"):
    want = bonus(region)
    counts[want] = counts.get(want, 0) + 1
    if (xp, rp, bp, coin) == want:
        continue
    edits.append({"table": "Zones", "where": {"ZoneID": zone_id},
                  "set": dict(zip(("Experience", "Realmpoints", "Bountypoints", "Coin"), want))})

edits.append({"table": "ServerProperty", "where": {"Key": "enable_zone_bonuses"}, "set": {"Value": "True"}})
out = HERE / "dr197-live-zone-bonuses-2026-11-23.edits.json"
out.write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
print(f"{len(edits)} edits -> {out.name}")
for (x, r, b, c), n in sorted(counts.items(), reverse=True):
    print(f"  {n:4d} zones: XP +{x}%  RP +{r}%  BP +{b}%  coin +{c}%")
