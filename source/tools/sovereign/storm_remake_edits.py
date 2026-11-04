"""Storm Sovereign remake (2026-11-04): writes a World Workshop edits file for an installed world.

Hammer, Axe and Shields with 60 chained styles (storm_remake_styles.json), seven merged or reworked spells,
Sword and Spear retired, and a trimmed realm ability list with three new Sovereign RAs. Only class-64 rows change;
Pendragon (65) and Tuatha (67) are untouched. No player table is read for writing or written.

    python storm_remake_edits.py <world.db> edits.json
    python ../world_editor/cli.py --db <world.db> --client <app> plan edits.json --out remake.patch.json
"""
import json
from pathlib import Path
import sqlite3
import sys
import uuid

HERE = Path(__file__).resolve().parent
CLASS, CAREER, PACKAGE = 64, "StormforgedCareer", "OfflineDAoC.Stormforged.v1"
LEVELS = (1, 5, 10, 15, 20, 30, 40, 50)
FIRST_STYLE, FIRST_EFFECT, FIRST_SPELL = 7100, 63500, 63600   # StormRemakeStyles / StormRemakeSpells in C#
STYLE_TOOLTIP, SPELL_TOOLTIP = 27500, 27600
WEAPON = {"Hammer": 12, "Axe": 13, "Shields": 42}
POSITION = {"back": 0, "side": 1, "front": 2}
RESULT = {"chain": 7, "parry": 3, "block": 4}

# key: name, unlock, target, settings at level 50 (damage scales by rank), description. Keys match StormRemakeSpells.
SPELLS = {
    0: ("Royal Decree", 15, "Self", dict(Radius=1500, Duration=10, RecastDelay=30, Power=5, ClientEffect=3534),
        "Your party gains 15% movement speed, 10% absorption, 10 accuracy and 10% melee damage for 10 seconds."),
    1: ("Rally to the Crown", 11, "Self", dict(Radius=1500, RecastDelay=30, Power=15, ClientEffect=3517),
        "Restore 25% of your health, 15% of your party's health and 20% endurance to everyone. 30-second reuse."),
    2: ("Runic Armament", 11, "Self", dict(Duration=65535, Power=0, ClientEffect=3537),
        "Maintained: hammers splash lightning, axes open frost wounds, blocks can raise a storm shield; extra magic resistance outside the buff cap."),
    3: ("Runic Reversal", 35, "Self", dict(Duration=8, RecastDelay=45, Power=5, ClientEffect=3536),
        "For 8 seconds, every blow you parry or block returns lightning to the attacker. 45-second reuse."),
    4: ("Hunter's Mark", 25, "Enemy", dict(Range=1500, Duration=30, RecastDelay=30, Power=0, ClientEffect=3538),
        "From stealth: mark an enemy. Your next melee hit on it deals 30% more, and you gain 3 Tempest."),
    5: ("Royal Thunder", 21, "Enemy", dict(Range=1500, Damage=180, RecastDelay=15, Power=12, ClientEffect=3549),
        "Instant lightning strike. Spends Tempest: +20% damage per charge. 15-second reuse."),
    6: ("Stormbreak", 41, "Self", dict(Radius=350, Damage=250, CastTime=2.3, Power=25, ClientEffect=3528),
        "Lightning bursts around you, 350 radius. Spends Tempest: +20% damage per charge."),
}
RETIRED_LINES = (2, 4, 7, 8, 9, 10, 11, 14, 15, 17, 18)   # old royal keys replaced by SPELLS
RETIRED_SPECS = ("Sword", "Spear")
RETIRED_WEAPONRY = ("Weaponry: Swords", "Weaponry: Spears")
DROPPED_RAS = ("AtlasOF_Serenity", "AtlasOF_EtherealBond", "Mastery of Focus", "AtlasOF_MasteryOfConcentration", "Dual Threat",
               "AtlasOF_RagingPower", "AtlasOF_MCL", "AtlasOF_WildPower", "AtlasOF_Lifter", "AtlasOF_VeilRecovery")
NEW_RAS = (
    ("Sovereign_Thunderlord", "Thunderlord", 1904, 3549, "SovereignThunderlord",
     "Weapon style damage +5/10/15%. Three ranks, incremental costs 2/4/6 realm skill points."),
    ("Sovereign_EyeOfTempest", "Eye of Tempest", 1905, 3534, "SovereignEyeOfTempest",
     "Finishers refund 1/1/2 Tempest charges, and charges last 30/45/60 seconds. Three ranks, incremental costs 2/4/6."),
    ("Sovereign_Shieldwall", "Shieldwall of Asgard", 1906, 3536, "SovereignShieldwall",
     "Block chance +3/6/9%, and style storm shields absorb 10/20/30% more. Three ranks, incremental costs 2/4/6."),
)


