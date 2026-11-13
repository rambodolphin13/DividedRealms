using System;
using System.Collections.Generic;
using DOL.Events;
using DOL.GS.Effects;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Heretic Reanimate Corpse (Class Library, 1.127): raises a dead friend at the spell's health/power, clears
	/// resurrection sickness, and the risen body becomes a monster whose pulse (the SubSpellID MonsterDoT spell) hits
	/// enemies around it on landing and again every Frequency for Value seconds. Damage is the Heretic's. The old
	/// code cast monster spell 14078, which is not in the world, so the monster never appeared (DR-333).
	/// </summary>
	[SpellHandler(eSpellType.ReanimateCorpse)]
	public class MonsterRez : ResurrectSpellHandler
	{
		private const string PROPERTY = "HERETIC_MONSTER_PULSE";

		public MonsterRez(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line)
		{
		}

		protected override bool ClearsResurrectionSickness => true;

		protected override void ResurrectLiving(GameLiving living)
		{
			base.ResurrectLiving(living);

			Spell pulse = SkillBase.GetSpellByID(Spell.SubSpellID);
			if (pulse == null || pulse.SpellType != eSpellType.MonsterDoT || living == null || !living.IsAlive)
				return;

			int every = pulse.Frequency > 0 ? pulse.Frequency : 6000;
			// The pulse is a direct-damage spell, so its Duration must stay 0 (a Duration would make it a timed effect with no
			// damage); Value holds how many seconds the corpse keeps pulsing.
			long ends = GameLoop.GameLoopTime + Math.Max((long) (pulse.Value * 1000), every);
			ushort region = living.CurrentRegionID;
			GameLiving heretic = m_caster;

			living.TempProperties.GetProperty<ECSGameTimer>(PROPERTY)?.Stop();
			ECSGameTimer timer = new(living, _ =>
			{
				if (!living.IsAlive || living.ObjectState != GameObject.eObjectState.Active || living.CurrentRegionID != region ||
					heretic == null || !heretic.IsAlive || GameLoop.GameLoopTime > ends)
				{
					living.TempProperties.RemoveProperty(PROPERTY);
					return 0;
				}

				ScriptMgr.CreateSpellHandler(heretic, pulse, SpellLine)?.StartSpell(living);
				return every;
			});
			living.TempProperties.SetProperty(PROPERTY, timer);
			timer.Start(1);
		}
	}

	/// <summary>
	/// Summary description for ReanimateCorpe.
	/// </summary>
	[SpellHandler(eSpellType.SummonMonster)]
	public class SummonMonster : SpellHandler
	{
		private ushort m_model = 0;
		private SpellLine m_monsterspellline = null;
		private GamePlayer m_owner = null;

		public SpellLine MonsterSpellLine
		{
			get
			{
				if (m_monsterspellline == null)
					m_monsterspellline = SkillBase.GetSpellLine("Summon Monster");

				return m_monsterspellline;
			}
		}

		public Spell MonsterSpellDoT
		{
			get
			{
                return (SkillBase.GetSpellByID (14077));
			}
		}

		public Spell MonsterSpellDisease
		{
			get
			{
                return (SkillBase.GetSpellByID (14079));
			}
		}

		protected override GameSpellEffect CreateSpellEffect(GameLiving target, double effectiveness)
		{
			return new GameSpellEffect(this, Spell.Duration, Spell.Frequency, effectiveness);
		}

		public override void OnEffectStart(GameSpellEffect effect)
		{
			if (!(effect.Owner is GamePlayer))
				return;

			GamePlayer player = effect.Owner as GamePlayer;
			m_owner = player;
			m_model = player.Model;
			player.Model = (ushort)Spell.Value;

            player.BaseBuffBonusCategory[eProperty.MagicAbsorption] += (int)Spell.LifeDrainReturn;
            player.BaseBuffBonusCategory[eProperty.PhysicalAbsorption] += (int)Spell.LifeDrainReturn;
			player.Out.SendCharStatsUpdate();
			player.Health = player.MaxHealth;
			GameEventMgr.AddHandler(player, GameLivingEvent.Dying, new DOLEventHandler(EventRaised));
			GameEventMgr.AddHandler(player, GamePlayerEvent.Linkdeath, new DOLEventHandler(EventRaised));
			GameEventMgr.AddHandler(player, GamePlayerEvent.Quit, new DOLEventHandler(EventRaised));
			GameEventMgr.AddHandler(player, GamePlayerEvent.RegionChanged, new DOLEventHandler(EventRaised));

			base.OnEffectStart(effect);
		}

		public override void OnEffectPulse(GameSpellEffect effect)
		{

			if (effect.Owner is GamePlayer)
			{
				GamePlayer player = effect.Owner as GamePlayer;
				player.CastSpell(this.MonsterSpellDoT, this.MonsterSpellLine);
				player.CastSpell(this.MonsterSpellDisease, this.MonsterSpellLine);
			}

			base.OnEffectPulse(effect);
		}

		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			if (!(effect.Owner is GamePlayer))
				return 0;

			GamePlayer player = effect.Owner as GamePlayer;

			player.Model = m_model;

            player.BaseBuffBonusCategory[eProperty.MagicAbsorption] -= (int)Spell.LifeDrainReturn ;
            player.BaseBuffBonusCategory[eProperty.PhysicalAbsorption] -= (int)Spell.LifeDrainReturn ;
			player.Out.SendCharStatsUpdate();

			int leftHealth = Convert.ToInt32(player.MaxHealth * 0.10);
			player.Health = leftHealth;

			GameEventMgr.RemoveHandler(player, GameLivingEvent.Dying, new DOLEventHandler(EventRaised));
			GameEventMgr.RemoveHandler(player, GamePlayerEvent.Linkdeath, new DOLEventHandler(EventRaised));
			GameEventMgr.RemoveHandler(player, GamePlayerEvent.Quit, new DOLEventHandler(EventRaised));
			GameEventMgr.RemoveHandler(player, GamePlayerEvent.RegionChanged, new DOLEventHandler(EventRaised));

			return base.OnEffectExpires(effect, noMessages);
		}

		public void EventRaised(DOLEvent e, object sender, EventArgs arguments)
		{
			GamePlayer player = sender as GamePlayer; //attacker
			if (player == null) return;
			player.Model = m_model;
			GameSpellEffect effect = SpellHandler.FindEffectOnTarget(player, "SummonMonster");
			if (effect != null)
				effect.Cancel(false);
		}

		// Constructor
		public SummonMonster(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

	}

	/// <summary>
	/// Summary description for MonsterDoT.
	/// </summary>
	[SpellHandler(eSpellType.MonsterDoT)]
	public class MonsterDoT : DirectDamageSpellHandler
	{
		public override List<GameLiving> SelectTargets(GameObject castTarget)
		{
			var list = GameLoop.GetListForTick<GameLiving>();
			GameLiving target = castTarget as GameLiving;

			//if (target == null || Spell.Range == 0)
			//	target = Caster;

			foreach (GamePlayer player in target.GetPlayersInRadius((ushort)Spell.Radius))
			{
				if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true))
				{
					list.Add(player);
				}
			}
			foreach (GameNPC npc in target.GetNPCsInRadius((ushort)Spell.Radius))
			{
				if (GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true))
				{
					list.Add(npc);
				}
			}

			return list;
		}


		// Constructor
		public MonsterDoT(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}

	/// <summary>
	/// Summary description for MonsterDoT.
	/// </summary>
	[SpellHandler(eSpellType.MonsterDisease)]
	public class MonsterDisease : DiseaseSpellHandler
	{
		public override List<GameLiving> SelectTargets(GameObject castTarget)
		{
			var list = GameLoop.GetListForTick<GameLiving>();
			GameLiving target = castTarget as GameLiving;

			if (target == null || Spell.Range == 0)
				target = Caster;

			foreach (GamePlayer player in target.GetPlayersInRadius((ushort)Spell.Radius))
			{
				if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true))
				{
					list.Add(player);
				}
			}
			foreach (GameNPC npc in target.GetNPCsInRadius((ushort)Spell.Radius))
			{
				if (GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true))
				{
					list.Add(npc);
				}
			}

			return list;
		}

		// Constructor
		public MonsterDisease(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}
}
