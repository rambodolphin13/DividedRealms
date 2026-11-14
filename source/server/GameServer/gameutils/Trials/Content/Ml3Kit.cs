using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Spells;

namespace DOL.GS.Trials
{
    /// <summary>Crowd control and damage over time the Temple of Twilight's monsters lay on players, built from the stock spell handlers.</summary>
    public enum Ml3Effect
    {
        Stun,
        Mez,
        Root,
        Poison,
    }

    /// <summary>The three kinds of player Medusa's wall statues hunt (Prima, encounter 3-10).</summary>
    public enum Ml3Role
    {
        Melee,
        Caster,
        Healer,
    }

    /// <summary>
    /// Casta's jewellery (Prima, encounter 3-3, and patch 1.89). The necklace turns arrows until crushing or slashing blows chip it
    /// twice. The bracelets harden her against the last kind of damage that struck her for seven seconds.
    /// </summary>
    public sealed class CastaJewels
    {
        public const int ChipsToShatter = 2;
        public const int BraceletSeconds = 7;
        public const double BraceletResist = 0.75;

        private readonly double _chipChance;
        private int _bracelet;

        public CastaJewels(double chipChance) => _chipChance = chipChance;

        public int Chips { get; private set; }
        public bool NecklaceWhole => Chips < ChipsToShatter;
        public eDamageType? Hardened => _bracelet > 0 ? BraceletType : null;
        public eDamageType BraceletType { get; private set; }

        /// <summary>Damage to give back for one blow, and what broke or changed. The roll is 0 to 1.</summary>
        public int Blow(eDamageType type, bool ranged, int damage, double roll, out bool chipped, out bool hardened)
        {
            chipped = hardened = false;
            if (ranged && NecklaceWhole)
                return damage;

            if (!ranged && NecklaceWhole && type is eDamageType.Crush or eDamageType.Slash && roll < _chipChance)
            {
                Chips++;
                chipped = true;
            }

            if (_bracelet > 0)
                return type == BraceletType ? (int)(damage * BraceletResist) : 0;

            BraceletType = type;
            _bracelet = BraceletSeconds;
            hardened = true;
            return 0;
        }

        public void Second()
        {
            if (_bracelet > 0)
                _bracelet--;
        }
    }

    /// <summary>Shraogh's hide (Prima, encounter 3-7; ToA guide): steel barely marks him until an eel stomach's bile eats into him.</summary>
    public static class ShraoghHide
    {
        /// <summary>Share of a blow his closed hide shrugs off: firsthand reports hit him for 1 or 2.</summary>
        public const double ClosedResist = 0.95;
        public const int OpenSeconds = 30;
        public const double WeakenedBy = 0.75;
        public const double WeakestDamage = 0.5;

        /// <summary>A stomach "instantly drains half Shraogh's health": half of what he has left, so a stomach alone never kills.</summary>
        public static int AfterStomach(int health) => Math.Max(1, health - health / 2);

        public static double Weaken(double damageFactor, double calmFactor) => Math.Max(calmFactor * WeakestDamage, damageFactor * WeakenedBy);
    }

    public static class Ml3Kit
    {
        private static readonly ConcurrentDictionary<(Ml3Effect, int, int, eDamageType), Spell> Spells = new();

        public static Ml3Role RoleOf(ICharacterClass characterClass) =>
            characterClass == null ? Ml3Role.Melee : RoleOf((eCharacterClass)characterClass.ID, characterClass.ClassType);

        public static Ml3Role RoleOf(eCharacterClass id, eClassType type)
        {
            if (BotPartyRoles.IsHealingClass(id))
                return Ml3Role.Healer;
            return type == eClassType.ListCaster ? Ml3Role.Caster : Ml3Role.Melee;
        }

        public static Ml3Role RoleOf(GameLiving living) => living switch
        {
            GamePlayer player => RoleOf(player.CharacterClass),
            GameBot bot => RoleOf(bot.CharacterClass),
            _ => Ml3Role.Melee,
        };

        /// <summary>The kind most of the party plays; ties go melee, then caster. Medusa wakes the statues that hunt it.</summary>
        public static Ml3Role CommonestRole(IEnumerable<Ml3Role> roles)
        {
            Dictionary<Ml3Role, int> counts = roles.GroupBy(role => role).ToDictionary(group => group.Key, group => group.Count());
            return Enum.GetValues<Ml3Role>().OrderByDescending(role => counts.GetValueOrDefault(role)).First();
        }

