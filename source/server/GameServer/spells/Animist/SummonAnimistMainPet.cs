using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Spell handler to summon a animist pet.
    /// </summary>
    /// <author>IST</author>
    [SpellHandler(eSpellType.SummonAnimistPet)]
    public class SummonAnimistMainPet : SummonAnimistPet
    {
        /// <summary>Spell.DamageType value that marks a movable main pet (0 = caster turret, 1 = melee turret).</summary>
        public const int MobilePetDamageType = 2;

        public SummonAnimistMainPet(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        /// <summary>
        /// The level 40 pets of each Animist line (Guardian of the Forest, Heart of the Grove, Harbinger of War,
        /// patch 1.111): normal pets that walk, follow the Animist and are not cast on a ground target. They share
        /// the one controllable pet slot with the main turrets.
        /// </summary>
        public static bool IsMobilePet(Spell spell) =>
            spell != null && spell.SpellType == eSpellType.SummonAnimistPet && (int) spell.DamageType == MobilePetDamageType;

        protected override bool UsesGroundTarget => !IsMobilePet(Spell);

        public override bool CheckEndCast(GameLiving selectedTarget)
        {
            if (Caster is GamePlayer && Caster.ControlledBrain != null)
            {
                MessageToCaster(LanguageMgr.GetTranslation((Caster as GamePlayer).Client, "SummonAnimistPet.CheckBeginCast.AlreadyHaveaPet"), eChatType.CT_SpellResisted);
                return false;
            }

            return base.CheckEndCast(selectedTarget);
        }

        protected override GameSummonedPet GetGamePet(INpcTemplate template)
        {
            if (IsMobilePet(Spell))
                return Caster.CreateGamePet(template);

            if (Spell.DamageType == 0)
                return new TurretMainPetCaster(template);

            if (Spell.DamageType == (eDamageType) 1)
                return new TurretMainPetTank(template);

            return base.GetGamePet(template);
        }

        protected override IControlledBrain GetPetBrain(GameLiving owner)
        {
            if (IsMobilePet(Spell))
                return new ControlledMobBrain(owner);

            if (Spell.DamageType == 0)
                return new TurretMainPetCasterBrain(owner);

            if (Spell.DamageType == (eDamageType) 1)
                return new TurretMainPetTankBrain(owner);

            return base.GetPetBrain(owner);
        }
    }
}
