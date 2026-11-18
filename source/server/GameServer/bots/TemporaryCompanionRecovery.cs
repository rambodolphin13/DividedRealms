using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>Ephemeral /spawn helpers only. Never changes persistent character recovery.</summary>
    public static class TemporaryCompanionRecovery
    {
        public const int MaximumLeaderDistance = 3000;
        public const int CheckIntervalMilliseconds = 2000;

        public static bool CanReleaseToOwner(bool temporary, bool active, bool sameGroup,
            bool ownerAlive, bool ownerActive, bool partyInCombat) =>
            temporary && active && sameGroup && ownerAlive && ownerActive && !partyInCombat;

        public static bool CanReleaseToOwner(GameBot companion)
        {
            GamePlayer owner = companion?.Owner;
            bool sameGroup = owner != null && companion.Group != null && companion.Group == owner.Group &&
                companion.Group.IsInTheGroup(companion) && companion.Group.IsInTheGroup(owner);
            return companion != null && CanReleaseToOwner(companion.IsTemporaryGroupHelper,
                companion.ObjectState == GameObject.eObjectState.Active, sameGroup,
                owner?.IsAlive == true, owner?.ObjectState == GameObject.eObjectState.Active,
                HasPartyCombat(companion) || HasCombatNearOwner(owner));
        }

        // The frontier raid aggro leash: a raid bot chasing an enemy gamebot far from the player (its own part of the
        // raid still busy) is recalled anyway once nothing is fighting near the player, so roamer chases never drag the
        // raid away. PvE fights keep the normal recall rules.
        public static bool FrontierChaseLeash(GameBot companion) =>
            CompanionRaid.IsMember(companion) && CompanionPvpEngagement.FrontierRaidAggro(companion.Owner) &&
            CompanionPvpEngagement.Enemy(companion.Owner, companion.TargetObject as GameLiving) &&
            !HasCombatNearOwner(companion.Owner);

        // DR-474: a following companion that stops short of the player (a failed path off the navmesh: the player on a
        // keep wall or tower, a bot that crossed a zone border at the wrong height) was never recalled, because the
        // recall only fired beyond 3000 units. Now a companion more than StallDistance from the player that has not
        // closed StallProgress units for StallMilliseconds is put back in formation.
        public const int StallDistance = 500;
        public const int StallProgress = 100;
        public const int StallMilliseconds = 12_000;

        /// <summary>
        /// One stall check. <paramref name="best"/> is the closest the companion has been since <paramref name="since"/>.
        /// Not eligible (fighting, held, not following) or within StallDistance resets the clock. True when stalled.
        /// </summary>
        public static bool StallStep(bool eligible, int distance, long now, ref int best, ref long since)
        {
            if (!eligible || distance <= StallDistance || since <= 0 || distance < best - StallProgress)
            {
                best = distance;
                since = now;
                return false;
            }
            return now - since >= StallMilliseconds;
        }

        public static bool HasCombatNearOwner(GamePlayer owner) => owner?.Group != null &&
            owner.Group.GetMembersInTheGroup().Any(member => member.IsAlive &&
                member.CurrentRegionID == owner.CurrentRegionID && owner.IsWithinRadius(member, MaximumLeaderDistance) &&
                (member.InCombat || member.IsAttacking ||
                 member.ControlledBrain?.Body is { IsAlive: true, InCombat: true } ||
                 member is GameBot { Brain: BotBrain { HasAggro: true } }));

        public static bool ShouldRecall(bool temporary, bool alive, bool active, bool sameGroup,
            bool followsOwner, bool riding, bool ownerRiding, bool sameRegion, int distance, bool released) =>
            temporary && alive && active && sameGroup && followsOwner && !riding &&
            (released || !ownerRiding && (!sameRegion || distance > MaximumLeaderDistance));

        public static bool HasPartyCombat(GameBot companion)
        {
            if (companion?.Group == null) return false;
            return companion.Group.GetMembersInTheGroup().Any(member =>
                member.IsAlive && member.CurrentRegionID == companion.CurrentRegionID &&
                companion.IsWithinRadius(member, MaximumLeaderDistance) &&
                (member.InCombat || member.IsAttacking ||
                 member.ControlledBrain?.Body is { IsAlive: true, InCombat: true } ||
                 member is GameBot { Brain: BotBrain { HasAggro: true } }));
        }

        public static bool CanPrioritizeOwnerResurrection(bool temporary, bool companionAlive, bool ownerAlive,
            bool sameGroup, bool followsOwner, bool hasLearnedResurrection, bool partyInCombat) =>
            temporary && companionAlive && !ownerAlive && sameGroup && followsOwner &&
            hasLearnedResurrection && !partyInCombat;
    }
}
