using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using static DOL.GS.Trials.Ml7Kit;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Master Level 8 trials, in Green Glades and Arbor Glen: rebuilding Agne. Each step's body part stands for itself;
    /// the finale's step requirement takes the place of carrying the pieces to the Constructor. Encounter rules follow
    /// the Prima Trials of Atlantis guide (Trial Eight) and the 2004 player guide; see live-pass-2026-11-14/ml8.md.
    /// </summary>
    public static class Ml8
    {
        private const ushort Servant = 995, Jinni = 1198, Centaur = 1213, Bowman = 1214, Soldier = 1315, Gorgon = 1182, Cobra = 1234, Whirlwind = 1269,
            Marble = 994, Bronze = 995, Stone = 997, Reliquary = 1203, FireSpirit = 125, Sphinx = 1176;
        private const string ServantName = "Constructor's Servant";

        private const int StormSeconds = 25;
        private const int LastTorsoKill = 12;
        public const int TalosHealsTo = 60;
        public const int TalosRetryMinutes = 45;
        private const int CobraCap = 30;

        private static readonly TrialPoint Kratos = new(455206, 686214, 13781);
        private static readonly TrialPoint Constructor = new(461608, 688125, 14405);
        private static readonly TrialPoint Battlefield = new(463000, 688100, 13957);

        /// <summary>Leaders who killed Agne and then lost to Talos, with when they lost. Live gave 45 minutes to try Talos again.</summary>
        private static readonly ConcurrentDictionary<string, long> TalosRetries = new();

        public static IEnumerable<TrialDefinition> Trials()
        {
            yield return AgnesCrystal();
            yield return AgnesShield();
            yield return AgnesSword();
            yield return CentaursHunt();
            yield return TreasureOfTheCyclops();
            yield return AgnesWill();
            yield return AgnesMight();
            yield return GorgonsSecret();
            yield return AgnesSpareArm();
            yield return AgnesGreatMight();
        }

        /// <summary>Ibn breaks into storms at each of these marks, once each.</summary>
        public static int IbnStorms(int healthPercent) => MarksPassed(healthPercent, 75, 50, 25);

        /// <summary>Dawar flees at each of these marks, once each.</summary>
        public static int DawarFlights(int healthPercent) => MarksPassed(healthPercent, 66, 33);

        /// <summary>The torso turns up on a lucky break, and always by the last kill.</summary>
        public static bool TorsoFound(int kills, double roll) => kills >= LastTorsoKill || roll < 0.15;

        /// <summary>Self-maintaining statues mend whenever they are below 90% health (Prima).</summary>
        public static bool StatueMends(int healthPercent) => healthPercent > 0 && healthPercent < 90;

        public static bool TalosCallsForHealing(int healthPercent, bool alreadyHealed) => !alreadyHealed && healthPercent <= 25;

        /// <summary>How low Agne can be beaten while each wave of his army stands: 75, 50, 25, then down (Prima).</summary>
        public static int AgneFloor(int wave) => wave switch
        {
            1 => 75,
            2 => 50,
            3 => 25,
            _ => 0,
        };

        /// <summary>After a lost fight with Talos the raid may come back to him without rebuilding Agne for 45 minutes.</summary>
        public static bool TalosRetryOpen(long lostAt, long now) => lostAt > 0 && now >= lostAt && now - lostAt <= TalosRetryMinutes * 60_000L;

        private static string RetryKey(GamePlayer leader) => $"{leader.InternalID}:{leader.CurrentRegionID}";

        /// <summary>The starter steps out of sight while its own fight runs (Ibn, the Academy statue), and comes back after.</summary>
        private static void Hide(TrialRun run) => run.Starter.Flags = GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET | GameNPC.eFlags.STEALTH;

        private static void Show(TrialRun run) => run.Starter.Flags = GameNPC.eFlags.PEACE;

        private static bool NoneRequiredLeft(TrialRun run) => !run.Mobs().Any(mob => mob.Spec.Required);

        private static void Mend(TrialMob mob, int percent) => mob.Health = Math.Min(mob.MaxHealth, mob.Health + mob.MaxHealth * percent / 100);

        private static void SetFloor(TrialMob mob, int percent) => mob.Health = Math.Max(1, mob.MaxHealth * percent / 100);

        /// <summary>When one of a band is fighting, the rest of the band joins in on the same target.</summary>
        private static void Rally(TrialRun run, string tag)
        {
            List<TrialMob> band = run.Mobs(tag).ToList();
            GameLiving prey = band.Where(mob => mob.InCombat).Select(mob => mob.TargetObject as GameLiving).FirstOrDefault(target => target is { IsAlive: true });
            if (prey == null)
                return;
            foreach (TrialMob mob in band.Where(mob => !mob.InCombat))
                Aggro(mob, prey);
        }

        private static void Aggro(TrialMob mob, GameLiving target, long amount = 1)
        {
            if (mob.Brain is StandardMobBrain brain && target is { IsAlive: true })
                brain.AddToAggroList(target, amount);
        }

        /// <summary>True once each time the monster has lost health since the last check: "when hit" in a once-a-second tick.</summary>
        private static bool WasHit(TrialRun run, TrialMob mob)
        {
            string key = $"hp.{mob.ObjectID}";
            int last = run.Get(key, mob.Health);
            run.Set(key, mob.Health);
            return mob.Health < last;
        }

        /// <summary>The participating player standing closest to a spot: whoever just clicked something there.</summary>
        private static GamePlayer NearestPlayer(TrialRun run, GameObject spot)
        {
            IEnumerable<GamePlayer> players = run.Players.Concat(run.Leader.Group?.GetPlayersInTheGroup() ?? Enumerable.Empty<GamePlayer>());
            return players.Distinct().Where(player => player.IsAlive && player.ObjectState == GameObject.eObjectState.Active && player.CurrentRegion == run.Starter.CurrentRegion)
                .OrderBy(player => (long)(player.X - spot.X) * (player.X - spot.X) + (long)(player.Y - spot.Y) * (player.Y - spot.Y))
                .FirstOrDefault() ?? run.Leader;
        }

        /// <summary>Monsters that leave by themselves after a few seconds (Kynthia's massive cobra, Talos's purogenes).</summary>
        private static void SpawnFor(TrialRun run, TrialMobSpec spec, int seconds)
        {
            TrialMob mob = run.Spawn(spec);
            if (mob == null)
                return;
            List<(TrialMob Mob, int Until)> brief = run.Get<List<(TrialMob, int)>>("brief") ?? new();
            brief.Add((mob, run.Get("clock", 0) + seconds));
            run.Set("brief", brief);
        }

        private static void Expire(TrialRun run)
        {
            int now = Count(run, "clock");
            List<(TrialMob Mob, int Until)> brief = run.Get<List<(TrialMob, int)>>("brief");
            if (brief == null)
                return;
            foreach ((TrialMob mob, int until) in brief.Where(entry => entry.Item2 <= now).ToList())
            {
                if (mob.ObjectState == GameObject.eObjectState.Active)
                    mob.Delete();
                brief.Remove((mob, until));
            }
        }

        /// <summary>Lets a run pick one chest of several in its own game of chance.</summary>
        private static List<TrialMobSpec> Chests(string name, TrialPoint[] spots, string key, string rightText, string wrongText, Action<TrialRun, TrialMob> wrong)
        {
            List<TrialMobSpec> chests = new();
            for (int i = 0; i < spots.Length; i++)
            {
                int index = i;
                chests.Add(Clickable(name, Reliquary, 28, spots[i], "The lid gives under your hand.", "chest", (run, mob) =>
                {
                    if (run.Get(key, -1) == index)
                    {
                        run.Tell(rightText);
                        run.Set(key + ".found", true);
                        return;
                    }
                    run.Tell(wrongText);
                    wrong(run, mob);
                }));
            }
            return chests;
        }

        private static void ClearChests(TrialRun run)
        {
            foreach (TrialMob chest in run.Mobs("chest").ToList())
                chest.Delete();
        }

        private static TrialDefinition AgnesCrystal()
        {
            TrialPoint[] guards =
            {
                new(368773, 692749, 8104),
                new(366581, 693245, 8161),
                new(369404, 695535, 8161),
                new(371334, 691608, 8161),
            };

            // Prima: stone statues have very high defence, veined marble very high offence, bronze a mixture.
            static TrialMobSpec StoneStatue(int x, int y, int z) => Foe("Stone Statue", Stone, 60, 100, 8000, 0.8, new TrialPoint(x, y, z), "bisul.statue");
            static TrialMobSpec VeinedStatue(int x, int y, int z) => Foe("Veined Marble Statue", Marble, 60, 100, 4500, 1.6, new TrialPoint(x, y, z), "bisul.statue");
            static TrialMobSpec BronzeStatue(int x, int y, int z) => Foe("Bronze Statue", Bronze, 60, 100, 6000, 1.1, new TrialPoint(x, y, z), "bisul.statue");

            // Bisul's own spells: an area burn around him, and he mends his statues while they fight.
            static TrialPhase Powers(TrialPhase phase)
            {
                phase.Pulses.Add(new TrialPulse { IntervalSeconds = 10, Damage = 160, Radius = 600, SourceTag = "bisul", DamageType = eDamageType.Heat, Message = "Bisul's flames roll over the statues' foes!" });
                Action<TrialRun> tick = phase.OnTick;
                phase.OnTick = run =>
                {
                    tick?.Invoke(run);
                    if (Count(run, "bisul.mend") % 10 != 0 || Gone(run, "bisul"))
                        return;
                    foreach (TrialMob statue in run.Mobs("bisul.statue").Where(statue => statue.HealthPercent < 100))
                        Mend(statue, 5);
                };
                return phase;
            }

            static TrialPhase Set(string announce, TrialPoint flight, TrialMobSpec first, TrialMobSpec second, TrialMobSpec third) => Powers(new()
            {
                Announce = announce,
                Mobs = { first, second, third },
                OnStart = run =>
                {
                    foreach (TrialMob bisul in run.Mobs("bisul").ToList())
                        Relocate(run, bisul, flight);
                },
                Done = NoneRequiredLeft,
            });

            return new TrialDefinition
            {
                ML = 8,
                Step = 1,
                Name = "Agne's Crystal",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Oceanus,
                Anchor = new TrialPoint(367575, 695413, 8133),
                StarterName = ServantName,
                StarterModel = Servant,
                Replaces = new[] { "Bisul", "Bronze Statue", "Stone Statue", "Veined Marble Statue" },
                Intro = "My master cannot wake Agne without the control crystal, and the jinni Bisul keeps it. He uses it to drive statues against the centaurs. While statues fight beside him nothing you do will touch the jinni, and he mends them as they fight. Break the three beside him and he will run for three more, four times in all. Then he is yours.",
                Victory = "Bisul falls, and Agne's control crystal is yours.",
                TimeLimitSeconds = 2400,
                Phases =
                {
                    Powers(new TrialPhase
                    {
                        Announce = "Bisul sees you, and the first statues step down.",
                        Mobs =
                        {
                            new TrialMobSpec
                            {
                                Name = "Bisul",
                                Model = Jinni,
                                Level = 66,
                                Size = 60,
                                Health = 45000,
                                DamageFactor = 1.6,
                                At = guards[0],
                                Tag = "bisul",
                                Immune = true,
                                Required = false,
                                MeleeDamage = eDamageType.Heat,
                            },
                            BronzeStatue(368894, 692606, 8104),
                            StoneStatue(368891, 692578, 8104),
                            VeinedStatue(368803, 692565, 8104),
                        },
                        Done = NoneRequiredLeft,
                    }),
                    Set("Bisul flees west to the second set!", guards[1],
                        BronzeStatue(366581, 693052, 8161), StoneStatue(367100, 693244, 8161), VeinedStatue(366578, 693437, 8161)),
                    Set("Bisul flees south to the third set!", guards[2],
                        BronzeStatue(369597, 695755, 8161), StoneStatue(369404, 695313, 8161), VeinedStatue(369213, 695754, 8161)),
                    Set("Bisul flees east to the last set!", guards[3],
                        BronzeStatue(371223, 691301, 8161), StoneStatue(371086, 691821, 8161), VeinedStatue(371597, 691701, 8161)),
                    Powers(new TrialPhase
                    {
                        Announce = "No statue is left to shield Bisul. Strike!",
                        OnStart = run => run.SetImmune("bisul", false),
                        Done = run => Gone(run, "bisul"),
                    }),
                },
            };
        }

        private static TrialDefinition AgnesShield()
        {
            TrialPoint academy = new(441650, 712211, 13515);
            TrialPoint champion = new(442176, 712735, 13528);
            TrialPoint commander = new(442702, 712218, 13515);
            TrialPoint general = new(442174, 713593, 13528);
            TrialPoint lord = new(442176, 713148, 13558);

            static TrialMobSpec Guard(int x, int y) => Foe("Tholos' soldier", Soldier, 60, 75, 4000, 0.9, new TrialPoint(x, y, 13515), melee: eDamageType.Slash);

            return new TrialDefinition
            {
                ML = 8,
                Step = 2,
                Name = "Agne's Shield",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Oceanus,
                Anchor = academy,
                StarterName = "Tholos at the Military Academy",
                StarterModel = Soldier,
                Replaces = new[] { "Tholos at the Military Academy", "Trainee", "Tholos' soldier", "Tholos Tournament Champion", "Commander Tholos", "General Tholos", "Lord Tholos" },
                Intro = "An inscription runs round this base: the first of Tholos's statues, the cadet. Agne's shield was lost, but his brother Tholos carried its twin, and every age of Tholos stands in this hall in the order he lived it. Read each one and it will test you, up to the Lord himself. He strikes without pity and turns aside steel. Do not stand before him.",
                Victory = "Lord Tholos topples, and the twin of Agne's shield is yours.",
                TimeLimitSeconds = 2400,
                OnEnd = (run, _) => Show(run),
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The cadet Tholos steps down from his plinth, and his trainees with him.",
                        Mobs =
                        {
                            Foe("Tholos at the Military Academy", Soldier, 60, 62, 6000, 0.9, academy, melee: eDamageType.Slash),
                            Foe("Trainee", Soldier, 58, 55, 3000, 0.7, new TrialPoint(441643, 712546, 13515), melee: eDamageType.Slash),
                            Foe("Trainee", Soldier, 58, 55, 3000, 0.7, new TrialPoint(441661, 712949, 13515), melee: eDamageType.Slash),
                            Foe("Trainee", Soldier, 58, 55, 3000, 0.7, new TrialPoint(441980, 712211, 13515), melee: eDamageType.Slash),
                        },
                        OnStart = Hide,
                    },
                    new TrialPhase
                    {
                        Announce = "An inscription glows on the Tournament Champion.",
                        Mobs = { Clickable("Tholos Tournament Champion", Soldier, 72, champion, "'Glory first, and then the field,' reads the base. The stone stirs.") },
                    },
                    new TrialPhase
                    {
                        Announce = "The Champion fights alone.",
                        Mobs = { Foe("Tholos Tournament Champion", Soldier, 62, 72, 14000, 1.2, champion, melee: eDamageType.Slash) },
                    },
                    new TrialPhase
                    {
                        Announce = "An inscription glows on the Commander.",
                        Mobs = { Clickable("Commander Tholos", Soldier, 82, commander, "'Who follows, wins,' reads the base. The soldiers turn their heads.") },
                    },
                    new TrialPhase
                    {
                        Announce = "Commander Tholos calls three soldiers to him.",
                        Mobs =
                        {
                            Foe("Commander Tholos", Soldier, 65, 82, 22000, 1.4, commander, melee: eDamageType.Slash),
                            Guard(442370, 712211),
                            Guard(442703, 712546),
                            Guard(442696, 712958),
                        },
                    },
                    new TrialPhase
                    {
                        Announce = "An inscription glows on the General.",
                        Mobs = { Clickable("General Tholos", Soldier, 92, general, "'All stand when I stand,' reads the base, and the whole hall answers.") },
                    },
                    new TrialPhase
                    {
                        Announce = "The whole room comes to life! Break the eight soldiers while someone keeps the General busy.",
                        Mobs =
                        {
                            Foe("General Tholos", Soldier, 67, 92, 40000, 1.7, general, melee: eDamageType.Slash),
                            Guard(441651, 713348),
                            Guard(442707, 713350),
                            Guard(441638, 713766),
                            Guard(442705, 713771),
                            Guard(441645, 714096),
                            Guard(441972, 714088),
                            Guard(442389, 714091),
                            Guard(442706, 714086),
                        },
                    },
                    new TrialPhase
                    {
                        Announce = "One statue is left, and its inscription burns.",
                        Mobs = { Clickable("Lord Tholos", Soldier, 110, lord, "'Truth shall always succeed,' reads the base. The Lord lifts his head.") },
                    },
                    new TrialPhase
                    {
                        Announce = "Lord Tholos walks, his sword aflame. Stay behind him!",
                        Mobs = { Foe("Lord Tholos", Soldier, 70, 110, 80000, 2.2, lord, "lord", eDamageType.Slash) },
                        Pulses = { new TrialPulse { IntervalSeconds = 6, Damage = 150, Radius = 300, SourceTag = "lord", DamageType = eDamageType.Heat } },
                        OnStart = run =>
                        {
                            foreach (TrialMob tholos in run.Mobs("lord"))
                                Resist(tholos, 30, eProperty.Resist_Crush, eProperty.Resist_Slash, eProperty.Resist_Thrust);
                        },
                    },
                },
            };
        }

        private static TrialDefinition AgnesSword()
        {
            TrialPoint ibn = new(404813, 703252, 12214);
            TrialPoint[] chests = Enumerable.Range(0, 5).Select(i => new TrialPoint(404213 + i * 300, 703852, 12214)).ToArray();
            TrialPoint[] hideouts = { new(405700, 702700, 12214), new(404000, 702500, 12214), new(405600, 703900, 12214) };

            static TrialMobSpec Sword(TrialPoint at, int kind) => kind switch
            {
                1 => Foe("animate burning sword", Stone, 60, 40, 3000, 0.9, at, "sword", eDamageType.Heat, false),
                2 => Foe("animate freezing sword", Stone, 60, 40, 3000, 0.9, at, "sword", eDamageType.Cold, false),
                _ => Foe("animate sword", Stone, 60, 40, 3000, 0.9, at, "sword", eDamageType.Slash, false),
            };

            return new TrialDefinition
            {
                ML = 8,
                Step = 3,
                Name = "Agne's Sword",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Oceanus,
                Anchor = ibn,
                StarterName = "Ibn",
                StarterModel = Jinni,
                Replaces = new[] { "Ibn" },
                Intro = "A sword? Agne's sword? I may have seen such a thing. I will even give you a key, one at a time. Here are five chests, friend, and one of them holds what you seek. The rest hold what I find funny, and they keep on being funny until you find the right one. And if you do find the sword, do not imagine I will just hand it over.",
                Victory = "Ibn scatters on the wind, and Agne's sword drops into the grass.",
                TimeLimitSeconds = 2400,
                OnEnd = (run, _) => Show(run),
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Five chests stand in the grass. Ibn hands you a key. Choose.",
                        Mobs = Chests("Ibn's chest", chests, "sword", "Agne's sword rises out of the chest and hangs in the air! Ibn laughs and steps between you and it.", "Blades rise out of the chest, and more will follow! Take another key from Ibn.",
                            (run, mob) =>
                            {
                                List<TrialPoint> wrong = run.Get<List<TrialPoint>>("wrong") ?? new();
                                wrong.Add(new TrialPoint(mob.X, mob.Y, mob.Z));
                                run.Set("wrong", wrong);
                                run.Spawn(Sword(new TrialPoint(mob.X, mob.Y, mob.Z), 0));
                            }),
                        OnStart = run => run.Set("sword", Util.Random(0, chests.Length - 1)),
                        // Prima: a wrong chest keeps sending floating swords every 15 seconds.
                        OnTick = run =>
                        {
                            List<TrialPoint> wrong = run.Get<List<TrialPoint>>("wrong");
                            if (wrong == null || Count(run, "chest.swords") % 15 != 0 || run.Mobs("sword").Count() >= 4)
                                return;
                            foreach (TrialPoint point in wrong)
                                run.Spawn(Sword(point, 0));
                        },
                        Done = run => run.Get("sword.found", false),
                    },
                    new TrialPhase
                    {
                        Announce = "'Mine,' says Ibn. Take it from him. When he turns to dust, he cannot be hurt until he reforms somewhere else.",
                        Mobs =
                        {
                            new TrialMobSpec
                            {
                                Name = "Ibn",
                                Model = Jinni,
                                Level = 68,
                                Size = 60,
                                Health = 60000,
                                DamageFactor = 1.8,
                                At = ibn,
                                Tag = "ibn",
                                Required = false,
                                MeleeDamage = eDamageType.Energy,
                            },
                        },
                        Pulses = { new TrialPulse { IntervalSeconds = 4, Damage = 160, Radius = 300, SourceTag = "storm.hostile", DamageType = eDamageType.Slash } },
                        OnStart = run =>
                        {
                            ClearChests(run);
                            Hide(run);
                        },
                        OnTick = run =>
                        {
                            TrialMob trickster = run.Mobs("ibn").FirstOrDefault();
                            if (trickster == null)
                                return;

                            int storm = run.Get("storm.left", 0);
                            if (storm > 0)
                            {
                                run.Set("storm.left", storm - 1);
                                if (storm > 1)
                                    return;
                                foreach (TrialMob dust in run.Mobs("storm").Concat(run.Mobs("storm.hostile")).ToList())
                                    dust.Delete();
                                run.SetImmune("ibn", false);
                                run.Tell("The whirlwinds die away, and Ibn stands whole again.");
                                return;
                            }

                            int passed = IbnStorms(trickster.HealthPercent);
                            if (passed > run.Get("storms", 0))
                            {
                                run.Set("storms", passed);
                                run.Set("storm.left", StormSeconds);
                                run.SetImmune("ibn", true);
                                run.Tell("Ibn becomes a whirlwind, and four more spin up around him!");
                                TrialPoint from = new(trickster.X, trickster.Y, trickster.Z);
                                int hostile = Util.Random(0, 3);
                                int index = 0;
                                // Prima: four decoy whirlwinds, with a small chance of attacking.
                                foreach (TrialPoint point in Circle(from, 250, 4))
                                {
                                    TrialMobSpec dust = Foe("whirlwind", Whirlwind, 66, 70, 5000, 0.4, point, index++ == hostile ? "storm.hostile" : "storm", eDamageType.Slash, false);
                                    dust.Immune = true;
                                    dust.AggroRange = 0;
                                    run.Spawn(dust);
                                }
                                Relocate(run, trickster, hideouts[passed - 1]);
                                return;
                            }

                            // Prima: hits on Ibn may call an animate sword (slash), a burning one (fire) or a freezing one (ice).
                            if (WasHit(run, trickster) && Util.Chance(10) && run.Mobs("sword").Count() < 6)
                            {
                                run.Tell("A blade answers Ibn's call!");
                                run.Spawn(Sword(Circle(new TrialPoint(trickster.X, trickster.Y, trickster.Z), 150, 1).First(), Util.Random(0, 2)));
                            }
                        },
                        Done = run => Gone(run, "ibn"),
                    },
                },
            };
        }

        private static TrialDefinition CentaursHunt()
        {
            TrialPoint camp = new(365582, 706299, 8080);
            TrialPoint[] ring = Circle(camp, 300, 7).ToArray();

            return new TrialDefinition
            {
                ML = 8,
                Step = 4,
                Name = "Centaur's Hunt",
                Scope = TrialScope.Group,
                Zone = TrialZone.Oceanus,
                Anchor = new TrialPoint(366490, 709300, 8083),
                StarterName = ServantName,
                StarterModel = Servant,
                Replaces = new[] { "Chief Arsene", "Chief Oighrig" },
                Intro = "One of the centaur lieutenants keeps Agne's left leg in his camp, and the old trial asked every hero to beat a lieutenant anyway. Their scout is already looking for you. When he finds you he blows his horn, and the lieutenant rides out to that spot with his whole warband: spears, bows, a healer and a caster. The lieutenant is the one that matters.",
                Victory = "Chief Oighrig falls, and Agne's left leg is yours.",
                TimeLimitSeconds = 1500,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "A centaur scout has spotted you and comes to look.",
                        Mobs = { Foe("centaur scout superior", Bowman, 60, 64, 3000, 0.8, new TrialPoint(366994, 709577, 8080), "scout", eDamageType.Thrust, false) },
                        Done = run => Gone(run, "scout") || run.Mobs("scout").Any(scout => scout.InCombat),
                    },
                    new TrialPhase
                    {
                        Announce = "The scout's horn sounds! Chief Oighrig gallops out of his camp with his warband.",
                        Mobs =
                        {
                            Foe("Chief Oighrig", Bowman, 65, 65, 18000, 1.4, camp, "band", eDamageType.Thrust),
                            Foe("centaur impaler", Centaur, 62, 57, 4000, 0.8, ring[0], "band", eDamageType.Thrust, false),
                            Foe("centaur impaler", Centaur, 62, 57, 4000, 0.8, ring[1], "band", eDamageType.Thrust, false),
                            Foe("centaur archer", Bowman, 60, 54, 3000, 0.7, ring[2], "band", eDamageType.Thrust, false),
                            Foe("centaur archer", Bowman, 60, 54, 3000, 0.7, ring[3], "band", eDamageType.Thrust, false),
                            Foe("centaur healer", Bowman, 60, 60, 2500, 0.6, ring[4], "healer", required: false),
                            Foe("centaur arcanist", Centaur, 62, 57, 2500, 0.6, ring[5], "nuker", required: false),
                            Foe("centaur advisor", Bowman, 60, 62, 2500, 0.6, ring[6], "band", required: false),
                        },
                        Pulses = { new TrialPulse { IntervalSeconds = 9, Damage = 150, Radius = 1200, SourceTag = "nuker", DamageType = eDamageType.Heat, Message = "The centaur arcanist hurls fire!" } },
                        OnStart = run =>
                        {
                            foreach (TrialMob centaur in run.Mobs().Where(mob => mob.Spec.Tag is "band" or "healer" or "nuker"))
                                Aggro(centaur, run.Leader);
                        },
                        OnTick = run =>
                        {
                            Rally(run, "band");
                            if (Count(run, "centaur.heal") % 8 != 0 || Gone(run, "healer"))
                                return;
                            foreach (TrialMob chief in run.Mobs().Where(mob => mob.Spec.Required && mob.HealthPercent < 100))
                                Mend(chief, 4);
                        },
                    },
                },
            };
        }

        private static TrialDefinition TreasureOfTheCyclops()
        {
            TrialPoint gaspara = new(410339, 702297, 11857);
            TrialPoint[] chests = Enumerable.Range(0, 5).Select(i => new TrialPoint(409739 + i * 300, 702797, 11857)).ToArray();
            (string Name, ushort Model, eDamageType Damage)[] figments =
            {
                ("fire figment", FireSpirit, eDamageType.Heat),
                ("wind figment", Whirlwind, eDamageType.Cold),
                ("energy figment", FireSpirit, eDamageType.Energy),
                ("stone figment", Stone, eDamageType.Crush),
            };

            static TrialMobSpec Son(string name, ushort model, int x, int y, int z) => Foe(name, model, 62, 60, 5000, 1.0, new TrialPoint(x, y, z), "son", eDamageType.Thrust);
            static TrialMobSpec Warder(int x, int y, int z) => Foe("controlled statue", 986, 60, 50, 3000, 0.9, new TrialPoint(x, y, z), "ghita", required: false);

            // Prima: a wrong chest wakes a level 65 figment; beat it and Gaspara deals the chests again, the treasure moved.
            void Deal(TrialRun run)
            {
                ClearChests(run);
                run.Set("cyclops", Util.Random(0, chests.Length - 1));
                foreach (TrialMobSpec chest in Chests("Gaspara's chest", chests, "cyclops", "Kratos's chest lies inside. Take it back to him.", "The chest bursts open and a figment climbs out!",
                    (r, mob) =>
                    {
                        ClearChests(r);
                        var figment = figments[Util.Random(0, figments.Length - 1)];
                        TrialMobSpec spec = Foe(figment.Name, figment.Model, 65, 70, 10000, 1.3, new TrialPoint(mob.X, mob.Y, mob.Z), "figment", figment.Damage, false);
                        spec.OnDeath = (again, _) =>
                        {
                            again.Tell("Gaspara laughs, waves her hand, and the chests stand in a row again.");
                            Deal(again);
                        };
                        r.Spawn(spec);
                    }))
                    run.Spawn(chest);
            }

            return new TrialDefinition
            {
                ML = 8,
                Step = 5,
                Name = "Treasure of the Cyclops",
                Scope = TrialScope.Group,
                Zone = TrialZone.Oceanus,
                Anchor = Kratos,
                StarterName = "Kratos",
                StarterModel = 1229,
                Replaces = new[] { "Kratos", "Ghita", "Gaspara", "controlled statue", "Adras son of Creon", "Maur son of Creon", "Nemos son of Creon", "Piet son of Creon", "Sarpedon son of Creon", "Teurcer son of Creon" },
                Intro = "The leg? Yes, I have your bronze leg, and I want payment. A ketos fillet and soothing spring water, those my crate here already holds if you will fetch them out. Then Ghita's scrying stone from the northern ruins, the heads of Creon's six eldest sons, and my own chest out of Gaspara's game in the Glades. All of it at once, or no leg.",
                Victory = "Kratos counts it all twice, grunts, and drops Agne's leg at your feet.",
                TimeLimitSeconds = 2400,
                LeashRange = 60000,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Open the crate beside Kratos.",
                        Mobs =
                        {
                            Clickable("Kratos's fish crate", Reliquary, 30, new TrialPoint(455006, 686214, 13786), "A giant ketos fillet, still cold. You set it aside for him."),
                            Clickable("Kratos's water cask", Reliquary, 30, new TrialPoint(455006, 686414, 13786), "Soothing water of the Golden Oasis, sealed and heavy. You set it aside for him."),
                        },
                    },
                    new TrialPhase
                    {
                        Announce = "Now Ghita. Her ruins lie far to the northwest, and her statues wake when she is attacked.",
                        Mobs =
                        {
                            Foe("Ghita", 1183, 66, 66, 18000, 1.4, new TrialPoint(428874, 677215, 12144), "ghita", eDamageType.Slash),
                            Warder(428606, 677215, 12144),
                            Warder(429275, 676815, 12144),
                            Warder(429408, 677483, 12144),
                            Warder(429676, 677216, 12141),
                            Warder(428810, 676350, 12144),
                            Warder(429205, 676350, 12144),
                            Warder(429943, 677215, 12129),
                            Warder(429945, 677633, 12137),
                        },
                        OnTick = run => Rally(run, "ghita"),
                    },
                    new TrialPhase
                    {
                        Announce = "Ghita's scrying stone is yours. Now the six sons of Creon, in the village south of her ruins. Strike one and all six come.",
                        Mobs =
                        {
                            Son("Adras son of Creon", 1215, 442621, 695271, 11976),
                            Son("Maur son of Creon", 1214, 440037, 696377, 11992),
                            Son("Nemos son of Creon", 1214, 442942, 694081, 12397),
                            Son("Piet son of Creon", 1215, 441401, 695793, 12009),
                            Son("Sarpedon son of Creon", 1213, 442422, 697718, 11992),
                            Son("Teurcer son of Creon", 1213, 444048, 696755, 11976),
                        },
                        OnTick = run => Rally(run, "son"),
                    },
                    new TrialPhase
                    {
                        Announce = "Six heads, bagged. Last, go to Gaspara in Green Glades and pick the chest that holds Kratos's treasure.",
                        OnStart = run =>
                        {
                            Deal(run);
                            run.Spawn(new TrialMobSpec { Name = "Gaspara", Model = 1195, Level = 65, Size = 70, At = gaspara, Objective = true, ObjectiveText = "Gaspara only smiles and points at the chests.", Required = false });
                        },
                        Done = run => run.Get("cyclops.found", false),
                    },
                    new TrialPhase
                    {
                        Announce = "Bring it all back to Kratos.",
                        OnStart = run =>
                        {
                            ClearChests(run);
                            foreach (TrialMob figment in run.Mobs("figment").ToList())
                                figment.Delete();
                        },
                        Done = run => Near(run.Leader, Kratos, 500),
                    },
                },
            };
        }

        private static TrialDefinition AgnesWill()
        {
            TrialPoint home = new(413031, 673814, 10539);
            TrialPoint statueAt = new(412451, 673534, 10416);
            TrialPoint[] flights = { new(413700, 674300, 10539), new(412500, 674400, 10539) };

            return new TrialDefinition
            {
                ML = 8,
                Step = 6,
                Name = "Agne's Will",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Oceanus,
                Anchor = new TrialPoint(412451, 674100, 10416),
                StarterName = ServantName,
                StarterModel = Servant,
                Replaces = new[] { "Dawar" },
                Intro = "Dawar set Agne's head on a great headless statue and bound it to him with a stolen control crystal. Both must die. Dawar fights from afar, and when his statue calls he comes in a whirlwind and burns everything near it. The statue sometimes sleeps to mend itself, and chips struck from it walk on their own. Kill Dawar first if you can.",
                Victory = "Dawar and his statue are dead, and Agne's bronze head stares up at you.",
                TimeLimitSeconds = 1800,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Dawar sends his statue forward and backs away to cast.",
                        Mobs =
                        {
                            Foe("controlled statue", 989, 64, 72, 25000, 1.5, statueAt, "statue"),
                            new TrialMobSpec
                            {
                                Name = "Dawar",
                                Model = 1194,
                                Level = 68,
                                Size = 60,
                                Health = 50000,
                                DamageFactor = 1.3,
                                At = home,
                                Tag = "dawar",
                                MeleeDamage = eDamageType.Energy,
                            },
                        },
                        Pulses = { new TrialPulse { IntervalSeconds = 9, Damage = 200, Radius = 1500, SourceTag = "dawar", DamageType = eDamageType.Energy, Message = "Dawar's bolts of air strike from afar!" } },
                        OnTick = run =>
                        {
                            int clock = Count(run, "dawar.clock");
                            TrialMob statue = run.Mobs("statue").FirstOrDefault();
                            TrialMob dawar = run.Mobs("dawar").FirstOrDefault();

                            if (dawar != null)
                            {
                                int passed = DawarFlights(dawar.HealthPercent);
                                if (passed > run.Get("fled", 0))
                                {
                                    run.Set("fled", passed);
                                    run.Tell("Dawar flees across the clearing!");
                                    Relocate(run, dawar, flights[passed - 1]);
                                }
                                else if (run.Get("called", 0) == clock)
                                    Relocate(run, dawar, flights[clock % flights.Length]);
                            }

                            if (statue == null)
                                return;

                            int sleep = run.Get("sleep", 0);
                            if (sleep > 0)
                            {
                                run.Set("sleep", sleep - 1);
                                Mend(statue, 3);
                                if (sleep == 1)
                                {
                                    statue.Immune = false;
                                    run.Tell("The statue wakes.");
                                }
                                return;
                            }
                            // Prima: the statue goes dormant for ten seconds and mends fast; it cannot be hurt meanwhile.
                            if (clock % 40 == 0 && statue.HealthPercent < 100)
                            {
                                run.Set("sleep", 10);
                                statue.Immune = true;
                                run.Tell("The controlled statue goes still and begins to mend!");
                                return;
                            }
                            // Prima: melee on the statue can chip off a chunk that fights under Dawar's control.
                            if (WasHit(run, statue) && Util.Chance(8) && run.Mobs("chip").Count() < 4)
                            {
                                run.Tell("A chunk breaks from the statue and gets up!");
                                run.Spawn(Foe("animated statue", 986, 60, 35, 2500, 0.8, Circle(new TrialPoint(statue.X, statue.Y, statue.Z), 150, 1).First(), "chip", required: false));
                            }
                            // Prima: the statue calls Dawar, who comes as a whirlwind and burns everything near it.
                            if (dawar != null && clock % 30 == 0 && statue.InCombat)
                            {
                                run.Tell("The statue calls, and Dawar comes in a whirlwind of burning air!");
                                Relocate(run, dawar, new TrialPoint(statue.X + 120, statue.Y, statue.Z));
                                foreach (GameLiving victim in run.Hostiles(statue, 900))
                                    victim.TakeDamage(dawar, eDamageType.Energy, (int)(300 * run.DamageScale), 0);
                                run.Set("called", clock + 4);
                            }
                        },
                    },
                },
            };
        }

        private static TrialDefinition AgnesMight()
        {
            TrialPoint camp = new(397333, 664878, 8058);
            TrialPoint[] spots = Circle(camp, 500, 6).ToArray();

            // Prima: level 56 to look at, twice the hit points, half the damage taken, and mending below 90%.
            TrialMobSpec Statue(TrialPoint at) => new()
            {
                Name = "cracked self-maintaining statue",
                Model = Servant,
                Level = 56,
                Size = 50,
                Health = 7000,
                DamageFactor = 0.9,
                At = at,
                AggroRange = 300,
                Tag = "statue",
                Required = false,
                OnDeath = (run, mob) =>
                {
                    int kills = Count(run, "kills");
                    if (TorsoFound(kills, Util.RandomDouble()))
                    {
                        run.Tell("You have found Agne's torso in the wreck of that statue!");
                        run.Set("torso", true);
                        return;
                    }
                    run.Spawn(Statue(spots[kills % spots.Length]));
                },
            };

            return new TrialDefinition
            {
                ML = 8,
                Step = 7,
                Name = "Agne's Might",
                Scope = TrialScope.Group,
                Zone = TrialZone.Oceanus,
                Anchor = camp,
                StarterName = ServantName,
                StarterModel = Servant,
                Intro = "These statues mend themselves and each other from whatever bronze comes to hand, and one of them has used Agne's torso for the work. There is no telling which. Break them until it shows, and hit hard: one that is only scratched will close its wounds.",
                Victory = "Agne's torso comes away from the wreck in one piece.",
                TimeLimitSeconds = 1500,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The statues turn toward you.",
                        Mobs = spots.Select(Statue).ToList(),
                        OnTick = run =>
                        {
                            if (Count(run, "mend") % 5 != 0)
                                return;
                            foreach (TrialMob statue in run.Mobs("statue").Where(statue => StatueMends(statue.HealthPercent)))
                                Mend(statue, 2);
                        },
                        Done = run => run.Get("torso", false),
                    },
                },
            };
        }

        private static TrialDefinition GorgonsSecret()
        {
            TrialPoint kynthiaAt = new(485110, 715254, 15208);
            (string Name, TrialPoint At)[] sisters =
            {
                ("Kaeriand", new TrialPoint(478571, 716158, 15165)),
                ("Lomandeara", new TrialPoint(484736, 707904, 15240)),
                ("Giathna", new TrialPoint(481363, 711803, 15238)),
                ("Quaria", new TrialPoint(482268, 715065, 15208)),
                ("Minarak", new TrialPoint(484397, 712332, 15213)),
            };
            const int Neighbours = 5500, CobrasPerCall = 4, CallSeconds = 120;

            static TrialMobSpec Snake(TrialPoint at) => Foe("summoned cobra", Cobra, 60, 45, 2000, 0.7, at, "cobra", eDamageType.Thrust, false);

            // Prima: a gorgon under attack is helped by cobras from each neighbouring lair whose gorgon is not fighting,
            // again every two minutes. Rushing Kynthia meets about 25 of them; the edges first meet the fewest.
            static void SendCobras(TrialRun run)
            {
                int clock = Count(run, "gorgon.clock");
                List<TrialMob> gorgons = run.Mobs("sister").Concat(run.Mobs("kynthia")).ToList();
                foreach (TrialMob engaged in gorgons.Where(gorgon => gorgon.InCombat))
                {
                    foreach (TrialMob helper in run.Mobs("sister").Where(sister => sister != engaged && !sister.InCombat && engaged.IsWithinRadius(sister, Neighbours)))
                    {
                        string key = $"sent.{helper.Name}";
                        int last = run.Get(key, -CallSeconds);
                        if (clock - last < CallSeconds || run.Mobs("cobra").Count() >= CobraCap)
                            continue;
                        run.Set(key, clock);
                        run.Tell($"{helper.Name} sends her cobras to {engaged.Name}!");
                        foreach (TrialPoint point in Circle(new TrialPoint(engaged.X, engaged.Y, engaged.Z), 240, CobrasPerCall))
                        {
                            TrialMob cobra = run.Spawn(Snake(point));
                            if (engaged.TargetObject is GameLiving prey)
                                Aggro(cobra, prey);
                        }
                    }
                }
            }

            return new TrialDefinition
            {
                ML = 8,
                Step = 8,
                Name = "Gorgon's Secret",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Oceanus,
                Anchor = new TrialPoint(481000, 712100, 15238),
                StarterName = ServantName,
                StarterModel = Servant,
                Replaces = new[] { "Lomandeara", "Giathna", "Quaria", "Minarak", "Kaeriand", "Kynthia", "Kynthia's Guide", "summoned cobra" },
                Intro = "The gorgons remember what the old trials cost them, and they have hidden Agne's left arm to spite us. Kynthia has it, at the head of a triangle of lairs. Attack any gorgon and her neighbours send cobras, unless they are busy fighting themselves. Take the edges first: Kaeriand and Lomandeara have only one or two neighbours. Kynthia is blind; her Guide picks her targets, so kill the Guide. Magic slides off her, steel does not.",
                Victory = "Kynthia lies still, and Agne's left arm is found beneath her.",
                TimeLimitSeconds = 2400,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The gorgons stir in their lairs. Kynthia waits at the far end.",
                        Mobs = sisters.Select(sister => Foe(sister.Name, Gorgon, 66, 65, 18000, 1.3, sister.At, "sister", eDamageType.Thrust, false))
                            .Append(Foe("Kynthia's Guide", Gorgon, 66, 63, 12000, 1.1, new TrialPoint(485177, 715236, 15208), "guide", eDamageType.Thrust, false))
                            .Append(new TrialMobSpec
                            {
                                Name = "Kynthia",
                                Model = Gorgon,
                                Level = 69,
                                Size = 72,
                                Health = 40000,
                                DamageFactor = 1.3,
                                At = kynthiaAt,
                                Tag = "kynthia",
                                Required = false,
                                MeleeDamage = eDamageType.Thrust,
                            })
                            .ToList(),
                        OnStart = run =>
                        {
                            foreach (TrialMob gorgon in run.Mobs("kynthia"))
                            {
                                Resist(gorgon, 70, eProperty.Resist_Body, eProperty.Resist_Cold, eProperty.Resist_Energy, eProperty.Resist_Heat, eProperty.Resist_Matter, eProperty.Resist_Spirit);
                                Resist(gorgon, -20, eProperty.Resist_Crush, eProperty.Resist_Slash, eProperty.Resist_Thrust);
                            }
                        },
                        OnTick = run =>
                        {
                            Expire(run);
                            SendCobras(run);
                            TrialMob kynthia = run.Mobs("kynthia").FirstOrDefault();
                            TrialMob guide = run.Mobs("guide").FirstOrDefault();
                            if (kynthia == null || guide == null || !kynthia.InCombat && !guide.InCombat)
                                return;
                            if (!kynthia.InCombat && guide.TargetObject is GameLiving spotted)
                                Aggro(kynthia, spotted);
                            int clock = Count(run, "kynthia.clock");
                            // Prima: while her Guide lives, Kynthia bolts the targets it picks and calls a massive cobra.
                            if (clock % 6 == 0)
                            {
                                foreach (GameLiving victim in run.Hostiles(kynthia, 1500))
                                    victim.TakeDamage(kynthia, eDamageType.Spirit, (int)(220 * run.DamageScale), 0);
                                run.Tell("The Guide points, and Kynthia's bolts strike!");
                            }
                            if (clock % 40 == 0)
                            {
                                run.Tell("Kynthia calls up a massive cobra!");
                                TrialMobSpec massive = Foe("summoned massive cobra", Cobra, 65, 80, 4000, 2.0, Circle(kynthiaAt, 200, 1).First(), "massive", eDamageType.Body, false);
                                SpawnFor(run, massive, 20);
                            }
                        },
                        Done = run => Gone(run, "kynthia"),
                    },
                },
            };
        }

        private static TrialDefinition AgnesSpareArm()
        {
            TrialPoint exit = new(439050, 661800, 12101);
            TrialPoint hall = new(439050, 665600, 12110);

            static TrialMobSpec Chrysiron(int x, int y, int z, bool required = true) => Foe("chrysiron statue", Marble, 67, 100, 6000, 1.0, new TrialPoint(x, y, z), "hall", required: required);
            static TrialMobSpec Helicaon(int x, int y, int z, bool required = true) => Foe("helicaon statue", Marble, 69, 120, 8000, 1.2, new TrialPoint(x, y, z), "hall", required: required);
            static TrialPulse IceBlast() => new() { IntervalSeconds = 8, Damage = 120, Radius = 350, SourceTag = "hall", DamageType = eDamageType.Cold };

            TrialMobSpec Case() => Clickable("Agne's reliquary", Reliquary, 40, hall, "You lift Agne's right arm out of the reliquary, and every statue in the hall turns to look at you.", "case",
                (run, mob) =>
                {
                    GamePlayer carrier = NearestPlayer(run, mob);
                    run.Set("carrier", carrier);
                    run.Tell($"{carrier.Name} carries Agne's right arm. Get {carrier.Name} out alive!");
                    foreach (TrialMob statue in run.Mobs("hall"))
                        Aggro(statue, carrier, 1000);
                });

            return new TrialDefinition
            {
                ML = 8,
                Step = 9,
                Name = "Agne's Spare Arm",
                Scope = TrialScope.Group,
                Zone = TrialZone.Oceanus,
                Anchor = exit,
                StarterName = ServantName,
                StarterModel = Servant,
                Intro = "This row of statues was once the great hall of a temple, and Agne's right arm still lies in a chest at the heart of it. Clear one side of the hall and the chest will open. Whoever takes the arm must carry it out past me alive, for the far side of the hall will wake and come for the bearer. If the bearer falls, the arm goes back to its chest.",
                Victory = "You carry Agne's right arm clear of the hall.",
                TimeLimitSeconds = 1500,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The near side of the hall wakes.",
                        Mobs =
                        {
                            Chrysiron(438556, 662968, 12096),
                            Chrysiron(439510, 662989, 12096),
                            Chrysiron(438446, 663467, 12096),
                            Chrysiron(439601, 663494, 12097),
                            Chrysiron(439489, 664078, 12096),
                            Chrysiron(438577, 664095, 12096),
                            Helicaon(438707, 664845, 12096),
                            Helicaon(439381, 664870, 12096),
                        },
                        Pulses = { IceBlast() },
                    },
                    new TrialPhase
                    {
                        Announce = "The reliquary stands open at the heart of the hall.",
                        Mobs = { Case() },
                    },
                    new TrialPhase
                    {
                        Announce = "Run! Bring the arm out past the Servant!",
                        Mobs =
                        {
                            Helicaon(438638, 666316, 12121, false),
                            Helicaon(439402, 666343, 12115, false),
                            Chrysiron(438721, 667095, 12119, false),
                            Chrysiron(439515, 667108, 12127, false),
                            Chrysiron(438571, 667728, 12096, false),
                            Chrysiron(439645, 667730, 12120, false),
                        },
                        Pulses = { IceBlast() },
                        OnStart = run =>
                        {
                            if (run.Get<GamePlayer>("carrier") is GamePlayer carrier)
                                foreach (TrialMob statue in run.Mobs("hall"))
                                    Aggro(statue, carrier, 1000);
                        },
                        OnTick = run =>
                        {
                            GamePlayer carrier = run.Get<GamePlayer>("carrier");
                            if (carrier == null || carrier.IsAlive && carrier.ObjectState == GameObject.eObjectState.Active)
                                return;
                            run.Set("carrier", null);
                            run.Tell("The arm falls from its bearer and returns to its reliquary.");
                            run.Spawn(Case());
                        },
                        Done = run => run.Get<GamePlayer>("carrier") is { IsAlive: true } carrier && Near(carrier, exit, 500),
                    },
                },
            };
        }

        private static TrialDefinition AgnesGreatMight()
        {
            TrialPoint talosHome = new(481060, 691913, 16457);
            TrialPoint[] altheus = { new(479498, 691349, 16411), new(481689, 693529, 16411) };
            TrialPoint guardian = new(461188, 688122, 13955);
            TrialPoint chestAt = new(461388, 688122, 13955);

            static bool Resuming(TrialRun run) => run.Get("resume", false);

            // A phase on the way to Talos. A raid back within 45 minutes of losing to him skips it.
            static TrialPhase BeforeTalos(TrialPhase phase)
            {
                string announce = phase.Announce;
                Action<TrialRun> start = phase.OnStart;
                Action<TrialRun> tick = phase.OnTick;
                Func<TrialRun, bool> done = phase.Done;
                phase.Announce = null;
                phase.OnStart = run =>
                {
                    if (Resuming(run))
                    {
                        foreach (TrialMob mob in run.Mobs().ToList())
                            mob.Delete();
                        return;
                    }
                    if (!string.IsNullOrEmpty(announce))
                        run.Tell(announce);
                    start?.Invoke(run);
                };
                phase.OnTick = run =>
                {
                    if (!Resuming(run))
                        tick?.Invoke(run);
                };
                phase.Done = run => Resuming(run) || (done?.Invoke(run) ?? NoneRequiredLeft(run));
                return phase;
            }

            // Agne fights beside his army and cannot be beaten below 75, 50 and 25% while each wave stands (Prima).
            static TrialPhase Wave(int wave, string announce, byte level, int warriors, int sages, int mages)
            {
                List<TrialPoint> spots = Circle(Battlefield, 500, warriors + sages + mages).ToList();
                return BeforeTalos(new TrialPhase
                {
                    Announce = announce,
                    Mobs = spots.Take(warriors).Select(at => Foe("Statue Warrior", 1327, level, 60, 4000, 1.0, at, melee: eDamageType.Slash))
                        .Concat(spots.Skip(warriors).Take(sages).Select(at => Foe("Statue Sage", 1321, level, 60, 3500, 0.8, at, "caster")))
                        .Concat(spots.Skip(warriors + sages).Select(at => Foe("Statue Mage", 1315, level, 60, 3000, 0.7, at, "caster")))
                        .ToList(),
                    Pulses = { new TrialPulse { IntervalSeconds = 10, Damage = 140, Radius = 800, SourceTag = "caster", DamageType = eDamageType.Energy } },
                    OnStart = run =>
                    {
                        if (wave == 1)
                        {
                            TrialMob agne = run.Spawn(new TrialMobSpec
                            {
                                Name = "Agne",
                                Model = Servant,
                                Level = 68,
                                Size = 120,
                                Health = 45000,
                                DamageFactor = 1.7,
                                At = new TrialPoint(Battlefield.X + 300, Battlefield.Y, Battlefield.Z),
                                Tag = "agne",
                                Required = false,
                                MeleeDamage = eDamageType.Slash,
                                OnDeath = (r, _) => r.Set("agne.down", true),
                            });
                            Aggro(agne, run.Leader);
                        }
                        run.SetImmune("agne", false);
                    },
                    OnTick = run =>
                    {
                        foreach (TrialMob agne in run.Mobs("agne").Where(agne => !agne.Immune && agne.HealthPercent <= AgneFloor(wave)))
                        {
                            SetFloor(agne, AgneFloor(wave));
                            agne.Immune = true;
                            run.Tell("Agne shrugs off your blows. Break his soldiers first!");
                        }
                    },
                    Done = NoneRequiredLeft,
                });
            }

            return new TrialDefinition
            {
                ML = 8,
                Step = 10,
                Name = "Agne's Great Might",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Oceanus,
                Anchor = Constructor,
                StarterName = "Agne's Constructor",
                StarterModel = Servant,
                Replaces = new[] { "Agne's Constructor", "Statue Warrior", "Statue Mage", "Statue Sage", "Guardian of the Talos Crystal", "Talos", "altheus Talos" },
                Intro = "Crystal, shield, sword, head, torso, arms and legs: you have earned them all, and the earning is what I take from you, for I fit them as you bring them. Stand clear. Agne will come down onto the field with his army, three ranks of it. He cannot be beaten while his ranks stand, so break them. His key opens the chest the sphinx keeps, and the crystal in it wakes Talos in his temple. Talos's two guardians will mend him. Break them when they wake. Fall to Talos and you have three quarters of an hour to come back to him before Agne must be built again.",
                Victory = "Talos crashes down across his temple, and his gold rings on the stones.",
                TimeLimitSeconds = 2400,
                LeashRange = 30000,
                OnEnd = (run, success) =>
                {
                    string key = RetryKey(run.Leader);
                    if (success)
                        TalosRetries.TryRemove(key, out _);
                    else if (run.Get("agne.down", false))
                        TalosRetries[key] = GameLoop.GameLoopTime;
                },
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The Constructor fits the last pieces. Agne is nearly whole.",
                        SurviveSeconds = 10,
                        OnStart = run =>
                        {
                            string key = RetryKey(run.Leader);
                            if (!TalosRetries.TryGetValue(key, out long lostAt) || run.Sweeping)
                                return;
                            if (TalosRetryOpen(lostAt, GameLoop.GameLoopTime))
                            {
                                run.Set("resume", true);
                                run.Set("agne.down", true);
                                run.Tell("Agne is already beaten and the crystal still answers you. Go straight to Talos.");
                            }
                            else
                                TalosRetries.TryRemove(key, out _);
                        },
                        Done = run => Resuming(run) || Count(run, "build") >= 10,
                    },
                    Wave(1, "Agne steps down, and his first rank marches onto the field with him.", 55, 6, 0, 0),
                    Wave(2, "Agne goes still as his second rank comes on, sages among them.", 58, 4, 3, 0),
                    Wave(3, "The last rank has more casters than blades. Take them first!", 61, 3, 2, 3),
                    BeforeTalos(new TrialPhase
                    {
                        Announce = "Agne's army is broken. Now he can fall!",
                        OnStart = run => run.SetImmune("agne", false),
                        Done = run => Gone(run, "agne"),
                    }),
                    BeforeTalos(new TrialPhase
                    {
                        Announce = "Agne falls, and a key rolls from his hand. Behind his platform the sphinx's door stands open.",
                        Mobs =
                        {
                            Clickable("Agne's treasure chest", Reliquary, 34, chestAt, "The key turns. A crystal tumbles from the chest and lands on the floor."),
                            new TrialMobSpec { Name = "Guardian of the Talos Crystal", Model = Sphinx, Size = 48, Level = 65, At = guardian, Objective = true, ObjectiveText = "The sphinx watches the chest and says nothing.", Required = false },
                        },
                    }),
                    BeforeTalos(new TrialPhase
                    {
                        Announce = "The Talos Power Crystal lies on the floor. Someone must pick it up!",
                        Mobs = { Clickable("Talos Power Crystal", 966, 30, new TrialPoint(chestAt.X + 120, chestAt.Y, chestAt.Z), "It hums in your hand, and the great floating temple answers.") },
                    }),
                    new TrialPhase
                    {
                        Announce = "Carry the crystal to the golden head at the centre of Talos's island.",
                        Mobs = { Clickable("Talos", 1181, 125, talosHome, "You set the crystal into the gold. The head sinks, and the floating columns with it.") },
                    },
                    new TrialPhase
                    {
                        Announce = "Talos wakes, and two altheus statues stand dormant beside him!",
                        Mobs =
                        {
                            new TrialMobSpec
                            {
                                Name = "Talos",
                                Model = 1181,
                                Level = 70,
                                Size = 125,
                                Health = 140000,
                                DamageFactor = 2.2,
                                At = talosHome,
                                Tag = "talos",
                                Required = false,
                            },
                            new TrialMobSpec { Name = "altheus Talos", Model = 998, Level = 66, Size = 125, Health = 8000, DamageFactor = 0.05, AggroRange = 0, At = altheus[0], Tag = "altheus", Immune = true, Required = false },
                            new TrialMobSpec { Name = "altheus Talos", Model = 989, Level = 66, Size = 125, Health = 8000, DamageFactor = 0.05, AggroRange = 0, At = altheus[1], Tag = "altheus", Immune = true, Required = false },
                        },
                        Pulses = { new TrialPulse { IntervalSeconds = 12, Damage = 260, Radius = 450, SourceTag = "talos", DamageType = eDamageType.Crush, Message = "Talos stomps, and the ground heaves!" } },
                        OnTick = run =>
                        {
                            Expire(run);
                            TrialMob talos = run.Mobs("talos").FirstOrDefault();
                            if (talos == null)
                                return;
                            if (TalosCallsForHealing(talos.HealthPercent, run.Get("healed", false)))
                            {
                                run.Set("healed", true);
                                if (!Gone(run, "altheus"))
                                {
                                    SetFloor(talos, TalosHealsTo);
                                    run.SetImmune("altheus", false);
                                    run.Tell("The altheus statues wake and pour their strength into Talos. Break them!");
                                }
                                return;
                            }
                            if (!run.Get("healed", false))
                                return;
                            List<TrialMob> healers = run.Mobs("altheus").ToList();
                            if (healers.Count > 0 && Count(run, "altheus.heal") % 3 == 0)
                                Mend(talos, healers.Count);
                            // Prima: an altheus that is hit has a small chance to loose a purogenes for 15 seconds.
                            foreach (TrialMob healer in healers.Where(healer => WasHit(run, healer) && Util.Chance(5)))
                            {
                                run.Tell("A purogenes bursts out of the altheus!");
                                SpawnFor(run, Foe("purogenes", FireSpirit, 62, 40, 1500, 1.0, Circle(new TrialPoint(healer.X, healer.Y, healer.Z), 150, 1).First(), "purogenes", eDamageType.Heat, false), 15);
                            }
                        },
                        Done = run => Gone(run, "talos"),
                    },
                },
            };
        }
    }
}
