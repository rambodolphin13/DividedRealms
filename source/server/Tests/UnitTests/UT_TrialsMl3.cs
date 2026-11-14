using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Trials;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrialsMl3
    {
        private static readonly string[] Names =
        {
            "Moirai's Destruction", "Kepa's Downfall", "Casta's Annihilation", "Laodamedia's Furious End", "Antioos' Deceptive Death",
            "Glowing Barracuda's Fading", "Shraogh's Fear Removed", "Sting Ray's Power Absorbed", "Sinovia's Power Relinquished", "Conquering Twilight",
        };

        private static readonly TrialScope[] Scopes =
        {
            TrialScope.Group, TrialScope.Raid, TrialScope.Raid, TrialScope.Raid, TrialScope.Group,
            TrialScope.Group, TrialScope.Group, TrialScope.Group, TrialScope.Group, TrialScope.Raid,
        };

        [Test]
        public void EveryStepExistsInOrder()
        {
            Assert.That(Ml3.Trials().Select(trial => trial.Step), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(Ml3.Trials().Select(trial => trial.Name), Is.EqualTo(Names));
            Assert.That(Ml3.Trials().Select(trial => trial.ML).Distinct(), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void ScopesMatchTheWindowAndReports()
        {
            Assert.That(Ml3.Trials().Select(trial => trial.Scope), Is.EqualTo(Scopes));
        }

        [Test]
        public void Finale_NeedsAllNineSteps()
        {
            TrialDefinition finale = Ml3.Trials().Last();
            Assert.That(finale.RequiresSteps, Is.Null);
            Assert.That(TrialManager.Required(finale), Is.EqualTo(Enumerable.Range(1, 9)));
        }

        [Test]
        public void EverythingButShraoghIsInTheTemple()
        {
            foreach (TrialDefinition trial in Ml3.Trials())
            {
                Assert.That(trial.Zone, Is.EqualTo(trial.Step == 7 ? TrialZone.Oceanus : TrialZone.Twilight), trial.Key);
                if (trial.Zone == TrialZone.Twilight)
                {
                    Assert.That(trial.Anchor.X, Is.InRange(8192, 8192 + 65536), trial.Key);
                    Assert.That(trial.Anchor.Y, Is.InRange(8192, 8192 + 65536), trial.Key);
                }
            }
        }

        [Test]
        public void TimeLimitsAreSane()
        {
            Assert.That(Ml3.Trials().Select(trial => trial.TimeLimitSeconds), Is.All.InRange(900, 2400));
        }

        [Test]
        public void Replaces_NeverNamesCommonCreatures()
        {
            string[] common = { "cobra", "lethal cobra", "pregnant cobra", "gorgon guardian", "water crocodile", "entranced cobra", "cobra hatchling", "clinging octopus", "darid gremna eel" };
            Assert.That(Ml3.Trials().SelectMany(trial => trial.Replaces).Intersect(common), Is.Empty);
        }

        [Test]
        public void RageMeter_FiresOnAStreakOfOneDamageType()
        {
            RageMeter meter = new(3, 30);
            Assert.That(meter.Hit(eDamageType.Slash), Is.False);
            Assert.That(meter.Hit(eDamageType.Slash), Is.False);
            Assert.That(meter.Hit(eDamageType.Slash), Is.True);
        }

        [Test]
        public void RageMeter_MixedDamageTypesKeepHerCalm()
        {
            RageMeter meter = new(3, 30);
            foreach (eDamageType type in new[] { eDamageType.Slash, eDamageType.Crush, eDamageType.Thrust, eDamageType.Slash, eDamageType.Slash, eDamageType.Crush })
                Assert.That(meter.Hit(type), Is.False);
        }

        [Test]
        public void RageMeter_CalmsBeforeRagingAgain()
        {
            RageMeter meter = new(2, 2);
            meter.Hit(eDamageType.Slash);
            Assert.That(meter.Hit(eDamageType.Slash), Is.True);
            Assert.That(meter.Hit(eDamageType.Slash), Is.False);
            Assert.That(meter.Hit(eDamageType.Slash), Is.False);
            meter.Second();
            meter.Second();
            Assert.That(meter.Hit(eDamageType.Slash), Is.True);
        }

        [Test]
        public void InRing_LeavesTheCentreOfThePlatformSafe()
        {
            Assert.That(Ml34Kit.InRing(100, 250, 1100), Is.False);
            Assert.That(Ml34Kit.InRing(600, 250, 1100), Is.True);
            Assert.That(Ml34Kit.InRing(1500, 250, 1100), Is.False);
        }

        [Test]
        public void Scaled_ShrinksWavesButKeepsAMinimum()
        {
            Assert.That(Ml34Kit.Scaled(12, 1.0), Is.EqualTo(12));
            Assert.That(Ml34Kit.Scaled(12, 0.5), Is.EqualTo(6));
            Assert.That(Ml34Kit.Scaled(12, 0.25), Is.EqualTo(3));
            Assert.That(Ml34Kit.Scaled(3, 0.25), Is.EqualTo(2));
            Assert.That(Ml34Kit.Scaled(1, 0.25), Is.EqualTo(1));
        }

        [Test]
        public void On_PlacesPointsAroundTheCentre()
        {
            TrialPoint center = new(1000, 1000, 50);
            TrialPoint east = Ml34Kit.On(center, 100, 0, 4);
            Assert.That((east.X, east.Y, east.Z), Is.EqualTo((1100, 1000, 50)));
            Assert.That(Ml34Kit.Distance(center, Ml34Kit.On(center, 100, 1, 4)), Is.EqualTo(100).Within(2));
        }

        private sealed class HelperPlayer : GamePlayer
        {
            private HelperPlayer() : base(null, null) { }
            public int Ml;
            public int[] Done = System.Array.Empty<int>();
            public override int MLLevel { get => Ml; set => Ml = value; }
            public override bool HasFinishedMLStep(int mlLevel, int step) => mlLevel <= Ml || mlLevel == 3 && Done.Contains(step);
        }

        private static TrialDefinition Step(int step) => Ml3.Trials().Single(trial => trial.Step == step);

        [Test]
        public void Finale_GivesNoCreditToAHelperShortOfTheNineSteps()
        {
            using EpicTestServerScope server = new();
            HelperPlayer helper = (HelperPlayer)RuntimeHelpers.GetUninitializedObject(typeof(HelperPlayer));
            helper.Ml = 2;
            helper.Done = new[] { 1, 2, 3, 4, 5 };
            Assert.That(TrialManager.MissingSteps(helper, Step(10)), Is.EqualTo(new[] { 6, 7, 8, 9 }));
            Assert.That(TrialManager.MissingSteps(helper, Step(6)), Is.Empty, "steps 1-9 need nothing first");

            helper.Done = Enumerable.Range(1, 9).ToArray();
            Assert.That(TrialManager.MissingSteps(helper, Step(10)), Is.Empty);

            helper.Ml = 3;
            helper.Done = System.Array.Empty<int>();
            Assert.That(TrialManager.MissingSteps(helper, Step(10)), Is.Empty, "a player past ML3 may help");
        }

        [Test]
        public void KepaAndAntioos_FightOnTheSpotAsAfterPatch175()
        {
            foreach (TrialDefinition trial in new[] { Step(2), Step(5) })
            {
                Assert.That(trial.Phases[0].SurviveSeconds, Is.InRange(15, 60), $"{trial.Key} warns before she attacks");
                Assert.That(trial.Phases[0].Mobs, Is.Empty, trial.Key);
                Assert.That(trial.Phases.SelectMany(phase => phase.Mobs).Any(mob => mob.Objective), Is.False, $"{trial.Key} has no fetch items");
                Assert.That(trial.Phases.SelectMany(phase => phase.Mobs).Select(mob => mob.Name), Has.None.EqualTo("Moughart"), trial.Key);
            }
        }

        [Test]
        public void Antioos_CanBeHurtWhileHerCobraLives()
        {
            TrialMobSpec antioos = Step(5).Phases.SelectMany(phase => phase.Mobs).Single(mob => mob.Name == "Antioos");
            TrialMobSpec cobra = Step(5).Phases.SelectMany(phase => phase.Mobs).Single(mob => mob.Name == "giant cobra");
            Assert.That(antioos.Immune, Is.False);
            Assert.That(cobra.Required, Is.False, "credit comes with Antioos' death even if the cobra lives");
        }

        [Test]
        public void Shraogh_IsNeverFullyImmune()
        {
            TrialMobSpec shraogh = Step(7).Phases.SelectMany(phase => phase.Mobs).Single(mob => mob.Name == "Shraogh");
            Assert.That(shraogh.Immune, Is.False);
        }

        [Test]
        public void MoiraiShield_TakesSixDrinksForAnyParty()
        {
            Assert.That(Ml3.BarrierCharges, Is.EqualTo(6));
        }

        [Test]
        public void MoiraiMinions_KeepEnoughHealthToBeDrained()
        {
            Assert.That(Ml3.MinionHealth(5000, 1.0), Is.EqualTo(5000));
            Assert.That(TrialScaling.Health(Ml3.MinionHealth(5000, TrialScaling.MinimumScale), TrialScaling.MinimumScale), Is.GreaterThanOrEqualTo(Ml3.MinionFloor));
            Assert.That(TrialScaling.Health(Ml3.MinionHealth(5000, 0.25), 0.25), Is.GreaterThanOrEqualTo(Ml3.MinionFloor));
        }

        [Test]
        public void Laodameia_RagesOnThreeBlowsOfOneType()
        {
            RageMeter meter = new(Ml3.RageStreak, Ml3.RageCalmSeconds);
            Assert.That(meter.Hit(eDamageType.Crush), Is.False);
            Assert.That(meter.Hit(eDamageType.Crush), Is.False);
            Assert.That(meter.Hit(eDamageType.Crush), Is.True);
        }

        [Test]
        public void CastaNecklace_TurnsArrowsUntilChippedTwiceByCrushOrSlash()
        {
            CastaJewels jewels = new(0.5);
            Assert.That(jewels.Blow(eDamageType.Thrust, true, 100, 0.0, out _, out _), Is.EqualTo(100), "arrows are turned");
            jewels.Blow(eDamageType.Thrust, false, 100, 0.0, out bool chipped, out _);
            Assert.That(chipped, Is.False, "thrust never chips the necklace");
            jewels.Blow(eDamageType.Crush, false, 100, 0.9, out chipped, out _);
            Assert.That(chipped, Is.False, "a failed roll does not chip");
            jewels.Blow(eDamageType.Crush, false, 100, 0.1, out chipped, out _);
            Assert.That(chipped && jewels.NecklaceWhole, Is.True);
            jewels.Blow(eDamageType.Slash, false, 100, 0.1, out chipped, out _);
            Assert.That(chipped && !jewels.NecklaceWhole, Is.True);
            for (int i = 0; i < CastaJewels.BraceletSeconds; i++)
                jewels.Second();
            Assert.That(jewels.Blow(eDamageType.Thrust, true, 100, 0.0, out _, out _), Is.Zero, "a shattered necklace lets arrows through");
        }

        [Test]
        public void CastaBracelets_HardenAgainstTheLastDamageTypeForSevenSeconds()
        {
            CastaJewels jewels = new(0.0);
            Assert.That(jewels.Blow(eDamageType.Slash, false, 100, 1.0, out _, out bool hardened), Is.Zero);
            Assert.That(hardened && jewels.Hardened == eDamageType.Slash, Is.True);
            Assert.That(jewels.Blow(eDamageType.Slash, false, 100, 1.0, out _, out _), Is.EqualTo(75));
            Assert.That(jewels.Blow(eDamageType.Crush, false, 100, 1.0, out _, out _), Is.Zero, "another type gets through");
            for (int i = 0; i < CastaJewels.BraceletSeconds; i++)
                jewels.Second();
            Assert.That(jewels.Hardened, Is.Null);
            jewels.Blow(eDamageType.Crush, false, 100, 1.0, out _, out hardened);
            Assert.That(hardened && jewels.Hardened == eDamageType.Crush, Is.True);
        }

        [Test]
        public void ShraoghStomach_DrainsHalfButNeverKills()
        {
            Assert.That(ShraoghHide.AfterStomach(1000), Is.EqualTo(500));
            Assert.That(ShraoghHide.AfterStomach(1), Is.EqualTo(1));
            Assert.That(ShraoghHide.Weaken(1.0, 1.0), Is.EqualTo(0.75));
            Assert.That(ShraoghHide.Weaken(0.5, 1.0), Is.EqualTo(0.5), "weakening stops at half");
        }

        [Test]
        public void MedusaStatues_HuntTheCommonestKindOfFighter()
        {
            Assert.That(Ml3Kit.RoleOf(eCharacterClass.Cleric, eClassType.ListCaster), Is.EqualTo(Ml3Role.Healer));
            Assert.That(Ml3Kit.RoleOf(eCharacterClass.Wizard, eClassType.ListCaster), Is.EqualTo(Ml3Role.Caster));
            Assert.That(Ml3Kit.RoleOf(eCharacterClass.Armsman, eClassType.PureTank), Is.EqualTo(Ml3Role.Melee));
            Assert.That(Ml3Kit.CommonestRole(new[] { Ml3Role.Caster, Ml3Role.Healer, Ml3Role.Caster }), Is.EqualTo(Ml3Role.Caster));
            Assert.That(Ml3Kit.CommonestRole(new[] { Ml3Role.Healer, Ml3Role.Melee }), Is.EqualTo(Ml3Role.Melee), "ties go to melee");
            Assert.That(Ml3Kit.CommonestRole(System.Array.Empty<Ml3Role>()), Is.EqualTo(Ml3Role.Melee));
            Assert.That(Ml3Kit.StatueName(Ml3Role.Healer), Does.Contain("lifebane"));
        }

        [Test]
        public void Toward_StopsShortOfTheTarget()
        {
            TrialPoint boss = new(0, 0, 10);
            TrialPoint far = Ml3Kit.Toward(boss, new TrialPoint(1000, 0, 20), 150);
            Assert.That((far.X, far.Y, far.Z), Is.EqualTo((150, 0, 20)));
            TrialPoint near = Ml3Kit.Toward(boss, new TrialPoint(100, 0, 20), 150);
            Assert.That((near.X, near.Y), Is.EqualTo((100, 0)));
        }
    }
}
