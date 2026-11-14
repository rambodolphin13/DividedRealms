using System;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The Mithril store balance (live: real-money store currency, here earned in game). It is account-wide like live and is
    /// shown in the Mithril field of the inventory money row in every zone (owner 2026-11-14), not only in capital cities.
    /// </summary>
    public static class MithrilBalance
    {
        /// <summary>Largest balance the client's 16-bit money field can show.</summary>
        public const int DisplayMax = ushort.MaxValue;

        public static long Get(GamePlayer player)
        {
            return player?.Client?.Account?.Mithril ?? 0;
        }

        /// <summary>Adds Mithril to the player's account, saves it and refreshes the money row. Returns the amount added.</summary>
        public static long Add(GamePlayer player, long amount, string reason = null)
        {
            DbAccount account = player?.Client?.Account;

            if (account == null || amount <= 0)
                return 0;

            account.Mithril = Math.Min(long.MaxValue / 2, account.Mithril + amount);
            Save(account);
            player.Out.SendUpdateMoney();
            player.Out.SendMessage(reason == null ? $"You gain {amount} Mithril." : $"You gain {amount} Mithril ({reason}).",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            return amount;
        }

        /// <summary>Takes Mithril if the account has enough. Returns false and changes nothing otherwise.</summary>
        public static bool TrySpend(GamePlayer player, long amount)
        {
            DbAccount account = player?.Client?.Account;

            if (account == null || amount < 0 || account.Mithril < amount)
                return false;

            account.Mithril -= amount;
            Save(account);
            player.Out.SendUpdateMoney();
            return true;
        }

        /// <summary>The value shown in the money row's Mithril field.</summary>
        public static ushort DisplayValue(long balance)
        {
            return (ushort) Math.Clamp(balance, 0, DisplayMax);
        }

        /// <summary>
        /// The platinum shown in the money row: coin money's own Mithril unit (1 = 1,000 platinum) is folded into platinum so the
        /// Mithril field only ever shows the store balance.
        /// </summary>
        public static ushort DisplayPlatinum(int coinMithril, int platinum)
        {
            return (ushort) Math.Clamp((long) coinMithril * 1000 + platinum, 0, ushort.MaxValue);
        }

        /// <summary>Mithril for killing this monster: bosses a flat amount, other named monsters per 10 levels.</summary>
        public static int ForNpcKill(GameNPC npc)
        {
            if (npc == null || npc.Brain is DOL.AI.Brain.IControlledBrain || npc.Realm != eRealm.None || !LootContext.IsNamedMob(npc))
                return 0;

            if (npc is GameEpicNPC || npc.Level >= 60)
                return Properties.MITHRIL_BOSS_KILL;

            return Math.Max(1, Properties.MITHRIL_NAMED_KILL * npc.Level / 10);
        }

        /// <summary>
        /// Mithril store copies of respec stones (mithril_respec_full, ...) are priced in Mithril but work exactly like the
        /// stones they copy: trainers and the throne see the original id.
        /// </summary>
        public static string BaseItemId(string id)
        {
            const string prefix = "mithril_respec_";

            return id != null && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? id["mithril_".Length..] : id;
        }

        public static int ForQuest(int questLevel)
        {
            return Properties.MITHRIL_QUEST <= 0 ? 0 : Properties.MITHRIL_QUEST + Math.Max(0, questLevel) / 10;
        }

        private static void Save(DbAccount account)
        {
            try
            {
                GameServer.Database?.SaveObject(account);
            }
            catch (Exception)
            {
                // The balance stays on the account object and is saved with the next account save.
            }
        }
    }
}
