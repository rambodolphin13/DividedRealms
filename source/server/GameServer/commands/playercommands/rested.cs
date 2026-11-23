using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>/rested (live 1.94, DR-196): how much rest experience the character has gathered.</summary>
    [CmdAttribute(
        "&rested",
        ePrivLevel.Player,
        "Shows how rested your character is (rest experience)",
        "/rested")]
    public class RestedCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null || IsSpammingCommand(player, "rested"))
                return;
            if (!ServerProperties.Properties.ENABLE_REST_EXPERIENCE)
            {
                player.Out.SendMessage("Rest experience is turned off on this server.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            long pool = RestExperience.Pool(player);
            string text = RestExperience.RestedText(pool, player.ExperienceForNextLevel - player.Experience);
            if (pool > 0)
                text += $" ({pool:N0} rest experience)";
            player.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
