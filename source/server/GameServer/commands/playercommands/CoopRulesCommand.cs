using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;

namespace DOL.GS.Commands
{
    [CmdAttribute("&coop", ePrivLevel.Player, "Show co-op and guild warfare rules for your location", "/coop")]
    public class CoopRulesCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null) return;
            var player = client.Player;
            if (!HybridCoopRules.Enabled)
            {
                player.Out.SendMessage("Hybrid co-op mode is not enabled on this server.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            string location = HybridCoopRules.IsWarLocation(player)
                ? "Guild warfare area: unrelated players/guilds may fight. Safe areas and portal keeps remain protected."
                : "Co-op area: players and their pets cannot attack one another.";
            player.Out.SendMessage(location, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            player.Out.SendMessage("Groupmates, guildmates and allied guilds are friendly. Cross-realm groups, guilds and trade are allowed. Darkness Falls is open to every realm.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
