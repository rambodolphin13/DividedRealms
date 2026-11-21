using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Diagnostic only (DR-394/395/396): what a player's companion is doing while its leader fights. One debug line per
    /// companion every three seconds, and only while the leader is in combat, so a slow or idle companion can be read
    /// from the server log after a fight. Never changes a decision.
    /// </summary>
    public partial class BotBrain
    {
        public const int CompanionFightTraceMilliseconds = 3_000;
        private long _nextCompanionFightTraceTick;

        private void TraceCompanionFight()
        {
            if (!log.IsDebugEnabled || BotBody is not { IsPlayerCompanion: true, IsPlayerLedGroup: true } bot)
                return;
            long now = GameLoop.GameLoopTime;
            if (now < _nextCompanionFightTraceTick || AssistedPlayer is not { InCombat: true } leader)
                return;
            _nextCompanionFightTraceTick = now + CompanionFightTraceMilliseconds;
            GameLiving target = Body.TargetObject as GameLiving;
            GameLiving leaderTarget = leader.TargetObject as GameLiving;
            log.Debug($"COMPANION_FIGHT bot=\"{bot.Name}\" class=\"{bot.ClassName}\" alive={bot.IsAlive} state={FSM.GetCurrentState()?.StateType} " +
                      $"target=\"{target?.Name}\" targetDist={(target != null ? Body.GetDistanceTo(target) : -1)} " +
                      $"leaderTarget=\"{leaderTarget?.Name}\" leaderTargetDist={(leaderTarget != null ? Body.GetDistanceTo(leaderTarget) : -1)} " +
                      $"leaderDist={Body.GetDistanceTo(leader)} casting=\"{(Body.IsCasting ? Body.castingComponent?.SpellHandler?.Spell?.Name : null)}\" " +
                      $"pending={Body.castingComponent?.HasPendingSkillRequests == true} hitBy={Body.IsBeingInterruptedByOther} ownSwing={Body.IsBeingSelfInterrupted} " +
                      $"attacking={Body.attackComponent?.AttackState == true} moving={Body.IsMoving} resting={bot.IsRecoveryResting} " +
                      $"aggro={HasAggro} ordered=\"{ActiveOrderedPullTarget?.Name}\" hp={Body.HealthPercent} power={Body.ManaPercent}");
        }
    }
}
