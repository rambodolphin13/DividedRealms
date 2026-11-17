using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A companion in a player's group fights the leader's target. Each turn used to choose "whoever just hit me, else the
    /// most threat", so in a pack every companion turned on the monster hitting it (DR-418), stayed on a boss after
    /// /bots attack on an add or obelisk (DR-417), and hit a boss that could not be hurt while the player broke its
    /// guards (DR-421). The leader's live attack target, else the last /bots attack order, now comes first for damage
    /// companions. Tanks keep their own choice (they hold the boss and peel adds), and a mesmerized or rooted target is
    /// left alone as before (BotBrain.CrowdControlRespect).
    /// </summary>
    public partial class BotBrain
    {
        internal GameLiving LeaderFocusTarget()
        {
            if (BotBody?.IsPlayerLedGroup != true || BotPartyRoles.IsTank(BotBody) || AssistedPlayer is not GamePlayer leader)
                return null;

            GameLiving focus = PlayerLedPullCoordinator.FindLeaderTarget(leader) ?? ActiveOrderedPullTarget;
            if (focus == null || focus == Body || !focus.IsAlive || focus.ObjectState != GameObject.eObjectState.Active ||
                focus.CurrentRegion != Body.CurrentRegion || !Body.IsWithinRadius(focus, MAX_AGGRO_LIST_DISTANCE) ||
                IsBreakablyControlled(focus) || !CompanionEngagementMode.Allows(Body, focus) ||
                !GameServer.ServerRules.IsAllowedToAttack(Body, focus, true))
                return null;

            if (!AggroList.ContainsKey(focus))
                AddToAggroList(focus, System.Math.Max(100, focus.EffectiveLevel * 10));
            return focus;
        }
    }
}
