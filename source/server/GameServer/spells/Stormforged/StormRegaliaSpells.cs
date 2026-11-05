using System.Collections.Generic;

namespace DOL.GS.Spells
{
    /// <summary>
    /// The Storm Regalia's item abilities (spell IDs 63740-63743, type StormSovereignAbility): two use abilities and two
    /// armor procs. Armor procs start on the attacker, so the ward targets the wearer itself.
    /// </summary>
    public static class StormRegaliaSpells
    {
        public const int CrownOfTempest = 63740;   // helm, use: fill Tempest
        public const int Wolfmantle = 63741;       // cloak, use: absorption and haste
        public const int StormguardWard = 63742;   // hauberk, struck: storm shield on the wearer
        public const int ThunderRebuke = 63743;    // sleeves, struck: lightning at the attacker

        public static bool Owns(int id) => id is >= CrownOfTempest and <= ThunderRebuke;

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            yield return (CrownOfTempest, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => { StormTempest.Set(h.Caster, StormTempest.MaxCharges); h.Animate(h.Caster); }
            });
            yield return (Wolfmantle, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Mods = new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20), MLMod.Buff(eProperty.MeleeSpeed, 20) },
                Run = (h, _) => { h.Hold(h.Caster); h.Animate(h.Caster); }
            });
            yield return (StormguardWard, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, KeepsStealth = true, Describe = s => s.Description,
                Run = (h, _) => h.Cast(h.Caster, 1, eSpellType.BothAblativeArmor, db =>
                {
                    db.Target = "Self"; db.Duration = 10; db.Damage = 50; db.Value = 200;
                })
            });
            yield return (ThunderRebuke, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Describe = s => s.Description,
                Run = (h, t) => StormSovereignAbilities.Strike(h, t, 150)
            });
        }
    }
}
