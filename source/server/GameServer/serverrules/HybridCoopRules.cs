using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.ServerRules
{
    /// <summary>
    /// Opt-in co-op world (owner 2026-10-27, war zones redrawn 2026-11-15): realms cooperate everywhere except the
    /// war zones in coop_war_zones (New Frontiers, battlegrounds, Darkness Falls and the NF dungeons by default),
    /// where they fight realm against realm as normal. A group stays one team everywhere, so a mixed-realm group
    /// that walks into a war zone is not split.
    /// </summary>
    public class HybridCoopRules : AbstractServerRules
    {
        private readonly NormalServerRules _normal = new();
        public static bool Enabled => Properties.HYBRID_COOP_PVP;

        /// <summary>
        /// Owner 2026-11-15: Old Frontiers, the home and expansion lands, dungeons and the Summoner's Hall raid copy
        /// (248) are co-op. Realm war is New Frontiers 163, the battlegrounds (165, 234-242, 250-253), stock
        /// Summoner's Hall 233, Passage of Conflict 244, Labyrinth 245 and Darkness Falls 249.
        /// The server setting coop_war_zones overrides this; the Old Frontiers zones of 2026-10-27 were
        /// 1:11,1:12,1:14,1:15,100:111,100:112,100:113,100:115,200:210,200:211,200:212,200:214.
        /// </summary>
        public const string DefaultWarZones = "163,165,233-242,244,245,249-253";

        private sealed record WarZoneList(string Spec, HashSet<ushort> Regions, HashSet<(ushort Region, ushort Zone)> Zones);
        private static WarZoneList _warZones;

        private static WarZoneList WarZones
        {
            get
            {
                string spec = Properties.COOP_WAR_ZONES ?? string.Empty;
                WarZoneList list = _warZones;
                if (list == null || list.Spec != spec)
                    _warZones = list = ParseWarZones(spec);
                return list;
            }
        }

        private static WarZoneList ParseWarZones(string spec)
        {
            var regions = new HashSet<ushort>();
            var zones = new HashSet<(ushort, ushort)>();
            foreach (string raw in spec.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = raw.Split(':');
                if (parts.Length == 1 && TryRange(parts[0], out ushort from, out ushort to))
                    for (int id = from; id <= to; id++) regions.Add((ushort)id);
                else if (parts.Length == 2 && ushort.TryParse(parts[0], out ushort region) &&
                         TryRange(parts[1], out ushort zoneFrom, out ushort zoneTo))
                    for (int id = zoneFrom; id <= zoneTo; id++) zones.Add((region, (ushort)id));
            }
            return new WarZoneList(spec, regions, zones);
        }

        private static bool TryRange(string text, out ushort from, out ushort to)
        {
            string[] ends = text.Split('-');
            from = to = 0;
            if (ends.Length == 1)
            {
                bool ok = ushort.TryParse(ends[0], out from);
                to = from;
                return ok;
            }
            return ends.Length == 2 && ushort.TryParse(ends[0], out from) && ushort.TryParse(ends[1], out to) && from <= to;
        }

        public static bool IsWarLocation(ushort region, ushort zone)
        {
            WarZoneList list = WarZones;
            return list.Regions.Contains(region) || list.Zones.Contains((region, zone));
        }

        /// <summary>Battlegrounds: realm war with their own level brackets, not the open frontier.</summary>
        public static bool IsBattlegroundRegion(ushort region) => region is 165 or >= 234 and <= 242 or >= 250 and <= 253;

        /// <summary>
        /// The open-frontier war (keeps, relics, roaming RvR) the gamebots travel to: the war zones minus the
        /// battlegrounds and Darkness Falls, which have their own bot handling.
        /// </summary>
        public static bool IsFrontierWarZone(ushort region, ushort zone) =>
            region != 249 && !IsBattlegroundRegion(region) && IsWarLocation(region, zone);
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
        public static bool Friends(GameLiving a, GameLiving b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Realm != eRealm.None && a.Realm == b.Realm) return true;
            if (a.Group != null && a.Group == b.Group) return true;
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
            GameLiving owner = Controller(living);
            if (owner == null || owner.Realm == keep.Realm) return false;
            // A guest of another realm grouped with one of the keep's realm is that group's teammate.
            if (includeGroup && owner.Group != null)
                foreach (GameLiving member in owner.Group.GetMembersInTheGroup())
                    if (Controller(member)?.Realm == keep.Realm) return false;
            return true;
        }

        /// <summary>Realms group freely in the co-op lands; in a war area only within a realm, as normal.</summary>
        public static bool CanGroupAcrossRealms(GameLiving a, GameLiving b) =>
            !IsWarLocation(a) && !IsWarLocation(b);

        /// <summary>The realm test for a gamebot joining a player's group, under whichever rules are running.</summary>
        public static bool CanGroup(GameLiving a, GameLiving b) => a != null && b != null &&
            (a.Realm == b.Realm || Enabled && CanGroupAcrossRealms(a, b));

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

        public override string RulesDescription() => "Co-op world / realm vs realm in New Frontiers, DF and battlegrounds";
        public override bool IsAllowedCharsInAllRealms(GameClient client) => true;
        public override bool IsAllowedToGroup(GamePlayer a, GamePlayer b, bool quiet)
        {
            if (a == null || b == null || a.Realm == b.Realm || CanGroupAcrossRealms(a, b)) return true;
            if (!quiet) a.Out.SendMessage("You can't group with another realm in a realm war area.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return false;
        }
        public override bool IsAllowedToJoinGuild(GamePlayer player, Guild guild) => true;
        public override bool IsAllowedToTrade(GameLiving a, GameLiving b, bool quiet) => true;
        public override bool IsAllowedToUnderstand(GameLiving a, GamePlayer b) => true;
        public override byte GetColorHandling(GameClient client) => 2;
        public override byte GetLivingRealm(GamePlayer player, GameLiving target) => IsSameRealm(player, target, true)
            ? (byte)player.Realm : IsCombatant(Controller(target)) && target.Realm == player.Realm
                ? (byte)((int)player.Realm % 3 + 1) : (byte)target.Realm;
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

        /// <summary>
        /// The client keeps a living's realm, name and title from when it was created, and the old frontier zones
        /// share their regions with the home lands, so crossing between them changes nothing on screen by itself.
        /// When a player, gamebot or pet crosses that border, rebuild the nameplates that depend on it.
        /// </summary>
        public static void OnZoneCrossed(GameObject obj, Zone from, Zone to)
        {
            if (!Enabled || obj is not GameLiving living || from == null || to == null || from.ZoneRegion != to.ZoneRegion ||
                IsWarLocation(from.ZoneRegion.ID, from.ID) == IsWarLocation(to.ZoneRegion.ID, to.ID))
                return;
            GameLiving owner = Controller(living);
            if (!IsCombatant(owner)) return;
            foreach (GamePlayer viewer in living.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                Recreate(viewer, living);
            if (living is not GamePlayer player) return;
            foreach (GamePlayer other in player.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                Recreate(player, other);
            foreach (GameNPC npc in player.GetNPCsInRadius(WorldMgr.VISIBILITY_DISTANCE))
                if (IsCombatant(Controller(npc)))
                    Recreate(player, npc);
        }

        private static void Recreate(GamePlayer viewer, GameLiving target)
        {
            // Teammates look the same on both sides of the border.
            if (viewer == target || viewer.Client?.ClientState != GameClient.eClientState.Playing ||
                Friends(viewer, Controller(target)) || !target.IsVisibleTo(viewer))
                return;
            viewer.Out.SendObjectRemove(target);
            if (target is GamePlayer player)
            {
                viewer.Out.SendPlayerCreate(player);
                viewer.Out.SendLivingEquipmentUpdate(player);
            }
            else
                ClientService.CreateObjectForPlayer(viewer, target);
        }
    }
}
