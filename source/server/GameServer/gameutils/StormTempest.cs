using System;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Storm Sovereign Tempest charges, shown to everyone nearby as the classic Warlock chamber orbs over the head
    /// (VisualEffect sub-code 3, five slots: one lit orb per charge). Nothing in the client is changed.
    /// </summary>
    public static class StormTempest
    {
        public const int MaxCharges = 5;
        public const int DecayMs = 30000;
        public const int RefreshMs = 2000;
        public const int CycleMs = 5000;
        private const string StateKey = "StormTempest.State";

        // The chamber colour codes the stock Warlock packet used (primary only, then one per secondary spell type).
        public static readonly byte[] StockCodes = { 0x03, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 };

        // Lit-orb colour. 0x66 (the old direct-damage chamber) until the owner picks one in game with /tempest cycle.
        public static byte OrbCode = 0x66;

        private sealed class State
        {
            public int Charges;
            public long LastGain;
            public byte? Preview;
            public int CycleIndex = -1;
            public long NextCycle;
            public ECSGameTimer Timer;
        }

        public static int Charges(GamePlayer player) => Get(player)?.Charges ?? 0;

        /// <summary>Adds charges (capped at five) and restarts the 30-second decay.</summary>
        public static void Add(GamePlayer player, int amount = 1)
        {
            if (player == null || amount <= 0)
                return;
            var state = GetOrCreate(player);
            state.Charges = Math.Min(MaxCharges, state.Charges + amount);
            state.LastGain = GameLoop.GameLoopTime;
            Refresh(player, state);
        }

        /// <summary>Spends every charge and returns how many there were.</summary>
        public static int Spend(GamePlayer player)
        {
            var state = Get(player);
            if (state == null || state.Charges == 0)
                return 0;
            int spent = state.Charges;
            state.Charges = 0;
            Refresh(player, state);
            return spent;
        }

        public static void Set(GamePlayer player, int charges)
        {
            if (player == null)
                return;
            var state = GetOrCreate(player);
            state.Charges = Math.Clamp(charges, 0, MaxCharges);
            state.LastGain = GameLoop.GameLoopTime;
            Refresh(player, state);
        }

        /// <summary>Lights all five orbs in one colour code until cleared (GM colour test).</summary>
        public static void Preview(GamePlayer player, byte code)
        {
            var state = GetOrCreate(player);
            state.CycleIndex = -1;
            state.Preview = code;
            Refresh(player, state);
        }

        /// <summary>Shows each stock colour code for five seconds in turn, naming it in chat.</summary>
        public static void Cycle(GamePlayer player)
        {
            var state = GetOrCreate(player);
            state.CycleIndex = 0;
            ShowCycleStep(player, state);
            Refresh(player, state);
        }

        public static void Clear(GamePlayer player)
        {
            var state = Get(player);
            if (state == null)
                return;
            state.Charges = 0;
            state.Preview = null;
            state.CycleIndex = -1;
            Refresh(player, state);
        }

        /// <summary>The five slot bytes: lit orbs first, the rest empty.</summary>
        public static byte[] Slots(int charges, byte code)
        {
            var slots = new byte[MaxCharges];
            for (int i = 0; i < Math.Clamp(charges, 0, MaxCharges); i++)
                slots[i] = code;
            return slots;
        }

        public static bool TryParseCode(string text, out byte code)
        {
            code = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out code);
            return byte.TryParse(text, out code);
        }

        private static State Get(GamePlayer player) => player?.TempProperties.GetProperty<State>(StateKey);

        private static State GetOrCreate(GamePlayer player)
        {
            var state = Get(player);
            if (state == null)
            {
                state = new State();
                player.TempProperties.SetProperty(StateKey, state);
            }
            return state;
        }

        private static void ShowCycleStep(GamePlayer player, State state)
        {
            byte code = StockCodes[state.CycleIndex];
            state.Preview = code;
            state.NextCycle = GameLoop.GameLoopTime + CycleMs;
            player.Out.SendMessage($"Tempest orb colour {state.CycleIndex + 1}/{StockCodes.Length}: 0x{code:X2}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void Refresh(GamePlayer player, State state)
        {
            Send(player, state);
            bool showing = state.Charges > 0 || state.Preview != null;
            if (showing && state.Timer?.IsAlive != true)
                state.Timer = new ECSGameTimer(player, _ => Tick(player, state), RefreshMs);
        }

        private static int Tick(GamePlayer player, State state)
        {
            if (player.ObjectState != GameObject.eObjectState.Active || player.Client?.ClientState != GameClient.eClientState.Playing)
            {
                state.Charges = 0;
                state.Preview = null;
                state.CycleIndex = -1;
                return 0;
            }
            if (state.CycleIndex >= 0 && GameLoop.GameLoopTime >= state.NextCycle)
            {
                if (++state.CycleIndex >= StockCodes.Length)
                {
                    state.CycleIndex = -1;
                    state.Preview = null;
                    player.Out.SendMessage("Tempest colour cycle finished. /tempest show <code> to look at one again.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
                else
                    ShowCycleStep(player, state);
            }
            if (state.Preview == null && state.Charges > 0 && GameLoop.GameLoopTime - state.LastGain >= DecayMs)
                state.Charges = 0;
            // Re-send while lit: a player who walks into view needs the orbs too (Warlocks re-sent on every move).
            Send(player, state);
            return state.Charges > 0 || state.Preview != null ? RefreshMs : 0;
        }

        private static void Send(GamePlayer player, State state)
        {
            byte[] slots = state.Preview is byte code ? Slots(MaxCharges, code) : Slots(state.Charges, OrbCode);
            foreach (GamePlayer viewer in player.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                (viewer.Out as PacketLib168)?.SendChamberOrbs(player, slots);
        }
    }
}
