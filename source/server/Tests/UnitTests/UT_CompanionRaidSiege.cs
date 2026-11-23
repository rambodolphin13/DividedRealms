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
        public void AccountCompanionsHoldingBothSetsUseTheOneTheyTrainedHigher()
        {
            // DR-525: loaded with the one-hand set active, a two-hand-trained companion still swung its one-hander.
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.Standard, true, true, preferTwoHand: true), Is.EqualTo(eActiveWeaponSlot.TwoHanded));
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.TwoHanded, true, true, preferTwoHand: false), Is.EqualTo(eActiveWeaponSlot.Standard));
            // Only one set usable: that one, whatever is preferred.
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.Standard, false, true, preferTwoHand: false), Is.EqualTo(eActiveWeaponSlot.TwoHanded));
            Assert.That(GameBot.CompanionWeaponSet(eActiveWeaponSlot.Standard, true, false, preferTwoHand: true), Is.EqualTo(eActiveWeaponSlot.Standard));

            Assert.That(GameBot.CompanionPrefersTwoHand(oneHandSkill: 1, twoHandSkill: 50, planIs2H: false), Is.True);
            Assert.That(GameBot.CompanionPrefersTwoHand(oneHandSkill: 44, twoHandSkill: 10, planIs2H: true), Is.False);
            // Same line (a Midgard sword in each hand): the plan decides.
            Assert.That(GameBot.CompanionPrefersTwoHand(39, 39, planIs2H: true), Is.True);
            Assert.That(GameBot.CompanionPrefersTwoHand(39, 39, planIs2H: false), Is.False);
        }

        [Test]
        public void TheRaidTakesTheDoorThePlayerStandsAtAndTargets()
        {
            // DR-491: the player's targeted door leads the squads when it stands, is the enemy's and the player is at it.
            Assert.That(CompanionRaidSiege.OrdersDoor(true, true, true, 400), Is.True);
            Assert.That(CompanionRaidSiege.OrdersDoor(true, true, true, CompanionRaidSiege.OrderedDoorRadius + 1), Is.False);
            Assert.That(CompanionRaidSiege.OrdersDoor(false, true, true, 400), Is.False, "a broken door");
            Assert.That(CompanionRaidSiege.OrdersDoor(true, false, true, 400), Is.False, "the player's own realm's door");
            Assert.That(CompanionRaidSiege.OrdersDoor(true, true, false, 400), Is.False, "another region");
        }

        [Test]
        public void ACompanionsRealmPointsLookUpOnlyARealGuild()
        {
            // DR-493: the companion's guild comes from its character's guild id.
            int lookups = 0;
            Guild Lookup(string id) { lookups++; return null; }
            Assert.That(GameBot.CompanionGuildFor(null, Lookup), Is.Null);
            Assert.That(GameBot.CompanionGuildFor("", Lookup), Is.Null);
            Assert.That(lookups, Is.Zero);
            GameBot.CompanionGuildFor("guild-1", Lookup);
            Assert.That(lookups, Is.EqualTo(1));
        }

        [Test]
        public void ABardCompanionGetsATrainingLuteOnlyWithNoInstrumentAtAll()
        {
            // DR-490: any instrument plays any song since 1.97, so a companion's own drum or lute is enough.
            static DOL.Database.DbInventoryItem Instrument(int type) => new GameInventoryItem(new DOL.Database.DbItemTemplate
                { Id_nb = $"instrument_{type}", Name = "instrument", Model = 227, Object_Type = (int)eObjectType.Instrument, DPS_AF = type });
            static DOL.Database.DbInventoryItem Sword() => new GameInventoryItem(new DOL.Database.DbItemTemplate
                { Id_nb = "sword", Name = "sword", Model = 4, Object_Type = (int)eObjectType.Blades });
            Assert.That(BotStarterInstruments.CompanionNeedsTrainingLute(new[] { Sword() }), Is.True);
            Assert.That(BotStarterInstruments.CompanionNeedsTrainingLute(new[] { Sword(), Instrument((int)eInstrumentType.Drum) }), Is.False);
            Assert.That(BotStarterInstruments.CompanionNeedsTrainingLute(null), Is.True);
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
        public void AccountCompanionsShowWhatTheyHoldEvenWithNoWeaponTheyCanSwing()
        {
            // A seer with only a shield, or a caster's staff: shown at load, not left in the slot unseen.
            Assert.That(GameBot.CompanionShownSet(true, true), Is.EqualTo(eActiveWeaponSlot.Standard));
            Assert.That(GameBot.CompanionShownSet(false, true), Is.EqualTo(eActiveWeaponSlot.TwoHanded));
            Assert.That(GameBot.CompanionShownSet(false, false), Is.Null);
        }

        [Test]
        public void APlayerRaisedDefenseMarchesWhenAKeepDoorFalls()
        {
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(true, false, true), Is.True);
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(true, false, false), Is.False);
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(true, true, true), Is.False);
            Assert.That(AutonomousRvrEventLayer.DefenseMarchesOnBreach(false, false, true), Is.False, "bot-planned sieges keep their own muster");
        }

        [Test]
        public void ACompanionLostOnAJumpIsRepairedByThePlayersNextTransfer()
        {
            // DR-420: two Wizards stayed "On the way to you" across many jumps until summoned again.
            var R = TemporaryGroupStableTravel.TransferRepair.PutBackInWorld;
            var T = TemporaryGroupStableTravel.TransferRepair.RestartReleaseTimer;
            var N = TemporaryGroupStableTravel.TransferRepair.None;
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(true, GameObject.eObjectState.Inactive, true, false), Is.EqualTo(R));
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(true, GameObject.eObjectState.Inactive, false, false), Is.EqualTo(R));
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(true, GameObject.eObjectState.Active, false, false), Is.EqualTo(T));
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(true, GameObject.eObjectState.Active, false, true), Is.EqualTo(N), "a running release timer is left to work");
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(true, GameObject.eObjectState.Active, true, false), Is.EqualTo(N));
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(true, GameObject.eObjectState.Deleted, true, false), Is.EqualTo(N), "dismissed");
            Assert.That(TemporaryGroupStableTravel.TransferRepairFor(false, GameObject.eObjectState.Inactive, true, false), Is.EqualTo(N), "gamebots keep their own recovery");
        }
    }
}
