using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_CompanionRaidSiege
    {
        [Test]
        public void RaidSlotsFormSquadsOfEight()
        {
            Assert.That(CompanionRaidSiege.SquadOf(1), Is.EqualTo(0));
            Assert.That(CompanionRaidSiege.SquadOf(8), Is.EqualTo(0));
            Assert.That(CompanionRaidSiege.SquadOf(9), Is.EqualTo(1));
            Assert.That(CompanionRaidSiege.SquadOf(39), Is.EqualTo(4));
            Assert.That(CompanionRaidSiege.SquadOf(79), Is.EqualTo(9));
        }

        [Test]
        public void Raid40AssaultHasBodyguardRamArtilleryWallClearersAndAssault()
        {
            var jobs = Enumerable.Range(0, 5).Select(i => CompanionRaidSiege.JobFor(CompanionRaidSiege.Mode.Assault, i, 5)).ToArray();
            Assert.That(jobs, Is.EqualTo(new[] { CompanionRaidSiege.Job.Bodyguard, CompanionRaidSiege.Job.Ram,
                CompanionRaidSiege.Job.Catapult, CompanionRaidSiege.Job.WallClear, CompanionRaidSiege.Job.Assault }));
        }

        [Test]
        public void Raid80BringsTwoRamsATrebuchetAndABallista()
        {
            var jobs = Enumerable.Range(0, 10).Select(i => CompanionRaidSiege.JobFor(CompanionRaidSiege.Mode.Assault, i, 10)).ToArray();
            Assert.That(jobs.Count(j => j == CompanionRaidSiege.Job.Ram), Is.EqualTo(2));
            Assert.That(jobs, Does.Contain(CompanionRaidSiege.Job.Trebuchet));
            Assert.That(jobs, Does.Contain(CompanionRaidSiege.Job.Ballista));
            Assert.That(jobs[0], Is.EqualTo(CompanionRaidSiege.Job.Bodyguard));
            Assert.That(jobs.Count(j => j == CompanionRaidSiege.Job.Assault), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void DefenseHoldsDoorsWithCourtyardEngines()
        {
            var jobs = Enumerable.Range(0, 5).Select(i => CompanionRaidSiege.JobFor(CompanionRaidSiege.Mode.Defense, i, 5)).ToArray();
            Assert.That(jobs[0], Is.EqualTo(CompanionRaidSiege.Job.Bodyguard));
            Assert.That(jobs, Does.Contain(CompanionRaidSiege.Job.Ballista));
            Assert.That(jobs, Does.Contain(CompanionRaidSiege.Job.Catapult));
            Assert.That(jobs, Does.Not.Contain(CompanionRaidSiege.Job.Ram));
            Assert.That(CompanionRaidSiege.EngineFor(CompanionRaidSiege.Job.Defend), Is.Null);
            Assert.That(CompanionRaidSiege.EngineFor(CompanionRaidSiege.Job.Ram), Is.EqualTo(BotSiegeKind.Ram));
        }

        [Test]
        public void ARaidsOwnHitsOnAKeepItJustTookDoNotCallADefense()
        {
            // Discord 2026-11-17: the raid hit an Albion keep, took it for Midgard, then held its doors instead of following.
            Assert.That(CompanionRaidSiege.CallsDefense(eRealm.Albion, eRealm.Midgard, eRealm.Midgard), Is.False);
            Assert.That(CompanionRaidSiege.CallsDefense(eRealm.Midgard, eRealm.Midgard, eRealm.Midgard), Is.True);
            Assert.That(CompanionRaidSiege.CallsDefense(eRealm.Midgard, eRealm.Albion, eRealm.Midgard), Is.False);
            Assert.That(CompanionRaidSiege.CallsDefense(eRealm.None, eRealm.None, eRealm.None), Is.False);
        }

        [Test]
        public void AccountCompanionsOnlyWearDropsIntoEmptySlots()
        {
            Assert.That(GameBot.CompanionWearsInto(null), Is.True);
            Assert.That(GameBot.CompanionWearsInto(new DOL.Database.DbInventoryItem()), Is.False);
        }

        [Test]
        public void AccountCompanionsSwingTheWeaponSetTheyHold()
        {
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.TwoHanded, true, true), Is.EqualTo(eActiveWeaponSlot.TwoHanded));
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.TwoHanded, true, false), Is.EqualTo(eActiveWeaponSlot.Standard));
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.Distance, false, true), Is.EqualTo(eActiveWeaponSlot.TwoHanded));
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.Standard, false, false), Is.Null);
        }

        [Test]
        public void AccountCompanionsPutTheirWeaponInHandEvenWhenTheSlotAlreadyMatches()
        {
            // DR-489: a companion loads with slot Standard and nothing in hand; that must still switch.
            Assert.That(GameBot.CompanionWeaponSwitchNeeded(eActiveWeaponSlot.Standard, eActiveWeaponSlot.Standard, false), Is.True);
            Assert.That(GameBot.CompanionWeaponSwitchNeeded(eActiveWeaponSlot.Standard, eActiveWeaponSlot.Standard, true), Is.False);
            Assert.That(GameBot.CompanionWeaponSwitchNeeded(eActiveWeaponSlot.Standard, eActiveWeaponSlot.TwoHanded, true), Is.True);
            Assert.That(GameBot.CompanionWeaponSwitchNeeded(eActiveWeaponSlot.Standard, null, false), Is.False);
        }

        [Test]
        public void APlayerRaisedDefenseMarchesWhenAKeepDoorFalls()
        {
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(true, false, true), Is.True);
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(true, false, false), Is.False);
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(true, true, true), Is.False);
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(false, false, true), Is.False, "bot-planned sieges keep their own muster");
        }
    }
}
