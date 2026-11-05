"""Storm Regalia (2026-11-04): the Storm Sovereign's armor and cloak as real items, as a World Workshop edits file.

Seven pieces on the Stormforged art (hauberk 4929 drawn with extension 5, legs 4930, sleeves 4931, gauntlets 4932,
boots 4933, winged helm 4942, wolf cloak 4937): AF 102 / 27% chain, class 64 only, no trade or sale. Two use
abilities and two armor procs (StormRegaliaSpells in C#, IDs 63740-63743). The set bonus is code (StormRegalia.cs);
the Crownkeeper gives missing pieces to a level 50 Storm Sovereign who whispers "Storm Regalia".

    python storm_regalia_edits.py <world.db> edits.json
    python ../world_editor/cli.py --db <world.db> --client <app> plan edits.json --out regalia.patch.json
"""
import json
from pathlib import Path
import sqlite3
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from storm_remake_edits import World, PACKAGE

P = dict(Str=1, Dex=2, Con=3, Qui=4, Pie=6, Hits=10, Body=11, Cold=12, Crush=13, Energy=14, Heat=15, Matter=16, Slash=17,
         Spirit=18, Thrust=19, Parry=40, Shields=43, Hammer=53, Axe=54, MeleeSpeed=155, AllMelee=164, MeleeDamage=173,
         SpellDamage=198, StyleDamage=200, StrCap=201, DexCap=202, ConCap=203, QuiCap=204, PieCap=206, HitsCap=210,
         CritMelee=214, Absorb=149)
SET_TEXT = ("Storm Regalia set: 2 pieces +5% all resistances; 4 pieces +10% melee and style damage; "
            "6 pieces +10% armor absorption and spell damage; all 7 +10% maximum health and every block or parry builds 2 Tempest.")

# id suffix, name, slot, model, extension, copy source, bonuses, use (spell, reuse s), proc spell
PIECES = (
    ("helm", "Stormcrown of the Sovereign", 21, 4942, 0, "2_Barbed_Ice_Queen_Crown",
     dict(Str=20, Con=20, Pie=15, Hits=60, Energy=10, Spirit=10, StrCap=5, ConCap=5, Hammer=4, Axe=4), (63740, 180), 0),
    ("hauberk", "Hauberk of the Storm Sovereign", 25, 4929, 5, "2_Baldurs_Hauberk",
     dict(Str=22, Con=22, Dex=15, Hits=80, Crush=10, Slash=10, Thrust=10, MeleeDamage=5, StyleDamage=5, Shields=4), None, 63742),
    ("sleeves", "Thunderbound Sleeves", 28, 4931, 0, "2_Arcanium_Hermes_Golden_Vambraces",
     dict(Str=18, Qui=15, Dex=12, Hits=50, Heat=10, Cold=10, Axe=4, MeleeSpeed=5, DexCap=5, QuiCap=5), None, 63743),
    ("gauntlets", "Gauntlets of the Thunder King", 22, 4932, 0, "Arcanium_Gloves_of_the_Rebirth",
     dict(Str=15, Dex=18, Qui=15, Hits=40, Matter=10, Body=10, Hammer=4, AllMelee=3, CritMelee=5, StrCap=5), None, 0),
    ("leggings", "Stormforged Leggings", 27, 4930, 0, "2_Foe_Hammers_Leggings",
     dict(Con=22, Str=15, Pie=15, Hits=70, Energy=10, Body=10, Spirit=10, Parry=4, PieCap=5, HitsCap=50), None, 0),
    ("boots", "Boots of the Rolling Thunder", 23, 4933, 0, "Arcanium_Boots_of_the_Rebirth",
     dict(Dex=15, Qui=18, Con=15, Hits=40, Cold=10, Matter=10, Heat=10, Shields=4, QuiCap=5, StyleDamage=3), None, 0),
    ("cloak", "Wolfmantle of the Storm King", 26, 4937, 0, "2_Frozen_Griffon_Mantle",
     dict(Str=15, Con=15, Pie=15, Hits=50, Crush=5, Slash=5, Thrust=5, MeleeDamage=5, SpellDamage=5, Absorb=5), (63741, 300), 0),
)

