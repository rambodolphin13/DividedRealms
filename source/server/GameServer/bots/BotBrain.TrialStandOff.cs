using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Trials;

namespace DOL.AI.Brain
{
    /// <summary>Applies <see cref="TrialStandOff"/> (DR-395): a player's casters and healers spread round a trial's boss.</summary>
    public partial class BotBrain
    {
        private TrialRun _standOffRun;
        private TrialMob _standOffBoss;
        private Vector3 _standOffCentre;
        private Vector3? _standOffSpot;
        private long _nextStandOffScanTick;
        private bool _standOffFight;

        /// <summary>Whether this companion takes a stand-off spot in trial fights (see <see cref="TrialStandOff.Applies"/>).</summary>
        internal bool TakesTrialStandOff() =>
            BotBody is { } bot && TrialStandOff.Applies(bot.IsPlayerCompanion, bot.IsPlayerLedGroup, bot.IsAlive,
                PrefersCurrentSpellRange(), BotPartyRoles.IsSupport(bot), PlayerBotStay.IsHeld(bot));

        /// <summary>
        /// True while this companion's player is in a trial fight it spreads out for; <paramref name="spot"/> is where it
        /// stands (null: no boss to spread around yet, or no ground for a spot: it holds where it is). Scanned every two
        /// seconds; the layout is kept while the boss stays near where the ring was laid out.
        /// </summary>
        private bool InTrialStandOffFight(out Vector3? spot)
        {
            spot = null;
            // Only while the player fights nearby: a player who walks off, or calls /bots follow out of combat, is followed.
            if (!TakesTrialStandOff() || AssistedPlayer is not { IsAlive: true } leader ||
                leader.CurrentRegion != Body.CurrentRegion || !Body.IsWithinRadius(leader, TrialStandOff.FightRadius) ||
                !TrialStandOff.LeaderFights(leader.InCombat, leader.IsAttacking))
            {
                _standOffFight = false;
                return false;
            }
            long now = GameLoop.GameLoopTime;
            if (now >= _nextStandOffScanTick)
            {
                _nextStandOffScanTick = now + 2_000;
                ScanTrialStandOff(leader);
            }
            spot = _standOffSpot;
            return _standOffFight;
        }

        private void ScanTrialStandOff(GamePlayer leader)
        {
            TrialRun run = (leader.TargetObject as TrialMob)?.Run ?? (Body.TargetObject as TrialMob)?.Run ?? _standOffRun;
            if (run == null || run.State != TrialRunState.Running)
            {
                _standOffRun = null;
                _standOffBoss = null;
                _standOffSpot = null;
                _standOffFight = false;
                return;
            }
            _standOffRun = run;
            List<TrialMob> mobs = run.Mobs().Where(mob => !mob.Spec.Objective && mob.CurrentRegion == leader.CurrentRegion &&
                mob.IsWithinRadius(leader, TrialStandOff.FightRadius)).ToList();
            _standOffFight = mobs.Count > 0;
            int index = TrialStandOff.AnchorIndex(mobs.Select(mob => new TrialStandOff.Mob(mob.Name, mob.MaxHealth)).ToList());
            if (index < 0)
            {
                // Packs only (the cobras before Runihura): no ring, but no walking back to the player's side either.
                _standOffBoss = null;
                _standOffSpot = null;
                return;
            }
            TrialMob boss = mobs[index];
            Vector3 at = new(boss.X, boss.Y, boss.Z);
            if (!TrialStandOff.NeedsNewLayout(boss == _standOffBoss, Vector2.Distance(new(at.X, at.Y), new(_standOffCentre.X, _standOffCentre.Y))))
                return;
            _standOffBoss = boss;
            _standOffCentre = at;
            _standOffSpot = LayOutTrialStandOffSpot(run, boss, at);
            if (log.IsDebugEnabled)
                log.Debug($"TRIAL_STANDOFF bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" boss=\"{boss.Name}\" " +
                          (_standOffSpot is Vector3 chosen
                              ? $"spot={(int)chosen.X},{(int)chosen.Y},{(int)chosen.Z} fromBoss={(int)Vector2.Distance(new(chosen.X, chosen.Y), new(at.X, at.Y))}"
                              : "spot=none"));
        }

