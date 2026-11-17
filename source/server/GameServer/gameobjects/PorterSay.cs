using System.Linq;

namespace DOL.GS
{
	/// <summary>
	/// Lets a player /say a destination to a porter instead of clicking its [link] (DR-453). Only one porter
	/// answers: the one the player has targeted, or else the nearest porter within interact range, so a player
	/// talking near a row of porters is not sent off by all of them.
	/// </summary>
	public static class PorterSay
	{
		/// <summary>The destination word from a /say line ("Camelot", "[Camelot]", "camelot."), or null.</summary>
		public static string Destination(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			string word = text.Trim().Trim('[', ']', '.', '!', '?', ' ').Trim();
			return word.Length == 0 ? null : word;
		}

		/// <summary>Whether <paramref name="porter"/> is the one that should answer this player's /say.</summary>
		public static bool ShouldAnswer(GameNPC porter, GamePlayer player)
		{
			if (porter == null || player == null || porter.CurrentRegion != player.CurrentRegion)
				return false;

			if (player.TargetObject == porter)
				return porter.IsWithinRadius(player, WorldMgr.SAY_DISTANCE);

			// A targeted porter elsewhere answers instead of this one.
			if (player.TargetObject is GameNPC targeted && targeted != porter && IsPorter(targeted) &&
			    targeted.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
				return false;

			if (!porter.IsWithinRadius(player, WorldMgr.INTERACT_DISTANCE))
				return false;

			GameNPC nearest = player.GetNPCsInRadius(WorldMgr.INTERACT_DISTANCE)
				.Where(IsPorter)
				.OrderBy(npc => npc.GetDistanceTo(player))
				.FirstOrDefault();
			return nearest == porter;
		}

		private static bool IsPorter(GameNPC npc) =>
			npc is GameTeleporter || npc is DOL.GS.Scripts.LiveTeleporter || npc is DOL.GS.Scripts.InlandTeleporter;
	}
}
