using System;
using System.Collections.Generic;

namespace DOL.GS
{
    public static class BotCasterPriority
    {
        // A Bonedancer command orders the commander to cast an attack, so it counts as damage.
        public static bool IsDamage(Spell spell) => spell != null &&
            (spell.Damage > 0 || spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
                eSpellType.Bolt or eSpellType.DirectDamageWithDebuff or eSpellType.DamageSpeedDecrease or
                eSpellType.CommanderCommand);

        public static bool AllowInstant(Spell spell, bool rangedCaster, bool meleePressure) =>
            !rangedCaster || IsDamage(spell) || meleePressure;

        /// <summary>Damage after the target's resist. Pure.</summary>
        public static double AfterResist(double damage, int resistPercent) =>
            damage * (1 - Math.Clamp(resistPercent, -100, 100) / 100d);

        /// <summary>What a damage spell does in all: one hit, or every tick of a damage over time. Pure.</summary>
        public static double TotalDamage(double damage, int durationMilliseconds, int frequencyMilliseconds) =>
            durationMilliseconds > 0 && frequencyMilliseconds > 0
                ? damage * Math.Max(1, durationMilliseconds / frequencyMilliseconds)
                : damage;

        /// <summary>
        /// Owner rule 2026-11-15: the damage spell that hits hardest after the target's resists; then the cheaper one, then
        /// the quicker one. Null when none of them is a damage spell with a listed damage. A level 50 companion used to cast
        /// its highest level spell: a fire Wizard threw Ice Blast (179 cold) at a trial boss instead of Major Conflagration
        /// (219 heat, less power), DR-394.
        /// </summary>
        public static Spell HardestHitting(IEnumerable<Spell> spells, Func<eDamageType, int> resistPercent, Func<Spell, int> powerCost)
        {
            Spell best = null;
            double bestDamage = 0;
            int bestCost = 0;
            foreach (Spell spell in spells)
            {
                if (spell == null || !IsDamage(spell) || spell.Damage <= 0)
                    continue;
                double damage = AfterResist(TotalDamage(spell.Damage, spell.Duration, spell.Frequency), resistPercent(spell.DamageType));
                int cost = powerCost(spell);
                if (best == null || damage > bestDamage || damage == bestDamage &&
                    (cost < bestCost || cost == bestCost && spell.CastTime < best.CastTime))
                {
                    best = spell;
                    bestDamage = damage;
                    bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>
        /// DR-394: a player's ranged caster that cannot pay for any damage spell keeps its power for the next one instead of
        /// spending it on a debuff for a monster (two Wizards cast Nearsight on a melee trial boss nine times each in seven
        /// minutes while out of power). Crowd control on adds goes another way and is not held. Pure.
        /// </summary>
        public static bool SavesPowerForNuke(bool rangedCaster, bool playerLed, bool targetIsMonster, bool outOfPowerForDamage) =>
            rangedCaster && playerLed && targetIsMonster && outOfPowerForDamage;
    }
}
