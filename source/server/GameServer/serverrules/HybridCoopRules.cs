using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;

namespace DOL.GS.ServerRules
{
    /// <summary>Opt-in co-op world with explicitly bounded guild warfare.</summary>
    public class HybridCoopRules : AbstractServerRules
    {
        private readonly NormalServerRules _normal = new();
        public static bool Enabled => Properties.HYBRID_COOP_PVP;

        // The old frontier zones share their regions with ordinary home lands.
        public static bool IsWarLocation(ushort region, ushort zone) => region switch
        {
            1 => zone is 11 or 12 or 14 or 15,
            100 => zone is 111 or 112 or 113 or 115,
            200 => zone is 210 or 211 or 212 or 214,
            163 or 165 or 233 or 244 or 249 or 250 or 251 or 252 or 253 => true,
            >= 234 and <= 242 => true,
            _ => false
        };
        public static bool IsWarLocation(GameLiving living) => living?.CurrentRegion != null &&
            IsWarLocation(living.CurrentRegionID, living.CurrentZone?.ID ?? ushort.MaxValue);
        public static bool IsWarKeep(AbstractGameKeep keep) => keep != null && !keep.IsPortalKeep &&
            IsWarLocation(keep.Region, keep.CurrentRegion?.GetZone(keep.X, keep.Y)?.ID ?? ushort.MaxValue);

        public static GameLiving Controller(GameLiving living)
        {
            // Bound the walk so malformed ownership cannot hang an attack tick.
            for (int depth = 0; living != null && depth < 16; depth++)
            {
                GameLiving next = living switch
                {
                    GameBot { Owner: { } owner } => owner,
                    GameSiegeWeapon { Owner: { } owner } => owner,
                    GameNPC { Brain: IControlledBrain brain } => brain.Owner,
                    _ => null
                };
                if (next == null || ReferenceEquals(next, living)) return living;
                living = next;
            }
            return null;
        }
        public static bool IsCombatant(GameLiving living) => living is GamePlayer or IGamePlayer;
        public static Guild GuildOf(GameLiving living) => living switch
        {
            GamePlayer player => player.Guild,
            IGamePlayer bot => bot.Guild,
            _ => null
        };
        public static bool AlliedGuilds(Guild a, Guild b) => a != null && b != null &&
            (ReferenceEquals(a, b) || !string.IsNullOrEmpty(a.GuildID) && a.GuildID == b.GuildID ||
             a.alliance != null && a.alliance.Guilds.Contains(b) ||
             b.alliance != null && b.alliance.Guilds.Contains(a));
        public static bool Friends(GameLiving a, GameLiving b)
        {
            if (a == b) return true;
            if (a?.Group != null && a.Group == b?.Group) return true;
            if (AlliedGuilds(GuildOf(a), GuildOf(b))) return true;
            if (a is GamePlayer pa && b is GamePlayer pb)
            {
                var group = pa.TempProperties?.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);
                if (group?.IsInTheBattleGroup(pb) == true) return true;
            }
            return false;
        }
        public static AbstractGameKeep KeepOf(GameLiving living) => living switch
        {
            GameKeepGuard guard => guard.Component?.Keep,
            GameKeepDoor door => door.Component?.Keep,
            GameKeepComponent component => component.Keep,
            _ => null
        };
        public static bool KeepEnemy(AbstractGameKeep keep, GameLiving living, bool includeGroup = true)
        {
            if (keep == null || keep.IsPortalKeep) return false;
            if (keep.Guild == null) return true;
            if (AlliedGuilds(keep.Guild, GuildOf(living))) return false;
            if (includeGroup && living?.Group != null)
                foreach (GameLiving member in living.Group.GetMembersInTheGroup())
                    if (AlliedGuilds(keep.Guild, GuildOf(Controller(member)))) return false;
            return true;
        }

        protected virtual bool ProtectedArea(GameLiving living) => living?.CurrentRegion != null &&
            living.CurrentRegion.GetAreasOfSpot(living).Any(area =>
                area is AbstractArea { IsSafeArea: true } || area is KeepArea { Keep.IsPortalKeep: true });
        protected virtual bool GeneralAttack(GameLiving attacker, GameLiving defender, bool quiet) =>
            base.IsAllowedToAttack(attacker, defender, quiet);