def edits_for(db):
    db.row_factory = sqlite3.Row
    one = lambda sql, *a: db.execute(sql, a).fetchone()
    rows = lambda sql, *a: db.execute(sql, a).fetchall()
    keys = {t: k for t, k in (("Style", "StyleID"), ("StyleXSpell", "StyleXSpell_ID"), ("Spell", "Spell_ID"),
                              ("SpellLine", "SpellLineID"), ("LineXSpell", "LineXSpell_ID"), ("Ability", "AbilityID"),
                              ("ClassXRealmAbility_Atlas", "ClassXRealmAbility_Atlas_ID"),
                              ("ClassXSpecialization", "ClassXSpecializationID"), ("SpecXAbility", "SpecXabilityID"))}
    counters = {}

    def new_key(table):
        key = keys[table]
        kind = next(r[2] for r in db.execute(f'PRAGMA table_info("{table}")') if r[1] == key).upper()
        if "INT" not in kind:
            return str(uuid.uuid4())
        counters[table] = counters.get(table) or (one(f'SELECT max("{key}") FROM "{table}"')[0] or 0)
        counters[table] += 1
        return counters[table]

    def copy(table, source_key, changes):
        return {"table": table, "copy": source_key, "key": new_key(table), "set": changes}

    out = []
    if one("SELECT 1 FROM Style WHERE ClassId=? AND ID BETWEEN ? AND ?", CLASS, FIRST_STYLE, FIRST_STYLE + 99):
        raise ValueError("The remake is already in this world")
    for lo, hi in ((FIRST_EFFECT, FIRST_EFFECT + 59), (FIRST_SPELL, FIRST_SPELL + 127)):
        if one("SELECT 1 FROM Spell WHERE SpellID BETWEEN ? AND ?", lo, hi):
            raise ValueError(f"Spell IDs {lo}-{hi} are taken")
    if one("SELECT 1 FROM Spell WHERE TooltipId BETWEEN ? AND ?", STYLE_TOOLTIP, SPELL_TOOLTIP + 127):
        raise ValueError("Tooltip IDs taken")

    # --- styles: replace the class's 35 old styles with the 60 new ones ---
    style_src = one("SELECT StyleID FROM Style WHERE ClassId=? ORDER BY ID LIMIT 1", CLASS)[0]
    link_src = one("SELECT StyleXSpell_ID FROM StyleXSpell WHERE ClassID=? LIMIT 1", CLASS)[0]
    spell_src = one("SELECT Spell_ID FROM Spell WHERE SpellID=62626")[0]
    for r in rows("SELECT StyleXSpell_ID FROM StyleXSpell WHERE ClassID=?", CLASS):
        out.append({"table": "StyleXSpell", "delete": r[0]})
    for r in rows("SELECT StyleID FROM Style WHERE ClassId=?", CLASS):
        out.append({"table": "Style", "delete": r[0]})

    styles = json.loads((HERE / "storm_remake_styles.json").read_text())
    ids = {s["name"]: FIRST_STYLE + s["index"] for s in styles}
    icons = {}
    for spec in WEAPON:
        # Stock Midgard icons for the line, lowest level first, so the bar looks like the line's own styles.
        found = [r[0] for r in rows("SELECT DISTINCT Icon FROM Style WHERE SpecKeyName=? AND ClassId IN (21,22,23,24,25,26,31,32,34,43) "
                                    "ORDER BY SpecLevelRequirement", spec)]
        icons[spec] = found or [one("SELECT Icon FROM Style WHERE SpecKeyName=? LIMIT 1", spec)[0]]
    two_hand = {spec: one("SELECT TwoHandAnimation FROM Style WHERE SpecKeyName=? AND ClassId<>? LIMIT 1", spec, CLASS)[0] for spec in WEAPON}
    position = {spec: 0 for spec in WEAPON}
    for s in styles:
        spec, op, index = s["line"], s["open"], s["index"]
        finisher = s["growth"] >= 1.45
        out.append(copy("Style", style_src, dict(
            ID=ids[s["name"]], ClassId=CLASS, Name=s["name"], SpecKeyName=spec, SpecLevelRequirement=s["level"],
            Icon=icons[spec][position[spec] % len(icons[spec])], EnduranceCost=14 if finisher else 8 if op == "anytime" else 10,
            StealthRequirement=int(op == "stealth"),
            OpeningRequirementType=1 if op in ("parry", "block") else 2 if op in POSITION else 0,
            OpeningRequirementValue=ids[s["after"]] if op == "chain" else POSITION.get(op, 0),
            AttackResultRequirement=RESULT.get(op, 0), WeaponTypeRequirement=WEAPON[spec],
            GrowthOffset=0, GrowthRate=s["growth"], BonusToHit=10, BonusToDefense=5,
            TwoHandAnimation=two_hand[spec], RandomProc=0, ArmorHitLocation=0)))
        position[spec] += 1
        effect = FIRST_EFFECT + index
        out.append(copy("Spell", spell_src, dict(
            SpellID=effect, Spell_ID=f"storm-remake-{effect}", Name=s["name"], Description=s["description"],
            Type="StormSovereignAbility", Target="Enemy", Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=10,
            Frequency=0, RecastDelay=0, Value=0, SubSpellID=0, SpellGroup=0, EffectGroup=0, ClientEffect=3541, Icon=3541,
            TooltipId=STYLE_TOOLTIP + index, PackageID=PACKAGE)))
        out.append(copy("StyleXSpell", link_src, dict(SpellID=effect, ClassID=CLASS, StyleID=ids[s["name"]], Chance=100)))

    # --- spells: retire the merged lines, add the remake lines ---
    for key in RETIRED_LINES:
        line = f"Stormforged.Sovereign{key:02}"
        for r in rows("SELECT LineXSpell_ID FROM LineXSpell WHERE LineName=?", line):
            out.append({"table": "LineXSpell", "delete": r[0]})
        for r in rows("SELECT SpellLineID FROM SpellLine WHERE KeyName=?", line):
            out.append({"table": "SpellLine", "delete": r[0]})
    line_src = one("SELECT SpellLineID FROM SpellLine WHERE KeyName='Stormforged.Sovereign00'")[0]
    lxs_src = one("SELECT LineXSpell_ID FROM LineXSpell WHERE LineName='Stormforged.Sovereign00' LIMIT 1")[0]
    for key, (name, unlock, target, settings, description) in SPELLS.items():
        line = f"Stormforged.Remake{key:02}"
        out.append(copy("SpellLine", line_src, dict(KeyName=line, Name=name, Spec=CAREER, IsBaseLine=1, ClassIDHint=CLASS)))
        for level in sorted({unlock, *[n for n in LEVELS if n >= unlock]}):
            tier = max(i for i, n in enumerate(LEVELS) if n <= level)
            spell_id = FIRST_SPELL + tier * 16 + key
            fields = dict(Range=0, Radius=0, Power=0, CastTime=0, Damage=0, Duration=0, Frequency=0, RecastDelay=0, Value=0,
                          SubSpellID=0, EffectGroup=0, Pulse=0, PulsePower=0, Concentration=0)
            fields.update(settings)
            if fields["Damage"] > 0:
                fields["Damage"] = max(5, int(fields["Damage"] * level / 50))
            if fields["Power"] > 0:
                fields["Power"] = max(1, int(fields["Power"] * max(.2, level / 50)))
            fields.update(SpellID=spell_id, Spell_ID=f"storm-remake-{spell_id}", Name=name, Description=description,
                          Type="StormSovereignAbility", Target=target, SpellGroup=FIRST_SPELL + key,
                          Icon=settings["ClientEffect"], TooltipId=SPELL_TOOLTIP + spell_id - FIRST_SPELL, PackageID=PACKAGE)
            out.append(copy("Spell", spell_src, fields))
            out.append(copy("LineXSpell", lxs_src, dict(LineName=line, SpellID=spell_id, Level=level)))

    # --- weapons: Hammer, Axe and Shields only ---
    for spec in RETIRED_SPECS:
        for r in rows("SELECT ClassXSpecializationID FROM ClassXSpecialization WHERE ClassID=? AND SpecKeyName=?", CLASS, spec):
            out.append({"table": "ClassXSpecialization", "delete": r[0]})
    for ability in RETIRED_WEAPONRY:
        for r in rows("SELECT SpecXabilityID FROM SpecXAbility WHERE Spec=? AND ClassId=? AND AbilityKey=?", CAREER, CLASS, ability):
            out.append({"table": "SpecXAbility", "delete": r[0]})

    # --- realm abilities: drop the caster picks, add three Sovereign ones ---
    for key in DROPPED_RAS:
        for r in rows("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? AND AbilityKey=?", CLASS, key):
            out.append({"table": "ClassXRealmAbility_Atlas", "delete": r[0]})
    ability_src = one("SELECT AbilityID FROM Ability WHERE KeyName='Sovereign_RoyalFortitude'")[0]
    binding_src = one("SELECT ClassXRealmAbility_Atlas_ID FROM ClassXRealmAbility_Atlas WHERE CharClass=? LIMIT 1", CLASS)[0]
    for key, name, internal, icon, implementation, description in NEW_RAS:
        if one("SELECT 1 FROM Ability WHERE KeyName=? OR InternalID=?", key, internal):
            raise ValueError(f"Realm ability {key} or InternalID {internal} exists")
        out.append(copy("Ability", ability_src, dict(KeyName=key, Name=name, Description=description, InternalID=internal,
                                                     IconID=icon, Implementation="DOL.GS.RealmAbilities." + implementation)))
        out.append(copy("ClassXRealmAbility_Atlas", binding_src, dict(CharClass=CLASS, AbilityKey=key)))
    return out


def main():
    db_path, out_path = Path(sys.argv[1]), Path(sys.argv[2])
    with sqlite3.connect(db_path.resolve().as_uri() + "?mode=ro", uri=True) as db:
        edits = edits_for(db)
    out_path.write_text(json.dumps(edits, indent=1))
    counts = {}
    for e in edits:
        op = "delete" if "delete" in e else "insert"
        counts[(e["table"], op)] = counts.get((e["table"], op), 0) + 1
    for (table, op), n in sorted(counts.items()):
        print(f"{op:7} {n:4}  {table}")


if __name__ == "__main__":
    main()
