using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Who takes part with a trial's leader. A solo trial is the leader alone; a group trial is the leader's group; a
    /// Raid-scope trial is also every other group of the leader's battlegroup. Live Raid steps credit the whole
    /// battlegroup, and the trial code read only the leader's own group, so a second group got nothing (DR-352). A
    /// companion raid (/bots raid) is one large group and was already covered.
    /// </summary>
    public static class TrialParty
    {
        /// <summary>The leader, their group (bots too) and, for a Raid-scope trial, the battlegroup's other groups.</summary>
        public static IEnumerable<GameLiving> Members(GamePlayer leader, TrialScope scope)
        {
            if (leader == null)
                return Enumerable.Empty<GameLiving>();
            if (scope == TrialScope.Solo)
                return new GameLiving[] { leader };

            var members = new List<GameLiving> { leader };
            if (leader.Group != null)
                members.AddRange(leader.Group.GetMembersInTheGroup());

            if (scope == TrialScope.Raid &&
                leader.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY) is { Members: { } battleMembers })
            {
                foreach (GamePlayer player in battleMembers.Keys.OfType<GamePlayer>().ToList())
                {
                    members.Add(player);
                    if (player.Group != null)
                        members.AddRange(player.Group.GetMembersInTheGroup());
                }
            }

            return members.Where(member => member != null).Distinct();
        }

        public static IEnumerable<GamePlayer> Players(GamePlayer leader, TrialScope scope) =>
            Members(leader, scope).OfType<GamePlayer>();
    }
}