        public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet)
        {
            if (!GeneralAttack(attacker, defender, quiet) || BotPvpCrowdControl.Protected(attacker, defender)) return false;
            GameLiving a = Controller(attacker), b = Controller(defender);
            if (a == null || b == null || a == b) return false;
            if (a is GameBot { IsOnStableMasterRoute: true } || b is GameBot { IsOnStableMasterRoute: true }) return false;
            if (IsCombatant(a) && IsCombatant(b))
            {
                if (a is GamePlayer duelist && duelist.IsDuelPartner(b)) return true;
                // Check the owners AND the actual attacking/receiving bodies.
                // A pet or ranged pulse cannot fight from across a safe boundary.
                if (attacker.CurrentRegion != defender.CurrentRegion || a.CurrentRegion != b.CurrentRegion ||
                    a.CurrentRegion != attacker.CurrentRegion ||
                    !IsWarLocation(attacker) || !IsWarLocation(defender) || !IsWarLocation(a) || !IsWarLocation(b) ||
                    ProtectedArea(attacker) || ProtectedArea(defender) || ProtectedArea(a) || ProtectedArea(b)) return false;
                return !Friends(a, b);
            }
            AbstractGameKeep ka = KeepOf(attacker), kb = KeepOf(defender);
            if (ka != null && IsCombatant(b)) return IsWarKeep(ka) && KeepEnemy(ka, b);
            if (kb != null && IsCombatant(a)) return IsWarKeep(kb) && KeepEnemy(kb, a);
            // Realm NPCs remain friendly throughout the co-op world. Neutral
            // monsters keep the ordinary faction/confusion/PvE legality path.
            if (IsCombatant(a) && b.Realm != eRealm.None || IsCombatant(b) && a.Realm != eRealm.None) return false;
            return _normal.IsAllowedToAttack(attacker, defender, quiet);
        }

        public override bool IsSameRealm(GameLiving source, GameLiving target, bool quiet)
        {
            if (source == null || target == null) return false;
            GameLiving a = Controller(source), b = Controller(target);
            if (a == null || b == null) return false;
            if (a == b) return true;
            if (IsCombatant(a) && IsCombatant(b))
                return Friends(a, b) || !IsWarLocation(source) && !IsWarLocation(target) &&
                    !IsWarLocation(a) && !IsWarLocation(b);
            AbstractGameKeep ka = KeepOf(source), kb = KeepOf(target);
            if (ka != null && IsCombatant(b)) return !KeepEnemy(ka, b);
            if (kb != null && IsCombatant(a)) return !KeepEnemy(kb, a);
            if (IsCombatant(a) && b.Realm != eRealm.None || IsCombatant(b) && a.Realm != eRealm.None) return true;
            return _normal.IsSameRealm(source, target, quiet);
        }

        public static bool RequiresDamageCheck(GameLiving attacker, GameLiving defender) => Enabled &&
            (IsCombatant(Controller(attacker)) && IsCombatant(Controller(defender)) ||
             KeepOf(attacker) != null || KeepOf(defender) != null);

        public static bool RewardEnemies(GameLiving a, GameLiving b)
        {
            var ownerA = Controller(a); var ownerB = Controller(b);
            return ownerA != null && ownerB != null && IsCombatant(ownerA) && IsCombatant(ownerB) &&
                IsWarLocation(a) && IsWarLocation(b) && !Friends(ownerA, ownerB);
        }
        public override void OnPlayerTeleport(GamePlayer player, GameLocation source, DOL.Database.DbTeleport destination)
        {
            if (source.RegionID == destination.RegionID)
                StartImmunityTimer(player, Properties.TIMER_PVP_TELEPORT * 1000);
        }
        public override void OnReleased(DOL.Events.DOLEvent e, object sender, EventArgs args)
        {
            var player = (GamePlayer)sender;
            StartImmunityTimer(player, (player.LastDeathPvP ? Properties.TIMER_KILLED_BY_PLAYER : Properties.TIMER_KILLED_BY_MOB) * 1000);
        }

        public override string RulesDescription() => "Co-op world / guild PvP in DF, battlegrounds and frontiers";
        public override bool IsAllowedCharsInAllRealms(GameClient client) => true;
        public override bool IsAllowedToGroup(GamePlayer a, GamePlayer b, bool quiet) => true;
        public override bool IsAllowedToJoinGuild(GamePlayer player, Guild guild) => true;
        public override bool IsAllowedToTrade(GameLiving a, GameLiving b, bool quiet) => true;
        public override bool IsAllowedToUnderstand(GameLiving a, GamePlayer b) => true;
        public override byte GetColorHandling(GameClient client) => 2;
        public override byte GetLivingRealm(GamePlayer player, GameLiving target) => IsSameRealm(player, target, true)
            ? (byte)player.Realm : IsCombatant(Controller(target)) ? (byte)((int)player.Realm % 3 + 1) : (byte)target.Realm;
        public override bool IsAllowedToClaim(GamePlayer player, Region region) =>
            base.IsAllowedToClaim(player, region) && IsWarLocation(player) && !ProtectedArea(player);
        public override void ResetKeep(GuardLord lord, GameObject killer)
        {
            base.ResetKeep(lord, killer);
            CaptureKeep(lord.Component?.Keep, killer as GameLiving);
        }
        public static void CaptureKeep(AbstractGameKeep keep, GameLiving killer)
        {
            if (!IsWarKeep(keep)) return;
            var winner = Controller(killer);
            if (!IsCombatant(winner)) return;
            keep.Reset(winner.Realm);
            // Store guild ownership using the native claim/persistence path.
            if (winner is GamePlayer player && player.Guild != null &&
                player.Guild.ClaimedKeeps.Count < Properties.GUILDS_CLAIM_LIMIT)
                keep.Claim(player);
        }
    }
}
