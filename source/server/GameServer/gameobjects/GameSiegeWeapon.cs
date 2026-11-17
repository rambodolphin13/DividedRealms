using System;
using System.Linq;
using System.Collections;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.Effects;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	#region GameSiegeweapon
	/// <summary>
	/// Description resumee de GameSiegeWeapon.
	/// </summary>
	public class GameSiegeWeapon : GameMovingObject
	{
		public bool EnableToMove { get; set; }

		public GameSiegeWeapon() : base(new BlankBrain())
		{
			this.Realm = 0;
			Level = 1;
			CurrentState = eState.Inactive;
			m_ammo = new ArrayList();

			m_ammoSlot = 0x14;
			ActionDelay	= new int[]
				{
					0,//none
					10000,//aiming
					15000,//arming
					5000,//loading
					0//fireing
				};//en ms
			EnableToMove = true;
			MaxSpeedBase = 50;
			MinAttackRange = -1;
			MaxAttackRange = -1;
			BaseDamage = 0;
		}
		public int SIEGE_WEAPON_CONTROLE_DISTANCE = 256;
		public const int TIME_TO_DECAY = 60 * 1000 * 3; //3 min
		public const int DECAYPERIOD = 240000; //ms
		#region enum
		public enum eState : byte
		{
			Inactive = 0x0,
			Armed = 0x01,
			Aimed = 0x02,
			Ready = 0x03,//armed+aimed
		}

		public enum eCommand : byte
		{
			None = 0x00,
			PutAmmo = 0x01,
			Aim = 0x03,
			Fire = 0x04,
			Move = 0x05,
			Repair = 0x06,
			Salvage = 0x07,
			Release = 0x08,
		}
		#endregion
		#region properties
		private GameKeepHookPoint m_hookPoint;
		public GameKeepHookPoint HookPoint
		{
			get { return m_hookPoint; }
			set { m_hookPoint = value; }
		}

		public override Region CurrentRegion
		{
			get
			{
				return base.CurrentRegion;
			}
			set
			{
				base.CurrentRegion = value;
				SiegeWeaponTimer = new SiegeTimer(this);
			}
		}

		private byte m_ammoType;
		public byte AmmoType
		{
			get { return m_ammoType; }
			set { m_ammoType = value; }
		}

		private GameLiving m_owner;
		/// <summary>
		/// The living entity operating this weapon.  Native player control remains
		/// supported, while NPC-backed <see cref="IGamePlayer"/> implementations can
		/// operate server-authoritative rams without a client control packet.
		/// </summary>
		public GameLiving Owner
		{
			get { return m_owner; }
			set
			{
				lock (AutonomousSiegeOwnership.ChangeGate)
				{
					GameLiving previous = m_owner;
					m_owner = value;
                    AutonomousSiegeOwnership.WeaponChanged(this, previous, value);
                    if (this is GameSiegeRam ram) AutonomousSiegeOwnership.Changed(ram, previous, value);
				}
			}
		}

		/// <summary>
		/// A siege UI only exists for connected players. Bot operators deliberately
		/// receive no packet traffic; nearby real players still receive broadcasts.
		/// </summary>
		protected IPacketLib OwnerOut => Owner is GamePlayer player ? player.Out : null;
		private eState m_currentState;
		public eState CurrentState
		{
			get { return m_currentState; }
			set { m_currentState = value; }
		}

		public virtual ushort Effect
		{
			get { return m_effect; }
			set { m_effect = value; }
		}
		protected ArrayList m_ammo;
		public virtual ArrayList Ammo
		{
			get { return m_ammo; }
			set { m_ammo = value; }
		}
		protected SiegeTimer m_siegeTimer;
		public virtual SiegeTimer SiegeWeaponTimer
		{
			get { return m_siegeTimer; }
			set { m_siegeTimer = value; }
		}
		public override int MaxHealth
		{
			get
			{
				return SiegeAmmo.Health(IsFortified);
			}
		}

		/// <summary>A crafted "fortified" engine: +50% health and +25% damage (crafting pass 2026-11-17).</summary>
		public bool IsFortified { get; set; }

		/// <summary>The crafted ammunition this engine takes (none: rams and cauldrons).</summary>
		public virtual eSiegeAmmoKind AmmoKind => eSiegeAmmoKind.None;

		private int m_selectedAmmo = -1;
		protected SiegeShot m_pendingShot;

		/// <summary>The shot being fired now: the loaded ammunition, or the engine's plain shot.</summary>
		public SiegeShot PendingShot => m_pendingShot.AmmoName == null && m_pendingShot.Damage == 0 ? SiegeShot.Plain(MeleeDamageType) : m_pendingShot;

		/// <summary>
		/// Lists the controller's backpack ammunition this engine takes; the siege window shows it and its Load button
		/// picks one (crafting pass 2026-11-17).
		/// </summary>
		public void RefreshAmmo()
		{
			DbInventoryItem selected = m_selectedAmmo >= 0 && m_selectedAmmo < m_ammo.Count ? m_ammo[m_selectedAmmo] as DbInventoryItem : null;
			m_ammo.Clear();

			if (Owner is GamePlayer player && AmmoKind != eSiegeAmmoKind.None)
			{
				foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
				{
					if (item != null && item.Count > 0 && SiegeAmmo.KindOf(item.Template) == AmmoKind)
						m_ammo.Add(item);
				}
			}

			m_selectedAmmo = selected != null ? m_ammo.IndexOf(selected) : -1;
			AmmoSlot = m_selectedAmmo >= 0 ? (ushort)m_selectedAmmo : (ushort)0x14;
		}

		/// <summary>Uses one piece of the loaded ammunition and returns its shot, or the plain shot.</summary>
		protected SiegeShot TakeShot()
		{
			RefreshAmmo();

			if (Owner is GamePlayer player && m_selectedAmmo >= 0 && m_ammo[m_selectedAmmo] is DbInventoryItem item)
			{
				SiegeShot shot = SiegeAmmo.ShotOf(item.Template, MeleeDamageType);
				player.Inventory.RemoveCountFromStack(item, 1);
				RefreshAmmo();
				return shot;
			}

			return SiegeShot.Plain(MeleeDamageType);
		}

		/// <summary>Damage to one target from the pending shot (ammo, fortified bonus and the target's resist).</summary>
		public int ShotDamage(GameLiving target, int baseAmount)
		{
			SiegeShot shot = PendingShot;
			int resist = shot.IsAmmo && target is not GameDoorBase ? target.GetResist(shot.DamageType) : 0;
			return SiegeAmmo.Damage(baseAmount, shot, IsFortified, resist);
		}

		public override int Mana
		{
			get { return 50000; }
		}

		private int m_timesrepaired;
		public int TimesRepaired
		{
			get { return m_timesrepaired; }
			set { m_timesrepaired = value; }
		}

		protected ECSGameTimer m_decayTimer;
		/// <summary>
		/// The lock object for lazy regen timers initialization
		/// </summary>
		protected readonly Lock _decayTimerLock = new();

		protected ECSGameTimer m_controlRangeTimer;
		/// <summary>
		/// The lock object for controlcheck timers initialization
		/// </summary>
		protected readonly Lock _controlRangeTimerLock = new();

		private ushort m_ammoSlot;
		public ushort AmmoSlot
		{
			get { return m_ammoSlot; }
			set { m_ammoSlot = value; }
		}

		//The Decayed HP ammount to put this Siege Weapon into "this siegeweapon needs to be repaired" and unusable;
		public int DecayedHp
		{
			get { return 3 * (this.MaxHealth / 10); }
		}

		//Amount of HP to deduct from Siege Weapon on each Decay Tick
		public int DecayDeductHp
		{
			get { return this.MaxHealth / 10; }
		}

		private string m_itemId;
		public string ItemId
		{
			get { return m_itemId; }
			set { m_itemId = value; }
		} 

		/// <summary>
		/// Base damage for this siege, used to calculate damage against specific targets
		/// <summary>
		private int m_baseDamage;
	
		public virtual int BaseDamage
		{
			get { return m_baseDamage; }
			set { m_baseDamage = value; }
		}

		/// <summary>
		/// The minimum range a target/groundtarget must be above for this siege to fire
		/// </summary>
		public int MinAttackRange;

		/// <summary>
		/// The maximum range a target/groundtarget must be below for this siege to fire
		/// </summary>
		public int MaxAttackRange;
		

		#endregion
		#region public methode
		public void TakeControl(GamePlayer player)
		{
			TryTakeControl(player);
		}

		/// <summary>
		/// Starts normal server-side control for a real player or a player-like NPC.
		/// Autonomous bots use server-side visibility checks for artillery;
		/// ordinary players retain their native controls and client visibility checks.
		/// </summary>
		public bool TryTakeControl(GameLiving player)
		{
			lock (AutonomousSiegeOwnership.ChangeGate) return TryTakeControlCore(player);
		}
		private bool TryTakeControlCore(GameLiving player)
		{
			if (player is not GamePlayer && (player is not GameBot { IsAutonomousWorldBot: true } && (this is not GameSiegeRam || player is not IGamePlayer)) &&
				!(player is GameBot raidBot && CompanionRaid.IsMember(raidBot)))
				return false;
			if (player is GameBot bot && (!bot.IsAlive || !IsAlive || ObjectState != eObjectState.Active ||
				Realm != bot.Realm || !IsWithinRadius(bot, SIEGE_WEAPON_CONTROLE_DISTANCE) ||
				AutonomousSiegeOwnership.All(bot).Any(w => w != this))) return false;

			if (Owner != null && Owner != player)
			{
				if (player is GamePlayer existingController)
					existingController.Out.SendMessage(GetName(0, true) + " is already under control.", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return false;
			}
			if (player is GamePlayer controllingPlayer && controllingPlayer.SiegeWeapon != null && controllingPlayer.SiegeWeapon != this)
			{
				controllingPlayer.Out.SendMessage("You already have a siege weapon under your control.", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return false;
			}
			if (IsMoving)
			{
				if (player is GamePlayer movingController)
					movingController.Out.SendMessage("You can't take control of a siege weapon while it is moving.", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (Realm == 0) Realm = player.Realm;
			Owner = player;
			if (player is GamePlayer assignedController)
			{
				assignedController.SiegeWeapon = this;
				RefreshAmmo();
				assignedController.Out.SendSiegeWeaponInterface(this, SiegeWeaponTimer.TimeUntilElapsed / 100);
				assignedController.Out.SendMessage("You take control of " + GetName(0, false) + ".", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
			}
			StartControlRangeCheck();
			if ((CurrentState & GameSiegeWeapon.eState.Armed) != GameSiegeWeapon.eState.Armed)
				Arm();
			return true;
		}
		public virtual void ReleaseControl()
		{
            lock (AutonomousSiegeOwnership.ChangeGate) ReleaseControlCore();
        }

        private void ReleaseControlCore()
		{
			if (Owner == null) return;
			if (Owner is GameBot) { SiegeWeaponTimer?.Stop(); _botShotGeneration++; StopMovingOnPath(); }
			OwnerOut?.SendMessage("You are no longer controlling " + GetName(0, false) + ".", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
			OwnerOut?.SendSiegeWeaponCloseInterface();
			if (Owner is GamePlayer player)
				player.SiegeWeapon = null;
			Owner = null;
			StopControlRangeCheck();
			StopMove();
		}

		public override void Die(GameObject killer)
		{
			StopDecay();
			ReleaseControl();
			Delete();
		}

		public virtual void Aim()
		{
			if (!CanUse()) return;
			if(SiegeWeaponTimer.IsAlive || this.IsMoving)
			{
				OwnerOut?.SendMessage(GetName(0, true) +" isn't ready to be aimed yet!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}
			if (Owner.TargetObject == null)
			{
				OwnerOut?.SendMessage("You must have a target!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			} 
			if (!GameServer.ServerRules.IsAllowedToAttack(Owner, ((GameLiving)Owner.TargetObject), true))
			{
				OwnerOut?.SendMessage("You cannot attack that!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			} 

			if (Owner is GameBot ? !BotSiegeRuntime.CanDamage(this, Owner.TargetObject as GameLiving) : !Owner.TargetInView)
			{
				OwnerOut?.SendMessage("Target is not in view!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			} 

			//Range Checks
			if (MinAttackRange != -1 && this.GetDistanceTo(Owner.TargetObject) < MinAttackRange)
			{
				OwnerOut?.SendMessage("The " + GetName(0, false) + "'s target location is too close!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}
			if (MaxAttackRange != -1 && this.GetDistanceTo(Owner.TargetObject) > MaxAttackRange)
			{
				OwnerOut?.SendMessage("The " + GetName(0, false) + "'s target is too far away to reach!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}

			CurrentState &= ~eState.Aimed;
			SetGroundTarget(Owner.TargetObject.X, Owner.TargetObject.Y, Owner.TargetObject.Z);
			TargetObject = Owner.TargetObject;
			SiegeWeaponTimer.CurrentAction = SiegeTimer.eAction.Aiming;
            Heading = GetHeading( GroundTarget );
			PreAction();
			if (Owner != null)
			{
				OwnerOut?.SendMessage(GetName(0, true) + " is turning to your target. (" + (GetActionDelay(SiegeTimer.eAction.Aiming) / 1000).ToString("N") + "s)", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
		}

		public void Arm()
		{
			if (!CanUse()) return;
			CurrentState &= ~eState.Armed;
			SiegeWeaponTimer.CurrentAction = SiegeTimer.eAction.Arming;
			PreAction();
			if (Owner != null)
			{//You prepare the cauldron of boiling oil for firing. (15.0s until armed)
				OwnerOut?.SendMessage("You prepare " + GetName(0, false) + " for firing. (" + (GetActionDelay(SiegeTimer.eAction.Arming) / 1000).ToString("N") + "s until armed)", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}

		}
		public void Move()
		{
			if (!CanUse()) return;
			if (!EnableToMove) return;
			if (Owner == null || !Owner.GroundTarget.IsValid) return;
            if ( !this.IsWithinRadius( Owner.GroundTarget, 1000 ) )
			{
				OwnerOut?.SendMessage("Ground target is too far away to move to!", eChatType.CT_System,
									  eChatLoc.CL_SystemWindow);
				return;
			}
            
            if (!Owner.GroundTargetInView)
			{
				OwnerOut?.SendMessage("Ground target is out of sight!", eChatType.CT_System,
									  eChatLoc.CL_SystemWindow);
				return;
			}
   
			if (Owner.GroundTarget.Z > this.Z + 100)
			{
				OwnerOut?.SendMessage("Ground target too high!", eChatType.CT_System,
					eChatLoc.CL_SystemWindow);
				return;
			}
   
			//let's check if we are trying to move too close to a door, if we are, don't move
			foreach (GameDoorBase door in Owner.CurrentRegion.GetDoorsInRadius(Owner.GroundTarget, (ushort)(attackComponent.AttackRange - 50)))
			{
				if (door is GameKeepDoor)
				{
					OwnerOut?.SendMessage("You can't move a ram that close to a door!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return;
				}
			}
   
			//unarmed & unaim siege weapon
			CurrentState &= ~eState.Armed;
			TargetObject = null;
			CurrentState &= ~eState.Aimed;
			WalkTo(Owner.GroundTarget, 100);
		}

		public void StopMove()
		{
			StopMoving();
		}

		public void Load(int ammo)
		{
			RefreshAmmo();

			if (ammo >= 0 && ammo < m_ammo.Count && m_ammo[ammo] is DbInventoryItem item)
			{
				m_selectedAmmo = ammo;
				AmmoSlot = (ushort)ammo;
				OwnerOut?.SendMessage("You load " + item.Name + " into " + GetName(0, false) + ".", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
			else
			{
				m_selectedAmmo = -1;
				AmmoSlot = 0x14;
			}

			if (Owner is GamePlayer player)
				player.Out.SendSiegeWeaponInterface(this, SiegeWeaponTimer.TimeUntilElapsed / 100);
		}

		/// <summary>Dropping ammunition on the engine loads it (it stays in the backpack until fired).</summary>
		public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
		{
			if (source == Owner && item != null && AmmoKind != eSiegeAmmoKind.None && SiegeAmmo.KindOf(item.Template) == AmmoKind)
			{
				RefreshAmmo();
				Load(m_ammo.IndexOf(item));
				return true; // handled; the item stays in the backpack until fired
			}

			return base.ReceiveItem(source, item);
		}
		public void Aimed()
		{
			if (!CanUse()) return;
			CurrentState |= eState.Aimed;
			if (Owner != null)
			{
				OwnerOut?.SendMessage("Your " + Name + " is now aimed!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
		}
		public void Armed()
		{
			if (!CanUse()) return;
			CurrentState |= eState.Armed;
			if (Owner != null)
			{
				OwnerOut?.SendMessage("Your " + Name + " is now armed!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
		}
		public virtual void Fire()
		{
			if (!CanUse()) return;
			if (CurrentState != eState.Ready)
			{
				if (Owner != null)
				{
					OwnerOut?.SendMessage("The " + Name + " is not ready to fire yet!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				}
				return;
			}
			if (TargetObject != null)
				SetGroundTarget(TargetObject.X, TargetObject.Y, TargetObject.Z);
			if (!GroundTarget.IsValid)
				return;

			//Range Checks
			if (MinAttackRange != -1 && this.GetDistanceTo(GroundTarget) < MinAttackRange)
			{
				OwnerOut?.SendMessage("Your target is too close to this siege weapon!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}
			if (MaxAttackRange != -1 && this.GetDistanceTo(GroundTarget) > MaxAttackRange)
			{
				OwnerOut?.SendMessage("Your target is too far away to this siege weapon!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}

			if (Owner is GameBot bot)
			{
				if (SiegeWeaponTimer.IsAlive || !BotSiegeRuntime.CanDamage(this, TargetObject as GameLiving)) return;
				m_pendingShot = SiegeShot.Plain(MeleeDamageType);
				GameObject firedTarget = TargetObject;
				int generation = _botShotGeneration;
				ShotsFired++;
				new ECSGameTimer(this, _ =>
				{
					if (generation == _botShotGeneration && Owner == bot && TargetObject == firedTarget &&
						BotSiegeRuntime.CanDamage(this, firedTarget as GameLiving)) DoDamage();
					return 0;
				}, Math.Max(1, GetActionDelay(SiegeTimer.eAction.Fire)));
			}
			else
			{
				m_pendingShot = TakeShot();
				if (m_pendingShot.IsAmmo)
					OwnerOut?.SendMessage("You fire " + m_pendingShot.AmmoName + ".", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(MakeDelayedDamage), GetActionDelay(SiegeTimer.eAction.Fire));
			}
			BroadcastFireAnimation(GetActionDelay(SiegeTimer.eAction.Fire));
			if (Owner != null)
				OwnerOut?.SendMessage("You fire " + GetName(0, false) + "!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			Arm();
		}

		private int MakeDelayedDamage(ECSGameTimer callingTimer)
		{
			DoDamage();
			return 0;
		}

        private int _botShotGeneration;
        public long ShotsFired { get; private set; }
        public long ConfirmedHits { get; private set; }
        public long LastHitTick { get; private set; }
        public void RecordBotHit(int healthBefore, GameLiving target)
        {
            if (Owner is GameBot && target.Health < healthBefore)
            {
                ConfirmedHits++;
                LastHitTick = GameLoop.GameLoopTime;
            }
        }

		/// <summary>
		/// Calculates the damage based on the target type (door, siege, player)
		/// <summary>
		public virtual int CalcDamageToTarget(GameLiving target)
		{
			return BaseDamage;
		}

		public virtual void DoDamage()
		{
		}
		/*	slot:48 level:90 value1:0x00 value2:0x00 hand:0x00 damageType:0x00 objectType:0x29 weight:2    con:100 dur:0   qual:0   bonus:0  model:0x0A3C color:0x0000 effect:0x00 unk1_172:1 "2 greek fire"
			slot:49 level:90 value1:0x00 value2:0x00 hand:0x00 damageType:0x00 objectType:0x29 weight:2    con:100 dur:0   qual:0   bonus:0  model:0x0A3D color:0x0000 effect:0x00 unk1_172:1 "2 ice ball"
			index:0  unk1:0x5A00 unk2:0x0000 unk3:0x2900 unk4:0x0264 unk5:0x00 unk6:0x0000 model:0x0A3D unk7:0x0001 unk7:0x0000 name:"2 ice ball")
			index level value1  value2 hand  objecttype damagetype weight conc dur qual bonnus model color effect
		*/

		public void TryRepair()
		{
			if (Owner is not GamePlayer player) return;
			if (TimesRepaired <= 3)
			{
				if (player.GetCraftingSkillValue(eCraftingSkill.WoodWorking) < 301)
				{
					player.Out.SendMessage("You must have woodworking skill to repair a siege weapon.", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
					return;
				}
			}
			else
			{
				player.Out.SendMessage("The siegeweapon has decayed beyond repairs!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}

			RepairCommandHandler repairCommand = new RepairCommandHandler();
			if (!repairCommand.PreFireChecks(player, this)) return;
			repairCommand.StartRepair(player, this);
		}
		public bool Repair(int amount) => Repair(amount, null);

        // Serialize player and bot repairs against the same lifetime counter.
        // Payment is attempted only after the repair is confirmed eligible.
        internal bool Repair(int amount, Func<bool> pay)
        {
            lock (AutonomousSiegeOwnership.ChangeGate)
            {
                if (!IsAlive || amount <= 0 || TimesRepaired > 3 || Health >= MaxHealth) return false;
                if (pay != null && !pay()) return false;
                TimesRepaired++;
                Health += amount;
                return true;
            }
        }

		public void salvage()
		{
			if (Owner is not GamePlayer player) return;
			if (player.GetCraftingSkillValue(eCraftingSkill.SiegeCrafting) == -1)
			{
				player.Out.SendMessage("You must be a Siege weapon crafter to salvage it.", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return;
			}
			player.SalvageSiegeWeapon(this);
		}
		#endregion
		#region private methods
		private void BroadcastAnimation()
		{
			foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
			{
				player.Out.SendSiegeWeaponAnimation(this);
			}
		}
		private void BroadcastFireAnimation(int timer)
		{
			foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
			{
				player.Out.SendSiegeWeaponFireAnimation(this, timer);
				//				player.Out.SendEmoteAnimation(this, (eEmote)201); // on trebuchet fire external ammo ?
			}
		}
		protected int[] ActionDelay;
		private ushort m_effect;

        /// <summary>
        /// delay to do action in Ms
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public int GetActionDelay(SiegeTimer.eAction action)
		{
			if (action == SiegeTimer.eAction.Fire && GroundTarget.IsValid)
                return (int)( ActionDelay[(int)action] + this.GetDistanceTo( GroundTarget ) );
			
			int delay = ActionDelay[(int)action];
			//TODO: better to use a property here - discuss to implement one? dunnow if siegespeed is used at another place.
			if (Owner != null && Owner.EffectList.CountOfType<BannerOfBesiegingEffect>() > 0)
			{
				BannerOfBesiegingEffect eff = Owner.EffectList.GetOfType<BannerOfBesiegingEffect>();
				if (eff != null)
					delay = (int)(delay * (1 - 0.06 * eff.Effectiveness));

			}
			return delay;
		}

		public Boolean CanUse()
		{
			// Read the owner once: a bot releasing the weapon on another thread cleared it between checks
			// (NullReferenceException in the siege timer, 2026-10-27 overnight run).
			GameLiving owner = Owner;
			if (owner == null)
				return false;
			if (owner is GamePlayer player)
				player.Stealth(false);
			else if (owner is IGamePlayer playerLike)
				playerLike.Stealth(false);
			if (owner.IsIncapacitated)
			{
				OwnerOut?.SendMessage("You can't use this siegeweapon now!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return false;
			}
			if (Health <= DecayedHp)
			{
				OwnerOut?.SendMessage("The siegeweapon needs to be repaired!", eChatType.CT_Say, eChatLoc.CL_SystemWindow);
				return false;
			}
			if (!this.IsWithinRadius(owner, SIEGE_WEAPON_CONTROLE_DISTANCE))
			{
				OwnerOut?.SendMessage("You are too far from your siege equipment to control it any longer!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			if (owner.Realm != this.Realm)
			{
				OwnerOut?.SendMessage($"This siege equipment is owned by an enemy realm!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}
				
			
			return true;
		}

		public void PreAction()
		{
			if (SiegeWeaponTimer.IsAlive)
			{
				SiegeWeaponTimer.Stop();
				OwnerOut?.SendSiegeWeaponCloseInterface();
			}

			SiegeWeaponTimer.Start(GetActionDelay(SiegeWeaponTimer.CurrentAction));

			if (Owner != null)
			{
				int actionDelay = GetActionDelay(SiegeWeaponTimer.CurrentAction) / 100;

				// Ram siege interface is 2 seconds faster for some reason. Adding 2 seconds to the action delay.
				if (this is GameSiegeRam)
					actionDelay += 20;

				OwnerOut?.SendSiegeWeaponInterface(this, actionDelay);
			}

			BroadcastAnimation();
		}

		private void StartControlRangeCheck()
		{
			if (ObjectState != eObjectState.Active)
				return;
			lock (_controlRangeTimerLock)
			{
				if (m_controlRangeTimer == null)
				{
					m_controlRangeTimer = new ECSGameTimer(this);
					m_controlRangeTimer.Callback = new ECSGameTimer.ECSTimerCallback(ControlRangeTimerCallback);
				}
				else if (m_controlRangeTimer.IsAlive)
					return;
				m_controlRangeTimer.Start(1500);
			}
		}

		private void StopControlRangeCheck()
		{
			lock (_controlRangeTimerLock)
			{
				if (m_controlRangeTimer == null)
					return;
				m_controlRangeTimer.Stop();
				m_controlRangeTimer = null;
			}
		}

		private int ControlRangeTimerCallback(ECSGameTimer callingTimer)
		{
			if(Owner==null)
			{
				StopControlRangeCheck();
				return 0;
			}
			if (Owner is GameBot && (!Owner.IsAlive || Owner.ObjectState != eObjectState.Active || Owner.CurrentRegion != CurrentRegion))
			{ ReleaseControl(); return 0; }
			if(this.GetDistanceTo(Owner)>SIEGE_WEAPON_CONTROLE_DISTANCE)
			{
				ReleaseControl();
				return 0;
			}
			return 1500;
		}
		#endregion
		#region override function
		public override void ModifyAttack(AttackData attackData)
		{
			attackData.Damage /= 30;
			attackData.StyleDamage /= 30;
			attackData.CriticalDamage /= 30;
		}

		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player))
				return false;

			if (Owner == player)
				return false;

			TakeControl(player);
			return true;
		}
		public override void LoadFromDatabase(DataObject obj)
		{
			base.LoadFromDatabase(obj);
			if (!(obj is DbItemTemplate)) return;
			DbItemTemplate item = (DbItemTemplate)obj;
            this.TranslationId = item.TranslationId;
			this.Name = item.Name;
            this.ExamineArticle = item.ExamineArticle;
            this.MessageArticle = item.MessageArticle;
			this.Model = (ushort)item.Model;
		}

		public override bool AddToWorld()
		{
			if (!base.AddToWorld()) return false;
			if (this is GameSiegeRam addedRam) AutonomousSiegeOwnership.WorldPresenceChanged(addedRam, true);
			StartDecay();
			TimesRepaired = 0;
			return true;
		}
		public override bool RemoveFromWorld()
		{
			if (Owner is GameBot) ReleaseControl();
			if (!base.RemoveFromWorld()) return false;
			if (this is GameSiegeRam removedRam) AutonomousSiegeOwnership.WorldPresenceChanged(removedRam, false);
			StopDecay();
			return true;
		}

		#endregion
		#region decay
		public override void StartHealthRegeneration()
		{
			//don't regenerate health
		}

		private void StartDecay()
		{
			if (ObjectState != eObjectState.Active)
				return;
			lock (_decayTimerLock)
			{
				if (m_decayTimer == null)
				{
					m_decayTimer = new ECSGameTimer(this);
					m_decayTimer.Callback = new ECSGameTimer.ECSTimerCallback(DecayTimerCallback);
				}
				else if (m_decayTimer.IsAlive)
					return;
				m_decayTimer.Start(DECAYPERIOD);
			}
		}

		private void StopDecay()
		{
			lock (_decayTimerLock)
			{
				if (m_decayTimer == null)
					return;
				m_decayTimer.Stop();
				m_decayTimer = null;
			}
		}

		private int DecayTimerCallback(ECSGameTimer callingTimer)
		{
			TakeDamage(this, eDamageType.Natural, DecayDeductHp, 0);
			return DECAYPERIOD;
		}

		#endregion

		private static SpellLine m_SiegeSpellLine;
		public static SpellLine SiegeSpellLine
		{
			get
			{
				if (m_SiegeSpellLine == null)
					m_SiegeSpellLine = new SpellLine("SiegeSpellLine", "Siege Weapon Spells", "unknown", false);

				return m_SiegeSpellLine;
			}
		}
	}
	#endregion
	#region siegeTimer
	public class SiegeTimer : ECSGameTimerWrapperBase
	{
		public enum eAction : byte
		{
			None = 0x00,
			Aiming = 0x01,
			Arming = 0x02,
			Loading = 0x03,
			Fire = 0x04,
		}

		/// <summary>
		/// Constructs a new UseSlotAction
		/// </summary>
		/// <param name="siegeWeapon">The siege weapon</param>
		public SiegeTimer(GameSiegeWeapon siegeWeapon)
			: base(siegeWeapon)
		{
			m_siegeWeapon = siegeWeapon;
		}

		private eAction m_currentAction;
		private GameSiegeWeapon m_siegeWeapon;

		public eAction CurrentAction
		{
			get { return m_currentAction; }
			set { m_currentAction = value; }
		}
		public GameSiegeWeapon SiegeWeapon
		{
			get { return m_siegeWeapon; }
			set { m_siegeWeapon = value; }
		}

		protected override int OnTick(ECSGameTimer timer)
		{
			if (SiegeWeapon.Owner == null)
				return 0;
			switch (CurrentAction)
			{
				case eAction.Arming:
					{
						SiegeWeapon.Armed();
						break;
					}
				case eAction.Aiming:
					{
						SiegeWeapon.Aimed();
						break;
					}
				case eAction.Loading:
					{
						//todo set ammo
						break;
					}
				case eAction.Fire:
					{
						SiegeWeapon.DoDamage();
						break;
					}
				default: break;
			}

			if (SiegeWeapon.Owner is GamePlayer player)
			{
				player.Out.SendSiegeWeaponInterface(this.SiegeWeapon, 0);
			}
			if ((SiegeWeapon.CurrentState & GameSiegeWeapon.eState.Armed) != GameSiegeWeapon.eState.Armed)
				SiegeWeapon.Arm();
			
			return 0;
		}
	}
	#endregion
}
/* messages:

You are too far from your siege equipment to control it any longer!
You can't salvage the trebuchet!
The trebuchet's target is too far away to reach!
Your target has moved out of range!
You put the ice ball into the field catapult.
The trebuchet is fully repaired!
That object isn't carryable...
*/
