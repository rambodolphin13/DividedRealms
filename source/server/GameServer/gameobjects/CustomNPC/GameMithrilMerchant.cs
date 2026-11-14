using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Live's capital Mithril, Pattern and Dye merchants: item Price is a Mithril amount, paid from the account's Mithril
    /// balance (<see cref="MithrilBalance"/>). Store items can't be sold back for Mithril.
    /// </summary>
    public class GameMithrilMerchant : GameMerchant
    {
        public override void SendMerchantWindow(GamePlayer player)
        {
            player.Out.SendMerchantWindow(m_tradeItems, eMerchantWindowType.Count);
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            player.Out.SendMessage($"You have {MithrilBalance.Get(player)} Mithril. Prices here are in Mithril.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return true;
        }

        public override void OnPlayerBuy(GamePlayer player, int item_slot, int number)
        {
            int pagenumber = item_slot / MerchantTradeItems.MAX_ITEM_IN_TRADEWINDOWS;
            int slotnumber = item_slot % MerchantTradeItems.MAX_ITEM_IN_TRADEWINDOWS;
            DbItemTemplate template = TradeItems?.GetItem(pagenumber, (eMerchantWindowSlot) slotnumber);

            if (template == null || number <= 0)
                return;

            if (!IsWithinRadius(player, ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
            {
                player.Out.SendMessage($"You are too far away from {GetName(0, false)}.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
                return;
            }

            int amountToBuy = template.PackSize > 0 ? number * template.PackSize : number;
            long cost = (long) number * template.Price;

            lock (player.Inventory.Lock)
            {
                if (MithrilBalance.Get(player) < cost)
                {
                    player.Out.SendMessage($"You need {cost} Mithril for that; you have {MithrilBalance.Get(player)}.",
                        eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }

                if (!player.Inventory.AddTemplate(GameInventoryItem.Create(template), amountToBuy, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                {
                    player.Out.SendMessage("You don't have enough room in your backpack.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }

                MithrilBalance.TrySpend(player, cost);
                InventoryLogging.LogInventoryAction(this, player, eInventoryActionType.Merchant, template, amountToBuy);
                player.Out.SendMessage($"You buy {template.GetName(1, false)} for {cost} Mithril.", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
            }
        }

        public override void OnPlayerSell(GamePlayer player, DbInventoryItem item)
        {
            player.Out.SendMessage($"{Name} doesn't buy items.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
