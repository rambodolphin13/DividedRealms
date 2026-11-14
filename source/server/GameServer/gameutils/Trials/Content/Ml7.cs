using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static DOL.GS.Trials.Ml7Kit;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Master Level 7 trials, in Deep Volcanus. The bridge lies on x 32000, the four feuding races share the grid
    /// north of it, and the Mediators' arenas are sealed rooms that can only be reached by being sent there.
    /// Live reference: Prima's Trials of Atlantis guide (pp. 91-98, monster levels p. 279) and the ToA guide's
    /// Master Level Seven page (2003-2004 player reports, 1.68 patch notes).
    /// </summary>
    public static class Ml7
    {
        private const ushort Apophian = 1199, Volurgon = 1197, Shaitan = 1198, Hephaestian = 1196, Fire = 456, Idol = 1203, Snake = 1202;

        private const int LavaWindowSeconds = 40;
        /// <summary>Live: every Mediator challenge lasts exactly ten minutes.</summary>
        public const int ChallengeSeconds = 600;
        private const int MaxApophians = 8;
        /// <summary>Live: a new Rage of the Volurgons every 20 seconds, at most nine.</summary>
        public const int RageEverySeconds = 20, MaxRages = 9;
        /// <summary>Live: a new Shaitan Enara every 20 seconds, whatever you do; she casts Shaitan's Reach two seconds after she appears.</summary>
        public const int EnaraEverySeconds = 20, ReachDelaySeconds = 2;
        /// <summary>Live: each blow on Typhon's Essence leaves Typhon open for two or three seconds.</summary>
        public const int TyphonOpenMs = 3000;

        private static readonly TrialPoint BridgeSouth = new(32000, 28800, 15662);
        private static readonly TrialPoint BridgeGate = new(32000, 28250, 15674);
        private static readonly TrialPoint BridgeNorth = new(32000, 25200, 15668);

        private static readonly TrialPoint GridCentre = new(32000, 22300, 15675);

        private static readonly TrialPoint MediatorApophian = new(36622, 26592, 15661);
        private static readonly TrialPoint MediatorVolurgon = new(27176, 20278, 15645);
        private static readonly TrialPoint MediatorShaitan = new(37454, 20987, 15663);
        private static readonly TrialPoint MediatorHephaestian = new(26745, 27309, 15645);

        private static readonly TrialPoint ArenaApophian = new(38637, 30575, 17725);
        private static readonly TrialPoint ArenaVolurgon = new(23514, 20620, 17050);
        private static readonly TrialPoint ArenaShaitan = new(41911, 28906, 17718);
        private static readonly TrialPoint ArenaHephaestian = new(26645, 29053, 16885);

        private static readonly TrialPoint FlameIsland = new(31654, 17559, 15832);

        private static readonly TrialPoint Transmuter = new(40717, 13042, 14909);
        private static readonly TrialPoint KatoriiHome = new(35078, 13048, 15677);
        private static readonly TrialPoint PetRoom = new(38091, 14233, 15678);
        private static readonly TrialPoint TyphonGate = new(42740, 15900, 14910);

        private static readonly TrialPoint Throat = new(45400, 22200, 14899);
        private static readonly TrialPoint NorthAlcove = new(45813, 21293, 14890);
        private static readonly TrialPoint SouthAlcove = new(45502, 22650, 14879);

        private static readonly TrialPoint PoolShore = new(47500, 22000, 14857);
        private static readonly TrialPoint EssenceHome = new(48805, 21418, 14741);
        private static readonly TrialPoint[] TyphonSpots =
        {
            new(50626, 19816, 14737),
            new(47000, 20000, 14741),
            new(50500, 24500, 14741),
            new(51000, 22500, 14741),
        };

        public static IEnumerable<TrialDefinition> Trials()
        {
            yield return LavaBridge();
            yield return KnowYourOpponents();
            yield return ApophiansChallenge();
            yield return VolurgonsChallenge();
            yield return ShaitansChallenge();
            yield return HephaestiansChallenge();
            yield return CrossingTheChamber();
            yield return Pillars();
            yield return FacingTyphon();
            yield return TyphonsRage();
        }

        /// <summary>
        /// A Dissos of this age, in seconds. Live: level 55 at first, several levels every 30 seconds, capped at 80;
        /// here 2 a half minute and capped at 70, the trials' level ceiling.
        /// </summary>
        public static byte ApophianLevel(int seconds) => (byte)Math.Min(70, 55 + 2 * (Math.Max(0, seconds) / 30));

        /// <summary>Which of Typhon's four pools he belongs in at this health. The last one is where he stays.</summary>
        public static int TyphonSpot(int healthPercent) => MarksPassed(healthPercent, 75, 50, 25);

        public static bool OnBridge(int x, int y) => Math.Abs(x - 32000) <= 400 && y > 25400 && y < 28300;

        /// <summary>Whether this second of a mob's life is one where it casts: first after <paramref name="first"/>, then every <paramref name="every"/>.</summary>
        public static bool CastsAt(int age, int first, int every) => age >= first && (age - first) % Math.Max(1, every) == 0;

        /// <summary>Live: whoever has not met a step's own requirement may help, but earns nothing (Prima on 7.3-7.6, 7.9, 7.10).</summary>
        public static bool Qualified(TrialRun run, GamePlayer player) => TrialManager.MissingSteps(player, run.Definition).Length == 0;

        /// <summary>Live 7.3-7.6: only those alive when the ten minutes end get credit; the dead /release and try again.</summary>
        public static bool QualifiedAndAlive(TrialRun run, GamePlayer player) => player.IsAlive && Qualified(run, player);

        private static Dictionary<TrialMob, int> Births(TrialRun run)
        {
            Dictionary<TrialMob, int> births = run.Get<Dictionary<TrialMob, int>>("births");
            if (births == null)
                run.Set("births", births = new Dictionary<TrialMob, int>());
            return births;
        }

        private static TrialMob Born(TrialRun run, TrialMob mob)
        {
            if (mob != null)
                Births(run)[mob] = run.Get("seconds", 0);
            return mob;
        }

        private static int Age(TrialRun run, TrialMob mob, int now) => now - Births(run).GetValueOrDefault(mob, 0);

        /// <summary>A trial's own spell, cast from each living mob with the tag on its own clock.</summary>
        private static void AreaCasts(TrialRun run, string tag, int now, int first, int every, int damage, int radius, eDamageType type, string message)
        {
            foreach (TrialMob caster in run.Mobs(tag).ToList())
            {
                if (!CastsAt(Age(run, caster, now), first, every))
                    continue;
                foreach (GameLiving victim in run.Hostiles(caster, radius))
                    victim.TakeDamage(caster, type, (int)(damage * run.DamageScale), 0);
                if (message != null)
                    run.Tell(message);
            }
        }

        /// <summary>A trial's own bolt: each living mob with the tag strikes one random enemy in range on its own clock.</summary>
        private static void Bolts(TrialRun run, string tag, int now, int every, int damage, int range, eDamageType type)
        {
            foreach (TrialMob caster in run.Mobs(tag).ToList())
            {
                if (!CastsAt(Age(run, caster, now), every, every))
                    continue;
                List<GameLiving> targets = run.Hostiles(caster, range).ToList();
                if (targets.Count > 0)
                    targets[Util.Random(targets.Count - 1)].TakeDamage(caster, type, (int)(damage * run.DamageScale), 0);
            }
        }

        private static TrialDefinition LavaBridge()
        {
            TrialMobSpec Device() => Clickable("hand print device", Idol, 30, BridgeGate,
                "You press the Volurgon Glove into the hand print. The warm hand is accepted, and the lavafall stops.", "device",
                (run, _) => run.Set("lava", LavaWindowSeconds));

            return new TrialDefinition
            {
                ML = 7,
                Step = 1,
                Name = "The Lava Bridge",
                Scope = TrialScope.Group,
                Zone = TrialZone.Volcanus,
                Anchor = BridgeSouth,
                StarterName = "Keeper of the Lavafall",
                StarterModel = Idol,
                Intro = "No flesh crosses while the lavafall runs, and the device that stops it answers only to the warm hand of a volurgon. A dead hand will not do; a glove shaped on a living one might. Salamander wool for the lining, a barracuda spine needle to sew it, and a pristine volurgon hand for the shape. All three can be had in this very room. One wearer is enough, but the fall will not stay shut for long, and each of you must cross to count.",
                Victory = "You stand on the far side of the lava bridge.",
                TimeLimitSeconds = 1200,
                // A sweep can't walk the bridge; it still checks that the step's credit reaches the tester.
                Earns = (run, player) => Qualified(run, player) && (run.Sweeping || Near(player, BridgeNorth, 1200)),
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "Gather the salamander wool, the barracuda spine needle and a pristine volurgon hand.",
                        Mobs =
                        {
                            Foe("salamander hatchling", 1261, 58, 50, 3000, 0.7, new TrialPoint(31750, 29050, 15662), melee: eDamageType.Heat),
                            Foe("salamander hatchling", 1261, 58, 50, 3000, 0.7, new TrialPoint(32250, 29050, 15662), melee: eDamageType.Heat),
                            Foe("pristine volurgon", Volurgon, 62, 55, 6000, 1.0, new TrialPoint(32000, 29150, 15662)),
                            Clickable("charred barracuda", 967, 40, new TrialPoint(31750, 28650, 15662), "You snap a clean needle from the spine of the burnt barracuda."),
                        },
                    },
                    new TrialPhase
                    {
                        Announce = "The wool, the needle and the hand draw together into a Volurgon Glove. Wear it to the hand print device at the head of the bridge.",
                        Mobs = { Device() },
                    },
                    new TrialPhase
                    {
                        Announce = "The lavafall is shut. Cross now, all of you!",
                        OnStart = run => run.Set("lava", LavaWindowSeconds),
                        OnTick = run =>
                        {
                            int left = run.Get("lava", 0);
                            run.Set("lava", left - 1);
                            if (left == 1)
                            {
                                run.Tell("The lavafall breaks loose again. Return to the hand print device.");
                                run.Spawn(Device());
                            }
                            if (left > 0)
                                return;
                            foreach (GameLiving victim in run.Hostiles(run.Starter, 4000).Where(living => OnBridge(living.X, living.Y)).ToList())
                                victim.TakeDamage(run.Starter, eDamageType.Heat, 120, 0);
                        },
                        // Live: credit once the group stands on the far side. Every living player must be over, the leader too.
                        Done = run => Near(run.Leader, BridgeNorth, 500) && run.Players
                            .Where(player => player.IsAlive && player.CurrentRegion == run.Starter.CurrentRegion)
                            .All(player => Near(player, BridgeNorth, 1200)),
                    },
                },
            };
        }

        private static TrialDefinition KnowYourOpponents()
        {
            static TrialPhase Feud(string race, ushort model, int x, int y, string announce) => new()
            {
                Announce = announce,
                Mobs = AtEach(Circle(new TrialPoint(x, y, 15675), 220, 5), at => Foe($"{race} feudist", model, 62, 55, 4000, 0.9, at)).ToList(),
            };

            return new TrialDefinition
            {
                ML = 7,
                Step = 2,
                Name = "Know your Opponents",
                Scope = TrialScope.Group,
                Zone = TrialZone.Volcanus,
                Anchor = GridCentre,
                RequiresSteps = new[] { 1 },
                StarterName = "Balance of the Four",
                StarterModel = Idol,
                Replaces = new[] { "Balance of the Four" },
                Intro = "Four races feud in these halls and I weigh them all. You will not end their quarrel, and I do not ask it. Stand in each of their rooms until each has marked you as a danger. What one of you learns, all of you learn. Only then will their Mediators speak to you.",
                Victory = "All four races now know your face. Their Mediators will receive you.",
                TimeLimitSeconds = 1800,
                Earns = Qualified,
                Phases =
                {
                    Feud("volurgon", Volurgon, 28900, 22240, "The volurgons come first, in the room to the northwest."),
                    Feud("hephaestian", Hephaestian, 28900, 23800, "You have learned all you can about the volurgons and the feud. The hephaestians wait to the southwest."),
                    Feud("shaitan", Shaitan, 35040, 22240, "You have learned all you can about the hephaestians and the feud. The shaitan wait to the northeast."),
                    Feud("apophian", Apophian, 35040, 23800, "You have learned all you can about the shaitan and the feud. The apophians wait to the southeast."),
                },
            };
        }

        /// <summary>
        /// The Mediators' shared shape (live): send the group to its arena, hold out ten minutes, and come home. The
        /// dead are carried back too (1.68), but only the living earn the step.
        /// </summary>
        private static TrialDefinition Challenge(int step, string name, string mediator, ushort model, string race, TrialPoint home, TrialPoint arena, string intro, TrialPhase fight)
        {
            Action<TrialRun> start = fight.OnStart;
            fight.SurviveSeconds = ChallengeSeconds;
            fight.OnStart = run =>
            {
                MoveParty(run, arena);
                start?.Invoke(run);
            };

            return new TrialDefinition
            {
                ML = 7,
                Step = step,
                Name = name,
                Scope = TrialScope.Group,
                Zone = TrialZone.Volcanus,
                Anchor = home,
                RequiresSteps = new[] { 2 },
                StarterName = mediator,
                StarterModel = model,
                Replaces = new[] { mediator },
                Intro = intro + " Ten minutes, no more and no less. Those standing at the end have my people's respect; the fallen will be carried back to me, and may try again.",
                Victory = $"The {race} accept that you can endure.",
                TimeLimitSeconds = 900,
                LeashRange = 12000,
                Earns = QualifiedAndAlive,
                OnEnd = (run, won) =>
                {
                    if (!won)
                        MoveParty(run, home);
                },
                Phases =
                {
                    fight,
                    new TrialPhase
                    {
                        Announce = "The Mediator calls you back.",
                        OnStart = run => MoveParty(run, home),
                        SurviveSeconds = 1,
                    },
                },
            };
        }

        private static TrialPoint[] ArenaSpots(TrialPoint arena) => Circle(arena, 450, 4).Append(arena).ToArray();

        private static TrialMobSpec Dissos(TrialPoint at) => new()
        {
            Name = "Apophian Dissos",
            Model = Apophian,
            Level = ApophianLevel(0),
            Size = 60,
            Health = 12000,
            DamageFactor = 0.8,
            At = at,
            Tag = "apophian",
            Required = false,
            MeleeDamage = eDamageType.Thrust,
            OnDeath = (run, mob) =>
            {
                if (run.Mobs("apophian").Count() + 1 >= MaxApophians)
                    return;
                run.Tell("The Dissos comes apart, and two rise from the pieces!");
                foreach (TrialPoint point in Circle(new TrialPoint(mob.X, mob.Y, mob.Z), 120, 2))
                    Born(run, run.Spawn(Dissos(point)));
            },
        };

        private static TrialDefinition ApophiansChallenge() => Challenge(3, "Apophian's Challenge", "Mediator of the Apophians", Apophian, "apophians", MediatorApophian, ArenaApophian,
            "My people ask one thing of you: endure. I will send you to the Dissos, who grows while you stand before him. Strike him down and you will have two to deal with, each young again but growing. Do not try to win. Be alive when he tires of you.",
            new TrialPhase
            {
                Announce = "Hold the Dissos. He grows by the half minute, and killing him only makes more.",
                Mobs = { Dissos(ArenaApophian) },
                OnTick = run =>
                {
                    int now = Count(run, "seconds");
                    foreach (TrialMob mob in run.Mobs("apophian"))
                    {
                        int age = Age(run, mob, now);
                        if (age % 30 != 0)
                            continue;
                        mob.Level = ApophianLevel(age);
                        mob.DamageFactor = run.DamageScale * (0.8 + age / 900.0);
                    }
                    Bolts(run, "apophian", now, 12, 150, 1500, eDamageType.Energy);
                },
            });

        private static TrialDefinition VolurgonsChallenge()
        {
            TrialPoint[] spots = ArenaSpots(ArenaVolurgon);

            void Mandra(TrialRun run, int seconds)
            {
                run.Tell("Volurgon Mandra rises and attacks!");
                run.Spawn(new TrialMobSpec
                {
                    Name = "Volurgon Mandra",
                    Model = Volurgon,
                    Level = 60,
                    Size = 70,
                    Health = 7000,
                    DamageFactor = 0.9,
                    At = spots[seconds % spots.Length],
                    Tag = "mandra",
                    Required = false,
                    OnDeath = (trial, _) =>
                    {
                        // Live: each Mandra slain takes one Rage with her.
                        TrialMob rage = trial.Mobs("rage").OrderBy(mob => Births(trial).GetValueOrDefault(mob, 0)).FirstOrDefault();
                        rage?.Delete();
                        trial.Set("next", trial.Get("seconds", 0) + 6);
                    },
                });
            }

            void Rage(TrialRun run, int seconds)
            {
                TrialMob rage = Born(run, run.Spawn(new TrialMobSpec
                {
                    Name = "Rage of the Volurgons",
                    Model = Fire,
                    Level = 70,
                    Size = 60,
                    Health = 5000,
                    DamageFactor = 0.1,
                    At = spots[(seconds + 2) % spots.Length],
                    Tag = "rage",
                    Immune = true,
                    Required = false,
                    AggroRange = 3000,
                    MeleeDamage = eDamageType.Heat,
                }));
                rage.MaxSpeedBase = 80;
            }

            return Challenge(4, "Volurgon's Challenge", "Mediator of the Volurgons", Volurgon, "volurgons", MediatorVolurgon, ArenaVolurgon,
                "Mandra of my people does not stay dead, and her rage walks behind her. Another rage wakes every little while, and nothing you carry can put one out; only Mandra's death takes one away with her.",
                new TrialPhase
                {
                    Announce = "Kill Mandra, again and again, before her rages fill the room.",
                    Pulses = { new TrialPulse { IntervalSeconds = 4, Damage = 250, Radius = 300, SourceTag = "rage" } },
                    OnTick = run =>
                    {
                        int seconds = Count(run, "seconds");
                        if (Gone(run, "mandra") && seconds >= run.Get("next", 1))
                        {
                            run.Set("next", int.MaxValue);
                            Mandra(run, seconds);
                        }
                        if (seconds % RageEverySeconds == 1 && run.Mobs("rage").Count() < MaxRages)
                        {
                            run.Tell("Another Rage of the Volurgons kindles.");
                            Rage(run, seconds);
                        }
                    },
                });
        }

        private static TrialDefinition ShaitansChallenge()
        {
            TrialPoint[] spots = ArenaSpots(ArenaShaitan);

            return Challenge(5, "Shaitan's Challenge", "Mediator of the Shaitan", Shaitan, "shaitan", MediatorShaitan, ArenaShaitan,
                "One of my sisters, an Enara, will find you in a place of her choosing, and a breath after she comes she burns all who stand close. Another follows whatever you do. Keep out of her reach, and cold steel is your friend.",
                new TrialPhase
                {
                    Announce = "A Shaitan Enara is coming. Do not stand close when she arrives.",
                    OnTick = run =>
                    {
                        int now = Count(run, "seconds");
                        if (now % EnaraEverySeconds == 1 && run.Mobs("enara").Count() < 6)
                        {
                            TrialMob enara = Born(run, run.Spawn(Foe("Shaitan Enara", Shaitan, 65, 75, 9000, 1.0, spots[Util.Random(spots.Length - 1)], "enara", eDamageType.Heat, false)));
                            Resist(enara, 50, eProperty.Resist_Heat);
                            Resist(enara, -25, eProperty.Resist_Cold);
                            run.Tell("A Shaitan Enara appears, gathering heat!");
                        }
                        AreaCasts(run, "enara", now, ReachDelaySeconds, 10, 300, 450, eDamageType.Heat, "The Enara casts Shaitan's Reach!");
                    },
                });
        }

        private static TrialDefinition HephaestiansChallenge()
        {
            TrialPoint[] spots = ArenaSpots(ArenaHephaestian);

            return Challenge(6, "Hephaestian's Challenge", "Mediator of the Hephaestians", Hephaestian, "hephaestians", MediatorHephaestian, ArenaHephaestian,
                "We are many and we do not hurry. A Katarch will step out of the wall, and then another, and then another. Each one burns the hand that strikes it. Break them faster than they come.",
                new TrialPhase
                {
                    Announce = "The Katarchs step out of the walls. Keep their numbers down.",
                    OnTick = run =>
                    {
                        int seconds = Count(run, "seconds");
                        if (seconds % 20 != 2 || run.Mobs("hephaestian").Count() >= 6)
                            return;
                        TrialMobSpec spec = Foe("Hephaestian Katarch", Hephaestian, 67, 60, 5000, 0.7, spots[seconds / 20 % spots.Length], "hephaestian", eDamageType.Heat, false);
                        spec.Reflect = 0.1;
                        Resist(run.Spawn(spec), -25, eProperty.Resist_Cold);
                    },
                });
        }

        private static TrialMobSpec Sphere(TrialPoint at) => new()
        {
            Name = "flame sphere",
            Model = Fire,
            Level = 60,
            Size = 30,
            Health = 2000,
            DamageFactor = 0.4,
            At = at,
            Tag = "sphere",
            Required = false,
            AggroRange = 0,
            MeleeDamage = eDamageType.Heat,
        };

        private static TrialDefinition CrossingTheChamber() => new()
        {
            ML = 7,
            Step = 7,
            Name = "Crossing the Chamber",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Volcanus,
            Anchor = new TrialPoint(32000, 19400, 15678),
            StarterName = "Watcher of the Chamber",
            StarterModel = Idol,
            Replaces = new[] { "Flame of Volcanus" },
            Intro = "North of here the rock itself burns. The Flame of Volcanus guards the way to Typhon's gate, ringed by flame spheres that are never all gone while it lives. The spheres circle and cast fire at whoever they please, and chasing them is wasted effort. Put out the Flame and nothing else.",
            Victory = "The Flame of Volcanus gutters out, and the chamber lies open.",
            TimeLimitSeconds = 1800,
            Earns = Qualified,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The Flame of Volcanus wakes on its island, its spheres around it.",
                    Mobs = new List<TrialMobSpec> { Foe("Flame of Volcanus", Fire, 70, 75, 70000, 1.6, FlameIsland, "flame", eDamageType.Heat) }
                        .Concat(AtEach(Circle(FlameIsland, 250, 4), Sphere)).ToList(),
                    OnStart = run =>
                    {
                        foreach (TrialMob flame in run.Mobs("flame"))
                        {
                            Resist(flame, 60, eProperty.Resist_Heat);
                            Resist(flame, -25, eProperty.Resist_Cold);
                        }
                    },
                    OnTick = run =>
                    {
                        int now = Count(run, "seconds");
                        Bolts(run, "sphere", now, 8, 160, 1800, eDamageType.Heat);
                        if (run.Mobs("sphere").Count() >= 4)
                        {
                            run.Set("sphere.back", 0);
                            return;
                        }
                        if (Count(run, "sphere.back") < 30)
                            return;
                        run.Set("sphere.back", 0);
                        foreach (TrialPoint point in Circle(FlameIsland, 250, 4).Skip(run.Mobs("sphere").Count()))
                            Born(run, run.Spawn(Sphere(point)));
                    },
                    Done = run => Gone(run, "flame"),
                },
            },
        };

        private static TrialPulse Gaze() => new() { IntervalSeconds = 2, Damage = 300, Radius = 700, SourceTag = "katorii", DamageType = eDamageType.Spirit };

        private static TrialMobSpec Serpent(string name, eDamageType melee, double reflect = 0)
        {
            TrialMobSpec spec = Foe(name, Snake, 66, 80, 22000, 1.4, PetRoom, "pet", melee, false);
            spec.Reflect = reflect;
            return spec;
        }

        private static TrialPhase SerpentHunt(TrialMobSpec serpent, string announce) => new()
        {
            Announce = announce,
            Mobs = { serpent },
            Pulses = { Gaze() },
            Done = run => Gone(run, "pet"),
        };

        private static TrialPhase Plate(string text) => new()
        {
            Announce = "It leaves a Runed Metal Plate. Carry it to the mechanism before Typhon's gate.",
            Mobs = { Clickable("gate mechanism", Idol, 30, TyphonGate, text, "box") },
            Pulses = { Gaze() },
            Done = run => Gone(run, "box"),
        };

        private static TrialDefinition Pillars() => new()
        {
            ML = 7,
            Step = 8,
            Name = "Pillars",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Volcanus,
            Anchor = Transmuter,
            StarterName = "Ancient Transmuter",
            StarterModel = 1191,
            Replaces = new[] { "Ancient Transmuter", "Katorii", "Katorii's pet" },
            Intro = "Katorii walks the pillared halls above us, and no blade can reach her while her three great serpents draw breath. Do not stand where she can see you. Hunt her Breath, her Touch and her Blood, one at a time, and feed the plates they leave to the mechanism at Typhon's gate. The last plate is her own.",
            Victory = "Katorii falls among her pillars and drops the last plate. Typhon's gate stands open.",
            TimeLimitSeconds = 2400,
            Earns = Qualified,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Katorii's Breath stirs at the top of the stairs. Keep clear of Katorii herself: her gaze withers all it finds.",
                    Mobs =
                    {
                        new TrialMobSpec
                        {
                            Name = "Katorii",
                            Model = 1195,
                            Level = 70,
                            Size = 85,
                            Health = 80000,
                            DamageFactor = 2.0,
                            At = KatoriiHome,
                            Tag = "katorii",
                            Immune = true,
                            Required = false,
                            AggroRange = 400,
                            MeleeDamage = eDamageType.Body,
                        },
                        Serpent("Katorii's Breath", eDamageType.Cold),
                    },
                    Pulses = { Gaze() },
                    Done = run => Gone(run, "pet"),
                },
                Plate("The plate slides home, and a scream comes through the stone."),
                SerpentHunt(Serpent("Katorii's Touch", eDamageType.Body), "Katorii screams and sends her Touch to the head of the stairs."),
                Plate("The plate slides home. Somewhere above, a great coil thrashes."),
                SerpentHunt(Serpent("Katorii's Blood", eDamageType.Body, 0.05), "Katorii screams and sends her Blood to the head of the stairs. It bleeds fire where it is cut."),
                Plate("The third plate slides home, and Katorii's deadly gaze goes out."),
                new TrialPhase
                {
                    Announce = "Katorii can be hurt! Go to her in the maze. Her pets gather to defend her.",
                    Mobs = AtEach(Circle(KatoriiHome, 280, 4), at => Foe("Katorii's pet", Snake, 55, 40, 1500, 1.0, at, required: false)).ToList(),
                    OnStart = run => run.SetImmune("katorii", false),
                    Done = run => Gone(run, "katorii"),
                },
            },
        };

        private static TrialDefinition FacingTyphon()
        {
            TrialMobSpec Guardian(string name, ushort model, TrialPoint at, string tag, eDamageType melee) => new()
            {
                Name = name,
                Model = model,
                Level = 62,
                Size = 60,
                Health = 6000,
                DamageFactor = 1.0,
                At = at,
                Tag = tag,
                MeleeDamage = melee,
                Required = false,
                OnDeath = (run, _) => run.Tell("The barrier falters for a moment as its keeper falls."),
            };

            return new TrialDefinition
            {
                ML = 7,
                Step = 9,
                Name = "Facing Typhon",
                Scope = TrialScope.Raid,
                Zone = TrialZone.Volcanus,
                Anchor = Throat,
                RequiresSteps = new[] { 8 },
                StarterName = "Battlewarder",
                StarterModel = Idol,
                Replaces = new[] { "apophian aggressor", "apophian crusher", "apophian enforcer", "corrupt volurgon", "mischievous volurgon", "unruly volurgon" },
                Intro = "Your plates opened the gate. They do not open the way to the pool. Barriers turn the air beyond and throw back all who push through, and two keepers feed them from the alcoves, one to the north and one to the south. Fell a keeper and you have faced Typhon's guard.",
                Victory = "A keeper falls, and the barriers falter. The way to the lava pool lies before you.",
                TimeLimitSeconds = 1200,
                Earns = Qualified,
                Phases =
                {
                    new TrialPhase
                    {
                        Announce = "The keepers stand in the alcoves. Fell one of them.",
                        Mobs =
                        {
                            Guardian("apophian aggressor", Apophian, NorthAlcove, "north", eDamageType.Thrust),
                            Guardian("corrupt volurgon", Volurgon, SouthAlcove, "south", eDamageType.Crush),
                        },
                        // Live (Prima): kill one of the room guardians to receive credit.
                        Done = run => Gone(run, "north") || Gone(run, "south"),
                    },
                },
            };
        }

        private static TrialMobSpec Essence() => new()
        {
            Name = "Typhon's Essence",
            Model = Fire,
            Level = 60,
            Size = 55,
            Health = 8000,
            DamageFactor = 0.01,
            At = EssenceHome,
            Tag = "essence",
            Immune = true,
            Required = false,
            AggroRange = 3500,
            MeleeDamage = eDamageType.Heat,
        };

        private static TrialDefinition TyphonsRage() => new()
        {
            ML = 7,
            Step = 10,
            Name = "Typhon's Rage",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Volcanus,
            Anchor = PoolShore,
            StarterName = "Warden of the Lava Pool",
            StarterModel = Idol,
            Replaces = new[] { "Typhon", "Typhon's Essence" },
            Intro = "He lies in the pool, and he has waited a long time. The gauntlet, the favour of the four races and the plates are spent; all you carry now is that you got here. His Essence drifts above the lava. It cannot be put out and it will not fight you, but every blow it takes opens Typhon to you for a few heartbeats. Keep someone on it always. Heat is nothing to him. Cold is what he fears, and a heavy blunt blow will not go amiss.",
            Victory = "Typhon sinks into his pool, and for the first time in an age the deep is quiet.",
            TimeLimitSeconds = 2400,
            Earns = Qualified,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Typhon rises from the lava!",
                    Mobs =
                    {
                        new TrialMobSpec
                        {
                            Name = "Typhon",
                            Model = 830,
                            Level = 70,
                            Size = 100,
                            Health = 130000,
                            DamageFactor = 2.0,
                            At = TyphonSpots[0],
                            Tag = "typhon",
                            Immune = true,
                            Required = false,
                            MeleeDamage = eDamageType.Heat,
                        },
                        Essence(),
                    },
                    Pulses = { new TrialPulse { IntervalSeconds = 15, Damage = 250, Radius = 900, SourceTag = "typhon", Message = "The pool heaves around Typhon!" } },
                    OnStart = run =>
                    {
                        foreach (TrialMob typhon in run.Mobs("typhon"))
                        {
                            Resist(typhon, 70, eProperty.Resist_Heat);
                            Resist(typhon, -50, eProperty.Resist_Cold);
                            Resist(typhon, -15, eProperty.Resist_Crush);
                        }
                    },
                    OnTick = TyphonTick,
                    Done = run => Gone(run, "typhon"),
                },
            },
        };

        /// <summary>Typhon can be hurt while his Essence was struck within the last <see cref="TyphonOpenMs"/>.</summary>
        public static bool TyphonOpen(long lastEssenceHit, long now) => lastEssenceHit > 0 && now - lastEssenceHit <= TyphonOpenMs;

        private static void TyphonTick(TrialRun run)
        {
            int now = Count(run, "seconds");
            long lastHit = run.Mobs("essence").Select(essence => essence.LastAttackedByEnemyTick).DefaultIfEmpty(0).Max();
            bool open = TyphonOpen(lastHit, GameLoop.GameLoopTime);
            if (open == run.Get("guarded", true))
            {
                run.Set("guarded", !open);
                run.SetImmune("typhon", !open);
                if (now - run.Get("told", -60) >= 30)
                {
                    run.Set("told", now);
                    run.Tell(open ? "The Essence is struck, and Typhon can be hurt!" : "No one strikes the Essence, and Typhon's hide closes over.");
                }
            }

            Bolts(run, "typhon", now, 10, 200, 2000, eDamageType.Heat);

            if (Gone(run, "essence") && Count(run, "essence.back") >= 20)
            {
                run.Set("essence.back", 0);
                run.Tell("Typhon's Essence rekindles above the pool.");
                run.Spawn(Essence());
            }

            // Live: the Essence moves about the room.
            if (now % 20 == 0)
            {
                foreach (TrialMob essence in run.Mobs("essence").Where(essence => !essence.InCombat))
                {
                    TrialPoint to = TyphonSpots[Util.Random(TyphonSpots.Length - 1)];
                    essence.WalkTo(new Vector3((to.X + EssenceHome.X) / 2, (to.Y + EssenceHome.Y) / 2, EssenceHome.Z), 120);
                }
            }

            foreach (TrialMob typhon in run.Mobs("typhon").ToList())
            {
                int spot = TyphonSpot(typhon.HealthPercent);
                if (spot <= run.Get("spot", 0))
                    continue;
                run.Set("spot", spot);
                run.Tell("Typhon dives, and rises across the pool!");
                Relocate(run, typhon, TyphonSpots[spot]);
            }
        }
    }
}
