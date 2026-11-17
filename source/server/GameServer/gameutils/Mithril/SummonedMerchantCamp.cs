using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The Merchant Summoning Gem (mithril_merchant_gem, owner 2026-11-17, DR-291): puts a merchant that buys loot and sells
    /// arrows and throwing weapons (live's Ancient Transmuter list), a smith and a recharger beside the player for two minutes,
    /// like live's Sojourner Ancient Transmuter. They stand still, take no damage and vanish when the time is up. The gem is kept.
    /// </summary>
    public static class SummonedMerchantCamp
    {
        public const int DurationMs = 120_000;
        public const int ReuseMs = 300_000;
        public const string MerchantList = "ML_transmuteritems";
        private const string LastUseProperty = "DR_MERCHANT_GEM_LAST_USE";

        /// <summary>Why the gem can't be used now, or null when it can.</summary>
        public static string Refusal(GamePlayer player, long now)
        {
            if (player == null || player.CurrentRegion == null)
                return "You can't do that here.";

            if (!player.IsAlive)
                return "You can't do that while dead.";

            if (player.InCombat)
                return "You can't summon merchants while in combat.";

            if (player.CurrentHouse != null || player.InHouse)
                return "You can't summon merchants inside a house.";

            long last = player.TempProperties.GetProperty<long>(LastUseProperty, 0);
            long wait = ReuseWaitMs(last, now);
            if (wait > 0)
                return $"The gem is still dim. You can use it again in {Math.Max(1, (wait + 59_999) / 60_000)} minute(s).";

            return null;
        }

        /// <summary>Milliseconds until the gem can be used again (0 when ready).</summary>
        public static long ReuseWaitMs(long lastUse, long now)
        {
            return lastUse <= 0 ? 0 : Math.Max(0, lastUse + ReuseMs - now);
        }

        /// <summary>Summons the camp. Returns the message for the player.</summary>
        public static string Summon(GamePlayer player)
        {
            long now = GameLoop.GameLoopTime;
            string refusal = Refusal(player, now);
            if (refusal != null)
                return refusal;

            var npcs = new List<GameNPC>
            {
                Place(new GameMerchant { TradeItems = new MerchantTradeItems(MerchantList) }, player, "Ancient Transmuter", 993, -1),
                Place(new Blacksmith(), player, "Wandering Smith", SmithModel(player.Realm), 0),
                Place(new Recharger(), player, "Wandering Recharger", RechargerModel(player.Realm), 1),
            };

            foreach (GameNPC npc in npcs)
                npc.AddToWorld();

            player.TempProperties.SetProperty(LastUseProperty, now);

            _ = new ECSGameTimer(player, _ =>
            {
                foreach (GameNPC npc in npcs)
                {
                    if (npc.ObjectState == GameObject.eObjectState.Active)
                        npc.Delete();
                }

                return 0;
            }, DurationMs);

            return "A merchant, a smith and a recharger answer the gem's call. They will stay for two minutes.";
        }

        private static GameNPC Place(GameNPC npc, GamePlayer player, string name, ushort model, int side)
        {
            // In front of the player in a short row (left, middle, right), all facing the player.
            Point2D front = player.GetPointFromHeading(player.Heading, 90);
            Point2D spot = side == 0 ? front : player.GetPointFromHeading((ushort) ((player.Heading + (side < 0 ? 512 : 3584)) % 4096), 110);

            npc.X = spot.X;
            npc.Y = spot.Y;
            npc.Z = player.Z;
            npc.CurrentRegion = player.CurrentRegion;
            npc.Heading = (ushort) ((player.Heading + 2048) % 4096);
            npc.Name = name;
            npc.GuildName = "Summoned";
            npc.Model = model;
            npc.Size = 50;
            npc.Level = 50;
            npc.Realm = player.Realm;
            npc.MaxSpeedBase = 0;
            npc.Flags |= GameNPC.eFlags.PEACE;
            return npc;
        }

        public static ushort SmithModel(eRealm realm) => realm switch
        {
            eRealm.Midgard => 213,
            eRealm.Hibernia => 350,
            _ => 10,
        };

        public static ushort RechargerModel(eRealm realm) => realm switch
        {
            eRealm.Midgard => 231,
            eRealm.Hibernia => 389,
            _ => 763,
        };
    }
}
