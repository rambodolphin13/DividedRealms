using System;
using System.Collections.Generic;
using System.Linq;
using static DOL.GS.Trials.Ml34Kit;
using static DOL.GS.Trials.Ml3Kit;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Master Level 3: the gorgons and sea beasts of the Temple of Twilight, ending with Medusa. Built to the live game as patched
    /// through 1.130: Kepa and Antioos fight on the spot (1.75), Casta has no area bolt and a frailer necklace (1.89), fewer
    /// barracuda (1.89), Sinovia's ranged poison (1.68), Medusa's shorter stun (1.89) and statues that can be hurt (1.73).
    /// </summary>
    public static class Ml3
    {
        private const ushort Gorgon = 1182;
        private const ushort GorgonQueen = 1183;
        private const ushort MedusaModel = 1188;
        private const ushort Cobra = 1234;
        private const ushort Hoplite = 997;
        private const ushort StonedBriton = 1315;
        private const ushort StonedSaracen = 1319;
        private const ushort StonedNorse = 1327;
        private const ushort Barracuda = 33735;
        private const ushort RayModel = 33743;
        private const ushort EelModel = 33742;
        private const ushort WhaleShark = 33736;
        private const ushort Octopus = 33738;
        private const ushort Triton = 33745;

        /// <summary>Prima, encounter 3-1: the shield goes after six drinks, for any party.</summary>
        public const int BarrierCharges = 6;
        public const int RayKillsNeeded = 3;
        /// <summary>Laodameia frenzies when hit three times in a row by one damage type (Prima, encounter 3-4).</summary>
        public const int RageStreak = 3;
        public const int RageCalmSeconds = 20;
        /// <summary>Patch 1.89 cut the barracuda adds; reports before it saw 10 to 15.</summary>
        public const int BarracudaAdds = 8;
        /// <summary>Antioos sets the giant cobra's brood loose at a quarter health (Prima, encounter 3-5).</summary>
        public const int BroodPercent = 25;
        public const int BroodSize = 10;
        public const int MinionFloor = 2500;

        public static IEnumerable<TrialDefinition> Trials()
        {
            yield return MoiraisDestruction();
            yield return KepasDownfall();
            yield return CastasAnnihilation();
            yield return LaodamediasFuriousEnd();
            yield return AntioosDeceptiveDeath();
            yield return GlowingBarracudasFading();
            yield return ShraoghsFearRemoved();
            yield return StingRaysPowerAbsorbed();
            yield return SinoviasPowerRelinquished();
            yield return ConqueringTwilight();
        }

        /// <summary>
        /// Base health that leaves a minion at least <see cref="MinionFloor"/> after scaling. Eighth-size minions died to a
        /// lone player's bots in one or two blows, so they never lived below half long enough to drink from the shield.
        /// </summary>
        public static int MinionHealth(int designHealth, double healthScale) =>
            healthScale <= 0 ? designHealth : Math.Max(designHealth, (int)Math.Ceiling(MinionFloor / healthScale));

        private static TrialDefinition MoiraisDestruction()
        {
            TrialPoint moirai = new(39695, 22801, 16178);
            TrialPoint[] minions = { new(39498, 22724, 16186), new(39520, 22916, 16186), new(39808, 22984, 16186) };

            TrialMobSpec Minion(TrialRun run, TrialPoint at)
            {
                TrialMobSpec spec = Mob("Minion of Moirai", Gorgon, 62, 57, 5000, 0.7, at, "minion", required: false, melee: eDamageType.Slash);
                spec.Health = MinionHealth(spec.Health, run.HealthScale);
                return spec;
            }

            void Burn(TrialRun run)
            {
                TrialMob boss = First(run, "moirai");
                if (boss == null)
                    return;
                if (Every(run, "fire", 12))
                {
                    Hurt(run, boss, Ring(run, Where(boss), 250, 1100), 180, eDamageType.Heat);
                    run.Tell("Fire races around the rim of Moirai's platform. Only its centre is safe.");
                }
                if (Every(run, "snakes", 30) && run.Mobs("snake").Count() < Scaled(3, run.HealthScale, 1) && AnyOf(Ring(run, Where(boss), 0, 1500)) is { } prey)
                {
                    TrialMob cobra = run.Spawn(At(Mob("cobra", Cobra, 56, 40, 1500, 0.6, tag: "snake", required: false, melee: eDamageType.Thrust), On(moirai, 300, Random.Shared.Next(6), 6)));
                    Fixate(cobra, prey);
                    run.Tell($"Moirai hisses, and a cobra uncoils towards {prey.Name}.");
                }
            }

            void Drain(TrialRun run)
            {
                foreach (TrialMob minion in run.Mobs("minion").Where(mob => HealthPercent(mob) < 50).ToList())
                {
                    minion.Health = minion.MaxHealth;
                    int left = run.Get<int>("barrier") - 1;
                    run.Set("barrier", left);
                    if (left > 0)
                        run.Tell($"{minion.Name} drinks from the barrier to mend herself. It flickers and shrinks ({left} left).");
                }
                if (run.Mobs("minion").Count() < minions.Length && Every(run, "resummon", 30))
                {
                    run.Tell("Moirai calls another servant. Slain minions take nothing from her shield!");
                    run.Spawn(Minion(run, minions[Random.Shared.Next(minions.Length)]));
                }
            }

            return new TrialDefinition
            {
                ML = 3,
                Step = 1,
                Name = "Moirai's Destruction",
                Scope = TrialScope.Group,
                Zone = TrialZone.Twilight,
                Anchor = new TrialPoint(39873, 24169, 16186),
                StarterName = "Gorgon Turncoat",
                StarterModel = Gorgon,
                Intro = "I served Moirai until her shield began to feed on her own servants. Nothing you own will scratch her while it holds. " +
                    $"Her three minions drink from it when they are hurt past half, so wound them again and again, but do not kill them. After {BarrierCharges} drinks it is gone. " +
                    "Then get to the very centre of her platform. Her fire does not reach it.",
                Victory = "Moirai falls, and the runic power behind her shield passes to you.",
                Replaces = new[] { "Moirai", "Minion of Moirai", "Moirai Invisible Energy" },
                TimeLimitSeconds = 1800,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Moirai's barrier hums. Wound her minions so they drain it. Do not kill them!",
                        Mobs = { Mob("Moirai", GorgonQueen, 66, 75, 30000, 1.3, moirai, "moirai", immune: true) },
                        OnStart = run =>
                        {
                            run.Set("barrier", BarrierCharges);
                            foreach (TrialPoint at in minions)
                                run.Spawn(Minion(run, at));
                        },
                        OnTick = run =>
                        {
                            Drain(run);
                            Burn(run);
                        },
                        Done = run => run.Get<int>("barrier") <= 0 || First(run, "moirai") == null,
                    },
                    new TrialPhase
                    {
                        Announce = "The barrier collapses! Moirai can be hurt!",
                        OnStart = run => run.SetImmune("moirai", false),
                        OnTick = Burn,
                        Done = Cleared("moirai"),
                    },
                },
            };
        }

        private static TrialDefinition KepasDownfall()
        {
            TrialPoint kepa = new(61186, 18776, 16938);
            const int CloudDelay = 4;

            void Cloud(TrialRun run)
            {
                if (First(run, "kepa") is not { } boss)
                    return;
                int drifting = run.Get<int>("cloud");
                if (drifting > 0)
                {
                    run.Set("cloud", drifting - 1);
                    if (drifting == 1 && run.Get<GameLiving>("cloudTarget") is { IsAlive: true } marked)
                    {
                        List<GameLiving> caught = Ring(run, Where(marked), 0, 300).ToList();
                        Hurt(run, boss, caught, 220, eDamageType.Body);
                        foreach (GameLiving victim in caught)
                            Lay(run, boss, victim, Ml3Effect.Stun, 3);
                        run.Tell($"Kepa's sickly cloud bursts over {marked.Name}!");
                    }
                    return;
                }
                if (Every(run, "cloudEvery", 20) && AnyOf(Ring(run, Where(boss), 0, 2000)) is { } target)
                {
                    run.Set("cloudTarget", target);
                    run.Set("cloud", CloudDelay);
                    run.Tell($"Kepa breathes out a sickly green cloud. It drifts after {target.Name}. Everyone else, step away!");
                }
            }

            return new TrialDefinition
            {
                ML = 3,
                Step = 2,
                Name = "Kepa's Downfall",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Twilight,
                Anchor = kepa,
                StarterName = "Kepa",
                StarterModel = Gorgon,
                Intro = "So, more of you come for my runic power. The tritons whisper that I am sick and weak. They are wrong. " +
                    "Say the word and I will give you a moment to think better of it. Stay, and we shall see who walks out of my cave.",
                Victory = "Kepa's bargain is paid in full, though not as she intended. Her runic power is yours.",
                // An Atlas "Lord of Battle" Mordred (LordOfBattle, level 75) stands 88 from Kepa in the Temple of Twilight. He pulls
                // every player within 7,000 back to him ("Cowardice is not appreciated in this arena."), so nobody can
                // /mltrial goto or port out after Kepa (bot playtest 2026-11-14; region 73's pair went in DR-383).
                Replaces = new[] { "Kepa", "Mordred" },
                ReplaceRadius = 7000,
                TimeLimitSeconds = 1500,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Kepa: You want my power? Then take it from me. You have a little time to run, and I advise you to use it.",
                        SurviveSeconds = 30,
                    },
                    new TrialPhase
                    {
                        Announce = "Kepa uncoils, and her sickness hangs in the air around her!",
                        Mobs = { Mob("Kepa", Gorgon, 66, 62, 60000, 1.3, kepa, "kepa", melee: eDamageType.Slash) },
                        OnStart = HideStarter,
                        OnTick = Cloud,
                        Done = Cleared("kepa"),
                    },
                },
            };
        }

        private static TrialDefinition CastasAnnihilation()
        {
            TrialPoint casta = new(55046, 19623, 15392);
            const double ChipChance = 0.05;

            void Jewels(TrialRun run)
            {
                if (First(run, "casta") is not { } boss)
                    return;
                CastaJewels jewels = run.Get<CastaJewels>("jewels");
                jewels.Second();
                int back = 0;
                foreach (TrialHit hit in TakeHits(run, "blows").Where(hit => hit.Damage > 0))
                {
                    back += jewels.Blow(hit.DamageType, hit.AttackType == AttackData.eAttackType.Ranged, hit.Damage, Random.Shared.NextDouble(), out bool chipped, out bool hardened);
                    if (chipped)
                        run.Tell(jewels.NecklaceWhole ? "A crack runs through Casta's necklace!" : "Casta's necklace shatters! Arrows and bolts bite her now.");
                    if (hardened)
                        run.Tell($"Casta's bracelets drink in the blow. She hardens against {hit.DamageType.ToString().ToLowerInvariant()} damage.");
                }
                if (back > 0)
                    boss.Health = Math.Min(boss.MaxHealth, boss.Health + back);

                if (Every(run, "ring", 15) && AnyOf(Ring(run, Where(boss), 400, 2500)) is { } far)
                {
                    Hurt(run, boss, new[] { far }, 300, eDamageType.Cold);
                    Lay(run, boss, far, Ml3Effect.Root, 10);
                    run.Tell($"Casta's ring flashes white, and frost pins {far.Name} in place.");
                }
            }

            return new TrialDefinition
            {
                ML = 3,
                Step = 3,
                Name = "Casta's Annihilation",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Twilight,
                Anchor = casta,
                StarterName = "Petrified Delver",
                StarterModel = StonedBriton,
                Intro = "The stone figure still clutches a rubbing of Casta's jewellery. Her necklace turns arrows and bolts until crushing or slashing blows crack it twice. " +
                    "Her bracelets harden her for a few heartbeats against whatever kind of blow struck her last, so vary your attacks. " +
                    "Her ring freezes anyone who hangs back from her, so the last words scratched here say to stay in her face.",
                Victory = "Casta's artifacts lie broken around her, and her runic power is yours.",
                Replaces = new[] { "Casta" },
                TimeLimitSeconds = 1500,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Casta's necklace blazes. Arrows slide off her!",
                        Mobs = { Mob("Casta", GorgonQueen, 66, 82, 70000, 1.4, casta, "casta", melee: eDamageType.Slash) },
                        OnStart = run =>
                        {
                            run.Set("jewels", new CastaJewels(ChipChance));
                            WatchHits(run, First(run, "casta"), "blows");
                        },
                        OnTick = Jewels,
                        Done = Cleared("casta"),
                    },
                },
            };
        }

        private static TrialDefinition LaodamediasFuriousEnd()
        {
            TrialPoint lair = new(64261, 19713, 16946);
            const double RageMultiplier = 1.8;
            const int RageSeconds = 12;

            void Frenzy(TrialRun run, TrialMob boss, string why)
            {
                run.Set("raging", RageSeconds);
                boss.DamageFactor = run.Get<double>("calm") * RageMultiplier;
                run.Tell(why);
            }

            void Temper(TrialRun run)
            {
                TrialMob boss = First(run, "laodameia");
                if (boss == null)
                    return;
                RageMeter meter = run.Get<RageMeter>("meter");
                meter.Second();

                int raging = run.Get<int>("raging");
                if (raging > 0)
                {
                    run.Set("raging", raging - 1);
                    if (raging == 1)
                    {
                        boss.DamageFactor = run.Get<double>("calm");
                        run.Tell("Laodameia's fury burns out.");
                    }
                }

                bool enraged = false;
                foreach (TrialHit hit in TakeHits(run, "blows").Where(hit => hit.Damage > 0 && hit.AttackType != AttackData.eAttackType.Spell))
                    enraged |= meter.Hit(hit.DamageType);
                if (enraged && raging == 0)
                    Frenzy(run, boss, "One weapon too many! Laodameia ENRAGES. Back away from her!");
                else if (raging == 0 && Every(run, "temper", 60) && Random.Shared.Next(3) == 0)
                    Frenzy(run, boss, "Laodameia flies into a rage for no reason at all. Back away from her!");

                if (Every(run, "venom", raging > 0 ? 2 : 4) && boss.TargetObject is GameLiving bitten && Random.Shared.Next(2) == 0)
                    Hurt(run, boss, new[] { bitten }, 120, eDamageType.Body);
            }

            void Attendants(TrialRun run)
            {
                if (First(run, "deidamia") is { } deidamia)
                {
                    if (Every(run, "snakes", 15) && run.Mobs("snake").Count() < Scaled(4, run.HealthScale))
                        run.Spawn(At(Mob("entranced cobra", Cobra, 58, 45, 2000, 0.6, tag: "snake", required: false, melee: eDamageType.Thrust), On(Where(deidamia), 200, Random.Shared.Next(6), 6)));
                    if (Every(run, "deidamiaMez", 25) && AnyOf(Ring(run, Where(deidamia), 0, 500).Where(living => living != deidamia.TargetObject)) is { } dazed)
                    {
                        Lay(run, deidamia, dazed, Ml3Effect.Mez, 8);
                        run.Tell($"Deidamia's eyes catch {dazed.Name}, who stands frozen.");
                    }
                }
                if (First(run, "prokne") is { } prokne && Every(run, "prokneVenom", 8) && AnyOf(Ring(run, Where(prokne), 0, 1500)) is { } poisoned)
                {
                    Lay(run, prokne, poisoned, Ml3Effect.Poison, 16, 90);
                    run.Tell($"Prokne spits venom at {poisoned.Name}.");
                }
                if (First(run, "ioclaste") is { } ioclaste && Every(run, "ioclasteStun", 6) && ioclaste.TargetObject is GameLiving struck && Random.Shared.Next(2) == 0)
                    Lay(run, ioclaste, struck, Ml3Effect.Stun, 3);
            }

            TrialMobSpec Deidamia() => Mob("Deidamia", Gorgon, 65, 62, 9000, 0.9, On(lair, 400, 0, 3), "deidamia", melee: eDamageType.Slash);
            TrialMobSpec Prokne() => Mob("Prokne", Gorgon, 65, 62, 9000, 0.8, On(lair, 400, 1, 3), "prokne", melee: eDamageType.Thrust);
            TrialMobSpec Ioclaste() => Mob("Ioclaste", Gorgon, 66, 66, 12000, 1.2, On(lair, 400, 2, 3), "ioclaste", melee: eDamageType.Slash);

            return new TrialDefinition
            {
                ML = 3,
                Step = 4,
                Name = "Laodamedia's Furious End",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Twilight,
                Anchor = lair,
                StarterName = "Petrified Swordsman",
                StarterModel = StonedNorse,
                Intro = "A warning is cut into the stone at this one's feet: Laodameia hates being struck the same way three times running. Vary your slashing, crushing and thrusting. " +
                    "Hit her that way and she flies into a rage, and sometimes she rages for no reason at all. While she rages, get clear of her. " +
                    "Three gorgons come for her lair when she falls. Deidamia breeds snakes and stares men still, Prokne poisons, Ioclaste stuns. Kill Deidamia first.",
                Victory = "The lair falls silent. Laodameia and her rivals are gone.",
                Replaces = new[] { "Laodameia" },
                TimeLimitSeconds = 2100,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Laodameia turns on you. Mix your weapons!",
                        Mobs = { Mob("Laodameia", GorgonQueen, 67, 75, 90000, 1.6, lair, "laodameia", melee: eDamageType.Slash) },
                        OnStart = run =>
                        {
                            run.Set("meter", new RageMeter(RageStreak, RageCalmSeconds));
                            run.Set("calm", First(run, "laodameia").DamageFactor);
                            WatchHits(run, First(run, "laodameia"), "blows");
                        },
                        OnTick = Temper,
                        Done = Cleared("laodameia"),
                    },
                    new TrialPhase
                    {
                        Announce = "The gate grinds shut. Three gorgons come to claim the lair: Deidamia first, or her snakes will swamp you!",
                        Mobs = { Deidamia(), Prokne(), Ioclaste() },
                        OnTick = Attendants,
                        Done = Cleared("deidamia", "prokne", "ioclaste"),
                    },
                },
            };
        }

        private static TrialDefinition AntioosDeceptiveDeath()
        {
            TrialPoint antioos = new(45823, 17245, 16170);

            return new TrialDefinition
            {
                ML = 3,
                Step = 5,
                Name = "Antioos' Deceptive Death",
                Scope = TrialScope.Group,
                Zone = TrialZone.Twilight,
                Anchor = antioos,
                StarterName = "Antioos",
                StarterModel = Gorgon,
                Intro = "Runic power? Of course, of course. I have no use for it. Come up onto my platform, all of you, and I shall hand it over. " +
                    "Say the word when you are ready.",
                Victory = "Antioos dies still trying to haggle. Her runic power is yours.",
                Replaces = new[] { "Antioos" },
                TimeLimitSeconds = 1800,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Antioos: Did you truly think I would give it away? My pet is hungry. Run if you like. It will not help for long.",
                        SurviveSeconds = 20,
                    },
                    new TrialPhase
                    {
                        Announce = "Antioos looses her giant cobra! Kill it before she calls its brood.",
                        Mobs =
                        {
                            Mob("Antioos", Gorgon, 67, 62, 38000, 1.7, antioos, "antioos", melee: eDamageType.Slash),
                            Mob("giant cobra", Cobra, 66, 90, 12000, 1.2, On(antioos, 300, 0, 4), "giant cobra", required: false, melee: eDamageType.Thrust),
                        },
                        OnStart = HideStarter,
                        OnTick = run =>
                        {
                            if (run.Get<bool>("brood") || First(run, "antioos") is not { } boss || HealthPercent(boss) > BroodPercent)
                                return;
                            run.Set("brood", true);
                            if (First(run, "giant cobra") is not { } mother)
                                return;
                            run.Tell("Antioos shrieks, and the giant cobra spills out a brood of young!");
                            Wave(run, Mob("cobra hatchling", Cobra, 65, 35, 1500, 0.5, tag: "brood", required: false, melee: eDamageType.Thrust), BroodSize, Where(mother), 250);
                        },
                        Done = Cleared("antioos"),
                    },
                },
            };
        }

        private static TrialDefinition GlowingBarracudasFading()
        {
            TrialPoint shoal = new(27617, 30741, 14925);
            return new TrialDefinition
            {
                ML = 3,
                Step = 6,
                Name = "Glowing Barracuda's Fading",
                Scope = TrialScope.Group,
                Zone = TrialZone.Twilight,
                Anchor = new TrialPoint(28852, 32492, 15325),
                StarterName = "triton treasure hunter",
                StarterModel = Triton,
                Intro = "One of the barracuda down that passage has swallowed runic power, and it shines through its hide. " +
                    "It freezes whoever it picks with a flash of that power and sends the rest of the shoal after them. You only need the shining one, so put your whole party on it.",
                Victory = "The glow fades from the great barracuda, and the power settles on you.",
                Replaces = new[] { "triton treasure hunter", "bonerending barracuda" },
                TimeLimitSeconds = 1200,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The shoal turns as one. The glowing barracuda is the one that matters!",
                        Mobs = { Mob("glowing barracuda", Barracuda, 66, 90, 22000, 1.2, shoal, "glowing", melee: eDamageType.Thrust) },
                        OnStart = run => Wave(run, Mob("bonerending barracuda", Barracuda, 62, 50, 2500, 0.6, tag: "shoal", required: false, melee: eDamageType.Thrust), BarracudaAdds, shoal, 500),
                        OnTick = run =>
                        {
                            if (First(run, "glowing") is not { } glowing || !Every(run, "flash", 20) || AnyOf(Ring(run, Where(glowing), 0, 1500)) is not { } marked)
                                return;
                            Lay(run, glowing, marked, Ml3Effect.Mez, 6);
                            foreach (TrialMob add in run.Mobs("shoal"))
                                Fixate(add, marked);
                            run.Tell($"Runic light lances out of the glowing barracuda into {marked.Name}, and the shoal turns on them!");
                        },
                        Done = Cleared("glowing"),
                    },
                },
            };
        }

        private static TrialDefinition ShraoghsFearRemoved()
        {
            TrialPoint hunter = new(345077, 650488, 5546);
            TrialPoint shark = new(345677, 650088, 5546);

            void Stomach(TrialRun run, TrialMob mob)
            {
                if (First(run, "shraogh") is not { } boss)
                    return;
                boss.Health = ShraoghHide.AfterStomach(boss.Health);
                boss.DamageFactor = ShraoghHide.Weaken(boss.DamageFactor, run.Get<double>("calm"));
                run.Set("exposed", ShraoghHide.OpenSeconds);
                run.Tell("The eel bile eats half the life out of Shraogh and burns open his hide. Strike while it burns!");
            }

            void Eel(TrialRun run, TrialMob mob)
            {
                TrialMobSpec stomach = Clickable("eel stomach", EelModel, 15, Where(mob), "You burst the stomach. Its bile clouds the water towards Shraogh.", required: false);
                stomach.Tag = "stomach";
                stomach.OnDeath = Stomach;
                run.Spawn(stomach);
            }

            TrialMobSpec Eels()
            {
                TrialMobSpec eel = Mob("darid gremna eel", EelModel, 63, 62, 3500, 0.8, tag: "eel", required: false, melee: eDamageType.Thrust);
                eel.OnDeath = Eel;
                return eel;
            }

            void Hide(TrialRun run)
            {
                int exposed = run.Get<int>("exposed");
                List<TrialHit> hits = TakeHits(run, "blows");
                if (exposed > 0)
                {
                    run.Set("exposed", exposed - 1);
                    if (exposed == 1)
                        run.Tell("The bile washes away, and Shraogh's hide closes again.");
                }
                else if (First(run, "shraogh") is { } boss && hits.Sum(hit => hit.Damage) is int taken and > 0)
                    boss.Health = Math.Min(boss.MaxHealth, boss.Health + (int)(taken * ShraoghHide.ClosedResist));

                if (!run.Mobs("eel").Any() && !run.Mobs("stomach").Any() && exposed == 0)
                    Wave(run, Eels(), 3, hunter, 500);
            }

            return new TrialDefinition
            {
                ML = 3,
                Step = 7,
                Name = "Shraogh's Fear Removed",
                Scope = TrialScope.Group,
                Zone = TrialZone.Oceanus,
                Anchor = hunter,
                StarterName = "triton treasure hunter",
                StarterModel = Triton,
                Intro = "Your blades will barely mark that whale shark. His hide shrugs off steel and spell alike. But the bile of a darid gremna eel eats right through it. " +
                    "I netted a few of them from the temple for this very purpose. Kill one and burst its stomach: the bile takes half of what life he has and leaves his hide open for a while.",
                Victory = "Shraogh sinks into the deep, and the waters before the temple are safe.",
                Replaces = new[] { "Shraogh", "triton treasure hunter" },
                TimeLimitSeconds = 1500,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Shraogh comes for you, and the netted eels break loose! Kill the eels and burst their stomachs.",
                        Mobs = { Mob("Shraogh", WhaleShark, 67, 25, 32000, 1.4, shark, "shraogh", melee: eDamageType.Crush) },
                        OnStart = run =>
                        {
                            run.Set("calm", First(run, "shraogh").DamageFactor);
                            WatchHits(run, First(run, "shraogh"), "blows");
                            Wave(run, Eels(), 3, hunter, 500);
                        },
                        OnTick = Hide,
                        Done = Cleared("shraogh"),
                    },
                },
            };
        }

        private static TrialDefinition StingRaysPowerAbsorbed()
        {
            TrialPoint swarm = new(34424, 44121, 14799);

            TrialMobSpec Glowing(TrialPoint at)
            {
                // Passive until struck (aggro 0), like the swarm: with aggro 1,500 all ten rays pulled at once on arrival and wiped a
                // full bot group (bot playtest 2026-11-14), leaving no way to thin the dull ones first as the intro says.
                TrialMobSpec ray = Mob("glowing sting ray", RayModel, 65, 40, 7000, 1.0, at, "glowing", required: false, aggro: 0, melee: eDamageType.Thrust);
                ray.OnDeath = PowerJumps;
                return ray;
            }

            void Light(TrialRun run, TrialPoint at)
            {
                TrialMob glowing = run.Spawn(Glowing(at));
                WatchAttackers(run, glowing, "attackers");
            }

            void PowerJumps(TrialRun run, TrialMob mob)
            {
                int caught = run.Get<int>("caught") + 1;
                run.Set("caught", caught);
                if (caught >= RayKillsNeeded)
                    return;
                run.Tell("The runic power leaps to another ray!");
                TrialMob next = First(run, "ray");
                if (next == null)
                {
                    Light(run, swarm);
                    return;
                }
                TrialPoint at = Where(next);
                next.Delete();
                Light(run, at);
            }

            return new TrialDefinition
            {
                ML = 3,
                Step = 8,
                Name = "Sting Ray's Power Absorbed",
                Scope = TrialScope.Group,
                Zone = TrialZone.Twilight,
                Anchor = swarm,
                StarterName = "triton ray hunter",
                StarterModel = Triton,
                Intro = "The rays here pass stolen runic power from one to another like a hot stone. Strike the one that glows and the whole swarm comes for you. " +
                    $"Kill it and the light jumps to a neighbour. Do that {RayKillsNeeded} times and it has nowhere left to run but into you. Thin out the dull ones if you can. They sting.",
                Victory = "The light of the rays gutters out, and the runic power is yours.",
                Replaces = new[] { "sting ray" },
                TimeLimitSeconds = 1200,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The swarm lifts from the sand. One of them glows!",
                        OnStart = run =>
                        {
                            Wave(run, Mob("sting ray", RayModel, 63, 22, 3000, 0.8, tag: "ray", required: false, aggro: 0, melee: eDamageType.Thrust), 9, swarm, 450);
                            Light(run, On(swarm, 200, 0, 1));
                        },
                        OnTick = run =>
                        {
                            if (TakeAttackers(run, "attackers").LastOrDefault(attacker => attacker.IsAlive) is not { } striker)
                                return;
                            foreach (TrialMob ray in run.Mobs("ray").Where(ray => !ray.InCombat))
                                ray.StartAttack(striker);
                        },
                        Done = run => run.Get<int>("caught") >= RayKillsNeeded,
                    },
                },
            };
        }

        private static TrialDefinition SinoviasPowerRelinquished()
        {
            TrialPoint den = new(58117, 35070, 14982);
            return new TrialDefinition
            {
                ML = 3,
                Step = 9,
                Name = "Sinovia's Power Relinquished",
                Scope = TrialScope.Group,
                Zone = TrialZone.Twilight,
                Anchor = den,
                StarterName = "triton wreck-diver",
                StarterModel = Triton,
                Intro = "Sinovia sits at the end of this tunnel with a belly full of runic power. Stand close to her. She reaches anyone who hangs back with a slow poison. " +
                    "Her arms drag whoever she fancies to her beak to drink their life, and she sends her clinging young after anyone she cannot reach.",
                Victory = "Sinovia goes limp, and the power she swallowed drifts free into your hands.",
                Replaces = new[] { "Sinovia" },
                // 30 minutes (was 20): a full bot group won at 17 minutes with half its members dead (bot playtest 2026-11-14).
                TimeLimitSeconds = 1800,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Sinovia uncoils from the wreckage of her meals.",
                        Mobs = { Mob("Sinovia", Octopus, 67, 32, 34000, 1.3, den, "sinovia") },
                        OnTick = run =>
                        {
                            if (First(run, "sinovia") is not { } boss)
                                return;
                            if (Every(run, "ink", 6))
                                Hurt(run, boss, Ring(run, Where(boss), 500, 2000), 90, eDamageType.Body);
                            if (Every(run, "drain", 15) && AnyOf(Ring(run, Where(boss), 0, 1000)) is { } victim)
                            {
                                if (!victim.IsWithinRadius(boss, 250))
                                {
                                    TrialPoint grab = Toward(Where(boss), Where(victim), 150);
                                    victim.MoveTo(boss.CurrentRegionID, grab.X, grab.Y, grab.Z, victim.Heading);
                                }
                                Hurt(run, boss, new[] { victim }, 200, eDamageType.Spirit);
                                boss.Health = Math.Min(boss.MaxHealth, boss.Health + boss.MaxHealth / 100);
                                run.Tell($"Sinovia's arm drags {victim.Name} to her beak and drinks their life.");
                            }
                            if (Every(run, "young", 40) && run.Mobs("young").Count() < Scaled(2, run.HealthScale, 1))
                            {
                                TrialMob young = run.Spawn(At(Mob("clinging octopus", Octopus, 63, 50, 4000, 0.9, tag: "young", required: false), On(den, 250, Random.Shared.Next(6), 6)));
                                if (AnyOf(Ring(run, Where(boss), 400, 2000)) is { } prey)
                                    Fixate(young, prey);
                            }
                        },
                        Done = Cleared("sinovia"),
                    },
                },
            };
        }

        private static TrialDefinition ConqueringTwilight()
        {
            TrialPoint medusa = new(68865, 19544, 17897);
            TrialPoint[] squares = { new(68700, 20521, 17897), new(69030, 20521, 17897), new(68700, 21000, 17897), new(69030, 21000, 17897) };
            TrialPoint[] guards =
            {
                new(68431, 19899, 17898), new(68432, 20035, 17898), new(68436, 20177, 17898),
                new(69290, 20380, 17898), new(69292, 20523, 17898), new(69300, 20663, 17898),
                new(68432, 20863, 17898), new(68432, 21004, 17898), new(68429, 21147, 17898),
            };
            const int TrapRearmSeconds = 20;

            void Spring(TrialRun run, int square, GameLiving trigger)
            {
                TrialPoint at = squares[square];
                List<GameLiving> near = Ring(run, at, 0, 350).ToList();
                // A trap's crowd control needs a hostile caster: the starter is at peace and the stock rules refuse it.
                GameLiving caster = First(run, "medusa") ?? run.Mobs().FirstOrDefault(mob => !mob.Spec.Objective);
                switch ((MedusaTrap)square)
                {
                    case MedusaTrap.Cobra:
                        run.Tell($"A dark square sinks under {trigger.Name}, and a cobra pours out of it!");
                        Fixate(run.Spawn(At(Mob("lethal cobra", Cobra, 62, 45, 2500, 0.8, tag: "snake", required: false, melee: eDamageType.Thrust), at)), trigger);
                        break;
                    case MedusaTrap.Fire:
                        run.Tell("A dark square sinks underfoot, and fire roars up out of it!");
                        Hurt(run, run.Starter, near, 220, eDamageType.Heat);
                        break;
                    case MedusaTrap.Poison:
                        run.Tell("A dark square sinks underfoot, and green poison boils up out of it!");
                        Hurt(run, run.Starter, near, 120, eDamageType.Body);
                        foreach (GameLiving victim in near)
                            Lay(run, caster, victim, Ml3Effect.Poison, 12, 40);
                        break;
                    default:
                        run.Tell("A dark square sinks underfoot, and stone hands grip the feet of everyone on it!");
                        foreach (GameLiving victim in near)
                            Lay(run, caster, victim, Ml3Effect.Root, 8);
                        break;
                }
            }

            void Traps(TrialRun run)
            {
                for (int i = 0; i < squares.Length; i++)
                {
                    string key = $"trap{i}";
                    int rearm = run.Get<int>(key);
                    if (rearm > 0)
                    {
                        run.Set(key, rearm - 1);
                        continue;
                    }
                    if (Ring(run, squares[i], 0, 130).FirstOrDefault() is not { } trigger)
                        continue;
                    run.Set(key, TrapRearmSeconds);
                    Spring(run, i, trigger);
                }
            }

            void Fight(TrialRun run)
            {
                Traps(run);
                if (First(run, "medusa") is not { } boss)
                    return;
                if (Every(run, "gaze", 25) && boss.TargetObject is GameLiving stared)
                {
                    foreach (GameLiving victim in Ring(run, Where(stared), 0, 300))
                    {
                        Lay(run, boss, victim, Ml3Effect.Stun, 3);
                        Lay(run, boss, victim, Ml3Effect.Poison, 12, 40);
                    }
                    run.Tell($"Medusa's gaze falls on {stared.Name}, and the stone creeps up their limbs.");
                }
                if (Every(run, "shriek", 40))
                    Hurt(run, boss, Ring(run, Where(boss), 0, 700), 250, eDamageType.Energy);
                if (HealthPercent(boss) <= 50 && !run.Get<bool>("statues"))
                {
                    run.Set("statues", true);
                    List<GameLiving> party = Ring(run, Where(boss), 0, 3000).ToList();
                    Ml3Role hunted = CommonestRole(party.Select(RoleOf));
                    List<GameLiving> prey = party.Where(living => RoleOf(living) == hunted).ToList();
                    int count = Scaled(4, run.HealthScale);
                    for (int i = 0; i < count; i++)
                    {
                        TrialMob statue = run.Spawn(At(Mob(StatueName(hunted), Hoplite, 64, 47, 4000, 1.0, tag: "wall statue", required: false), On(medusa, 900, i, count)));
                        if (prey.Count > 0)
                            Fixate(statue, prey[i % prey.Count]);
                    }
                    run.Tell("Stone grinds on stone. Medusa wakes the statues in her walls, and they hunt the kind of fighter you brought most of!");
                }
            }

            return new TrialDefinition
            {
                ML = 3,
                Step = 10,
                Name = "Conquering Twilight",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Twilight,
                Anchor = new TrialPoint(69127, 20936, 17898),
                StarterName = "Petrified Champion",
                StarterModel = StonedSaracen,
                Intro = "This one got further than most. Scratched in the plinth: clear the living statues first. Then fight Medusa at the side of the hall, away from the dark squares in the floor. " +
                    "One spits fire, one poison, one grips your feet and one lets out a snake. When she wakes the stone in her walls, it hunts one kind of fighter. It falls when she falls.",
                Victory = "Medusa is dead. The Temple of Twilight is yours, and the stone in its halls is only stone.",
                Replaces = new[]
                {
                    "Medusa", "treasure protector", "minor lifebane statue", "minor magebane statue", "minor shieldbreaker statue",
                    "superior lifebane statue", "superior magebane statue", "superior shieldbreaker statue",
                },
                TimeLimitSeconds = 2100,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The statues of Medusa's hall turn their heads. Break them before she wakes.",
                        OnStart = run =>
                        {
                            int count = Scaled(guards.Length, run.HealthScale);
                            for (int i = 0; i < count; i++)
                                run.Spawn(Mob(HallStatueName(i), Hoplite, 64, i % 3 == 1 ? (byte)47 : (byte)32, 4000, 1.0, guards[i], "guard"));
                        },
                        OnTick = Traps,
                        Done = Cleared("guard"),
                    },
                    new TrialPhase
                    {
                        Announce = "Medusa uncoils from her throne. The four squares in the floor are armed. Keep her at the side of the hall!",
                        // 70,000 at 1.3 (was 110,000 at 1.6): a full bot group wiped with her at 76% after 7 minutes, the healers first; now in
                        // line with Cetus (1.10) and the other ML3 bosses (bot playtest 2026-11-14).
                        Mobs = { Mob("Medusa", MedusaModel, 68, 75, 70000, 1.3, medusa, "medusa", melee: eDamageType.Slash) },
                        OnTick = Fight,
                        Done = Cleared("medusa"),
                    },
                    new TrialPhase
                    {
                        Announce = "With her last breath the traps of the hall go off together!",
                        Pulses = { new TrialPulse { IntervalSeconds = 3, Damage = 120, Radius = 900, DamageType = eDamageType.Heat } },
                        SurviveSeconds = 4,
                    },
                },
            };
        }

        /// <summary>Medusa's four floor squares, in the order of their positions (Prima, encounter 3-10).</summary>
        public enum MedusaTrap
        {
            Cobra,
            Fire,
            Poison,
            Root,
        }

        private static string HallStatueName(int index) => (index % 9) switch
        {
            0 or 2 => "minor shieldbreaker statue",
            1 => "superior shieldbreaker statue",
            3 or 5 => "minor lifebane statue",
            4 => "superior lifebane statue",
            6 or 8 => "minor magebane statue",
            _ => "superior magebane statue",
        };
    }
}
