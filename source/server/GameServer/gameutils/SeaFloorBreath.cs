using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Water breathing lent by a fight on the sea floor (DR-120): sea-floor artifact encounters and Master Level trials.
    /// Players near the fight breathe while it runs; when it ends they keep it for <see cref="GraceMs"/> to swim up, and
    /// are told so. Before this, artifact sites drowned solo players in about 28 s and trials took the breath away the
    /// moment they ended (a level 50 drowned 40 s after winning ML 1.2).
    /// Only a grant this helper made is taken back, and a later grant cancels an earlier release.
    /// </summary>
    public static class SeaFloorBreath
    {
        public const int GraceMs = 120_000;
        private const string TokenProperty = "DR_SEA_FLOOR_BREATH";

        /// <summary>Lends breath to a player who can't breathe underwater. True when this call lent it.</summary>
        public static bool Lend(GamePlayer player)
        {
            if (player == null)
                return false;

            object token = new();
            bool lentBefore = player.TempProperties.GetProperty<object>(TokenProperty) != null;
            if (player.CanBreathUnderWater && !lentBefore)
                return false; // a potion or spell of their own: not ours to take back

            player.TempProperties.SetProperty(TokenProperty, token);
            player.CanBreathUnderWater = true;
            return !lentBefore;
        }

        /// <summary>Starts the grace period for every player this fight lent breath to.</summary>
        public static void ReleaseLater(IEnumerable<GamePlayer> players, string what)
        {
            foreach (GamePlayer player in players)
            {
                object token = player?.TempProperties.GetProperty<object>(TokenProperty);
                if (token == null)
                    continue;

                if (player.Out != null)
                    player.Out.SendMessage($"{what} is over. You can breathe underwater for {GraceMs / 60_000} more minutes: swim up before then.",
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);

                _ = new ECSGameTimer(player, _ =>
                {
                    if (!ReferenceEquals(player.TempProperties.GetProperty<object>(TokenProperty), token))
                        return 0; // lent again by a newer fight

                    player.TempProperties.RemoveProperty(TokenProperty);
                    player.CanBreathUnderWater = false;
                    player.Out?.SendMessage("The sea's gift of breath leaves you.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    return 0;
                }, GraceMs);
            }
        }

        /// <summary>Pure rule: a fight lends breath when its definition says underwater or its site is below the water.</summary>
        public static bool FightIsUnderwater(bool definitionSaysUnderwater, bool siteBelowWater) =>
            definitionSaysUnderwater || siteBelowWater;
    }
}
