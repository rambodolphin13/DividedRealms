using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS.Trials
{
    /// <summary>Master Level 6 trials: Typhon's Reach and the Ashen Isles, ending with the Chimera.</summary>
    public static class Ml6
    {
        private const ushort UraeusModel = 1234;
        private const ushort SalamanderModel = 1261;
        private const ushort WarriorModel = 1045;
        private const ushort PriestModel = 1046;
        private const ushort TaurModel = 1190;
        private const ushort SmallTaurModel = 1191;
        private const ushort SphinxModel = 1177;
        private const ushort EfreetModel = 1199;
        private const ushort MauModel = 1040;
        private const ushort PartModel = 997;
        private const ushort GateModel = 998;
        private const ushort OrbModel = 1391;

        public const int GlandsNeeded = 20;
        public const int FlameSeconds = 60;
        public const int IncineratorReach = 400;
        public const int LateefSpeed = 120;
        public const int LateefPatienceSeconds = 420;
        public const int EscortRange = 1500;
        public const int BridgeReach = 500;
        public const int AmbushSeconds = 600;
        public const int PallidaSplitPercent = 50;
        public const int ChanterCount = 6;
        public const int OfferingsNeeded = 2;
        public const int OrbSeconds = 1200;
        public const int BrazierCount = 8;
        public const int PlatformRadius = 700;
        public const int ChallengerBolt = 100;
        public const int GateBreathRadius = 800;
        public const int GateAddLimit = 6;
        public const int OpenGateWaveSeconds = 25;
        public const int FailingGateWaveSeconds = 12;
        public const int FailingGatePercent = 30;

        private static readonly TrialPoint LateefRow = new(515919, 573893, 10336, 2635);
        private static readonly TrialPoint LateefBridge = new(486533, 574213, 10497, 1016);
        private static readonly TrialPoint OrbCentre = new(551166, 576515, 10769, 2048);

        private static readonly TrialPoint[] HatchlingRows =
        {
            new(477952, 554227, 9592), new(476445, 551307, 9592), new(477903, 550623, 9593),
            new(478320, 553268, 9592), new(474993, 555334, 9594), new(476685, 550226, 9602),
        };

        private static readonly TrialMobSpec[] Hatchlings = HatchlingRows.Select(Hatchling).ToArray();

        private static readonly TrialMobSpec Battler = new()
        {
            Name = "taur battler", Model = SmallTaurModel, Level = 60, Size = 60, Health = 4000, DamageFactor = 0.9,
            At = new TrialPoint(550925, 539420, 10796), Required = false, Tag = "adds", MeleeDamage = eDamageType.Slash,
        };

        private static readonly TrialMobSpec Lancer = new()
        {
            Name = "taur lancer", Model = TaurModel, Level = 63, Size = 60, Health = 6000, DamageFactor = 1.1,
            At = new TrialPoint(550567, 539088, 10734), Required = false, Tag = "adds", MeleeDamage = eDamageType.Thrust,
        };

        private static readonly TrialMobSpec NamedBattler = new()
        {
            Name = "Battler", Model = 1047, Level = 70, Size = 45, Health = 9000, DamageFactor = 1.3,
            At = new TrialPoint(550925, 539420, 10796), Required = false, Tag = "adds", MeleeDamage = eDamageType.Crush,
        };

        private static readonly TrialMobSpec FadilPet = new()
        {
            Name = "sa", Model = EfreetModel, Level = 58, Size = 35, Health = 2500, DamageFactor = 0.6,
            At = new TrialPoint(486534, 574389, 10497), Required = false, Tag = "sa", MeleeDamage = eDamageType.Heat,
        };

        private static readonly TrialPoint UraeusPen = new(535369, 576334, 10618);
        private static readonly TrialPoint PlebianPen = new(538380, 579653, 10579);
        private static readonly TrialPoint SalamanderPen = new(535315, 578541, 10616);

        private static readonly TrialMobSpec ChimeraGuard = new()
        {
            Name = "elite taur defender", Model = SmallTaurModel, Level = 60, Size = 45, Health = 5000, DamageFactor = 0.9,
            At = new TrialPoint(550743, 559385, 10672), Required = false, Tag = "defender", MeleeDamage = eDamageType.Slash,
        };

        public static IEnumerable<TrialDefinition> Trials()
        {
            yield return Hunt();
            yield return Sacrifice();
            yield return Lateef();
            yield return Infestation();
            yield return Flames();
            yield return WithHush(Siraadi(), new[] { SiraadiPlatform }, HushRadius);
            yield return WithHush(Ankhkare(), new[] { UraeusPen, PlebianPen, SalamanderPen, new TrialPoint(539324, 578368, 10699) }, HushRadius);
            yield return Efreet();
            yield return WithHush(Fortress(), new[] { new TrialPoint(550793, 539622, 10833) }, HushRadius);
            yield return WithHush(Chimera(), new[] { new TrialPoint(550183, 558467, 10763) }, HushRadius);
        }

        /// <summary>
        /// Where the live encounter is the world's own camp, that camp stands aside while a run lasts and comes back after
        /// (<see cref="Ml4.Hush"/>). Siraadi's platform holds 12 world chanters and 9 prema beside the trial's own; with
        /// them a full bot group wiped at the third pillar (bot playtest 2026-11-14). Ankhkare's pens hold 5 uraeus and 7
        /// salamanders, the fortress gate 6 taur, the Chimera's room 3 elite defenders.
        /// </summary>
        public const int HushRadius = 1600;

        public static readonly TrialPoint SiraadiPlatform = new(485430, 562826, 9592);

        public static TrialDefinition WithHush(TrialDefinition definition, TrialPoint[] points, int radius)
        {
            Action<TrialRun> first = definition.Phases[0].OnStart;
            definition.Phases[0].OnStart = run =>
            {
                Ml4.Hush(run, points, radius);
                first?.Invoke(run);
            };
            Action<TrialRun, bool> end = definition.OnEnd;
            definition.OnEnd = (run, won) =>
            {
                end?.Invoke(run, won);
                Ml4.Unhush(run);
            };
            return definition;
        }

        private static TrialDefinition Hunt() => new()
        {
            ML = 6, Step = 1, Name = "Hunt for the Haje-Uraei", Scope = TrialScope.Group, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(468644, 537649, 10424),
            StarterName = "Scorched Snake Hunter", StarterModel = 381,
            Intro = "The rare one, the haje-uraei, is born from the death of its lesser kin. I have seen it rise from a corpse and slide off for the lava. Kill the uraeus around here and it will show itself. It spits fire from far off, so close with it quickly.",
            Victory = "The haje-uraei's coils go slack, and its venom is yours.",
            TimeLimitSeconds = 1200,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The uraeus nest uncoils around you.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "uraeus", Model = UraeusModel, Level = 58, Health = 3000, DamageFactor = 0.8, Count = 3, Spread = 500, MeleeDamage = eDamageType.Thrust },
                    },
                },
                new TrialPhase
                {
                    Announce = "A haje-uraei springs to life from the last body and turns toward you!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "haje-uraei", Model = UraeusModel, Level = 63, Size = 60, Health = 20000, DamageFactor = 1.1, Spread = 300, Tag = "haje", MeleeDamage = eDamageType.Thrust },
                    },
                    OnTick = HajeTick,
                },
            },
        };

        private static TrialDefinition Sacrifice() => new()
        {
            ML = 6, Step = 2, Name = "Sacrifice", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(464272, 561748, 10036, 2901),
            StarterName = "anubite prisoner", StarterModel = WarriorModel,
            Intro = "The siam-he were Anubites once, and they have forgotten it. They have dragged me here to feed the earth, one of their own blood. If my blood is spilled, something terrible will climb out of it. Help me against my escort, if you can. And if you cannot, stay and kill what comes.",
            Victory = "The Anathemic Salamander falls, and the trembling stops. The prisoner's death is paid for.",
            Replaces = new[] { "Anathemic Salamander", "siam-he mau prisoner", "siam-he sacrificial escort" },
            TimeLimitSeconds = 1500,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Seven sacrificial escorts close around the prisoner.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "siam-he sacrificial escort", Model = WarriorModel, Level = 61, Health = 3500, DamageFactor = 0.8, Count = 7, Spread = 450, MeleeDamage = eDamageType.Slash },
                    },
                },
                new TrialPhase
                {
                    Announce = "Too late. The last escort's blade finds its mark, and an Anubite dies on Am-he's altar. The earth trembles violently, and a curse climbs out of Volcanus!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Anathemic Salamander", Model = SalamanderModel, Level = 68, Size = 200, Health = 70000, DamageFactor = 0.9, Spread = 500, Tag = "anathemic" },
                    },
                    Pulses =
                    {
                        new TrialPulse { IntervalSeconds = 14, Damage = 160, Radius = 700, SourceTag = "anathemic", DamageType = eDamageType.Heat, Message = "The Anathemic Salamander breathes out a wash of cinders." },
                    },
                },
            },
        };

        private static TrialDefinition Lateef() => new()
        {
            ML = 6, Step = 3, Name = "Lateef", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = LateefRow,
            StarterName = "Lateef", StarterModel = PriestModel,
            Intro = "I followed Am-he and I am sick of fire. Sahathor has handed me the dagger and told me to die with it within the week. I want the desert. There is a great arched bridge to the west. See me across and the dagger is yours. I walk slowly and I will not go on alone, so stay close.",
            Victory = "Sutekh falls. You take back the dagger Lateef died for, and clean it.",
            Replaces = new[] { "Lateef", "Sutekh", "Fadil", "Udaji" },
            TimeLimitSeconds = 2400,
            LeashRange = 36000,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Lateef the Deserter sets off west for the bridge. He only walks while one of you is beside him.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Lateef the Deserter", Model = PriestModel, Level = 60, Size = 49, Objective = true, Required = false, Spread = 200, Tag = "deserter", ObjectiveText = "Lateef waves you on. He has no breath to spare for talk." },
                    },
                    OnStart = run => run.Set("deserter", run.Mobs("deserter").First()),
                    OnTick = LateefTick,
                    Done = run => run.Get<bool>("atBridge"),
                },
                new TrialPhase
                {
                    Announce = "Three servants of Am-he appear on the bridge. Sutekh takes the dagger from Lateef, kills him with it and sheathes it. They will leave in ten minutes unless you hold them in a fight!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Fadil", Model = PriestModel, Level = 66, Health = 12000, DamageFactor = 0.7, At = new TrialPoint(486534, 574289, 10497, 1016), Tag = "fadil", MeleeDamage = eDamageType.Crush },
                        new TrialMobSpec { Name = "Udaji", Model = PriestModel, Level = 66, Health = 20000, DamageFactor = 1.2, At = new TrialPoint(486532, 574139, 10498, 1016), Tag = "udaji", MeleeDamage = eDamageType.Slash },
                        new TrialMobSpec { Name = "Sutekh", Model = PriestModel, Level = 68, Health = 60000, DamageFactor = 1.4, At = LateefBridge, Tag = "sutekh", MeleeDamage = eDamageType.Slash },
                    },
                    Pulses =
                    {
                        new TrialPulse { IntervalSeconds = 12, Damage = 180, Radius = 700, SourceTag = "fadil", DamageType = eDamageType.Energy, Message = "Fadil looses a burst of raw energy across the bridge." },
                    },
                    OnStart = LateefFalls,
                    OnTick = AmbushTick,
                },
            },
        };

        private static TrialDefinition Infestation() => new()
        {
            ML = 6, Step = 4, Name = "Infestation", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(468740, 548123, 10136, 4009),
            StarterName = "Hissing Nest", StarterModel = UraeusModel,
            Intro = "The nest is seething. Kill what crawls out of it and the mother of them, Pallida-Uraei, will come looking. Bring her to half and she will split in three, but only one of the three is truly her. Kill that one and the others fade. Kill her before she splits and that counts too. The tail you take from her will open the way to Ankhkare.",
            Victory = "Pallida-Uraei lies still, and you cut away her tail. Its keeping is recorded, and Ankhkare's guardian will know it.",
            Replaces = new[] { "Pallida-Uraei" },
            TimeLimitSeconds = 1500,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The uraeus pour out of the nest.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "uraeus", Model = UraeusModel, Level = 58, Health = 3500, DamageFactor = 0.8, Count = 4, Spread = 450, MeleeDamage = eDamageType.Thrust },
                    },
                },
                new TrialPhase
                {
                    Announce = "A hissing noise is heard throughout Volcanus.",
                    Mobs =
                    {
                        new TrialMobSpec
                        {
                            Name = "Pallida-Uraei", Model = UraeusModel, Level = 66, Size = 75, Health = 45000, DamageFactor = 1.2, Spread = 300, Tag = "pallida", MeleeDamage = eDamageType.Thrust,
                            OnDeath = (run, _) => run.Set("pallidaDead", true),
                        },
                    },
                    OnTick = PallidaTick,
                    Done = run => run.Get<bool>("pallidaDead"),
                },
            },
        };

        private static TrialDefinition Flames() => new()
        {
            ML = 6, Step = 5, Name = "Flames", Scope = TrialScope.Solo, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(476838, 552923, 9592, 2070),
            StarterName = "crazed adventurer", StarterModel = 381,
            Intro = "Cold! It's all about cold! Hatchlings carry frozen glands, glands cool the spouts, and under the spouts are the four pieces. Bring me twenty glands. When the twentieth is in my hand the flames go out for sixty seconds, no more! Get the cogwheel, the fastener, the handle and the lens, target the big salamander, walk right up to him, and watch him burn.",
            Victory = "The livid salamander collapses into ash, and the ash is yours to keep. Ankhkare's guardian will know it.",
            Replaces = new[] { "livid salamander", "crazed adventurer" },
            TimeLimitSeconds = 2400,
            Phases =
            {
                new TrialPhase
                {
                    Announce = $"Hunt the hatchlings around the spouts. The adventurer needs {GlandsNeeded} frozen glands.",
                    Mobs = { Hatchlings[0], Hatchlings[1], Hatchlings[2] },
                    OnTick = HatchlingTick,
                    Done = run => run.Get<int>("glands") >= GlandsNeeded,
                },
                new TrialPhase
                {
                    Announce = $"\"That's the last one! Run!\" The flame spouts die. You have {FlameSeconds} seconds to grab all four pieces!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Cogwheel", Model = PartModel, Size = 27, Objective = true, At = new TrialPoint(476034, 554496, 9592), ObjectiveText = "You snatch the cogwheel from the cold spout." },
                        new TrialMobSpec { Name = "Fastener", Model = PartModel, Size = 27, Objective = true, At = new TrialPoint(476035, 551937, 9592), ObjectiveText = "You snatch the fastener from the cold spout." },
                        new TrialMobSpec { Name = "Handle", Model = PartModel, Size = 27, Objective = true, At = new TrialPoint(477568, 554493, 9592), ObjectiveText = "You snatch the handle from the cold spout." },
                        new TrialMobSpec { Name = "Lens", Model = PartModel, Size = 27, Objective = true, At = new TrialPoint(477572, 551934, 9592), ObjectiveText = "You snatch the lens from the cold spout." },
                    },
                    OnTick = SpoutTick,
                },
                new TrialPhase
                {
                    Announce = "The four pieces lock together into a Salamander Incinerator. Target the livid salamander and get close. Nothing else will hurt him.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "livid salamander", Model = SalamanderModel, Level = 65, Size = 150, Health = 8000, DamageFactor = 0.8, At = new TrialPoint(474883, 550113, 9781, 1174), Immune = true, Tag = "livid", MeleeDamage = eDamageType.Slash },
                    },
                    OnTick = IncineratorTick,
                },
            },
        };

        private static TrialDefinition Siraadi() => new()
        {
            ML = 6, Step = 6, Name = "Siraadi", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(484358, 564268, 9592),
            StarterName = "Escaped Offering", StarterModel = MauModel,
            Intro = "Stay down here by the lava, well back from the pillars. They answer one another's calls, so draw them out and fight them away from the platform. A chanter feeds each of the six pillars, and the pillars feed the altar. Kill all six chanters and Siraadi himself will come to see why the rites have stopped. His head will open the way to Ankhkare.",
            Victory = "Siraadi falls, and you take his head. Its keeping is recorded, and Ankhkare's guardian will know it.",
            Replaces = new[] { "Siraadi" },
            TimeLimitSeconds = 1800,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "Worshippers come down from the platform to see who has interrupted them.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "siam-he prema", Model = PriestModel, Level = 60, Health = 5000, DamageFactor = 1.0, Count = 2, At = new TrialPoint(485175, 562562, 9592), MeleeDamage = eDamageType.Slash },
                        Chanter(),
                    },
                    Pulses =
                    {
                        new TrialPulse { IntervalSeconds = 10, Damage = 120, Radius = 600, SourceTag = "chanter", DamageType = eDamageType.Heat },
                    },
                },
                new TrialPhase
                {
                    Announce = "A sacrificial escort marching for the ramp hears the fight and turns aside.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "siam-he sacrificial escort", Model = WarriorModel, Level = 61, Health = 6000, DamageFactor = 1.0, Count = 3, At = new TrialPoint(485175, 562562, 9592), MeleeDamage = eDamageType.Slash },
                    },
                },
                new TrialPhase
                {
                    Announce = "More worshippers run in from across Volcanus to refill the platform, a premloch at their head.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "siam-he premloch", Model = WarriorModel, Level = 61, Health = 6500, DamageFactor = 1.1, At = new TrialPoint(485175, 562562, 9592), MeleeDamage = eDamageType.Slash },
                        Chanter(),
                    },
                    Pulses =
                    {
                        new TrialPulse { IntervalSeconds = 10, Damage = 120, Radius = 600, SourceTag = "chanter", DamageType = eDamageType.Heat },
                    },
                },
                new TrialPhase
                {
                    Announce = "The last pillar goes dark and the altar dies. Siraadi the sacrificer descends from among the pillars.",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Siraadi", Model = PriestModel, Level = 67, Size = 62, Health = 60000, DamageFactor = 1.2, At = new TrialPoint(485430, 562826, 9592), Tag = "siraadi", MeleeDamage = eDamageType.Slash },
                    },
                    Pulses =
                    {
                        new TrialPulse { IntervalSeconds = 15, Damage = 200, Radius = 700, SourceTag = "siraadi", DamageType = eDamageType.Heat, Message = "Siraadi calls down Am-he's fire!" },
                    },
                },
            },
        };

        private static TrialDefinition Ankhkare() => new()
        {
            ML = 6, Step = 7, Name = "Ankhkare", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(536380, 575552, 10691, 2548),
            StarterName = "Ankhkare's Guardian", StarterModel = SphinxModel,
            Intro = "Three of us keep the burning way to Ankhkare, and each wants a toll: the tail of Pallida-Uraei, the ash of the livid salamander and the head of Siraadi. Bring at least two and the way opens. Each toll I take weakens him, and whatever stands behind a guardian you could not pay will come for you when he does. When a wall falls the swarm behind it is loosed, so kill it before you go on.",
            Victory = "Ankhkare falls, and the burning way goes dark.",
            Refuse = AnkhkareRefusal,
            Replaces = new[] { "Ankhkare's Guardian", "Ankhkare" },
            TimeLimitSeconds = 2100,
            LeashRange = 12000,
            Phases =
            {
                OfferingAt(4, null, "The guardian takes the tail of Pallida-Uraei and the fire falls."),
                Swarm(4, "The uraeus behind the wall strike at once!"),
                OfferingAt(6, new TrialPoint(539324, 578368, 10699, 2548), "Go on to the second guardian, to the south-east, with the head of Siraadi."),
                Swarm(6, "Siam-he and salamanders rush through the gap!"),
                OfferingAt(5, new TrialPoint(535871, 578879, 10699, 2560), "Go on to the third guardian, to the west, with the salamander ash."),
                Swarm(5, "Salamanders immune to fire and frost alike boil out of the last wall!"),
                new TrialPhase
                {
                    Announce = "Ankhkare rises to the north-west, and three more guardians with him. Keep your fighters bunched and go to him!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Ankhkare's Guardian", Model = SphinxModel, Level = 62, Health = 8000, DamageFactor = 1.0, Count = 3, At = new TrialPoint(532184, 582248, 10831), MeleeDamage = eDamageType.Slash },
                    },
                    OnStart = AnkhkareRises,
                    Pulses =
                    {
                        new TrialPulse { IntervalSeconds = 11, Damage = 240, Radius = 900, SourceTag = "ankhkare", DamageType = eDamageType.Heat, Message = "Ankhkare's fire washes over the ground around him." },
                    },
                },
            },
        };

        private static TrialDefinition Efreet() => new()
        {
            ML = 6, Step = 8, Name = "Efreet", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = OrbCentre,
            StarterName = "efreeti overseer", StarterModel = EfreetModel,
            Intro = "Do you dare the braziers? Say the word and the Orb of Apollo will burn where I stand, and eight challengers will take the braziers around it. Break the orb within twenty minutes. It will not strike back, but it burns whoever strikes it. The challengers hurl fire at anyone on the platform near the orb; step off and you are safe. They stand out of reach of a blade, but spells and arrows find them, and each one that falls gives its fire to the rest.",
            Victory = "The Orb of Apollo shatters. The braziers go out, and the challengers go with them.",
            Replaces = new[] { "efreeti overseer" },
            TimeLimitSeconds = 1500,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The Orb of Apollo blazes up, and eight challengers take their places at the braziers!",
                    Mobs = OrbMobs(),
                    OnStart = RootOrbMobs,
                    OnTick = OrbTick,
                },
            },
        };

        private static TrialDefinition Fortress() => new()
        {
            ML = 6, Step = 9, Name = "Fortress", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(551238, 539933, 10616),
            StarterName = "Taur Deserter", StarterModel = SmallTaurModel,
            Intro = "Vazul sits behind two gates, and I know how they are held. Breach them from here. Taurs will come the whole time, and faster when a gate is close to falling. When a gate is about to go, back away from it. Vazul sets traps. Break the second and he must come out, with his portalmasters. Kill those fast or the warping never stops.",
            Victory = "Vazul is dead and the fortress is breached.",
            Replaces = new[] { "Vazul" },
            TimeLimitSeconds = 2400,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The horns sound from the wall. Attack the outer gate!",
                    Mobs = { Gate("Outer Gate of Vazul's Fortress", new TrialPoint(550793, 539622, 10833)) },
                    OnStart = RootGate,
                    OnTick = GateTick,
                },
                new TrialPhase
                {
                    Announce = "The outer gate is down. Attack the inner gate!",
                    Mobs = { Gate("Inner Gate of Vazul's Fortress", new TrialPoint(550925, 539420, 10796)) },
                    OnStart = RootGate,
                    OnTick = GateTick,
                },
                new TrialPhase
                {
                    Announce = "Vazul strides out of the ruined gate with his portalmasters at his side!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Vazul", Model = TaurModel, Level = 68, Size = 75, Health = 80000, DamageFactor = 1.4, Spread = 300, Tag = "vazul", MeleeDamage = eDamageType.Crush },
                        new TrialMobSpec { Name = "taur portalmaster", Model = TaurModel, Level = 60, Size = 55, Health = 3500, DamageFactor = 0.6, Count = 2, Required = false, At = new TrialPoint(550567, 539088, 10734), Tag = "portal" },
                    },
                    OnTick = PortalTick,
                },
            },
        };

        private static TrialDefinition Chimera() => new()
        {
            ML = 6, Step = 10, Name = "Chimera's Fortification", Scope = TrialScope.Raid, Zone = TrialZone.Oceanus,
            Anchor = new TrialPoint(549139, 558294, 10672),
            StarterName = "Taur Stonecutter", StarterModel = SmallTaurModel,
            Intro = "You have the ash, the tail, the head and the fall of the fortress behind you. Only she is left. Clear her defenders from the room first. Then keep spread out: she breathes lightning, sunfire and poison in turn over everyone near her, her hide burns whoever strikes it, and a few of us will always come to her call.",
            Victory = "The Chimera falls, and the fire in the Ashen Isles sinks low.",
            Replaces = new[] { "Chimera" },
            TimeLimitSeconds = 2400,
            Phases =
            {
                new TrialPhase
                {
                    Announce = "The elite defenders turn on you. Clear the room!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "elite taur defender", Model = SmallTaurModel, Level = 60, Size = 45, Health = 5000, DamageFactor = 0.9, Count = 4, Spread = 500, MeleeDamage = eDamageType.Slash },
                        new TrialMobSpec { Name = "taur arieos", Model = SmallTaurModel, Level = 58, Size = 45, Health = 4000, DamageFactor = 0.9, Count = 2, Spread = 350, MeleeDamage = eDamageType.Slash },
                    },
                },
                new TrialPhase
                {
                    Announce = "The Chimera rises from her nest!",
                    Mobs =
                    {
                        new TrialMobSpec { Name = "Chimera", Model = 1180, Level = 70, Size = 150, Health = 130000, DamageFactor = 1.4, At = new TrialPoint(550183, 558467, 10763, 329), Tag = "chimera", MeleeDamage = eDamageType.Slash, Reflect = 0.05 },
                    },
                    OnTick = ChimeraTick,
                },
            },
        };

        /// <summary>An evenly spaced point on a circle, for monsters that need their own place.</summary>
        public static TrialPoint Ring(TrialPoint centre, int radius, int index, int count)
        {
            double angle = 2 * Math.PI * index / count;
            return new TrialPoint(centre.X + (int)Math.Round(radius * Math.Cos(angle)), centre.Y + (int)Math.Round(radius * Math.Sin(angle)), centre.Z, centre.Heading);
        }

        public static int GateWaveSeconds(int gateHealthPercent) => gateHealthPercent <= FailingGatePercent ? FailingGateWaveSeconds : OpenGateWaveSeconds;

        /// <summary>Each fallen challenger's fire passes to the rest (Prima, 6-8).</summary>
        public static int ChallengerBoltDamage(int fallen) => ChallengerBolt * (100 + 15 * Math.Clamp(fallen, 0, BrazierCount)) / 100;

        /// <summary>The steps that stand for Ankhkare's three tolls: the tail (6.4), the ash (6.5) and the head (6.6).</summary>
        public static readonly int[] OfferingSteps = { 4, 5, 6 };

        public static int OfferingsHeld(Func<int, bool> finished) => OfferingSteps.Count(finished);

        /// <summary>Live: Ankhkare was level 99, 80 after two tolls and 75 after all three.</summary>
        public static int AnkhkareHealth(int offerings) => offerings >= OfferingSteps.Length ? 90000 : 120000;

        private static IEnumerable<GamePlayer> TollBearers(GamePlayer leader) =>
            new[] { leader }.Concat(leader.Group?.GetPlayersInTheGroup() ?? Enumerable.Empty<GamePlayer>()).Distinct();

        private static string AnkhkareRefusal(GamePlayer leader)
        {
            List<GamePlayer> bearers = TollBearers(leader).ToList();
            if (OfferingsHeld(step => bearers.Any(player => player.HasFinishedMLStep(6, step))) >= OfferingsNeeded)
                return null;
            return "The guardians will not lower their fire for fewer than two tolls. Someone in your group must first finish two of steps 4, 5 and 6 of Master Level 6: the tail of Pallida-Uraei, the ash of the livid salamander, or the head of Siraadi.";
        }

        private static bool Holds(TrialRun run, int step)
        {
            HashSet<int> held = run.Get<HashSet<int>>("tolls");
            if (held == null)
            {
                List<GamePlayer> bearers = TollBearers(run.Leader).Concat(run.Players).Distinct().ToList();
                held = OfferingSteps.Where(toll => bearers.Any(player => player.HasFinishedMLStep(6, toll))).ToHashSet();
                run.Set("tolls", held);
            }
            return held.Contains(step);
        }

        private static string TollName(int step) => step switch
        {
            4 => "the tail of Pallida-Uraei",
            5 => "the salamander ash",
            _ => "the head of Siraadi",
        };

        private static TrialPhase OfferingAt(int step, TrialPoint? guardian, string announce) => new()
        {
            OnStart = run =>
            {
                if (!Holds(run, step))
                {
                    run.Tell($"No one carries {TollName(step)}. That guardian keeps its fire, and what waits behind it will join Ankhkare.");
                    return;
                }
                if (guardian == null)
                {
                    run.Tell(announce);
                    run.Set($"offered{step}", true);
                    return;
                }
                run.Tell(announce);
                run.Spawn(new TrialMobSpec
                {
                    Name = "Ankhkare's Guardian", Model = SphinxModel, Level = 60, Objective = true, Required = false, At = guardian,
                    ObjectiveText = $"You hold out {TollName(step)}. The guardian bows, and its fire falls.",
                    OnDeath = (finished, _) => finished.Set($"offered{step}", true),
                });
            },
            Done = run => !Holds(run, step) || run.Get<bool>($"offered{step}"),
        };

        private static TrialPhase Swarm(int step, string announce) => new()
        {
            OnStart = run =>
            {
                if (!Holds(run, step))
                    return;
                run.Tell(announce);
                foreach (TrialMobSpec spec in SwarmFor(step))
                    run.Spawn(spec);
                run.Set($"swarm{step}", true);
            },
            Done = run => !Holds(run, step) || run.Get<bool>($"swarm{step}") && !run.Mobs($"swarm{step}").Any(),
        };

        private static IEnumerable<TrialMobSpec> SwarmFor(int step) => step switch
        {
            4 => new[]
            {
                new TrialMobSpec { Name = "uraeus", Model = UraeusModel, Level = 60, Health = 5000, DamageFactor = 1.0, Count = 4, At = UraeusPen, Tag = "swarm4", MeleeDamage = eDamageType.Thrust },
            },
            6 => new[]
            {
                new TrialMobSpec { Name = "siam-he plebian", Model = WarriorModel, Level = 60, Health = 5000, DamageFactor = 1.0, Count = 3, At = PlebianPen, Tag = "swarm6", MeleeDamage = eDamageType.Slash },
                new TrialMobSpec { Name = "salamander", Model = SalamanderModel, Level = 60, Size = 100, Health = 6000, DamageFactor = 1.0, Count = 2, At = new TrialPoint(538646, 579172, 10587), Tag = "swarm6", MeleeDamage = eDamageType.Slash },
            },
            _ => new[]
            {
                new TrialMobSpec { Name = "salamander", Model = SalamanderModel, Level = 62, Size = 100, Health = 6000, DamageFactor = 1.0, Count = 5, At = SalamanderPen, Tag = "swarm5", MeleeDamage = eDamageType.Slash },
            },
        };

        private static void AnkhkareRises(TrialRun run)
        {
            int paid = OfferingSteps.Count(step => Holds(run, step));
            run.Spawn(new TrialMobSpec
            {
                Name = "Ankhkare", Model = SphinxModel, Level = (byte)(paid >= OfferingSteps.Length ? 69 : 70), Size = 150, Health = AnkhkareHealth(paid),
                DamageFactor = paid >= OfferingSteps.Length ? 1.3 : 1.5, At = new TrialPoint(531684, 582548, 10831, 2586), Tag = "ankhkare", MeleeDamage = eDamageType.Slash,
            });
            foreach (int unpaid in OfferingSteps.Where(step => !Holds(run, step)))
            {
                run.Tell("The fire behind an unpaid guardian dies, and its swarm joins Ankhkare!");
                foreach (TrialMobSpec spec in SwarmFor(unpaid))
                    run.Spawn(spec);
            }
        }

        private static TrialMobSpec Chanter() => new()
        {
            Name = "siam-he chanter", Model = PriestModel, Level = 60, Health = 3500, DamageFactor = 0.7, Count = ChanterCount / 2, At = new TrialPoint(485430, 562826, 9592), Tag = "chanter",
            OnDeath = (run, _) =>
            {
                int down = run.Get<int>("chantersDown") + 1;
                run.Set("chantersDown", down);
                run.Tell($"A pillar goes dark: {Math.Min(down, ChanterCount)} of {ChanterCount}.");
            },
        };

        private static TrialMobSpec Hatchling(TrialPoint point) => new()
        {
            Name = "salamander hatchling", Model = SalamanderModel, Level = 55, Size = 50, Health = 700, DamageFactor = 0.3, At = point, Required = false, Tag = "hatchling",
            MeleeDamage = eDamageType.Slash,
            OnDeath = (run, _) =>
            {
                int glands = run.Get<int>("glands") + 1;
                run.Set("glands", glands);
                if (glands <= GlandsNeeded)
                    run.Tell($"A frozen cold gland: {glands} of {GlandsNeeded}.");
            },
        };

        private static List<TrialMobSpec> OrbMobs()
        {
            List<TrialMobSpec> mobs = new()
            {
                new TrialMobSpec { Name = "Orb of Apollo", Model = OrbModel, Level = 70, Size = 80, Health = 80000, DamageFactor = 0.01, At = OrbCentre, AggroRange = 0, Reflect = 0.1, Tag = "orb", MeleeDamage = eDamageType.Heat },
            };
            for (int i = 0; i < BrazierCount; i++)
            {
                mobs.Add(new TrialMobSpec
                {
                    Name = "efreeti challenger", Model = EfreetModel, Level = 65, Health = 4000, DamageFactor = 0.5, Required = false, Hurts = TrialDamageKind.Ranged | TrialDamageKind.Spell,
                    At = Ring(OrbCentre, 1400, i, BrazierCount), AggroRange = 0, Tag = "brazier",
                    OnDeath = (run, _) =>
                    {
                        int fallen = run.Get<int>("challengersDown") + 1;
                        run.Set("challengersDown", fallen);
                        run.Tell(fallen >= BrazierCount ? "The last challenger falls. Only the orb is left." : "A challenger falls, and its fire flows into the others.");
                    },
                });
            }
            return mobs;
        }

        private static TrialMobSpec Gate(string name, TrialPoint point) => new()
        {
            Name = name, Model = GateModel, Level = 60, Size = 120, Health = 25000, DamageFactor = 0.01, At = point, AggroRange = 0, Tag = "gate",
            OnDeath = GateTrap,
        };

        private static void HajeTick(TrialRun run)
        {
            TrialMob haje = run.Mobs("haje").FirstOrDefault();
            if (haje == null || !Ml5.Every(run, "spit", 6))
                return;
            GameLiving target = Ml5.RandomHostile(run, haje, 1800);
            if (target != null)
                Ml5.Strike(run, haje, target, 170, eDamageType.Heat);
        }

        private static void LateefTick(TrialRun run)
        {
            TrialMob deserter = run.Get<TrialMob>("deserter");
            if (Ml5.Near(deserter, LateefBridge, BridgeReach))
            {
                run.Set("atBridge", true);
                return;
            }

            // His brain walks him back to where he spawned whenever he stops (sandbox 2026-11-14: he never left the starter).
            deserter.SpawnPoint = new(deserter);
            bool escorted = run.Sweeping || Ml5.Party(run).Any(member => member.IsAlive && member.IsWithinRadius(deserter, EscortRange));
            if (!escorted)
            {
                if (deserter.IsMoving)
                    deserter.StopMoving();
                if (Ml5.Every(run, "lateefWaits", 30))
                    run.Tell("Lateef has stopped and will not go on without you.");
                return;
            }

            int walked = run.Get<int>("walked") + 1;
            run.Set("walked", walked);
            if (walked >= LateefPatienceSeconds || run.Sweeping)
            {
                deserter.MoveTo(run.Starter.CurrentRegionID, LateefBridge.X, LateefBridge.Y, LateefBridge.Z, LateefBridge.Heading);
                run.Set("atBridge", true);
            }
            else if (!deserter.IsMoving)
            {
                deserter.PathTo(new Vector3(LateefBridge.X, LateefBridge.Y, LateefBridge.Z), LateefSpeed);
            }
        }

        private static void LateefFalls(TrialRun run)
        {
            run.Get<TrialMob>("deserter").Die(run.Mobs("sutekh").FirstOrDefault());
        }

        /// <summary>Live: the three leave ten minutes after they appear, but not while they are engaged.</summary>
        public static bool AmbushLeaves(int ageSeconds, bool engaged) => ageSeconds >= AmbushSeconds && !engaged;

        private static void AmbushTick(TrialRun run)
        {
            int age = run.Get<int>("ambushAge") + 1;
            run.Set("ambushAge", age);
            List<TrialMob> trio = run.Mobs("sutekh").Concat(run.Mobs("udaji")).Concat(run.Mobs("fadil")).ToList();
            if (AmbushLeaves(age, trio.Any(mob => mob.InCombat)))
            {
                run.End(false, "The servants of Am-he step back into the fire, and the dagger goes with them.");
                return;
            }

            TrialMob fadil = run.Mobs("fadil").FirstOrDefault();
            if (fadil != null)
            {
                if (Ml5.Every(run, "mend", 3))
                {
                    foreach (TrialMob ally in run.Mobs("sutekh").Concat(run.Mobs("udaji")))
                        ally.ChangeHealth(fadil, eHealthChangeType.Regenerate, ally.MaxHealth / 100);
                }
                if (Ml5.Every(run, "summon", 45) && !run.Mobs("sa").Any())
                {
                    run.Tell("Fadil calls a sa out of the fire.");
                    run.Spawn(FadilPet);
                }
            }

            TrialMob udaji = run.Mobs("udaji").FirstOrDefault();
            if (udaji?.TargetObject is GameLiving victim && victim.IsAlive && udaji.IsWithinRadius(victim, 400) && Ml5.Every(run, "burst", 8))
                Ml5.Strike(run, udaji, victim, 220, eDamageType.Heat);
        }

        /// <summary>Which of Pallida-Uraei's three splits is really her.</summary>
        public static int PallidaRealSplit(int roll) => Math.Clamp(roll, 0, 2);

        private static void PallidaTick(TrialRun run)
        {
            TrialMob pallida = run.Mobs("pallida").FirstOrDefault();
            if (pallida == null || pallida.HealthPercent > PallidaSplitPercent || run.Get<bool>("pallidaSplit"))
                return;

            run.Set("pallidaSplit", true);
            run.Tell("Pallida-Uraei tears apart into three! Only one of them is truly her.");
            TrialPoint at = new(pallida.X, pallida.Y, pallida.Z, pallida.Heading);
            pallida.Delete();
            int real = PallidaRealSplit(Util.Random(2));
            for (int i = 0; i < 3; i++)
            {
                bool isReal = i == real;
                run.Spawn(new TrialMobSpec
                {
                    Name = "Pallida-Uraei", Model = UraeusModel, Level = 64, Size = 60, Health = isReal ? 12000 : 6000, DamageFactor = 0.9, At = Ring(at, 350, i, 3),
                    Tag = isReal ? "pallida" : "pallidaShade", Required = isReal, MeleeDamage = eDamageType.Thrust,
                    OnDeath = isReal
                        ? (finished, _) =>
                        {
                            Ml5.Clear(finished, "pallidaShade");
                            finished.Set("pallidaDead", true);
                        }
                        : (finished, _) => finished.Tell("That one was only a shade of her. Find the real Pallida-Uraei!"),
                });
            }
        }

        private static void HatchlingTick(TrialRun run)
        {
            if (run.Sweeping)
                run.Set("glands", GlandsNeeded);
            if (run.Mobs("hatchling").Count() < 3 && Ml5.Every(run, "hatch", 5))
                run.Spawn(Hatchlings[Util.Random(Hatchlings.Length - 1)]);
        }

        private static void SpoutTick(TrialRun run)
        {
            int age = run.Get<int>("flameAge") + 1;
            run.Set("flameAge", age);
            if (age >= FlameSeconds)
                run.End(false, "The spouts roar back to life. You were too slow, and the adventurer has no glands left.");
        }

        private static void IncineratorTick(TrialRun run)
        {
            TrialMob livid = run.Mobs("livid").FirstOrDefault();
            if (livid == null || run.Leader.TargetObject != livid || !run.Leader.IsWithinRadius(livid, IncineratorReach))
                return;
            run.Tell("The Salamander Incinerator spends itself in one white burst.");
            livid.Immune = false;
            livid.Die(run.Leader);
        }

        private static void RootOrbMobs(TrialRun run)
        {
            foreach (TrialMob mob in run.Mobs("orb").Concat(run.Mobs("brazier")))
                mob.MaxSpeedBase = 0;
        }

        private static void OrbTick(TrialRun run)
        {
            int age = run.Get<int>("orbAge") + 1;
            run.Set("orbAge", age);
            if (age >= OrbSeconds)
            {
                run.End(false, "Time is up. The orb burns on, the braziers gutter out and the challengers wait for the next fool.");
                return;
            }

            if (!Ml5.Every(run, "bolts", 4))
                return;
            List<GameLiving> onPlatform = run.Hostiles(run.Starter, PlatformRadius).ToList();
            if (onPlatform.Count == 0)
                return;
            int damage = ChallengerBoltDamage(run.Get<int>("challengersDown"));
            foreach (TrialMob brazier in run.Mobs("brazier").ToList())
                Ml5.Strike(run, brazier, onPlatform[Util.Random(onPlatform.Count - 1)], damage, eDamageType.Heat);
        }

        private static void RootGate(TrialRun run)
        {
            run.Set("gateTrapWarned", false);
            foreach (TrialMob gate in run.Mobs("gate"))
                gate.MaxSpeedBase = 0;
        }

        private static void GateTick(TrialRun run)
        {
            TrialMob gate = run.Mobs("gate").FirstOrDefault();
            if (gate == null)
                return;

            if (gate.HealthPercent <= 10 && !run.Get<bool>("gateTrapWarned"))
            {
                run.Set("gateTrapWarned", true);
                run.Tell("The gate groans and begins to glow. Get back from it!");
            }

            if (!Ml5.Every(run, "gateWave", GateWaveSeconds(gate.HealthPercent)) || run.Mobs("adds").Count() >= GateAddLimit)
                return;

            int wave = run.Get<int>("wave") + 1;
            run.Set("wave", wave);
            run.Spawn(wave % 3 == 0 ? Lancer : Battler);
        }

        /// <summary>What a fallen gate sets off. Live had a slightly higher chance of the dragon breath than of the rest.</summary>
        public enum GateTrapKind
        {
            DragonBreath,
            Battler,
            Horde,
        }

        public static GateTrapKind GateTrapFor(int roll) => roll switch
        {
            <= 1 => GateTrapKind.DragonBreath,
            2 => GateTrapKind.Battler,
            _ => GateTrapKind.Horde,
        };

        private static void GateTrap(TrialRun run, TrialMob gate)
        {
            switch (GateTrapFor(Util.Random(3)))
            {
                case GateTrapKind.DragonBreath:
                    run.Tell("Dragon fire roars out of the fallen gate!");
                    foreach (GameLiving target in run.Hostiles(run.Starter, 4000).Where(target => Ml5.Near(target, new TrialPoint(gate.X, gate.Y, gate.Z), GateBreathRadius)).ToList())
                        Ml5.Strike(run, run.Starter, target, 300, eDamageType.Heat);
                    break;
                case GateTrapKind.Battler:
                    run.Tell("The Battler himself shoulders out through the gap, stronger than you have seen him!");
                    run.Spawn(NamedBattler);
                    break;
                default:
                    run.Tell("A horde of taur pours out through the gap!");
                    run.Spawn(Lancer);
                    run.Spawn(Battler);
                    run.Spawn(Battler);
                    break;
            }
        }

        private static void PortalTick(TrialRun run)
        {
            if (run.Mobs("portal").Any() && Ml5.Every(run, "warp", 30) && run.Mobs("adds").Count() < GateAddLimit)
            {
                run.Tell("A portalmaster warps in another taur.");
                run.Spawn(Battler);
            }
        }

        private static readonly (eDamageType Type, string Message)[] ChimeraBreaths =
        {
            (eDamageType.Energy, "The Chimera breathes a fork of lightning across the room!"),
            (eDamageType.Heat, "The Chimera breathes a blast of searing sunlight!"),
            (eDamageType.Body, "The Chimera breathes a cloud of poison!"),
        };

        public static int ChimeraBreathIndex(int breaths) => breaths % ChimeraBreaths.Length;

        private static void ChimeraTick(TrialRun run)
        {
            TrialMob chimera = run.Mobs("chimera").FirstOrDefault();
            if (chimera != null && Ml5.Every(run, "breath", 14))
            {
                int breaths = run.Get<int>("breaths");
                run.Set("breaths", breaths + 1);
                (eDamageType type, string message) = ChimeraBreaths[ChimeraBreathIndex(breaths)];
                run.Tell(message);
                foreach (GameLiving target in run.Hostiles(chimera, 700))
                    Ml5.Strike(run, chimera, target, 200, type);
            }

            if (Ml5.Every(run, "defender", 60) && run.Mobs("defender").Count() < 4)
            {
                run.Tell("Defenders answer the Chimera's call.");
                run.Spawn(ChimeraGuard);
                run.Spawn(ChimeraGuard);
            }
        }
    }
}
