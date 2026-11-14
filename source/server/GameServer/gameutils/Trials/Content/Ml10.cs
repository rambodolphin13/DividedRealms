using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Master Level 10: the five constellations of Celestius (region 91), fought one after another on the floating
    /// platform the stock 1.130 client draws there (zone 91 is a dungeon zone: Aerus_CIsland01.nif). Live sent Master
    /// Level 9 players in through an aurora portal to the hidden isle of Aerus and up its steps; here a stone in the city
    /// of Aerus does the same. Each kill opens a portal and the next constellation steps through, as on live.
    /// </summary>
    public static class Ml10
    {
        public const int RequiredMasterLevel = 9;
        public const int TimeLimit = 2400;
        public const int CheironSeconds = 8;
        public const int CheironCycle = 30;
        public const int ToothSeconds = 12;
        /// <summary>Seconds between a victory and the next constellation stepping out of the portal.</summary>
        public const int NextGateSeconds = 30;
        /// <summary>Scorpius's energy barrier holds its prisoner this close.</summary>
        public const int BarrierHold = 200;
        public const int BarrierRefreshSeconds = 30;
        public const int BowIntervalSeconds = 30;
        public const int MaxBows = 4;

        /// <summary>
        /// The in-game name live gave him (Allakhazam), which is also the name the world's loot rows use ("scorpious").
        /// The step and the /master window keep "Scorpius".
        /// </summary>
        public const string ScorpiusMobName = "Scorpious";

        public const ushort Stone = 1823;
        public const ushort StarryCentaur = 1255;
        public const ushort StarryLion = 1256;
        public const ushort StarryScorpion = 1257;
        public const ushort MortalCentaur = 1213;
        public const ushort DracoModel = 1266;
        private const ushort Swirl = 1684;
        private const ushort PurpleSwirl = 1685;

        /// <summary>Where ZonePoint 142 (the isle's steps) puts a player in Celestius, and where its exit trigger stands.</summary>
        public static readonly TrialPoint Landing = new(31892, 34784, 15763);

        /// <summary>The sunken floor of the great platform north of the landing, Z 15734 across x 30000-33750, y 29650-30900.</summary>
        public static readonly TrialPoint ArenaCentre = new(31900, 30250, 15734);

        /// <summary>The constellations come out of a portal on the far (north) side of the floor.</summary>
        public static readonly TrialPoint BossPoint = new(31900, 29950, 15734, 0);
        public static readonly TrialPoint PortalPoint = new(31900, 29750, 15734, 0);

        /// <summary>The city of Aerus, beside its zone-in (ZonePoint 140). Same ground in all three realm copies.</summary>
        public static readonly TrialPoint CityGate = new(42129, 38023, 10300, 2257);

        /// <summary>The hidden isle of Aerus where Celestius's exit (ZonePoint 143) lands; live had a djinn stone there.</summary>
        public static readonly TrialPoint Isle = new(51377, 39790, 11231, 290);

        /// <summary>The step stones stand in a row on the south side of the floor, where the bridge from the landing arrives.</summary>
        public static TrialPoint StoneFor(int step) => new(31900 + (step - 3) * 250, 30750, 15734, 2048);

        public static IEnumerable<TrialDefinition> Trials()
        {
            yield return Sagittarius();
            yield return Scorpius();
            yield return Centaurus();
            yield return Leo();
            yield return Draco();
        }

        public static bool CheironMortal(int seconds) => seconds % CheironCycle >= CheironCycle - CheironSeconds;

        /// <summary>How many of Leo's splits, at 75, 50 and 25 percent, this much health has brought on (1.89: he splits less).</summary>
        public static int LeoSplits(int healthPercent) => Math.Clamp((100 - healthPercent) / 25, 0, 3);

        /// <summary>Leo's fling: half the victim's health, like the fall from the sky on live.</summary>
        public static int FlingDamage(int maxHealth) => maxHealth / 2;

        private static TrialDefinition Step(int step, string name, string starter, string intro, string victory, params TrialPhase[] fight)
        {
            TrialDefinition definition = new()
            {
                ML = 10,
                Step = step,
                Name = name,
                Scope = TrialScope.Raid,
                Zone = TrialZone.Celestius,
                Anchor = StoneFor(step),
                StarterName = starter,
                StarterModel = Stone,
                Intro = intro,
                Victory = victory,
                RequiresSteps = Enumerable.Range(1, step - 1).ToArray(),
                Refuse = UnreadyReason,
                TimeLimitSeconds = TimeLimit,
                ClearRadius = 0,
                OnEnd = (run, won) =>
                {
                    if (won && step < 5 && !run.Sweeping)
                        OpenNextGate(run, step + 1);
                },
            };
            definition.Phases.Add(new TrialPhase { OnStart = RefuseTheUnready, SurviveSeconds = 1 });
            definition.Phases.AddRange(fight);
            return definition;
        }

        public const string UnreadyText = "The stone stays dark. It answers only to those who have reached Master Level 9.";

        public static string UnreadyReason(GamePlayer leader) => leader.MLLevel < RequiredMasterLevel ? UnreadyText : null;

        /// <summary>Backstop for runs that skip TrialManager.TryStart (the GM sweep).</summary>
        private static void RefuseTheUnready(TrialRun run)
        {
            if (UnreadyReason(run.Leader) is string reason)
                run.End(false, reason);
        }

        /// <summary>
        /// Live: "After killing Sagittarius, portal opens, Scorpius comes out." The next step's run starts on its own after a
        /// breather, for the same leader. If that can't happen (the leader died, left, or the stone is busy) the stone waits.
        /// </summary>
        private static void OpenNextGate(TrialRun run, int nextStep)
        {
            TrialStarter next = TrialManager.AllStarters.FirstOrDefault(starter => starter.Definition.ML == 10 && starter.Definition.Step == nextStep);
            if (next == null)
                return;
            string nextName = next.Definition.Name;
            run.Tell($"The portal flickers. {nextName} will step through in {NextGateSeconds} seconds.");
            GamePlayer leader = run.Leader;
            new ECSGameTimer(next, _ =>
            {
                GamePlayer current = leader.ObjectState == GameObject.eObjectState.Active ? leader : ClientService.Instance?.GetPlayerByExactName(leader.Name);
                if (current == null)
                    return 0;
                string reason = null;
                if (!current.IsAlive || current.CurrentRegion != next.CurrentRegion || !current.IsWithinRadius(next, next.Definition.LeashRange))
                    reason = "you are not there to meet it";
                else if (!TrialManager.TryStart(next, current, out string refused))
                    reason = refused;
                if (reason != null)
                    current.Out.SendMessage($"The portal closes, {reason}. Speak to the {next.Definition.StarterName} and say [begin] when you are ready for {nextName}.",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                return 0;
            }, NextGateSeconds * 1000);
        }

        private static TrialMobSpec Portal() => new()
        {
            Name = "celestial portal", Model = PurpleSwirl, Level = 60, Size = 120, Health = 1000, DamageFactor = 0.05, At = PortalPoint, AggroRange = 0, Immune = true, Required = false, Tag = "portal",
        };

        private static TrialDefinition Sagittarius() => Step(1, "Sagittarius", "Celestial Stone of the Archer",
            "Beyond this stone the Archer waits on the floor of the heavens. No spell will touch him; only steel and arrows find their mark. He looses enchanted bows into the air that shoot at you from afar, and if you strike one it becomes a spear and comes for you.",
            "Sagittarius's bow falls silent, and the portal behind him begins to churn.",
            new TrialPhase
            {
                Announce = "Sagittarius draws his bow!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = "Sagittarius", Model = StarryCentaur, Level = 70, Size = 130, Health = 110000, DamageFactor = 1.4, At = BossPoint, MeleeDamage = eDamageType.Thrust, Hurts = TrialDamageKind.Melee | TrialDamageKind.Ranged, Tag = "boss" },
                },
                OnStart = run => Calm(run, "portal"),
                OnTick = SagittariusTick,
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void SagittariusTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob boss = One(run, "boss");
            if (boss == null)
                return;

            if (seconds % BowIntervalSeconds == 0 && run.Mobs("bow").Count() + run.Mobs("spear").Count() < MaxBows)
            {
                for (int i = 0; i < 2; i++)
                {
                    TrialMob bow = run.Spawn(new TrialMobSpec
                    {
                        Name = "astral bow", Model = Swirl, Level = 52, Size = 60, Health = 3000, DamageFactor = 0.6, At = Ring(ArenaCentre, (seconds / BowIntervalSeconds * 2 + i) % 6, 6, 600),
                        MeleeDamage = eDamageType.Thrust, AggroRange = 0, Required = false, Tag = "bow",
                    });
                    bow.MaxSpeedBase = 0;
                }
                run.Tell("Sagittarius looses enchanted bows into the air!");
            }

            foreach (TrialMob bow in run.Mobs("bow").ToList())
            {
                if (bow.Health < bow.MaxHealth)
                {
                    TrialMob spear = run.Spawn(new TrialMobSpec
                    {
                        Name = "astral spear", Model = Swirl, Level = 52, Size = 60, Health = 3000, DamageFactor = 0.8, At = Beside(bow, 0), MeleeDamage = eDamageType.Thrust, Required = false, Tag = "spear",
                    });
                    spear.Health = Math.Max(1, spear.MaxHealth * bow.HealthPercent / 100);
                    bow.Delete();
                    run.Tell("A struck bow twists into a spear!");
                }
                else if (seconds % 4 == 0 && RandomTarget(run, bow, 2000) is { } mark)
                    Hurt(run, bow, mark, 110, eDamageType.Thrust);
            }

            if (seconds % 5 == 0 && RandomTarget(run, boss, 3000) is { } target)
                Hurt(run, boss, target, 180, eDamageType.Thrust);
        }

        private static TrialDefinition Scorpius() => Step(2, "Scorpius", "Celestial Stone of the Scorpion",
            "With the Archer gone, the Scorpion crawls through the portal. His plague leaps from one body to the next, so stand apart. He hurls his prey into a barrier of energy that holds them until it is broken. And when he weakens the sky itself begins to fall, and it goes on falling after he is dead.",
            "The last of the stars burns out, and the portal churns again.",
            new TrialPhase
            {
                Announce = "Scorpius scuttles out of the portal!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = ScorpiusMobName, Model = StarryScorpion, Level = 70, Size = 150, Health = 120000, DamageFactor = 1.6, At = BossPoint, MeleeDamage = eDamageType.Thrust, Tag = "boss" },
                },
                OnStart = run => Calm(run, "portal"),
                OnTick = ScorpiusTick,
                Done = run => !run.Mobs("boss").Any(),
            },
            new TrialPhase
            {
                Announce = "The stars are falling! Survive!",
                Pulses = { new TrialPulse { SourceTag = "portal", IntervalSeconds = 5, Damage = 280, Radius = 2500, DamageType = eDamageType.Heat, Message = "A star strikes the platform!" } },
                OnStart = Release,
                SurviveSeconds = 20,
            });

        private static void ScorpiusTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob boss = One(run, "boss");
            if (boss == null)
            {
                Release(run);
                return;
            }

            Dictionary<GameLiving, int> plague = run.Get<Dictionary<GameLiving, int>>("plague") ?? new();
            run.Set("plague", plague);

            if (seconds % 20 == 0 && RandomTarget(run, boss, 2500) is { } carrier)
                plague[carrier] = seconds;

            if (seconds % 3 == 0)
            {
                foreach (GameLiving victim in plague.Keys.Where(victim => !victim.IsAlive || seconds - plague[victim] >= 15).ToList())
                    plague.Remove(victim);

                foreach (GameLiving victim in plague.Keys.ToList())
                {
                    Hurt(run, boss, victim, 60, eDamageType.Body);
                    foreach (GameLiving neighbour in run.Hostiles(victim, 250).Where(neighbour => !plague.ContainsKey(neighbour)))
                        plague[neighbour] = seconds;
                }
            }

            HoldInBarrier(run, seconds, boss);

            if (boss.HealthPercent < 30 && seconds % 7 == 0)
                Splash(run, boss, 2500, 300, 280, eDamageType.Heat, "A star falls among you!");
        }

        /// <summary>
        /// Live: "he throws you into an energy barrier (yellow con) which you are stuck in until you die or someone kills the
        /// barrier. The barrier refreshes every 30 seconds or so after it's killed." The prisoner is pulled back if they stray.
        /// </summary>
        private static void HoldInBarrier(TrialRun run, int seconds, TrialMob boss)
        {
            TrialMob barrier = One(run, "barrier");
            GameLiving prisoner = run.Get<GameLiving>("prisoner");
            if (barrier == null)
            {
                if (prisoner != null)
                {
                    run.Set("prisoner", null);
                    run.Set("barrierFree", seconds);
                }
                if (seconds - run.Get("barrierFree", 0) < BarrierRefreshSeconds || RandomTarget(run, boss, 2500) is not { } marked)
                    return;
                TrialMob cage = run.Spawn(new TrialMobSpec
                {
                    Name = "energy barrier", Model = Swirl, Level = 51, Size = 90, Health = 4000, DamageFactor = 0.01, At = Beside(marked, 0), AggroRange = 0, Required = false, Tag = "barrier",
                });
                cage.MaxSpeedBase = 0;
                run.Set("prisoner", marked);
                run.Tell($"Scorpius hurls {marked.Name} into a barrier of energy!");
                return;
            }

            if (prisoner == null || !prisoner.IsAlive)
            {
                barrier.Delete();
                run.Set("prisoner", null);
                run.Set("barrierFree", seconds);
                return;
            }
            if (!prisoner.IsWithinRadius(barrier, BarrierHold))
                prisoner.MoveTo(barrier.CurrentRegionID, barrier.X, barrier.Y, barrier.Z, prisoner.Heading);
            Hurt(run, barrier, prisoner, 50, eDamageType.Energy);
        }

        private static void Release(TrialRun run)
        {
            foreach (TrialMob barrier in run.Mobs("barrier").ToList())
                barrier.Delete();
            run.Set("prisoner", null);
        }

        private static TrialDefinition Centaurus() => Step(3, "Centaurus", "Celestial Stone of the Healer",
            "The Centaur comes through the portal next, and he is too quick to outrun. Now and then he remembers the mortal he was, Cheiron, and his guard slips. The vials he drops hold the poison that once wounded him. Touch one while he is mortal and it will burn him deeply. While he is a god it is wasted.",
            "Cheiron closes his eyes, and the centaur's light goes out.",
            new TrialPhase
            {
                Announce = "Centaurus gallops out of the portal!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = "Centaurus", Model = StarryCentaur, Level = 70, Size = 130, Health = 130000, DamageFactor = 1.6, At = BossPoint, MeleeDamage = eDamageType.Thrust, Tag = "boss" },
                },
                Pulses = { new TrialPulse { SourceTag = "boss", IntervalSeconds = 9, Damage = 260, Radius = 450, DamageType = eDamageType.Energy } },
                OnStart = run => Calm(run, "portal"),
                OnTick = CentaurusTick,
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void CentaurusTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob boss = One(run, "boss");
            if (boss == null)
                return;

            bool mortal = CheironMortal(seconds);
            if (mortal != run.Get("mortal", false))
            {
                run.Set("mortal", mortal);
                // The name stays "Centaurus" so that his loot rows (MobXLootTemplate "centaurus") still drop if he dies mortal.
                boss.Model = mortal ? MortalCentaur : StarryCentaur;
                boss.Size = (byte)(mortal ? 90 : 130);
                run.Tell(mortal ? "The god falls away, and Cheiron stands there, mortal!" : "Centaurus blazes back into his divine form!");
            }

            if (seconds % 25 == 0 && run.Mobs("vial").Count() < 5)
            {
                run.Spawn(new TrialMobSpec
                {
                    Name = "vial of hydra's blood", Model = Stone, Level = 60, Size = 12, At = Beside(boss, 200), Objective = true, Required = false, Tag = "vial", OnDeath = BreakVial,
                });
                run.Tell("A vial slips from Centaurus's belt and rolls across the ground!");
            }
        }

        private static void BreakVial(TrialRun run, TrialMob vial)
        {
            vial.Delete();
            TrialMob boss = One(run, "boss");
            if (boss == null)
                return;
            if (!run.Get("mortal", false))
            {
                run.Tell("The vial shatters against the god's hide and does nothing.");
                return;
            }
            boss.Health = Math.Max(1, boss.Health - boss.MaxHealth / 10);
            run.Tell("The hydra's blood sears Cheiron!");
        }

        private static TrialDefinition Leo() => Step(4, "Leo", "Celestial Stone of the Lion",
            "The Lion is next, and he will not stay whole. Each time you wound him deeply, pieces of him tear loose, and no blade can touch those pieces: only arrows and spells. His roar shakes the platform, and he hurls his prey into the sky to fall where it may.",
            "Leo's pieces fade into the night, and he roars no more.",
            new TrialPhase
            {
                Announce = "Leo springs through the portal!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = "Leo", Model = StarryLion, Level = 70, Size = 130, Health = 130000, DamageFactor = 1.5, At = BossPoint, MeleeDamage = eDamageType.Slash, Tag = "boss" },
                },
                Pulses = { new TrialPulse { SourceTag = "boss", IntervalSeconds = 12, Damage = 220, Radius = 700, DamageType = eDamageType.Energy, Message = "Leo roars!" } },
                OnStart = run => Calm(run, "portal"),
                OnTick = LeoTick,
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void LeoTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob boss = One(run, "boss");
            if (boss == null)
                return;

            int due = LeoSplits(boss.HealthPercent);
            if (due > run.Get("splits", 0))
            {
                run.Set("splits", due);
                run.Spawn(new TrialMobSpec { Name = "shard of Leo", Model = StarryLion, Level = 60, Size = 70, Health = 5000, DamageFactor = 1.0, Count = 2, At = Beside(boss, 200), MeleeDamage = eDamageType.Slash, Hurts = TrialDamageKind.Ranged | TrialDamageKind.Spell, Required = false, Tag = "shard" });
                run.Tell("Leo splits apart, and pieces of him leap at you! No blade can bite them.");
            }

            if (seconds % 20 == 0 && RandomTarget(run, boss, 800) is { } prey)
            {
                prey.TakeDamage(boss, eDamageType.Crush, FlingDamage(prey.MaxHealth), 0);
                run.Tell($"Leo flings {prey.Name} into the sky!");
            }
        }

        private static TrialDefinition Draco() => Step(5, "Draco", "Celestial Stone of the Dragon",
            "Only the Dragon remains, Ladon's kin and the last of the constellations. He hits harder than anything you have faced, and his cold breath freezes everything before him. The teeth he sheds take root: smash one at once, or within moments it will sprout Ladon's minions.",
            "Draco crashes onto the platform, and the stars themselves go quiet. You have mastered the trials of Atlantis.",
            new TrialPhase
            {
                Announce = "Draco descends from the dark!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = "Draco", Model = DracoModel, Level = 70, Size = 150, Health = 150000, DamageFactor = 1.9, At = BossPoint, MeleeDamage = eDamageType.Slash, Tag = "boss" },
                },
                OnStart = run => Calm(run, "portal"),
                OnTick = DracoTick,
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void DracoTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob boss = One(run, "boss");
            if (boss == null)
                return;

            Dictionary<TrialMob, int> teeth = run.Get<Dictionary<TrialMob, int>>("teeth") ?? new();
            run.Set("teeth", teeth);

            foreach (TrialMob tooth in run.Mobs("tooth").Where(tooth => teeth.TryGetValue(tooth, out int since) && seconds - since >= ToothSeconds).ToList())
            {
                TrialPoint root = Beside(tooth, 0);
                tooth.Delete();
                if (run.Mobs("minion").Count() >= 6)
                    continue;
                run.Spawn(new TrialMobSpec { Name = "Ladon's minion", Model = DracoModel, Level = 53, Size = 40, Health = 4000, DamageFactor = 0.9, Count = 3, At = root, MeleeDamage = eDamageType.Slash, Required = false, Tag = "minion" });
                run.Tell("A tooth takes root, and Ladon's minions claw their way out!");
            }

            // Live: "a cold based AE breath ... when he breaths, he also spawns a bunch of orange con minions" (the tooth).
            if (seconds % 30 == 0 && RandomTarget(run, boss, 1500) is { } target)
            {
                HurtAround(run, boss, boss, 800, 350, eDamageType.Cold);
                teeth[run.Spawn(new TrialMobSpec
                {
                    Name = "tooth of Draco", Model = Stone, Level = 60, Size = 12, At = Beside(target), Objective = true, Required = false, Tag = "tooth",
                    ObjectiveText = "You grind the tooth to splinters.", OnDeath = (_, tooth) => tooth.Delete(),
                })] = seconds;
                run.Tell("Draco breathes frost across the platform, and a tooth bites into the ground!");
            }
            else if (Util.Chance(4))
                HurtAround(run, boss, boss, 600, 250, eDamageType.Heat);
        }

        // Small helpers of our own, so this file doesn't lean on another Master Level's content.
        private static int Clock(TrialRun run, string key = "clock")
        {
            int seconds = run.Get(key, 0) + 1;
            run.Set(key, seconds);
            return seconds;
        }

        private static TrialMob One(TrialRun run, string tag) => run.Mobs(tag).FirstOrDefault();

        private static void Hurt(TrialRun run, GameLiving source, GameLiving target, int damage, eDamageType type) =>
            target.TakeDamage(source, type, (int)(damage * run.DamageScale), 0);

        private static void HurtAround(TrialRun run, GameLiving source, GameObject center, int radius, int damage, eDamageType type)
        {
            foreach (GameLiving target in run.Hostiles(center, radius))
                Hurt(run, source, target, damage, type);
        }

        private static GameLiving RandomTarget(TrialRun run, GameObject source, int radius)
        {
            List<GameLiving> targets = run.Hostiles(source, radius).ToList();
            return targets.Count == 0 ? null : targets[Util.Random(targets.Count - 1)];
        }

        private static void Splash(TrialRun run, GameLiving source, int reach, int radius, int damage, eDamageType type, string message)
        {
            GameLiving target = RandomTarget(run, source, reach);
            if (target == null)
                return;
            HurtAround(run, source, target, radius, damage, type);
            if (message != null)
                run.Tell(message);
        }

        private static void Calm(TrialRun run, string tag)
        {
            foreach (TrialMob mob in run.Mobs(tag))
                mob.Flags |= GameNPC.eFlags.PEACE;
        }

        private static TrialPoint Beside(GameObject thing, int offset = 150) => new(thing.X + offset, thing.Y, thing.Z, thing.Heading);

        public static TrialPoint Ring(TrialPoint center, int index, int count, int radius)
        {
            double angle = 2 * Math.PI * index / count;
            return new TrialPoint(center.X + (int)(radius * Math.Cos(angle)), center.Y + (int)(radius * Math.Sin(angle)), center.Z, center.Heading);
        }

        // ---- The way in and out ----------------------------------------------------------------------------------

        private static readonly List<CelestialGate> Gates = new();

        public static IReadOnlyList<CelestialGate> AllGates => Gates;

        /// <summary>The Aerus copy a realm uses, as the trial starters pick it (Midgard 47, Albion 90, Hibernia 147).</summary>
        public static ushort AerusFor(eRealm realm) => TrialManager.RegionsOf(TrialZone.Aerus)[realm switch
        {
            eRealm.Midgard => 0,
            eRealm.Albion => 1,
            _ => 2,
        }];

        [GameServerStartedEvent]
        public static void OnServerStart(DOLEvent e, object sender, EventArgs arguments)
        {
            if (!Properties.ML_TRIALS_ENABLED)
                return;
            foreach (ushort aerus in TrialManager.RegionsOf(TrialZone.Aerus).Distinct())
            {
                Place(CelestialGate.Kind.Enter, aerus, CityGate);
                Place(CelestialGate.Kind.Leave, aerus, Isle);
            }
            foreach (ushort celestius in TrialManager.RegionsOf(TrialZone.Celestius).Distinct())
                Place(CelestialGate.Kind.Leave, celestius, new TrialPoint(Landing.X + 250, Landing.Y + 150, Landing.Z, 2048));
        }

        private static void Place(CelestialGate.Kind kind, ushort regionId, TrialPoint at)
        {
            try
            {
                Region region = WorldMgr.GetRegion(regionId);
                if (region == null)
                {
                    TrialManager.Log.Warn($"Master Level 10: region {regionId} is not loaded, so its {kind} stone is missing");
                    return;
                }
                CelestialGate gate = new(kind) { CurrentRegion = region, X = at.X, Y = at.Y, Z = at.Z, Heading = at.Heading };
                if (gate.AddToWorld())
                    Gates.Add(gate);
            }
            catch (Exception ex)
            {
                TrialManager.Log.Error($"Master Level 10: the {kind} stone could not be placed in region {regionId}", ex);
            }
        }

        /// <summary>Moves the speaker and the bots of their group that stand near them.</summary>
        public static void Carry(GamePlayer player, ushort regionId, TrialPoint to)
        {
            List<GameLiving> party = new() { player };
            if (player.Group != null)
                party.AddRange(player.Group.GetMembersInTheGroup().Where(member => member is GameBot && member.IsAlive && member.IsWithinRadius(player, 2000)));
            int slot = 0;
            foreach (GameLiving member in party.Distinct())
            {
                member.MoveTo(regionId, to.X + slot % 4 * 60, to.Y + slot / 4 * 60, to.Z, to.Heading);
                slot++;
            }
        }
    }

    /// <summary>
    /// The way to Celestius and back. Live used an aurora portal that only Master Level 9 players could pass, then the
    /// hidden isle's steps (ZonePoint 142); the way out was the landing's trigger (ZonePoint 143) to the isle and a djinn
    /// stone there. These stones stand in for the portal and the djinn stone.
    /// </summary>
    public class CelestialGate : GameNPC
    {
        public enum Kind
        {
            Enter,
            Leave,
        }

        public Kind Way { get; }

        public CelestialGate(Kind way)
        {
            Way = way;
            Name = way == Kind.Enter ? "Celestial Stone" : "Stone of Return";
            GuildName = "Celestius";
            Model = Ml10.Stone;
            Level = 50;
            Size = (byte)(way == Kind.Enter ? 70 : 50);
            Flags = eFlags.PEACE;
            Realm = eRealm.None;
            RespawnInterval = -1;
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            if (Way == Kind.Leave)
                SayTo(player, eChatLoc.CL_PopupWindow, "The stone hums with the light of a far city. Say [return] and it will carry you and your companions back to Aerus.");
            else if (Ml10.UnreadyReason(player) is string unready)
                SayTo(player, eChatLoc.CL_PopupWindow, unready);
            else
                SayTo(player, eChatLoc.CL_PopupWindow, "Cold starlight pours from the stone. It opens on Celestius, where the last of the trials are fought: Sagittarius, Scorpius, Centaurus, Leo and Draco, one after another. Say [enter] and it will carry you and your companions there.");
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text) || source is not GamePlayer player)
                return false;
            if (!IsWithinRadius(player, WorldMgr.INTERACT_DISTANCE))
                return false;
            if (Way == Kind.Enter && text.Equals("enter", StringComparison.OrdinalIgnoreCase))
            {
                if (Ml10.UnreadyReason(player) is string unready)
                {
                    SayTo(player, eChatLoc.CL_PopupWindow, unready);
                    return true;
                }
                ushort celestius = TrialManager.RegionsOf(TrialZone.Celestius)[0];
                if (WorldMgr.GetRegion(celestius) == null)
                {
                    SayTo(player, eChatLoc.CL_PopupWindow, "The starlight gutters. Celestius can't be reached in this world.");
                    return true;
                }
                Ml10.Carry(player, celestius, Ml10.Landing);
            }
            else if (Way == Kind.Leave && text.Equals("return", StringComparison.OrdinalIgnoreCase))
                Ml10.Carry(player, Ml10.AerusFor(player.Realm), Ml10.CityGate);
            return true;
        }
    }
}
