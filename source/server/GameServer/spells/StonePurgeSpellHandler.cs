using System.Collections.Generic;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Warden Stone Breaker proc (live 1.130a, DR-328): removes any ABS debuff from the target and any
    /// Weaponskill buff from the caster. Stone Breaker follows Stone Shaper (+25% weaponskill self proc)
    /// and Stone Temper (ABS debuff), so it trades both for its own hit.
    /// </summary>
    [SpellHandler(eSpellType.StonePurge)]
    public class StonePurgeSpellHandler : SpellHandler
    {
        public StonePurgeSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override bool HasPositiveEffect => false;

        public override void FinishSpellCast(GameLiving target)
        {
            m_caster.Mana -= PowerCost(target);
            base.FinishSpellCast(target);
        }

        public override void OnDirectEffect(GameLiving target)
        {
            base.OnDirectEffect(target);

            if (target != null && target.IsAlive)
                EndMatching(target, eSpellType.ArmorAbsorptionDebuff);

            if (Caster != null && Caster.IsAlive)
                EndMatching(Caster, eSpellType.WeaponSkillBuff);

            if (target != null)
                SendEffectAnimation(target, 0, false, 1);
        }

        private static void EndMatching(GameLiving living, eSpellType type)
        {
            List<ECSGameEffect> toEnd = new();

            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects())
            {
                if (effect.SpellHandler?.Spell?.SpellType == type)
                    toEnd.Add(effect);
            }

            foreach (ECSGameEffect effect in toEnd)
                effect.End();
        }
    }
}
