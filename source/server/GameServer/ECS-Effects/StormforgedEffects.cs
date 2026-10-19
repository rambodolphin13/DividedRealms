using System;
using System.Collections.Generic;
using DOL.GS.Spells;

namespace DOL.GS
{
    public class StormforgedAuraEffect : ECSGameSpellEffect
    {
        public StormforgedAuraEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public override void OnEffectPulse()
        {
            if (!Owner.IsAlive || Owner.ObjectState != GameObject.eObjectState.Active)
            {
                End();
                return;
            }
            if (Owner.IsCrowdControlled)
                return;
            Spell strike = SkillBase.GetSpellByID(SpellHandler.Spell.SubSpellID);
            if (strike != null)
                new StormforgedStrike(Owner, strike, SpellHandler.SpellLine).StartSpell(Owner);
        }
    }

    public class StormforgedChantEffect : ECSGameSpellEffect
    {
        private long _supportTick;
        private readonly List<StormforgedChantEffect> _children = new();

        public StormforgedChantEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            if (Owner == SpellHandler.Caster)
            {
                Duration = 0;
                ExpireTick = long.MaxValue;
            }
        }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            Owner.BuffBonusMultCategory1.Set((int)eProperty.MaxSpeed, this, SpellHandler.Spell.Value / 100.0);
            Owner.OnMaxSpeedChange();
        }

        public override void OnStopEffect()
        {
            Owner.BuffBonusMultCategory1.Remove((int)eProperty.MaxSpeed, this);
            Owner.OnMaxSpeedChange();
            StormforgedEffectSupport.EndSubSpell(SpellHandler.Caster, Owner, SpellHandler.Spell.SubSpellID);
            foreach (StormforgedChantEffect child in _children)
                child.End();
            _children.Clear();
            base.OnStopEffect();
        }

        public override void OnEffectPulse()
        {
            GameLiving singer = SpellHandler.Caster;
            if (!singer.IsAlive || singer.ObjectState != GameObject.eObjectState.Active || singer.CurrentRegion != Owner.CurrentRegion)
            {
                End();
                return;
            }
            if (Owner != singer && (!singer.IsWithinRadius(Owner, SpellHandler.Spell.Radius) ||
                singer is not GamePlayer playerSinger || playerSinger.Group == null ||
                !playerSinger.Group.GetMembersInTheGroup().Contains(Owner)))
            {
                End();
                return;
            }
            if (singer.IsCrowdControlled || !singer.IsWithinRadius(Owner, SpellHandler.Spell.Radius))
                return;

            int amount = Math.Max(1, (int)SpellHandler.Spell.Damage);
            Owner.ChangeHealth(singer, eHealthChangeType.Spell, amount);
            Owner.ChangeMana(singer, eManaChangeType.Spell, amount);
            Owner.ChangeEndurance(singer, eEnduranceChangeType.Spell, amount);
            StormforgedEffectSupport.ApplySubSpell(singer, Owner, SpellHandler.Spell.SubSpellID, SpellHandler.SpellLine);

            // Only the singer refreshes group members; children never create more singers.
            if (Owner != singer || GameLoop.GameLoopTime < _supportTick)
                return;
            _supportTick = GameLoop.GameLoopTime + SpellHandler.Spell.Frequency;
            _children.RemoveAll(child => child.IsEnded);
            if (singer is GamePlayer player && player.Group != null)
            {
                foreach (GameLiving member in player.Group.GetMembersInTheGroup())
                {
                    if (member != singer && member.IsAlive && member.CurrentRegion == singer.CurrentRegion && singer.IsWithinRadius(member, SpellHandler.Spell.Radius))
                    {
                        StormforgedChantEffect current = _children.Find(child => child.Owner == member && !child.IsEnding);
                        if (current != null)
                            current.ExpireTick = GameLoop.GameLoopTime + SpellHandler.Spell.Frequency * 2;
                        else
                            _children.Add(ECSGameEffectFactory.Create(new(member, SpellHandler.Spell.Frequency * 2, 1, SpellHandler), static (in i) => new StormforgedChantEffect(i)));
                    }
                }
            }
        }
    }

    public class StormforgedWardEffect : ECSGameSpellEffect
    {
        private int _baseStats;
        private int _specStats;
        private int _armor;
        private int _resists;
        private static readonly eProperty[] Stats = { eProperty.Strength, eProperty.Dexterity, eProperty.Constitution,
            eProperty.Quickness, eProperty.Intelligence, eProperty.Piety, eProperty.Empathy, eProperty.Charisma };
        private static readonly eProperty[] Resists = { eProperty.Resist_Body, eProperty.Resist_Cold, eProperty.Resist_Crush,
            eProperty.Resist_Energy, eProperty.Resist_Heat, eProperty.Resist_Matter, eProperty.Resist_Slash,
            eProperty.Resist_Spirit, eProperty.Resist_Thrust };

        public StormforgedWardEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            // Scale once with actual character level. The old rank-percentage multiplier
            // multiplied by level again, flooring early buffs to zero and weakening old ranks.
            _baseStats = Math.Max(1, (int)(Owner.Level * 1.25));
            _specStats = Math.Max(1, (int)(Owner.Level * 1.875));
            _armor = Math.Max(1, Owner.Level * 2);
            _resists = Math.Max(1, (int)Math.Ceiling(26 * Math.Min(40, (int)Owner.Level) / 40.0));
            ChangeBonuses(1);
            StormforgedEffectSupport.ApplySubSpell(Owner, Owner, SpellHandler.Spell.SubSpellID, SpellHandler.SpellLine);
            StormforgedEffectSupport.ApplySubSpell(Owner, Owner, SpellHandler.Spell.ID + 8, SpellHandler.SpellLine);
        }

        public override void OnStopEffect()
        {
            ChangeBonuses(-1);
            StormforgedEffectSupport.EndSubSpell(Owner, Owner, SpellHandler.Spell.SubSpellID);
            StormforgedEffectSupport.EndSubSpell(Owner, Owner, SpellHandler.Spell.ID + 8);
            base.OnStopEffect();
        }

        private void ChangeBonuses(int sign)
        {
            foreach (eProperty stat in Stats)
            {
                Owner.BaseBuffBonusCategory[stat] += sign * _baseStats;
                Owner.SpecBuffBonusCategory[stat] += sign * _specStats;
            }
            Owner.SpecBuffBonusCategory[eProperty.ArmorFactor] += sign * _armor;
            foreach (eProperty resist in Resists)
                Owner.BaseBuffBonusCategory[resist] += sign * _resists;
            if (Owner is GamePlayer player)
            {
                player.Out.SendCharStatsUpdate();
                player.UpdatePlayerStatus();
            }
        }
    }

    internal static class StormforgedEffectSupport
    {
        internal static void ApplySubSpell(GameLiving caster, GameLiving target, int id, SpellLine line)
        {
            Spell spell = SkillBase.GetSpellByID(id);
            if (spell == null)
                return;
            // Refresh the existing child silently, without reapplying its animation or bonuses.
            foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects(EffectHelper.GetEffectFromSpell(spell)))
            {
                if (!effect.IsEnded && !effect.IsEnding && effect.SpellHandler.Caster == caster && effect.SpellHandler.Spell.ID == id)
                {
                    effect.ExpireTick = GameLoop.GameLoopTime + spell.Duration;
                    return;
                }
            }
            // Ward (or another group member) may already supply a stronger damage add.
            // Do not recast a weaker internal child on every regeneration pulse.
            if (spell.SpellType == eSpellType.DamageAdd)
            {
                foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects(eEffect.DamageAdd))
                {
                    if (CoversDamageAdd(effect, spell))
                        return;
                }
            }
            ScriptMgr.CreateSpellHandler(caster, spell, line)?.StartSpell(target);
        }

        internal static bool CoversDamageAdd(ECSGameSpellEffect existing, Spell candidate)
        {
            return existing.IsActive && !existing.IsEnding &&
                existing.SpellHandler.Spell.SpellType == eSpellType.DamageAdd &&
                existing.SpellHandler.Spell.Damage >= candidate.Damage;
        }

        internal static void EndSubSpell(GameLiving caster, GameLiving target, int id)
        {
            foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects())
            {
                if (effect.SpellHandler.Caster == caster && effect.SpellHandler.Spell.ID == id)
                    effect.End();
            }
        }
    }
}
