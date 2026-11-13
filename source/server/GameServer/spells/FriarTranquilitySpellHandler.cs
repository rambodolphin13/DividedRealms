using System;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Friar Tranquility (1.121, 1.127): for Duration seconds, every Frequency, each other group member within Radius
    /// regains Value% of their health, power and endurance and takes less damage (the SubSpellID DamageReduction effect,
    /// refreshed each pulse). The Friar must be in a group and is not affected.
    /// </summary>
    [SpellHandler(eSpellType.FriarTranquility)]
    public class FriarTranquilitySpellHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        private const string PROPERTY = "FRIAR_TRANQUILITY_TIMER";

        public override string ShortDescription
        {
            get
            {
                Spell reduction = SkillBase.GetSpellByID(Spell.SubSpellID);
                string less = reduction == null ? string.Empty : $" take {reduction.Value}% less damage and";
                return $"For {Spell.Duration / 1000} seconds your group within {Spell.Radius} units (not you){less} regain {Spell.Value}% " +
                    $"health, power and endurance every {Math.Max(Spell.Frequency, 1000) / 1000.0:0.#} seconds. You must be in a group.";
            }
        }

        public override bool CheckBeginCast(GameLiving selectedTarget)
        {
            if (Caster.Group == null || Caster.Group.MemberCount < 2)
            {
                MessageToCaster("You must be in a group to cast this spell.", eChatType.CT_SpellResisted);
                return false;
            }

            return base.CheckBeginCast(selectedTarget);
        }

        public override bool StartSpell(GameLiving target)
        {
            if (Caster.Group == null || Spell.Duration <= 0)
                return false;

            Spell reduction = SkillBase.GetSpellByID(Spell.SubSpellID);
            int every = Spell.Frequency > 0 ? Spell.Frequency : 1000;
            long ends = GameLoop.GameLoopTime + Spell.Duration;
            ushort region = Caster.CurrentRegionID;

            Caster.TempProperties.GetProperty<ECSGameTimer>(PROPERTY)?.Stop();
            ECSGameTimer timer = new(Caster, _ =>
            {
                if (!Caster.IsAlive || Caster.ObjectState != GameObject.eObjectState.Active || Caster.CurrentRegionID != region ||
                    Caster.Group == null || GameLoop.GameLoopTime > ends)
                {
                    Caster.TempProperties.RemoveProperty(PROPERTY);
                    return 0;
                }

                Pulse(reduction);
                return every;
            });
            Caster.TempProperties.SetProperty(PROPERTY, timer);
            timer.Start(1);
            return true;
        }

        private void Pulse(Spell reduction)
        {
            foreach (GameLiving member in Caster.Group.GetMembersInTheGroup())
            {
                if (member == null || member == Caster || !member.IsAlive || member.CurrentRegionID != Caster.CurrentRegionID ||
                    !Caster.IsWithinRadius(member, Spell.Radius))
                    continue;

                double percent = Spell.Value / 100.0;
                member.Health = Math.Min(member.MaxHealth, member.Health + (int) (member.MaxHealth * percent));
                member.Mana = Math.Min(member.MaxMana, member.Mana + (int) (member.MaxMana * percent));
                member.Endurance = Math.Min(member.MaxEndurance, member.Endurance + (int) (member.MaxEndurance * percent));

                if (reduction != null)
                    (ScriptMgr.CreateSpellHandler(Caster, reduction, SpellLine) as SpellHandler)?.ApplyEffectOnTarget(member);
            }
        }

        public override void SendSpellMessages()
        {
            MessageToCaster($"You call {Spell.Name}!", eChatType.CT_Spell);
        }
    }
}
