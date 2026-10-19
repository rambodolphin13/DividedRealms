using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>All custom-class exceptions live behind class 64; stock Thanes are unchanged.</summary>
    public static class StormforgedRules
    {
        public static bool IsStormforged(int classId) => classId == (int)eCharacterClass.Stormforged;

        public static int CreationClass(int classId, int realm, bool enabled) =>
            enabled && realm == (int)eRealm.Midgard && classId == (int)eCharacterClass.MaulerMid
                ? (int)eCharacterClass.Stormforged : classId;

        public static byte OverviewClass(int classId, int realm) =>
            IsStormforged(classId) && realm == (int)eRealm.Midgard
                ? (byte)eCharacterClass.MaulerMid : (byte)classId;

        public static bool HasStormsong(GameLiving living) =>
            living.effectListComponent.ContainsEffectForEffectType(eEffect.StormforgedChant);

        public static int RestorePerHit(int damage, int percent) =>
            (int)Math.Min(int.MaxValue, Math.Max(0L, damage) * Math.Clamp(percent, 0, 1000) / 100);

        public static IEnumerable<Spell> VisibleSpellRanks(string lineKey, IEnumerable<Spell> spells)
        {
            var ordered = spells.OrderBy(spell => spell.Level).ThenBy(spell => spell.ID);
            return lineKey.StartsWith("Stormforged.", StringComparison.Ordinal) ? ordered.TakeLast(2) : ordered;
        }

        public static bool IsRogSkill(eProperty property) => property is
            eProperty.Skill_Sword or eProperty.Skill_Hammer or eProperty.Skill_Axe or
            eProperty.Skill_Shields or eProperty.Skill_Parry or eProperty.Skill_Stealth or
            eProperty.AllMeleeWeaponSkills or eProperty.AllSkills;

        public static double BuffProgress(double percentage) => Math.Clamp(percentage / 100.0, 0, 1);
    }
}
