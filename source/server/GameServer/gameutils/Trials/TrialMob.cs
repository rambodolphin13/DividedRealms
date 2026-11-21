using DOL.GS.PacketHandler;

namespace DOL.GS.Trials
{
    /// <summary>A monster or clickable objective that belongs to one trial run and leaves with it.</summary>
    public class TrialMob : GameNPC
    {
        public TrialRun Run { get; }
        public TrialMobSpec Spec { get; }
        public bool Immune { get; set; }
        public bool Finished { get; private set; }
        /// <summary>The player who clicked this objective; null when it was finished another way.</summary>
        public GamePlayer CompletedBy { get; private set; }

        private readonly int _maxHealth;

        public TrialMob(TrialRun run, TrialMobSpec spec, int maxHealth)
        {
            Run = run;
            Spec = spec;
            Immune = spec.Immune;
            _maxHealth = maxHealth;
        }

        public override int MaxHealth => _maxHealth;

        /// <summary>
        /// Only the trial's required mobs pay a kill. Optional extras (Ghillan's clones, respawning hatchlings, adds)
        /// spawn without end and were farmed for full XP, coin and Atlantean Glass.
        /// </summary>
        public bool Rewards => Spec.Required;

        public override long ExperienceValue => Rewards ? base.ExperienceValue : 0;

        public override long GetExperienceValueForLevel(int level) => Rewards ? base.GetExperienceValueForLevel(level) : 0;

        public override bool CanDropLoot => Rewards && base.CanDropLoot;

        /// <summary>
        /// An immune trial monster does not regenerate: its health only moves through the trial's own hooks (Zopureo's
        /// purros, the azure shark). Out of combat an idle monster refills an eighth of its health a tick, which undid every
        /// purros (bot playtest 2026-11-14, 1.1). Regeneration starts again with the next change of health.
        /// </summary>
        protected override int HealthRegenerationTimerCallback(ECSGameTimer callingTimer) =>
            Immune ? 0 : base.HealthRegenerationTimerCallback(callingTimer);

        public override void TakeDamage(GameObject source, eDamageType damageType, int damageAmount, int criticalAmount)
        {
            if (Spec.Objective || Immune || !Run.MayHurt(source) || !Spec.Hurts.HasFlag(KindOf(source, damageType)))
                return;

            if (Spec.OnHit != null)
            {
                int adjusted = Spec.OnHit(this, source, damageType, damageAmount + criticalAmount);
                if (adjusted <= 0)
                    return;
                if (adjusted != damageAmount + criticalAmount)
                {
                    damageAmount = adjusted;
                    criticalAmount = 0;
                }
            }

            base.TakeDamage(source, damageType, damageAmount, criticalAmount);

            if (Spec.Reflect > 0 && source is GameLiving attacker && attacker.IsAlive)
                attacker.TrialDamage(this, damageType, (int)(damageAmount * Spec.Reflect));
        }

        public static TrialDamageKind KindOf(GameObject source, eDamageType damageType)
        {
            if (damageType is not (eDamageType.Slash or eDamageType.Crush or eDamageType.Thrust))
                return TrialDamageKind.Spell;
            return source is GameLiving { ActiveWeaponSlot: eActiveWeaponSlot.Distance } ? TrialDamageKind.Ranged : TrialDamageKind.Melee;
        }

        public override void Die(GameObject killer)
        {
            Finished = true;
            Run.MobFinished(this);
            base.Die(killer);
        }

        /// <summary>Finishes an objective as a click would, without the click.</summary>
        public void Complete()
        {
            Finished = true;
            Run.MobFinished(this);
        }

        public override bool Interact(GamePlayer player)
        {
            if (!Spec.Objective)
                return base.Interact(player);
            if (Finished || !Run.IsParticipant(player))
                return false;
            if (!IsWithinRadius(player, WorldMgr.INTERACT_DISTANCE))
            {
                player.Out.SendMessage("You are too far away.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (!string.IsNullOrEmpty(Spec.ObjectiveText))
                player.Out.SendMessage(Spec.ObjectiveText, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            CompletedBy = player;
            Complete();
            return true;
        }
    }

    /// <summary>
    /// Damage a trial deals outside a normal attack (fireballs, pulses, spikes, falls). TakeDamage alone writes nothing
    /// to the combat log, so players watched their health drop with no line saying what hit them (DR-397).
    /// </summary>
    public static class TrialHits
    {
        public static void TrialDamage(this GameLiving target, GameObject source, eDamageType type, int amount)
        {
            if (amount > 0 && target is GamePlayer { IsAlive: true, ObjectState: GameObject.eObjectState.Active } player)
                player.Out.SendMessage(HitLine(source is GamePlayer ? null : source?.Name, amount, type), eChatType.CT_Damaged, eChatLoc.CL_SystemWindow);
            target.TakeDamage(source, type, amount, 0);
        }

        public static string HitLine(string source, int amount, eDamageType type) =>
            $"{(string.IsNullOrEmpty(source) ? "The trial" : source)} hits you for {amount} {type.ToString().ToLowerInvariant()} damage!";
    }
}
