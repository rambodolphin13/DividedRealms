using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Master Level 9 trials: the City of Aerus, ending at the Phoenix. Mechanics follow Prima's Official Strategy Guide
    /// (Trials of Atlantis pp. 111-118, Epic Edition pp. 307-312) and the 2004 player guide; see live-pass-2026-11-14/ml9.md.
    /// </summary>
    public static class Ml9
    {
        public const int AerusMasterLevel = 8;
        public const int KyrosFormSeconds = 30;
        public const int KyrosWizardSeconds = 15;
        public const int LachlenHeals = 12;
        public const int LachlenEvadePercent = 25;
        public const int KatriFloor = 75;
        public const int KatriHelpSeconds = 90;
        public const int KatriGuardians = 5;
        public const int NelosLessons = 5;
        public const int NelosForgetPercent = 25;
        public const int NeolaSisters = 30;
        public const int NeolaSistersAtOnce = 6;
        public const int BypassX = 36296;
        public const int SphinxGuardianCheckSeconds = 300;
        public const int SphinxGuardianChance = 35;
        public const int BraveFrenzyKills = 8;
        public const int GhostSplits = 4;
        public const int GhostSplitSeconds = 10;
        public const int GhostCap = 16;
        public const int CowardHealersAt = 75;
        public const int CowardFleesAt = 65;
        public const int CowardHideSeconds = 30;
        public const int CowardTrapSeconds = 15;
        public const int CowardRingRadius = 600;
        public const int TacticianReservesAt = 30;
        public const int IaculusMealSeconds = 20;
        public const int OperatorWalkSeconds = 60;

        private const int Statue = 993;
        private const int Sphinx = 1175;
        private const int Obelisk = 1823;
        private const int Avriel = 1216;
        private const int Iaculus = 1233;
        private const int ChanZ = 12049;

        public enum KyrosShape
        {
            Armsman,
            Wizard,
            Champion,
            Enchanter,
            Warrior,
            Runemaster,
        }

        // Each shape wears its realm's race, as Prima describes: a Highlander armsman, an Avalonian wizard, a Celt champion,
        // an Elf enchanter, a Troll warrior and a Kobold runemaster.
        private static readonly Dictionary<KyrosShape, (ushort Model, string Change)> KyrosShapes = new()
        {
            [KyrosShape.Armsman] = (39, "Kyros takes up a polearm in the manner of a Briton armsman and storms the front!"),
            [KyrosShape.Wizard] = (61, "Kyros takes a wizard's shape and looks past you, toward those who stand back."),
            [KyrosShape.Champion] = (302, "Kyros becomes a Hibernian champion, and a healing shield flickers around him."),
            [KyrosShape.Enchanter] = (334, "Kyros takes an enchanter's shape, and the air around him begins to crackle."),
            [KyrosShape.Warrior] = (137, "Kyros becomes a Midgard warrior and swells in size. He is growing in power!"),
            [KyrosShape.Runemaster] = (169, "Kyros takes a runemaster's shape, and spears of light gather over his head."),
        };

        private static readonly TrialPoint[] Generators =
        {
            new(26298, 40302, 16268),
            new(43375, 40010, 16268),
            new(34885, 25469, 16268),
        };

        private static readonly TrialPoint[] Operators =
        {
            new(26500, 36964, 16321),
            new(40396, 41732, 16344),
            new(37672, 27327, 16312),
        };

        private static readonly TrialPoint CowardHouse = new(45339, 53840, 10328);

        private static readonly string[] Kings = { "tactician", "brave", "terrible", "coward" };

        private static readonly string[] Statues = { "lethos", "anzelm", "timanthes", "zarlanthus", "gyges" };

        public static IEnumerable<TrialDefinition> Trials()
        {
            foreach (TrialDefinition trial in new[] { Wings(), Explorers(), Warlords(), KingsOfAerus(), Scholars(), Patrol(), Host(), Guardians(), CentaursPatrol(), Phoenix() })
            {
                trial.Refuse = AerusGate;
                yield return trial;
            }
        }

        /// <summary>Aerus opened only to Master Level 8 and above on live; the leader must have reached it.</summary>
        public static string AerusGate(GamePlayer leader) => leader.MLLevel >= AerusMasterLevel ? null
            : $"The City of Aerus admits only those of Master Level {AerusMasterLevel} or higher.";

        /// <summary>Kyros becomes the realm's melee or caster class of whoever hit him.</summary>
        public static KyrosShape KyrosShapeFor(eRealm realm, bool caster) => realm switch
        {
            eRealm.Albion => caster ? KyrosShape.Wizard : KyrosShape.Armsman,
            eRealm.Hibernia => caster ? KyrosShape.Enchanter : KyrosShape.Champion,
            _ => caster ? KyrosShape.Runemaster : KyrosShape.Warrior,
        };

        public static bool LachlenMayHeal(int healsDone) => healsDone < LachlenHeals;

        public static bool PastChan(int x, int z) => x > BypassX && Math.Abs(z - ChanZ) < 400;

        /// <summary>The health percent the Phoenix cannot fall below with this many generators running.</summary>
        public static int PhoenixFloor(int generatorsUp) => Math.Clamp(generatorsUp, 0, 3) * 25;

        public static int PhoenixFireball(int generatorsUp) => Math.Clamp(generatorsUp, 0, 3) * 90;

        public static int PhoenixFlare(int generatorsUp) => Math.Clamp(generatorsUp, 0, 3) * 70;

        /// <summary>The fireballs reach further as generators fall, while hitting softer: close, medium, then far range.</summary>
        public static int PhoenixFireballReach(int generatorsUp) => Math.Clamp(generatorsUp, 0, 3) switch
        {
            3 => 1000,
            2 => 2000,
            1 => 3000,
            _ => 0,
        };

        /// <summary>A ghostly statue's resistances: tough against matter, weak to heat, then crush, slash and thrust.</summary>
        public static int GhostDamage(eDamageType type, int damage) => type switch
        {
            eDamageType.Matter => damage * 3 / 10,
            eDamageType.Heat => damage * 3 / 2,
            eDamageType.Crush => damage * 5 / 4,
            eDamageType.Slash => damage * 23 / 20,
            eDamageType.Thrust => damage * 21 / 20,
            _ => damage,
        };

        /// <summary>Like every iaculus, the Puissant resists body and is weak to cold.</summary>
        public static int IaculusDamage(eDamageType type, int damage) => type switch
        {
            eDamageType.Body => damage * 3 / 10,
            eDamageType.Cold => damage * 3 / 2,
            _ => damage,
        };

        /// <summary>Counts seconds for a hook. Each key ticks once per call.</summary>
        internal static int Clock(TrialRun run, string key = "clock")
        {
            int seconds = run.Get(key, 0) + 1;
            run.Set(key, seconds);
            return seconds;
        }

        internal static TrialMob One(TrialRun run, string tag) => run.Mobs(tag).FirstOrDefault();

        internal static void Heal(TrialMob mob, double share) => mob?.ChangeHealth(mob, eHealthChangeType.Spell, (int)(mob.MaxHealth * share));

        internal static void Hurt(TrialRun run, GameLiving source, GameLiving target, int damage, eDamageType type) =>
            target.TakeDamage(source, type, (int)(damage * run.DamageScale), 0);

        internal static void HurtAround(TrialRun run, GameLiving source, GameObject center, int radius, int damage, eDamageType type)
        {
            foreach (GameLiving target in run.Hostiles(center, radius))
                Hurt(run, source, target, damage, type);
        }

        internal static GameLiving RandomTarget(TrialRun run, GameObject source, int radius)
        {
            List<GameLiving> targets = run.Hostiles(source, radius).ToList();
            return targets.Count == 0 ? null : targets[Util.Random(targets.Count - 1)];
        }

        /// <summary>Strikes around one random target, so a party that stands together takes it together.</summary>
        internal static void Splash(TrialRun run, GameLiving source, int reach, int radius, int damage, eDamageType type, string message)
        {
            GameLiving target = RandomTarget(run, source, reach);
            if (target == null)
                return;
            HurtAround(run, source, target, radius, damage, type);
            if (message != null)
                run.Tell(message);
        }

        /// <summary>Keeps a monster at or above a health percent. Floor 0 lets it die.</summary>
        internal static void HoldAt(TrialMob mob, int floor)
        {
            if (mob.HealthPercent < floor)
                mob.Health = mob.MaxHealth * floor / 100;
            mob.Immune = floor > 0 && mob.HealthPercent <= floor;
        }

        internal static void Root(TrialMob mob) => mob.MaxSpeedBase = 0;

        internal static void Calm(TrialRun run, string tag)
        {
            foreach (TrialMob mob in run.Mobs(tag))
                mob.Flags |= GameNPC.eFlags.PEACE;
        }

        /// <summary>Makes a dormant monster take damage and hunt the party.</summary>
        internal static void Wake(TrialRun run, string tag)
        {
            foreach (TrialMob mob in run.Mobs(tag))
            {
                mob.Immune = false;
                mob.Flags &= ~GameNPC.eFlags.PEACE;
                if (mob.Brain is StandardMobBrain brain)
                    brain.AggroRange = 1500;
            }
        }

        internal static TrialPoint Ring(TrialPoint center, int index, int count, int radius)
        {
            double angle = 2 * Math.PI * index / count;
            return new TrialPoint(center.X + (int)(radius * Math.Cos(angle)), center.Y + (int)(radius * Math.Sin(angle)), center.Z, center.Heading);
        }

        internal static TrialPoint Beside(GameObject thing, int offset = 150) => new(thing.X + offset, thing.Y, thing.Z, thing.Heading);

        private static TrialPoint PointOf(GameObject thing) => new(thing.X, thing.Y, thing.Z, thing.Heading);

        private static void Remove(TrialRun run, TrialMob mob) => mob.Delete();

        /// <summary>The living behind a hit: a pet's owner, else the attacker.</summary>
        internal static GameLiving Culprit(GameObject source) =>
            source is GameNPC { Brain: IControlledBrain controlled } pet ? controlled.GetLivingOwner() ?? pet : source as GameLiving;

        internal static bool IsCaster(GameLiving living) => living is IGamePlayer { CharacterClass.ClassType: eClassType.ListCaster };

        /// <summary>Players, bots and their companions taking part, alive or dead, still near the trial.</summary>
        internal static IEnumerable<GameLiving> Party(TrialRun run) =>
            run.Players.Cast<GameLiving>().Concat(run.Leader.Group?.GetMembersInTheGroup() ?? Enumerable.Empty<GameLiving>()).Distinct()
                .Where(living => living.ObjectState == GameObject.eObjectState.Active && living.CurrentRegion == run.Starter.CurrentRegion &&
                                 living.IsWithinRadius(run.Starter, run.Definition.LeashRange));

        /// <summary>Makes a monster hold on to one target, whatever else hits or taunts it.</summary>
        internal static void Stick(TrialMob mob, GameLiving target)
        {
            if (target is { IsAlive: true } && mob.Brain is StandardMobBrain brain)
                brain.AddToAggroList(target, 100000);
        }

        private static bool Engaged(TrialMob mob) => mob.InCombat || mob.HealthPercent < 100;

        /// <summary>What trial monsters learn from the hits they take. Filled on attackers' threads, read on the run's tick.</summary>
        internal sealed class HitLog
        {
            private readonly ConcurrentQueue<(GameLiving Attacker, bool Spell)> _recent = new();
            private GameLiving _first;
            public readonly ConcurrentQueue<TrialMob> Forgot = new();
            public volatile bool Warrior;

            public GameLiving First => Volatile.Read(ref _first);

            public void Note(GameLiving attacker, bool spell)
            {
                if (attacker == null)
                    return;
                Interlocked.CompareExchange(ref _first, attacker, null);
                _recent.Enqueue((attacker, spell));
                while (_recent.Count > 12)
                    _recent.TryDequeue(out _);
            }

            public (GameLiving Attacker, bool Spell)[] Recent => _recent.ToArray();
        }

        private static readonly ConditionalWeakTable<TrialRun, HitLog> Logs = new();

        internal static HitLog LogOf(TrialRun run) => Logs.GetValue(run, _ => new HitLog());

        private static int Noted(TrialMob mob, GameObject source, eDamageType type, int damage)
        {
            LogOf(mob.Run).Note(Culprit(source), TrialMob.KindOf(source, type) == TrialDamageKind.Spell);
            return damage;
        }

        // 9.1 Wings of Aerus

        private static TrialDefinition Wings() => new()
        {
            ML = 9,
            Step = 1,
            Name = "Wings of Aerus",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(41903, 30737, 8615, 3800),
            StarterName = "Statue of Kyros",
            StarterModel = 1315,
            Intro = "The stone eyes follow you. Kyros mastered every school of war before Aerus fell. Strike him and he studies you, then fights as you fight: an armsman or a wizard for Albion, a champion or an enchanter for Hibernia, a warrior or a runemaster for Midgard. Each shape brings its own trick.",
            Victory = "Kyros lowers his last borrowed weapon and returns to stone.",
            Replaces = new[] { "Kyros" },
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Kyros steps down from his plinth!",
                    Mobs = { new TrialMobSpec { Name = "Kyros", Model = 1315, Level = 65, Size = 150, Health = 90000, DamageFactor = 1.3, MeleeDamage = eDamageType.Slash, Tag = "kyros", OnHit = KyrosHit } },
                    OnTick = KyrosTick,
                },
            },
        };

        private static int KyrosHit(TrialMob mob, GameObject source, eDamageType type, int damage)
        {
            Noted(mob, source, type, damage);
            return LogOf(mob.Run).Warrior ? damage / 2 : damage;
        }

        private static void KyrosTick(TrialRun run)
        {
            TrialMob kyros = One(run, "kyros");
            HitLog log = LogOf(run);
            (GameLiving Attacker, bool Spell)[] recent = log.Recent;
            if (kyros == null || recent.Length == 0)
                return;

            if (run.Get("kyrosDamage", 0.0) == 0.0)
                run.Set("kyrosDamage", kyros.DamageFactor);

            int age = Clock(run, "formAge");
            if (run.Get("shape", -1) < 0 || age > KyrosFormSeconds)
            {
                (GameLiving attacker, bool spell) = recent[Util.Random(recent.Length - 1)];
                KyrosBecome(run, kyros, KyrosShapeFor(attacker.Realm, spell || IsCaster(attacker)), attacker);
                age = 1;
            }

            KyrosShape shape = (KyrosShape)run.Get("shape", 0);
            switch (shape)
            {
                case KyrosShape.Armsman:
                    Stick(kyros, run.Get<GameLiving>("charged"));
                    break;
                case KyrosShape.Wizard when age <= KyrosWizardSeconds && age % 3 == 0:
                    GameLiving distant = run.Hostiles(kyros, 3000).OrderByDescending(target => target.GetDistanceTo(kyros)).FirstOrDefault();
                    if (distant != null)
                        HurtAround(run, kyros, distant, 250, 220, eDamageType.Heat);
                    break;
                case KyrosShape.Champion when age % 4 == 0 && kyros.InCombat:
                    Heal(kyros, 0.01);
                    break;
                case KyrosShape.Enchanter when age % 5 == 0:
                    HurtAround(run, kyros, kyros, 1000, 280, eDamageType.Energy);
                    break;
                case KyrosShape.Runemaster when age % 4 == 0:
                    Splash(run, kyros, 2000, 200, 250, eDamageType.Energy, null);
                    break;
            }
        }

        private static void KyrosBecome(TrialRun run, TrialMob kyros, KyrosShape shape, GameLiving attacker)
        {
            run.Set("shape", (int)shape);
            run.Set("formAge", 1);
            run.Set("charged", attacker);
            bool warrior = shape == KyrosShape.Warrior;
            LogOf(run).Warrior = warrior;
            kyros.Model = KyrosShapes[shape].Model;
            kyros.Size = (byte)(warrior ? 200 : 150);
            kyros.DamageFactor = run.Get("kyrosDamage", kyros.DamageFactor) * (warrior ? 1.5 : 1.0);
            run.Tell(KyrosShapes[shape].Change);
        }

        // 9.2 Lachlen's Explorers

        private static TrialDefinition Explorers() => new()
        {
            ML = 9,
            Step = 2,
            Name = "Lachlen's Explorers",
            Scope = TrialScope.Group,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(43461, 17329, 6921, 354),
            StarterName = "Lachlen's Porter",
            StarterModel = 997,
            Intro = "Lachlen walked off the edge of every map there was, and his explorers followed him in stone. Speak with him first. He packs a remedy for every land he visits and he is hard to land a blow on, but no pack is bottomless. Press him hard.",
            Victory = "Lachlen closes his journal for the last time.",
            Replaces = new[] { "Weary Explorer", "Lachlen" },
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Lachlen looks up from his maps. Speak with him.",
                    Mobs =
                    {
                        new TrialMobSpec
                        {
                            Name = "Lachlen", Model = 998, Level = 65, Size = 50, At = new TrialPoint(43889, 16264, 6911, 147), Objective = true,
                            ObjectiveText = "Lachlen tells you of the lands his explorers crossed and the friends they lost on the way. \"And yet,\" he says, \"at least one of them remains alive.\" Behind you, stone feet begin to shuffle.",
                            OnDeath = Remove,
                        },
                    },
                },
                new TrialPhase
                {
                    Announce = "The weary explorers turn on you!",
                    Mobs =
                    {
                        Explorer(40234, 17495, 6886),
                        Explorer(41016, 17481, 6886),
                        Explorer(43083, 16941, 6932),
                        Explorer(43599, 17134, 6903),
                        Explorer(44106, 16747, 6943),
                        Explorer(43461, 17329, 6921),
                    },
                },
                new TrialPhase
                {
                    Announce = "Lachlen: You have killed my men. You will answer for every one of them!",
                    Mobs = { new TrialMobSpec { Name = "Lachlen", Model = 998, Level = 65, Size = 50, Health = 36000, DamageFactor = 1.2, At = new TrialPoint(43889, 16264, 6911, 147), MeleeDamage = eDamageType.Slash, Tag = "lachlen", OnHit = LachlenHit } },
                    OnTick = LachlenTick,
                },
            },
        };

        private static TrialMobSpec Explorer(int x, int y, int z) => new()
        {
            Name = "Weary Explorer", Model = 997, Level = 60, Size = 37, Health = 3000, DamageFactor = 0.9, At = new TrialPoint(x, y, z), MeleeDamage = eDamageType.Slash,
        };

        /// <summary>Lachlen evades often and answers some blows with a defensive bolt.</summary>
        private static int LachlenHit(TrialMob lachlen, GameObject source, eDamageType type, int damage)
        {
            if (TrialMob.KindOf(source, type) != TrialDamageKind.Spell && Util.Chance(LachlenEvadePercent))
                return 0;
            if (Util.Chance(10) && Culprit(source) is { IsAlive: true } attacker)
                attacker.TakeDamage(lachlen, eDamageType.Spirit, (int)(150 * lachlen.Run.DamageScale), 0);
            return damage;
        }

        private static void LachlenTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob lachlen = One(run, "lachlen");
            if (lachlen == null || seconds % 5 != 0 || lachlen.HealthPercent >= 100)
                return;

            int heals = run.Get("heals", 0);
            if (LachlenMayHeal(heals))
            {
                Heal(lachlen, 0.06);
                run.Set("heals", ++heals);
                if (!LachlenMayHeal(heals))
                    run.Tell("Lachlen reaches into his pack and finds it empty!");
            }
        }

        // 9.3 Atlantis Warlords

        private static TrialDefinition Warlords() => new()
        {
            ML = 9,
            Step = 3,
            Name = "Atlantis Warlords",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(43963, 24407, 5459, 1501),
            StarterName = "Shade of Evzen",
            StarterModel = 996,
            Intro = "Three realms warred until Lethos broke their warlords and set his advisors in stone around him. All of them sleep until Lethos is struck. Then Anzelm comes to his side, and the others strike at anyone who strays close. Each advisor that falls passes its gift to Lethos, and if Lethos falls first, his gifts pass to them. Lethos and all four advisors must fall.",
            Victory = "The Unifier and his advisors fall silent, and Evzen's shade nods.",
            Replaces = new[] { "Lethos the Unifier", "Advisor Anzelm", "Advisor Geges", "Advisor Gyges", "Advisor Timanthes", "Advisor Zarlanthus" },
            TimeLimitSeconds = 2100,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The statues of the Unifier's temple stand silent. Strike Lethos to wake them.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Lethos the Unifier", Model = 996, Level = 66, Size = 75, Health = 90000, DamageFactor = 1.4, At = new TrialPoint(44163, 24407, 5459, 1501), MeleeDamage = eDamageType.Slash, AggroRange = 0, Tag = "lethos", OnDeath = Confer },
                        Advisor("Advisor Anzelm", 994, 42513, 23964, 5278, 2038, "anzelm"),
                        Advisor("Advisor Timanthes", 993, 42982, 25645, 5331, 2483, "timanthes"),
                        Advisor("Advisor Zarlanthus", 997, 45121, 25598, 5155, 1267, "zarlanthus"),
                        Advisor("Advisor Gyges", 995, 45173, 23033, 5397, 450, "gyges"),
                    },
                    OnStart = run => run.Set("holders", new Dictionary<string, HashSet<string>>
                    {
                        ["lifetap"] = new() { "timanthes" },
                        ["mend"] = new() { "zarlanthus" },
                        ["storm"] = new() { "gyges" },
                        ["arms"] = new() { "anzelm" },
                    }),
                    OnTick = LethosTick,
                },
            },
        };

        private static TrialMobSpec Advisor(string name, ushort model, int x, int y, int z, ushort heading, string tag) => new()
        {
            Name = name, Model = model, Level = 64, Size = 75, Health = 12000, DamageFactor = 1.1, At = new TrialPoint(x, y, z, heading),
            AggroRange = 0, Immune = true, Tag = tag, OnDeath = Confer,
        };

        /// <summary>Who holds each gift after a statue falls: Lethos while he stands, else every statue still standing.</summary>
        public static IReadOnlyList<string> Heirs(string fallen, bool lethosStands, IEnumerable<string> standing) =>
            fallen != "lethos" && lethosStands ? new[] { "lethos" } : standing.Where(tag => tag != fallen).ToArray();

        private static void Confer(TrialRun run, TrialMob fallen)
        {
            string tag = fallen.Spec.Tag;
            Dictionary<string, HashSet<string>> holders = run.Get<Dictionary<string, HashSet<string>>>("holders");
            if (holders == null)
                return;

            List<string> standing = Statues.Where(statue => One(run, statue) != null).ToList();
            IReadOnlyList<string> heirs = Heirs(tag, standing.Contains("lethos"), standing);
            bool passed = false;
            foreach ((string gift, HashSet<string> owners) in holders)
            {
                if (!owners.Remove(tag))
                    continue;
                foreach (string heir in heirs)
                {
                    if (owners.Add(heir) && gift == "arms" && One(run, heir) is TrialMob armed)
                        armed.DamageFactor += 0.3 * run.DamageScale;
                    passed = true;
                }
            }
            if (passed && heirs.Count > 0)
                run.Tell($"{fallen.Name} crumbles, and its power flows into {(heirs.Count == 1 && heirs[0] == "lethos" ? "Lethos" : "the statues that remain")}!");

            if (tag == "lethos")
            {
                run.Tell("With the Unifier gone, every statue in the temple steps down from its pedestal!");
                foreach (string statue in standing.Where(statue => statue != "lethos"))
                {
                    if (One(run, statue) is TrialMob freed)
                        freed.MaxSpeedBase = 200;
                    Wake(run, statue);
                }
            }
        }

        private static void LethosTick(TrialRun run)
        {
            int seconds = Clock(run);
            TrialMob lethos = One(run, "lethos");
            if (!run.Get("awake", false))
            {
                if (lethos == null || !Engaged(lethos))
                    return;
                run.Set("awake", true);
                run.Tell("Lethos calls to Anzelm, and the temple's statues stir!");
                Wake(run, "anzelm");
                if (One(run, "anzelm") is TrialMob anzelm && lethos.TargetObject is GameLiving target)
                    Stick(anzelm, target);
                foreach (string tag in new[] { "timanthes", "zarlanthus", "gyges" })
                {
                    foreach (TrialMob statue in run.Mobs(tag))
                    {
                        statue.Immune = false;
                        Root(statue);
                        if (statue.Brain is StandardMobBrain brain)
                            brain.AggroRange = 500;
                    }
                }
            }

            Dictionary<string, HashSet<string>> holders = run.Get<Dictionary<string, HashSet<string>>>("holders");
            if (holders == null)
                return;

            foreach (string tag in holders["lifetap"])
            {
                if (seconds % 8 == 0 && One(run, tag) is { InCombat: true } tapper && tapper.TargetObject is GameLiving victim && victim.IsAlive)
                {
                    Hurt(run, tapper, victim, 180, eDamageType.Body);
                    Heal(tapper, 0.02);
                }
            }
            foreach (string tag in holders["mend"])
            {
                if (seconds % 10 == 3 && One(run, tag) is { InCombat: true })
                {
                    TrialMob weakest = Statues.Select(statue => One(run, statue)).Where(statue => statue != null).OrderBy(statue => statue.HealthPercent).FirstOrDefault();
                    Heal(weakest, 0.04);
                }
            }
            foreach (string tag in holders["storm"])
            {
                if (seconds % 12 == 6 && One(run, tag) is { InCombat: true } caller)
                    Splash(run, caller, 2500, 300, 280, eDamageType.Energy, "Lightning falls from the temple's statues!");
            }
        }

        // 9.4 Kings of Aerus

        private static TrialDefinition KingsOfAerus() => new()
        {
            ML = 9,
            Step = 4,
            Name = "Kings of Aerus",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(42135, 52640, 10304, 1945),
            StarterName = "Keeper of the Likenesses",
            StarterModel = 994,
            Intro = "Four likenesses of the old kings sleep here: the Tactician, the Brave, the Terrible and the Coward. Only a control crystal wakes one, and an avriel controller carries one nearby. Touch a king with it and he takes the crystal; defeat him to win it back for the next. Take them in any order you like.",
            Victory = "The last likeness crumbles. The kings of Aerus are at rest.",
            Replaces = new[] { "The Tactician", "The Brave", "The Terrible", "The Coward" },
            TimeLimitSeconds = 2400,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "An avriel controller guards the control crystal. Take it from her.",
                    Mobs =
                    {
                        new TrialMobSpec
                        {
                            Name = "avriel controller", Model = Avriel, Level = 62, Size = 50, Health = 6000, DamageFactor = 1.0, At = new TrialPoint(42435, 52640, 10304, 1945),
                            MeleeDamage = eDamageType.Thrust, AggroRange = 600, Required = false, Tag = "controller",
                            OnDeath = (run, _) =>
                            {
                                run.Set("crystal", true);
                                run.Tell("The avriel controller drops a control crystal. Touch one of the kings with it.");
                            },
                        },
                        Dormant("The Tactician", 41102, 51138, 10547, 3720, "tactician"),
                        Dormant("The Brave", 42135, 52840, 10304, 1945, "brave"),
                        Dormant("The Terrible", 45339, 53840, 10328, 2013, "terrible"),
                        Dormant("The Coward", 45606, 51138, 10330, 2810, "coward"),
                    },
                    OnTick = run =>
                    {
                        TacticianTick(run);
                        BraveTick(run);
                        TerribleTick(run);
                        CowardTick(run);
                    },
                    Done = run => Kings.All(king => run.Get($"fell-{king}", false)),
                },
            },
        };

        private static TrialMobSpec Dormant(string name, int x, int y, int z, ushort heading, string tag) => new()
        {
            Name = name, Model = 998, Level = 65, Size = 60, At = new TrialPoint(x, y, z, heading), Objective = true, Required = false, Tag = tag,
            ObjectiveText = $"You hold the control crystal up to {name}.", OnDeath = Touched,
        };

        /// <summary>A touched king wakes if the party holds the crystal, and keeps it until he falls.</summary>
        private static void Touched(TrialRun run, TrialMob dormant)
        {
            dormant.Delete();
            string tag = dormant.Spec.Tag;
            if (!run.Get("crystal", false) && !run.Sweeping)
            {
                run.Tell(One(run, "controller") != null ? "Nothing happens. You need the controller's crystal first."
                    : "Nothing happens. The crystal is held by the king you woke; defeat him to win it back.");
                run.Spawn(Dormant(dormant.Name, dormant.X, dormant.Y, dormant.Z, dormant.Heading, tag));
                return;
            }
            run.Set("crystal", false);
            TrialMob king = run.Spawn(new TrialMobSpec
            {
                Name = dormant.Name, Model = 998, Level = 65, Size = 60, Health = 40000, DamageFactor = 1.3, At = PointOf(dormant), MeleeDamage = eDamageType.Crush,
                Required = false, Tag = $"king-{tag}", Hurts = tag == "tactician" ? TrialDamageKind.Melee | TrialDamageKind.Ranged : TrialDamageKind.All,
                OnDeath = (r, fallen) =>
                {
                    r.Set($"fell-{tag}", true);
                    r.Set("crystal", true);
                    r.Tell($"{fallen.Name} falls, and the control crystal rolls free.");
                },
            });
            run.Tell($"The crystal sinks into {dormant.Name}, and he wakes!");
            if (tag == "tactician")
            {
                run.Tell("The Tactician sends his soldiers out, each with orders!");
                foreach (string order in new[] { "front", "rear", "hunt" })
                    run.Spawn(new TrialMobSpec { Name = "Soldier of the Tactician", Model = Statue, Level = 63, Size = 60, Health = 4000, DamageFactor = 1.0, At = Beside(king, 200), MeleeDamage = eDamageType.Slash, Required = false, Tag = $"soldier-{order}" });
            }
            else if (tag == "brave")
                run.Tell("The Brave picks one of you out and will not look away!");
            else if (tag == "terrible")
                run.Tell("The Terrible breathes out a sickly cloud. Do not let anyone fall to him!");
            else
                run.Tell("The Coward takes one look at you and thinks about running.");
        }

        private static void TacticianTick(TrialRun run)
        {
            TrialMob tactician = One(run, "king-tactician");
            if (tactician == null)
                return;

            if (One(run, "soldier-front") is TrialMob front)
                Stick(front, tactician.TargetObject as GameLiving);
            if (One(run, "soldier-rear") is TrialMob rear)
            {
                List<GameLiving> near = run.Hostiles(tactician, 3000).ToList();
                Stick(rear, near.FirstOrDefault(IsCaster) ?? near.OrderByDescending(target => target.GetDistanceTo(tactician)).FirstOrDefault());
            }
            if (One(run, "soldier-hunt") is TrialMob hunter)
            {
                GameLiving prey = run.Get<GameLiving>("prey");
                if (prey is not { IsAlive: true })
                    run.Set("prey", prey = RandomTarget(run, tactician, 3000));
                Stick(hunter, prey);
            }

            if (!run.Get("reserves", false) && tactician.HealthPercent <= TacticianReservesAt)
            {
                run.Set("reserves", true);
                run.Tell("Two statues beside the Tactician come to life!");
                run.Spawn(new TrialMobSpec { Name = "statue of the Tactician", Model = Statue, Level = 64, Size = 70, Health = 5000, DamageFactor = 1.1, Count = 2, At = Beside(tactician, 250), Required = false, Tag = "reserve" });
            }
        }

        private static void BraveTick(TrialRun run)
        {
            TrialMob brave = One(run, "king-brave");
            if (brave == null)
                return;

            GameLiving chosen = run.Get<GameLiving>("chosen");
            if (chosen is { IsAlive: false })
            {
                int kills = run.Get("braveKills", 0) + 1;
                run.Set("braveKills", kills);
                if (kills == BraveFrenzyKills)
                {
                    brave.Level += 2;
                    brave.DamageFactor *= 1.5;
                    run.Tell("The Brave roars over his eighth victim and grows stronger!");
                }
            }
            if (chosen is not { IsAlive: true })
            {
                List<GameLiving> near = run.Hostiles(brave, 2500).ToList();
                List<GameLiving> healers = near.Where(IsCaster).ToList();
                chosen = healers.Count > 0 && Util.Chance(60) ? healers[Util.Random(healers.Count - 1)] : near.Count > 0 ? near[Util.Random(near.Count - 1)] : null;
                run.Set("chosen", chosen);
                if (chosen != null)
                {
                    Hurt(run, brave, chosen, 200, eDamageType.Body);
                    Heal(brave, 0.02);
                    run.Tell($"The Brave turns on {chosen.Name} and drinks in their strength!");
                }
            }
            Stick(brave, chosen);
        }

        private static void TerribleTick(TrialRun run)
        {
            int seconds = Clock(run, "terrible");
            TrialMob terrible = One(run, "king-terrible");
            Dictionary<TrialMob, (int Born, int Splits)> ghosts = run.Get<Dictionary<TrialMob, (int, int)>>("ghosts");
            if (terrible == null && (ghosts == null || ghosts.Count == 0) && !run.Mobs("ghost").Any())
                return;
            ghosts ??= new();
            run.Set("ghosts", ghosts);

            if (terrible != null && seconds % 6 == 0 && terrible.TargetObject is GameLiving sick && sick.IsAlive)
                Hurt(run, terrible, sick, 90, eDamageType.Matter);

            HashSet<GameLiving> fallen = run.Get<HashSet<GameLiving>>("fallen");
            if (fallen == null)
                run.Set("fallen", fallen = Party(run).Where(member => !member.IsAlive).ToHashSet());
            foreach (GameLiving member in Party(run).ToList())
            {
                if (member.IsAlive)
                {
                    fallen.Remove(member);
                    continue;
                }
                if (!fallen.Add(member) || run.Mobs("ghost").Count() >= GhostCap)
                    continue;
                TrialMob ghost = run.Spawn(Ghost("ghostly statue", PointOf(member), "ghost"));
                ghosts[ghost] = (seconds, 0);
                run.Tell($"A ghostly statue rises over {member.Name}!");
            }

            foreach ((TrialMob ghost, (int born, int splits)) in ghosts.ToList())
            {
                if (!ghost.IsAlive || ghost.ObjectState != GameObject.eObjectState.Active)
                {
                    ghosts.Remove(ghost);
                    continue;
                }
                if (seconds - born < GhostSplitSeconds * (splits + 1))
                    continue;
                if (run.Mobs("ghost").Count() < GhostCap)
                    run.Spawn(Ghost("lesser ghostly statue", Beside(ghost, 120), "ghost"));
                if (splits + 1 >= GhostSplits)
                {
                    ghosts.Remove(ghost);
                    ghost.Delete();
                    run.Tell("A ghostly statue shatters into its copies!");
                }
                else
                    ghosts[ghost] = (born, splits + 1);
            }
        }

        private static TrialMobSpec Ghost(string name, TrialPoint at, string tag) => new()
        {
            Name = name, Model = Statue, Level = 60, Size = (byte)(name.StartsWith("lesser") ? 40 : 55), Health = 2500, DamageFactor = 0.8, At = at, Required = false, Tag = tag,
            OnHit = (_, _, type, damage) => GhostDamage(type, damage),
        };

        private static void CowardTick(TrialRun run)
        {
            TrialMob coward = One(run, "king-coward");
            if (coward == null)
                return;
            int seconds = Clock(run, "coward");

            if (!run.Get("cowardHealers", false) && coward.HealthPercent <= CowardHealersAt)
            {
                run.Set("cowardHealers", true);
                run.Tell("The Coward cries out, and two sacrificial healers rush to his side!");
                run.Spawn(new TrialMobSpec { Name = "sacrificial healer", Model = 997, Level = 60, Size = 35, Health = 3000, DamageFactor = 0.4, At = Beside(coward, 200), Count = 2, Spread = 150, Required = false, Tag = "healer" });
            }
            if (seconds % 5 == 0)
            {
                foreach (TrialMob _ in run.Mobs("healer"))
                    Heal(coward, 0.02);
            }

            string stage = run.Get("cowardStage", "fight");
            int since = Clock(run, "cowardSince");
            switch (stage)
            {
                case "fight" when coward.HealthPercent <= CowardFleesAt:
                    run.Set("cowardStage", "fleeing");
                    run.Set("cowardSince", 0);
                    run.Set("cowardSpeed", (int)coward.MaxSpeedBase);
                    (coward.Brain as StandardMobBrain)?.ClearAggroList();
                    coward.StopAttack();
                    coward.WalkTo(new Point3D(CowardHouse.X, CowardHouse.Y, CowardHouse.Z), 300);
                    run.Set("trapped", RandomTarget(run, coward, 1500));
                    run.Set("trapClock", 0);
                    run.Tell("The Coward bolts for a nearby house, springing an energy trap behind him!");
                    break;
                case "fleeing" when coward.GetDistanceTo(new Point3D(CowardHouse.X, CowardHouse.Y, CowardHouse.Z)) < 200 || since >= 8:
                    coward.MoveInRegion(coward.CurrentRegionID, CowardHouse.X, CowardHouse.Y, CowardHouse.Z, coward.Heading, false);
                    Root(coward);
                    run.Set("cowardStage", "hiding");
                    run.Set("cowardSince", 0);
                    break;
                case "hiding" when since >= CowardHideSeconds:
                    coward.MaxSpeedBase = (short)run.Get("cowardSpeed", 200);
                    run.Set("cowardStage", "ring");
                    run.Set("ring", PointOf(coward));
                    run.Tell("The Coward comes out of hiding behind a ring of energy, and statues rise to hold you back!");
                    run.Spawn(new TrialMobSpec { Name = "rooting statue", Model = Statue, Level = 60, Size = 45, Health = 2500, DamageFactor = 0.5, At = Beside(coward, 250), Count = 2, Spread = 200, Required = false, Tag = "rooter" });
                    break;
            }

            if (run.Get<GameLiving>("trapped") is { IsAlive: true } trapped && Clock(run, "trapClock") <= CowardTrapSeconds)
                Hurt(run, coward, trapped, 100, eDamageType.Energy);

            if (run.Get("cowardStage", "fight") == "ring")
            {
                TrialPoint ring = run.Get<TrialPoint>("ring");
                Point3D centre = new(ring.X, ring.Y, ring.Z);
                foreach (GameLiving target in run.Hostiles(coward, 3000))
                {
                    if (target.GetDistanceTo(centre) <= CowardRingRadius)
                        Hurt(run, coward, target, 70, eDamageType.Energy);
                }
                if (seconds % 4 == 0)
                {
                    List<GameLiving> outside = run.Hostiles(coward, 3000).Where(target => target.GetDistanceTo(centre) > CowardRingRadius).ToList();
                    if (outside.Count > 0)
                        Hurt(run, coward, outside[Util.Random(outside.Count - 1)], 350, eDamageType.Energy);
                }
            }
        }

        // 9.5 Scholars of Aerus

        private static TrialDefinition Scholars() => new()
        {
            ML = 9,
            Step = 5,
            Name = "Scholars of Aerus",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(28245, 24572, 8821, 3140),
            StarterName = "Nelos's Lectern",
            StarterModel = 1315,
            Intro = "Class is in session. Strike Nelos and his students come running from the houses around him. Watch him closely: whatever he does five times, they learn to do. Cold, energy or spirit can knock a lesson out of a student's head for good. Be quick, or it gets crowded.",
            Victory = "Nelos dismisses the class.",
            Replaces = new[] { "Nelos", "student of Nelos" },
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Nelos turns from his slate and looks you over.",
                    Mobs = { new TrialMobSpec { Name = "Nelos", Model = 1315, Level = 70, Size = 87, Health = 70000, DamageFactor = 1.3, Tag = "nelos" } },
                    OnTick = NelosTick,
                    Done = run => !run.Mobs("nelos").Any(),
                },
            },
        };

        public static readonly string[] NelosAbilities = { "pbaoe", "heal", "stun" };

        /// <summary>Students learn a lesson once Nelos has used it this many times.</summary>
        public static bool StudentsLearn(int uses) => uses >= NelosLessons;

        private static readonly TrialPoint[] Houses = { new(29804, 27576, 8643), new(29435, 24025, 8653) };

        private static void NelosTick(TrialRun run)
        {
            TrialMob nelos = One(run, "nelos");
            if (nelos == null || !Engaged(nelos))
                return;

            int seconds = Clock(run);
            Dictionary<TrialMob, HashSet<string>> taught = run.Get<Dictionary<TrialMob, HashSet<string>>>("taught") ?? new();
            run.Set("taught", taught);
            HashSet<TrialMob> forgetful = run.Get<HashSet<TrialMob>>("forgetful") ?? new();
            run.Set("forgetful", forgetful);
            Dictionary<string, int> uses = run.Get<Dictionary<string, int>>("uses") ?? NelosAbilities.ToDictionary(ability => ability, _ => 0);
            run.Set("uses", uses);

            if (seconds == 1)
                run.Tell("Nelos rings for his students!");
            int students = run.Get("students", 0);
            if ((seconds == 1 || seconds % 30 == 0) && students < 16)
            {
                int wave = seconds == 1 ? 4 : 1;
                foreach (TrialPoint house in Houses)
                {
                    for (int i = 0; i < wave; i++)
                    {
                        TrialMob student = run.Spawn(Student(Ring(house, i, wave, wave > 1 ? 150 : 0)));
                        taught[student] = uses.Where(use => StudentsLearn(use.Value)).Select(use => use.Key).ToHashSet();
                    }
                }
                run.Set("students", students + 2 * wave);
            }

            HitLog log = LogOf(run);
            while (log.Forgot.TryDequeue(out TrialMob dazed))
            {
                if (taught.TryGetValue(dazed, out HashSet<string> lessons) && lessons.Count > 0 && forgetful.Add(dazed))
                {
                    lessons.Clear();
                    run.Tell("A student of Nelos shakes his head, his lessons forgotten!");
                }
            }

            if (seconds % 10 == 0 && Use(run, nelos, "pbaoe", uses, taught, forgetful))
                HurtAround(run, nelos, nelos, 500, 220, eDamageType.Energy);
            if (seconds % 15 == 5 && nelos.HealthPercent < 95 && Use(run, nelos, "heal", uses, taught, forgetful))
                Heal(nelos, 0.04);
            if (seconds % 12 == 8 && Use(run, nelos, "stun", uses, taught, forgetful) && RandomTarget(run, nelos, 1500) is GameLiving dazedTarget)
                Hurt(run, nelos, dazedTarget, 220, eDamageType.Energy);

            foreach ((TrialMob student, HashSet<string> lessons) in taught.ToList())
            {
                if (!student.IsAlive || student.ObjectState != GameObject.eObjectState.Active)
                {
                    taught.Remove(student);
                    continue;
                }
                int offset = student.ObjectID % 5;
                if (lessons.Contains("pbaoe") && (seconds + offset) % 10 == 0)
                    HurtAround(run, student, student, 300, 120, eDamageType.Energy);
                if (lessons.Contains("heal") && (seconds + offset) % 12 == 0)
                    Heal(nelos.HealthPercent < student.HealthPercent ? nelos : student, 0.02);
                if (lessons.Contains("stun") && (seconds + offset) % 15 == 0 && student.TargetObject is GameLiving struck && struck.IsAlive)
                    Hurt(run, student, struck, 120, eDamageType.Energy);
            }
        }

        private static bool Use(TrialRun run, TrialMob nelos, string ability, Dictionary<string, int> uses, Dictionary<TrialMob, HashSet<string>> taught, HashSet<TrialMob> forgetful)
        {
            uses[ability]++;
            if (uses[ability] == NelosLessons)
            {
                foreach ((TrialMob student, HashSet<string> lessons) in taught.Where(pair => !forgetful.Contains(pair.Key)))
                    lessons.Add(ability);
                run.Tell(ability switch
                {
                    "pbaoe" => "The students have watched Nelos burn the air around him, and now they do it too!",
                    "heal" => "The students have learned to mend wounds from watching Nelos!",
                    _ => "The students copy Nelos's dazing strike!",
                });
            }
            return true;
        }

        private static TrialMobSpec Student(TrialPoint house) => new()
        {
            Name = "student of Nelos", Model = 1315, Level = 60, Size = 50, Health = 2500, DamageFactor = 1.0, At = house, AggroRange = 3500, Required = false, Tag = "student",
            OnHit = (student, _, type, damage) =>
            {
                if (type is eDamageType.Cold or eDamageType.Energy or eDamageType.Spirit && Util.Chance(NelosForgetPercent))
                    LogOf(student.Run).Forgot.Enqueue(student);
                return damage;
            },
        };

        // 9.6 Patrolling the City

        private static TrialDefinition Patrol() => new()
        {
            ML = 9,
            Step = 6,
            Name = "Patrolling the City",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(32113, 14034, 6872, 1040),
            StarterName = "Katri's Herald",
            StarterModel = 1216,
            Intro = "Katri walks these streets and keeps the peace. Attack her and she sends her personal guard running for help, and you will not wear her down past three quarters until he comes back with five avriel guardians. Then she can fall. She and her guards all heal, so break the guards first.",
            Victory = "Katri's patrol is broken. The streets are open.",
            Replaces = new[] { "Katri", "Katri's Personal Guard" },
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Katri and her guard turn to face you.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Katri", Model = 1217, Level = 66, Size = 50, Health = 75000, DamageFactor = 1.2, At = new TrialPoint(32313, 14034, 6872, 1040), MeleeDamage = eDamageType.Thrust, Tag = "katri" },
                        new TrialMobSpec { Name = "Katri's Personal Guard", Model = 1216, Level = 64, Size = 50, Health = 20000, DamageFactor = 1.0, At = new TrialPoint(32611, 13997, 6875, 1039), MeleeDamage = eDamageType.Thrust, AggroRange = 0, Required = false, Tag = "guard" },
                    },
                    OnTick = KatriTick,
                },
            },
        };

        private static void KatriTick(TrialRun run)
        {
            TrialMob katri = One(run, "katri");
            if (katri == null)
                return;
            int seconds = Clock(run);
            string stage = run.Get("katriStage", "patrol");

            if (stage == "patrol" && Engaged(katri))
            {
                run.Set("katriStage", stage = "alone");
                run.Set("helpClock", 0);
                if (One(run, "guard") is TrialMob runner)
                {
                    (runner.Brain as StandardMobBrain)?.ClearAggroList();
                    runner.StopAttack();
                    runner.WalkTo(new Point3D(runner.X + 600, runner.Y - 45, runner.Z), 250);
                }
                run.Tell("Katri sends her personal guard galloping off for help!");
            }

            if (stage == "alone")
            {
                HoldAt(katri, KatriFloor);
                int waited = Clock(run, "helpClock");
                if (waited == 8 && One(run, "guard") is TrialMob gone && !gone.InCombat)
                {
                    gone.Delete();
                    run.Set("guardAway", true);
                }
                if (waited >= KatriHelpSeconds || run.Sweeping)
                {
                    run.Set("katriStage", "reinforced");
                    HoldAt(katri, 0);
                    if (run.Get("guardAway", false))
                        run.Spawn(new TrialMobSpec { Name = "Katri's Personal Guard", Model = 1216, Level = 64, Size = 50, Health = 20000, DamageFactor = 1.0, At = Beside(katri, 300), MeleeDamage = eDamageType.Thrust, Required = false, Tag = "guard" });
                    run.Spawn(new TrialMobSpec { Name = "avriel guardian", Model = Avriel, Level = 60, Size = 52, Health = 5000, DamageFactor = 1.0, Count = KatriGuardians, Spread = 300, MeleeDamage = eDamageType.Thrust, Required = false, Tag = "avrielGuard" });
                    Wake(run, "guard");
                    run.Tell("The guard returns with five avriel guardians, and Katri's defence breaks!");
                }
            }

            if (seconds % 12 == 0 && katri.InCombat)
                Splash(run, katri, 2000, 300, 220, eDamageType.Energy, "Katri calls down lightning!");
            if (seconds % 10 == 0 && katri.HealthPercent < 100)
            {
                Heal(katri, 0.02);
                foreach (TrialMob _ in run.Mobs("guard").Concat(run.Mobs("avrielGuard")))
                    Heal(katri, 0.005);
            }
            if (seconds % 10 == 5)
            {
                foreach (TrialMob guard in run.Mobs("guard").Concat(run.Mobs("avrielGuard")).Where(guard => guard.HealthPercent < 80).Take(1))
                    Heal(guard, 0.05);
            }
        }

        // 9.7 Uncontrolled Host

        private static readonly (string Name, string Gift)[] Kin =
        {
            ("armoured iaculus", "hide"),
            ("venomous iaculus", "venom"),
            ("spitting iaculus", "spit"),
            ("rending iaculus", "bleed"),
        };

        private static TrialDefinition Host() => new()
        {
            ML = 9,
            Step = 7,
            Name = "Uncontrolled Host",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(33398, 41401, 10343, 489),
            StarterName = "Shed Iaculus Skin",
            StarterModel = 1233,
            Intro = "The skin is still warm. The iaculi of this pit eat one another, and the one that has eaten every kind becomes the Puissant Iaculus. A hungry one is feeding now on four kin: armoured, venomous, spitting and rending. Each kin it eats gives it that kin's gift, so kill what you can before it does, then kill the Puissant.",
            Victory = "The Puissant Iaculus coils and lies still.",
            Replaces = new[] { "Puissant Iaculus" },
            // The pit holds 26 aggressive world iaculi within 700 (aggro 500); the trial brings its own, and the default 450
            // left six that killed the sweep tester at the starter (live pass 2026-11-14).
            ClearRadius = 800,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The pit begins to writhe, and a hungry iaculus starts to feed!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "hungry iaculus", Model = Iaculus, Level = 62, Size = 70, Health = 5000, At = new TrialPoint(33398, 41601, 10343, 489), AggroRange = 0, Immune = true, Required = false, Tag = "hungry" },
                        KinSpec(0), KinSpec(1), KinSpec(2), KinSpec(3),
                    },
                    OnStart = run =>
                    {
                        run.Mobs("hungry").ToList().ForEach(Root);
                        run.Set("eaten", new HashSet<string>());
                    },
                    OnTick = FeedingTick,
                    Done = run => run.Get("risen", false),
                },
                new TrialPhase
                {
                    Announce = "The Puissant Iaculus rises from the pit!",
                    OnStart = RisePuissant,
                    OnTick = PuissantTick,
                    Done = run => !run.Mobs("puissant").Any(),
                },
            },
        };

        private static TrialMobSpec KinSpec(int index) => new()
        {
            Name = Kin[index].Name, Model = Iaculus, Level = 60, Size = 55, Health = 3500, DamageFactor = 0.8, At = Ring(new TrialPoint(33398, 41601, 10343), index, Kin.Length, 450),
            MeleeDamage = eDamageType.Thrust, AggroRange = 600, Required = false, Tag = $"kin{index}",
            Hurts = Kin[index].Gift == "hide" ? TrialDamageKind.Melee | TrialDamageKind.Spell : TrialDamageKind.All,
        };

        private static void FeedingTick(TrialRun run)
        {
            int seconds = Clock(run);
            HashSet<string> eaten = run.Get<HashSet<string>>("eaten");
            List<int> alive = Enumerable.Range(0, Kin.Length).Where(index => run.Mobs($"kin{index}").Any()).ToList();
            TrialMob hungry = One(run, "hungry");

            if (hungry != null && alive.Count > 0 && seconds % IaculusMealSeconds == 0)
            {
                int meal = alive[Util.Random(alive.Count - 1)];
                One(run, $"kin{meal}")?.Delete();
                eaten.Add(Kin[meal].Gift);
                hungry.Size = (byte)Math.Min(130, hungry.Size + 12);
                run.Tell($"The hungry iaculus devours the {Kin[meal].Name} and takes its strength!");
                alive.Remove(meal);
            }

            if (alive.Count == 0 || hungry == null)
                run.Set("risen", true);
        }

        private static void RisePuissant(TrialRun run)
        {
            HashSet<string> eaten = run.Get<HashSet<string>>("eaten") ?? new();
            TrialPoint at = One(run, "hungry") is TrialMob hungry ? PointOf(hungry) : new TrialPoint(33398, 41601, 10343, 489);
            run.Mobs("hungry").ToList().ForEach(mob => mob.Delete());
            run.Spawn(new TrialMobSpec
            {
                Name = "Puissant Iaculus", Model = Iaculus, Level = (byte)(65 + eaten.Count), Size = 132, Health = 30000 + 5000 * eaten.Count, DamageFactor = 1.0 + 0.1 * eaten.Count,
                At = at, MeleeDamage = eDamageType.Thrust, Tag = "puissant",
                Hurts = eaten.Contains("hide") ? TrialDamageKind.Melee | TrialDamageKind.Spell : TrialDamageKind.All,
                OnHit = (mob, source, type, damage) => IaculusDamage(type, Noted(mob, source, type, damage)),
            });
            run.Tell(eaten.Count == 0 ? "It ate nothing, and rises weak and hungry." : $"It carries the gifts of {eaten.Count} of its kin: {string.Join(", ", eaten)}.");
        }

        private static void PuissantTick(TrialRun run)
        {
            TrialMob puissant = One(run, "puissant");
            if (puissant == null)
                return;
            int seconds = Clock(run, "puissant");
            HashSet<string> eaten = run.Get<HashSet<string>>("eaten") ?? new();
            Stick(puissant, LogOf(run).First);

            GameLiving target = puissant.TargetObject as GameLiving;
            if (eaten.Contains("venom") && seconds % 6 == 0 && target is { IsAlive: true })
                Hurt(run, puissant, target, 90, eDamageType.Body);
            if (eaten.Contains("bleed") && seconds % 5 == 0 && target is { IsAlive: true })
                Hurt(run, puissant, target, 70, eDamageType.Slash);
            if (eaten.Contains("spit") && seconds % 9 == 0 && puissant.InCombat)
                Splash(run, puissant, 1500, 300, 160, eDamageType.Body, null);
        }

        // 9.8 Guardians of the Phoenix

        private static TrialDefinition Guardians() => new()
        {
            ML = 9,
            Step = 8,
            Name = "Guardians of the Phoenix",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(34963, 48751, 12049, 1126),
            StarterName = "Phoenix Herald",
            StarterModel = 1176,
            Intro = "None pass to the Phoenix until a sphinx calls them worthy. Chan, Kiran and Roshan stand on the road and will not move unless struck. Defeat any one of them. Slip past Chan without a fight and a guardian of your own will shadow you, and sooner or later it will turn on you.",
            Victory = "The sphinxes bow their heads. You may pass.",
            Replaces = new[] { "Chan", "Roshan", "Kiran" },
            TimeLimitSeconds = 2100,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Three sphinxes watch the road. Choose one.",
                    Mobs =
                    {
                        SphinxSpec("Chan", new TrialPoint(35213, 48751, 12049, 1126)),
                        SphinxSpec("Kiran", new TrialPoint(38401, 50415, 12049, 2025)),
                        SphinxSpec("Roshan", new TrialPoint(40097, 48749, 12049, 1103)),
                    },
                    OnTick = SphinxTick,
                    Done = run => run.Get("sphinxFell", false),
                },
            },
        };

        private static TrialMobSpec SphinxSpec(string name, TrialPoint at) => new()
        {
            Name = name, Model = Sphinx, Level = 66, Size = 100, Health = 60000, DamageFactor = 1.3, At = at, AggroRange = 0, Required = false, Tag = "sphinx",
            OnDeath = (run, fallen) =>
            {
                run.Set("sphinxFell", true);
                run.Tell($"{fallen.Name} falls. The road's guardians fade.");
            },
        };

        private static void SphinxTick(TrialRun run)
        {
            int seconds = Clock(run);
            Dictionary<GamePlayer, TrialMob> shadows = run.Get<Dictionary<GamePlayer, TrialMob>>("shadows") ?? new();
            run.Set("shadows", shadows);

            foreach (GamePlayer player in run.Players.Concat(run.Leader.Group?.GetPlayersInTheGroup() ?? Enumerable.Empty<GamePlayer>()).Distinct().ToList())
            {
                if (shadows.ContainsKey(player) || !player.IsAlive || player.HasFinishedMLStep(9, 8) || !PastChan(player.X, player.Z) || run.Mobs("sphinx").Any(sphinx => sphinx.InCombat))
                    continue;
                TrialMob shadow = run.Spawn(new TrialMobSpec
                {
                    Name = $"{player.Name} guardian", Model = 1177, Level = 70, Size = 80, Health = 8000, DamageFactor = 1.3, At = Beside(player, 200),
                    AggroRange = 0, Required = false, Tag = "shadow", Hurts = TrialDamageKind.Spell,
                });
                shadow.Follow(player, 150, 5000);
                shadows[player] = shadow;
                run.Tell($"{player.Name} has passed without leave, and a guardian falls in behind them!");
            }

            if (seconds % SphinxGuardianCheckSeconds != 0)
                return;
            foreach ((GamePlayer player, TrialMob shadow) in shadows)
            {
                if (shadow.IsAlive && !shadow.InCombat && player.IsAlive && Util.Chance(SphinxGuardianChance))
                {
                    Stick(shadow, player);
                    run.Tell($"{shadow.Name} turns on its charge!");
                }
            }
        }

        // 9.9 Centaur's Patrol

        private static TrialDefinition CentaursPatrol() => new()
        {
            ML = 9,
            Step = 9,
            Name = "Centaur's Patrol",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(38453, 29061, 8584, 2887),
            StarterName = "Avriel Grave Marker",
            StarterModel = 1216,
            Intro = "Neola stays out of sight while her sisters patrol, and she counts every one that falls. Kill thirty of the sisters who answer this marker and she will come for you with two guardians. The avriel who roam the rest of the city don't count. Neola and both guardians must fall, and all three of them heal.",
            Victory = "Neola falls among her sisters.",
            Replaces = new[] { "Neola" },
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Avriel sisters close in around the marker!",
                    OnTick = SistersTick,
                    Done = run => run.Get("sistersSlain", 0) >= NeolaSisters,
                },
                new TrialPhase
                {
                    Announce = "Neola: Enough! You will answer for my sisters!",
                    OnStart = run =>
                    {
                        TrialPoint at = run.Get("lastSister", new TrialPoint(38453, 29061, 8584, 2887));
                        run.Spawn(new TrialMobSpec { Name = "Neola", Model = Avriel, Level = 65, Size = 60, Health = 70000, DamageFactor = 1.3, At = at, MeleeDamage = eDamageType.Thrust, Tag = "neola" });
                        for (int i = 0; i < 2; i++)
                            run.Spawn(new TrialMobSpec { Name = "avriel guardian", Model = Avriel, Level = 60, Size = 56, Health = 6000, DamageFactor = 1.1, At = Ring(at, i, 2, 250), MeleeDamage = eDamageType.Thrust, Tag = "neolaGuard" });
                        foreach (TrialMob mob in run.Mobs("neola").Concat(run.Mobs("neolaGuard")))
                        {
                            if (mob.Brain is StandardMobBrain brain)
                                brain.AggroRange = 2500;
                        }
                    },
                    OnTick = NeolaTick,
                    Done = run => !run.Mobs("neola").Any() && !run.Mobs("neolaGuard").Any(),
                },
            },
        };

        private static void SistersTick(TrialRun run)
        {
            int slain = run.Get("sistersSlain", 0);
            int spawned = run.Get("sistersSpawned", 0);
            int alive = run.Mobs("sister").Count();
            int want = Math.Min(NeolaSisters - slain, run.Sweeping ? NeolaSisters : NeolaSistersAtOnce);
            while (alive < want)
            {
                run.Spawn(new TrialMobSpec
                {
                    Name = "avriel sister", Model = Avriel, Level = 58, Size = 48, Health = 2500, DamageFactor = 0.8,
                    At = Ring(new TrialPoint(38453, 29061, 8584), spawned % 7, 7, 500 + 100 * (spawned % 3)), MeleeDamage = eDamageType.Thrust, AggroRange = 900, Required = false, Tag = "sister",
                    OnDeath = (r, sister) =>
                    {
                        int count = r.Get("sistersSlain", 0) + 1;
                        r.Set("sistersSlain", count);
                        r.Set("lastSister", PointOf(sister));
                        r.Tell(count < NeolaSisters ? $"Neola has felt the loss of {count} of her sisters." : "Neola has felt the loss of thirty sisters, and she is coming!");
                    },
                });
                alive++;
                spawned++;
            }
            run.Set("sistersSpawned", spawned);
        }

        private static void NeolaTick(TrialRun run)
        {
            int seconds = Clock(run, "neola");
            TrialMob neola = One(run, "neola");
            if (neola != null && seconds % 15 == 0 && neola.HealthPercent < 100)
                Heal(neola, 0.05);
            if (neola != null && seconds % 10 == 5 && neola.InCombat)
                Splash(run, neola, 2000, 50, 260, eDamageType.Energy, null);
            if (seconds % 12 == 0)
            {
                foreach (TrialMob guard in run.Mobs("neolaGuard"))
                {
                    TrialMob patient = new[] { neola, guard }.Where(mob => mob != null).OrderBy(mob => mob.HealthPercent).First();
                    Heal(patient, 0.03);
                }
            }
        }

        // 9.10 Rise of the Phoenix

        private static TrialDefinition Phoenix() => new()
        {
            ML = 9,
            Step = 10,
            Name = "Rise of the Phoenix",
            Scope = TrialScope.Raid,
            Zone = TrialZone.Aerus,
            Anchor = new TrialPoint(35318, 32993, 19050, 76),
            StarterName = "Ember Warden",
            StarterModel = 994,
            Intro = "Three generators on the ring below feed the Phoenix, and while one runs she cannot truly die. Each one holds back another quarter of her. Clear the Ozos, then break the crystal. An operator will walk out to mend it, and letting him arrive undoes your work, so kill him first. Break all three and she will burn out for good. Fire will not touch her.",
            Victory = "The Phoenix burns down to ash, and this time the ash stays cold.",
            Replaces = new[] { "The Phoenix", "Phoenix Ozos", "Generator Operator", "Jai", "Lahamu" },
            TimeLimitSeconds = 2400,
            LeashRange = 12000,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The generators hum, and the Phoenix spreads her wings above the city!",
                    Mobs =
                    {
                        PhoenixSpec(),
                        GeneratorSpec(0), Ozos(0, 0), Ozos(0, 1), Ozos(0, 2),
                        GeneratorSpec(1), Ozos(1, 0), Ozos(1, 1), Ozos(1, 2),
                        GeneratorSpec(2), Ozos(2, 0), Ozos(2, 1), Ozos(2, 2),
                    },
                    OnStart = run =>
                    {
                        for (int i = 0; i < Generators.Length; i++)
                            run.Mobs($"gen{i}").ToList().ForEach(Root);
                    },
                    OnTick = PhoenixTick,
                    Done = run => GeneratorsUp(run) == 0 && !run.Mobs("phoenix").Any(),
                },
            },
        };

        private static TrialMobSpec PhoenixSpec() => new()
        {
            Name = "The Phoenix", Model = 1219, Level = 68, Size = 110, Health = 130000, DamageFactor = 1.6, At = new TrialPoint(34854, 35280, 19153, 4073),
            MeleeDamage = eDamageType.Heat, Required = false, Tag = "phoenix",
            OnHit = (_, _, type, damage) => type == eDamageType.Heat ? 0 : damage,
        };

        private static TrialMobSpec GeneratorSpec(int index) => new()
        {
            Name = "Phoenix Generator", Model = Obelisk, Level = 65, Size = 110, Health = 8000, DamageFactor = 0.05, At = Generators[index],
            AggroRange = 0, Immune = true, Required = false, Tag = $"gen{index}", OnDeath = (run, _) => SendOperator(run, index),
        };

        private static TrialMobSpec Ozos(int generator, int index) => new()
        {
            Name = "Phoenix Ozos", Model = 1042, Level = 62, Size = 57, Health = 3000, DamageFactor = 0.9, At = Ring(Generators[generator], index, 3, 300),
            MeleeDamage = eDamageType.Thrust, Required = false, Tag = $"ozos{generator}",
        };

        private static TrialMobSpec OperatorSpec(int index) => new()
        {
            Name = "Generator Operator", Model = Statue, Level = 65, Size = 75, Health = 6000, DamageFactor = 0.6, At = Operators[index], AggroRange = 0, Required = false, Tag = $"op{index}",
            OnDeath = (run, _) => run.Tell("The operator falls. That generator stays dark."),
        };

        private static int GeneratorsUp(TrialRun run) => Enumerable.Range(0, Generators.Length).Count(i => run.Mobs($"gen{i}").Any());

        private static void SendOperator(TrialRun run, int index)
        {
            run.Set($"walk{index}", 0);
            run.Spawn(OperatorSpec(index));
            run.Tell($"A generator goes dark, leaving {GeneratorsUp(run)} running. A Generator Operator sets out to repair it!");
        }

        private static void PhoenixTick(TrialRun run)
        {
            int seconds = Clock(run);
            for (int i = 0; i < Generators.Length; i++)
            {
                if (!run.Mobs($"ozos{i}").Any())
                    run.SetImmune($"gen{i}", false);
                WalkOperator(run, i);
            }

            int up = GeneratorsUp(run);
            TrialMob phoenix = One(run, "phoenix");
            if (phoenix == null)
            {
                if (up > 0)
                {
                    phoenix = run.Spawn(PhoenixSpec());
                    phoenix.Health = phoenix.MaxHealth * PhoenixFloor(up) / 100;
                    run.Tell("The Phoenix bursts into flame and is reborn!");
                }
                return;
            }

            HoldAt(phoenix, PhoenixFloor(up));
            if (up == 0 || !phoenix.InCombat)
                return;

            if (seconds % 10 == 0)
                Splash(run, phoenix, PhoenixFireballReach(up), 350, PhoenixFireball(up), eDamageType.Heat, null);
            if (seconds % 6 == 0)
                HurtAround(run, phoenix, phoenix, 500, PhoenixFlare(up), eDamageType.Heat);
        }

        private static void WalkOperator(TrialRun run, int index)
        {
            TrialMob walker = One(run, $"op{index}");
            if (walker == null || walker.InCombat)
                return;

            TrialPoint generator = Generators[index];
            if (!walker.IsMoving)
                walker.WalkTo(new Point3D(generator.X, generator.Y, generator.Z), 60);

            if (Clock(run, $"walk{index}") < OperatorWalkSeconds)
                return;

            walker.Delete();
            TrialMob repaired = run.Spawn(GeneratorSpec(index));
            repaired.Immune = false;
            Root(repaired);
            repaired.Health = repaired.MaxHealth / 2;
            run.Tell("A Generator Operator reaches his crystal, and it lights again!");
        }
    }
}
