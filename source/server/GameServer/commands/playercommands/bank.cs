using System.Collections.Generic;
using System.Linq;
using DOL.GS.Housing;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /bank opens the player's vaults from anywhere (owner 2026-11-20, DR-487): the personal vault, both account vault
    /// pages, the vaults of the player's own house and of the guild house. Not in combat; house and guild vault
    /// permissions still apply.
    /// </summary>
    [CmdAttribute(
        "&bank",
        ePrivLevel.Player,
        "Open your vaults from anywhere",
        "/bank - your personal vault",
        "/bank account [1|2] - your account vault",
        "/bank house [1-4] - a vault in your house",
        "/bank guild [1-4] - a vault in your guild house")]
    public class BankCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (player == null)
                return;

            string refusal = Refusal(player.IsAlive, player.InCombat);
            if (refusal != null)
            {
                Tell(player, refusal);
                return;
            }

            string which = args.Length > 1 ? args[1].ToLowerInvariant() : "personal";
            int page = args.Length > 2 && int.TryParse(args[2], out int n) ? n : 1;

            switch (which)
            {
                case "personal":
                case "vault":
                    OpenPersonal(player);
                    break;
                case "account":
                    OpenAccount(player, page);
                    break;
                case "house":
                    OpenHouse(player, HouseMgr.GetHouseByPlayer(player), page, "You don't own a house.");
                    break;
                case "guild":
                    OpenHouse(player, HouseMgr.GetGuildHouseByPlayer(player), page, "Your guild doesn't own a house.");
                    break;
                default:
                    DisplaySyntax(client);
                    break;
            }
        }

        /// <summary>Why /bank can't be used now, or null when it can.</summary>
        public static string Refusal(bool alive, bool inCombat)
        {
            if (!alive)
                return "You can't reach your vaults while dead.";

            if (inCombat)
                return "You can't reach your vaults while in combat.";

            return null;
        }

        private static void OpenPersonal(GamePlayer player)
        {
            player.ActiveInventoryObject?.RemoveObserver(player);
            player.ActiveInventoryObject = null;
            var items = player.Inventory.GetItemRange(eInventorySlot.FirstVault, eInventorySlot.LastVault);
            player.Out.SendInventoryItemsUpdate(eInventoryWindowType.PlayerVault, items.Count > 0 ? items : null);
        }

        private static void OpenAccount(GamePlayer player, int page)
        {
            if (player.HCFlag)
            {
                Tell(player, "Hardcore characters have no account vault.");
                return;
            }

            if (page is < 1 or > 2)
            {
                Tell(player, "Your account vault has pages 1 and 2.");
                return;
            }

            AccountVault vault = page == 1 && player.AccountVault != null
                ? player.AccountVault
                : new AccountVault(player, page - 1, AccountVaultKeeper.GetDummyVaultItem(player));

            vault.OpenRemotely(player);
        }

        private static void OpenHouse(GamePlayer player, House house, int number, string noHouse)
        {
            if (house == null)
            {
                Tell(player, noHouse);
                return;
            }

            List<GameHouseVault> vaults = house.HousepointItems.Values
                .Select(item => item.GameObject as GameHouseVault)
                .Where(vault => vault != null)
                .OrderBy(vault => vault.Index)
                .ToList();

            if (vaults.Count == 0)
            {
                Tell(player, "That house has no vaults.");
                return;
            }

            GameHouseVault chosen = vaults.FirstOrDefault(vault => vault.Index + 1 == number);
            if (chosen == null)
            {
                Tell(player, $"That house has vault(s) {string.Join(", ", vaults.Select(vault => vault.Index + 1))}.");
                return;
            }

            if (!chosen.OpenRemotely(player))
                Tell(player, "You don't have permission to view this vault!");
        }

        private static void Tell(GamePlayer player, string message)
        {
            player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