# id, name, target, duration, description (StormRegaliaSpells)
SPELLS = (
    (63740, "Crown of Tempest", "Self", 0, "Use: your Tempest fills to 5. Three-minute reuse."),
    (63741, "Wolfmantle", "Self", 30, "Use: 20% absorption and 20% melee haste for 30 seconds. Five-minute reuse."),
    (63742, "Stormguard Ward", "Self", 0, "When struck: a 200-point storm shield absorbs half of each blow for 10 seconds."),
    (63743, "Thunder Rebuke", "Enemy", 0, "When struck: lightning strikes the attacker."),
)
SPELL_TOOLTIP = 27740


def edits_for(db):
    w = World(db)
    if w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb LIKE 'storm_regalia_%'"):
        raise ValueError("The Storm Regalia is already in this world")
    if w.one("SELECT 1 FROM Spell WHERE SpellID BETWEEN 63740 AND 63749 OR TooltipId BETWEEN ? AND ?", SPELL_TOOLTIP, SPELL_TOOLTIP + 9):
        raise ValueError("Storm Regalia spell or tooltip IDs are taken")
    spell_src = w.spell_source()
    for spell_id, name, target, duration, description in SPELLS:
        w.copy("Spell", spell_src, dict(
            SpellID=spell_id, Spell_ID=f"storm-regalia-{spell_id}", Name=name, Description=description, Type="StormSovereignAbility",
            Target=target, Range=0 if target == "Self" else 1500, Radius=0, Power=0, CastTime=0, Damage=0, Duration=duration,
            Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=3541 if target == "Enemy" else 3537,
            Icon=3541 if target == "Enemy" else 3537, TooltipId=SPELL_TOOLTIP + spell_id - 63740, PackageID=PACKAGE))
    for suffix, name, slot, model, extension, source, bonuses, use, proc in PIECES:
        if not w.one("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", source):
            raise ValueError(f"Missing copy source {source}")
        fields = dict(Name=name, Level=51, Item_Type=slot, Model=model, Extension=extension, Realm=2, AllowedClasses="64",
                      Object_Type=41 if slot == 26 else 35, DPS_AF=0 if slot == 26 else 102, SPD_ABS=0 if slot == 26 else 27,
                      Quality=100, Bonus=35, BonusLevel=0, LevelRequirement=0, Color=0, Emblem=0, Effect=0,
                      IsDropable=0, IsTradable=0, CanDropAsLoot=0, IsPickable=1, Price=0,
                      SpellID=0, Charges=0, MaxCharges=0, CanUseEvery=0, SpellID1=0, Charges1=0, MaxCharges1=0,
                      ProcSpellID=proc, ProcSpellID1=0, ProcChance=10 if proc else 0, PoisonSpellID=0, PoisonCharges=0, PoisonMaxCharges=0,
                      ExtraBonus=0, ExtraBonusType=0, PackageID="OfflineDAoC.Stormforged.Regalia", ClassType="", Description=SET_TEXT)
        for i in range(1, 11):
            fields[f"Bonus{i}"], fields[f"Bonus{i}Type"] = 0, 0
        for i, (prop, value) in enumerate(bonuses.items(), 1):
            fields[f"Bonus{i}"], fields[f"Bonus{i}Type"] = value, P[prop]
        if use:
            # MaxCharges -1: no charges, only the reuse timer.
            fields.update(SpellID=use[0], Charges=-1, MaxCharges=-1, CanUseEvery=use[1])
        w.out.append({"table": "ItemTemplate", "copy": source, "key": f"storm_regalia_{suffix}", "set": fields})
    return w.out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    print(f"{len(edits)} edits: {sum(e['table'] == 'ItemTemplate' for e in edits)} items, {sum(e['table'] == 'Spell' for e in edits)} spells")


if __name__ == "__main__":
    main()