        private Vector3? LayOutTrialStandOffSpot(TrialRun run, TrialMob boss, Vector3 centre)
        {
            Group group = Body.Group;
            if (group == null || boss.CurrentZone == null)
                return null;
            // The same order for every companion: by object id among those that take a spot.
            List<int> takers = group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(member => member.IsAlive && member.Brain is BotBrain brain && brain.TakesTrialStandOff())
                .Select(member => (int)member.ObjectID).OrderBy(id => id).ToList();
            int slot = takers.IndexOf(Body.ObjectID);
            if (slot < 0)
                return null;
            Vector2 middle = new(centre.X, centre.Y);
            double approach = TrialStandOff.Bearing(middle, new(run.Starter.X, run.Starter.Y));
            double bearing = TrialStandOff.BearingDegrees(approach, slot, takers.Count);
            var nav = PathfindingProvider.Instance;
            bool mesh = nav.IsAvailable && nav.HasNavmesh(boss.CurrentZone);
            foreach (int radius in TrialStandOff.Radii)
            {
                Vector2 raw = TrialStandOff.Spot(middle, bearing, radius);
                Vector3 wanted = new(raw.X, raw.Y, centre.Z);
                if (!mesh)
                    return wanted;
                // Slide along the ground from the boss toward the spot: a wall or a ledge stops it short. The spot must
                // also see the boss, or the caster would only walk back in to cast (BotLineOfSight).
                if (nav.GetMoveAlongSurface(boss.CurrentZone, centre, wanted, nav.DefaultFilters) is Vector3 ground &&
                    Vector2.Distance(new(ground.X, ground.Y), middle) >= TrialStandOff.MinimumRadius &&
                    HasNavmeshSight(boss.CurrentZone, ground, centre))
                    return ground;
            }
            return null;
        }

        private static bool HasNavmeshSight(Zone zone, Vector3 from, Vector3 to)
        {
            var nav = PathfindingProvider.Instance;
            return !nav.IsAvailable || !nav.HasNavmesh(zone) ||
                nav.HasLineOfSight(zone, new(from.X, from.Y, from.Z + 48), new(to.X, to.Y, to.Z + 48), nav.DefaultFilters);
        }

        /// <summary>
        /// In a trial fight, walks to its spot when it should (true: the turn is used). False when it is there, has no
        /// spot, or is not in a trial fight. <paramref name="attackTarget"/> is the monster it is casting at, if any.
        /// </summary>
        private bool TryTrialStandOff(GameLiving attackTarget)
        {
            if (!InTrialStandOffFight(out Vector3? spot) || spot is not Vector3 point)
                return false;
            double toSpot = Vector2.Distance(new(Body.X, Body.Y), new(point.X, point.Y));
            int spellRange = attackTarget != null && PrefersCurrentSpellRange() ? OffensiveApproachRange(attackTarget) : 0;
            double spotToTarget = attackTarget != null ? Vector2.Distance(new(point.X, point.Y), new(attackTarget.X, attackTarget.Y)) : 0;
            bool casting = Body.IsCasting || Body.castingComponent?.HasPendingSkillRequests == true;
            if (!TrialStandOff.WalksToSpot(casting, toSpot, spotToTarget, spellRange))
                return false;
            // Its target must be in view from the spot too (the boss may have moved since the ring was laid out).
            if (spellRange > 0 && attackTarget.CurrentZone != null &&
                !TrialStandOff.WalksToSpot(casting, toSpot, spotToTarget, spellRange,
                    HasNavmeshSight(attackTarget.CurrentZone, point, new(attackTarget.X, attackTarget.Y, attackTarget.Z))))
                return false;
            BotBody.WakeRecoveryRest();   // a resting bot ignores movement orders
            Body.StopAttack();
            Body.StopFollowing();
            Body.PathTo(point, Body.MaxSpeed);
            return true;
        }
    }
}
