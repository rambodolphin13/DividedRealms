using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute("&mithril", ePrivLevel.Player, "Shows your Mithril balance and uses Mithril store change tokens",
        "/mithril", "/mithril rename <name>", "/mithril gender", "/mithril race <race>")]
    public class MithrilCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;

            if (player == null)
                return;

            string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            string message = sub switch
            {
                "rename" or "name" => MithrilCharacterChanges.Rename(player, args.Length > 2 ? args[2] : null),
                "gender" => MithrilCharacterChanges.ChangeGender(player),
                "race" => MithrilCharacterChanges.ChangeRace(player, args.Length > 2 ? string.Join("", args[2..]) : null),
                _ => $"Your account has {MithrilBalance.Get(player)} Mithril. Spend it at the Mithril, Pattern and Dye merchants in your capital city.",
            };

            player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            player.Out.SendUpdateMoney();
        }
    }
}