        public static string StatueName(Ml3Role role) => role switch
        {
            Ml3Role.Caster => "superior magebane statue",
            Ml3Role.Healer => "superior lifebane statue",
            _ => "superior shieldbreaker statue",
        };

        /// <summary>Lays a stun, mez, root or poison on the target through the stock handlers, so resists and immunity work as for any monster spell.</summary>
        public static void Lay(TrialRun run, GameLiving caster, GameLiving target, Ml3Effect effect, int seconds, int damage = 0, eDamageType type = eDamageType.Body)
        {
            if (caster is not { IsAlive: true } || target is not { IsAlive: true })
                return;
            // A spell that fails must not end the run: the run's tick fails the whole trial on any exception.
            try
            {
                int scaled = (int)(damage * run.DamageScale);
                Spell spell = Spells.GetOrAdd((effect, seconds, scaled, type), key => Build(key.Item1, key.Item2, key.Item3, key.Item4));
                if (SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells) is { } line)
                    ScriptMgr.CreateSpellHandler(caster, spell, line)?.StartSpell(target);
            }
            catch (Exception e)
            {
                TrialManager.Log.Warn($"Trial {run.Definition.Key}: {effect} from {caster.Name} on {target.Name} failed", e);
            }
        }

        private static Spell Build(Ml3Effect effect, int seconds, int damage, eDamageType type)
        {
            DbSpell spell = new()
            {
                AllowAdd = false,
                Target = eSpellTarget.ENEMY.ToString(),
                Range = 3000,
                Duration = seconds,
                Uninterruptible = true,
                DamageType = (int)type,
            };
            switch (effect)
            {
                case Ml3Effect.Stun:
                    spell.Name = "Petrifying Gaze";
                    spell.Type = eSpellType.Stun.ToString();
                    spell.ClientEffect = spell.Icon = 1162;
                    break;
                case Ml3Effect.Mez:
                    spell.Name = "Runic Glare";
                    spell.Type = eSpellType.Mesmerize.ToString();
                    spell.ClientEffect = spell.Icon = 1172;
                    break;
                case Ml3Effect.Root:
                    spell.Name = "Grasping Stone";
                    spell.Type = eSpellType.SpeedDecrease.ToString();
                    spell.Value = 99;
                    spell.ClientEffect = 11120;
                    spell.Icon = 11149;
                    break;
                default:
                    spell.Name = "Gorgon Venom";
                    spell.Type = eSpellType.DamageOverTime.ToString();
                    spell.Damage = damage;
                    spell.Frequency = 40;
                    spell.ClientEffect = spell.Icon = 4427;
                    break;
            }
            return new Spell(spell, 1);
        }

        /// <summary>Who struck a watched monster, queued like <see cref="Ml34Kit.WatchHits"/> until the run's tick.</summary>
        public static void WatchAttackers(TrialRun run, TrialMob mob, string key)
        {
            ConcurrentQueue<GameLiving> attackers = run.Get<ConcurrentQueue<GameLiving>>(key);
            if (attackers == null)
            {
                attackers = new();
                run.Set(key, attackers);
            }
            GameEventMgr.AddHandler(mob, GameLivingEvent.AttackedByEnemy, (e, sender, arguments) =>
            {
                if (arguments is AttackedByEnemyEventArgs { AttackData.Attacker: GameLiving attacker })
                    attackers.Enqueue(attacker);
            });
        }

        public static List<GameLiving> TakeAttackers(TrialRun run, string key)
        {
            List<GameLiving> taken = new();
            ConcurrentQueue<GameLiving> attackers = run.Get<ConcurrentQueue<GameLiving>>(key);
            while (attackers != null && attackers.TryDequeue(out GameLiving attacker))
                taken.Add(attacker);
            return taken;
        }

        /// <summary>A party member at random from those given, or null.</summary>
        public static GameLiving AnyOf(IEnumerable<GameLiving> candidates) =>
            candidates.OrderBy(_ => Random.Shared.Next()).FirstOrDefault();

        /// <summary>Points the monster at a victim and keeps it there: aggro high enough that healing elsewhere won't pull it away.</summary>
        public static void Fixate(TrialMob mob, GameLiving victim)
        {
            if (mob == null || victim is not { IsAlive: true })
                return;
            if (mob.Brain is StandardMobBrain brain)
                brain.AddToAggroList(victim, 100000);
            mob.StartAttack(victim);
        }

        public static TrialPoint Toward(TrialPoint from, TrialPoint to, int distance)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= distance || length < 1)
                return to;
            return new TrialPoint(from.X + (int)(dx / length * distance), from.Y + (int)(dy / length * distance), to.Z, from.Heading);
        }
    }
}
