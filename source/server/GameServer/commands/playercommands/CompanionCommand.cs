using System;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Clickable popup listing the account's other characters. Uses the same invisible
    /// conversation NPC trick as the /spawn class menu, because the classic client only
    /// turns [bracketed] text into links when an NPC owns the popup.
    /// </summary>
    public sealed class AccountCompanionMenu : GameNPC
    {
        private const string ActiveMenuProperty = "OfflineDAoC.ActiveCompanionMenu";
        private const int MenuLifetimeMilliseconds = 600_000;
        private const ushort InvisibleModel = 150;

        private readonly GamePlayer _owner;
        private ECSGameTimer _expirationTimer;
        private bool _closed;

        private AccountCompanionMenu(GamePlayer owner)
        {
            _owner = owner;
            Name = "Companion Menu";
            Realm = owner.Realm;
            Level = 1;
            Model = InvisibleModel;
            Size = 1;
            Flags = eFlags.PEACE | eFlags.DONTSHOWNAME;
            X = owner.X;
            Y = owner.Y;
            Z = owner.Z;
            Heading = owner.Heading;
            CurrentRegionID = owner.CurrentRegionID;
        }

        public static string BuildMenuText(GamePlayer owner)
        {
            var lines = AccountCompanionManager.AccountCharacters(owner)
                .Where(character => character.Realm == (int)owner.Realm)
                .Select(character =>
                {
                    string className = ScriptMgr.FindCharacterClass(character.Class)?.Name ?? "Unknown";
                    string state = AccountCompanionManager.IsSummoned(character.ObjectId) ? " (with you)" : string.Empty;
                    return $"[{character.Name}] level {character.Level} {className}{state}";
                })
                .ToArray();
            if (lines.Length == 0)
                return "You have no other characters in this realm. Create one on the character screen, then type /companion.";
            return "Click a character to bring it into your group, or click one that is with you to send it home:\n\n" +
                   string.Join('\n', lines);
        }

        internal static bool Open(GamePlayer player)
        {
            if (player?.CurrentRegion == null)
                return false;
            player.TempProperties.GetProperty<AccountCompanionMenu>(ActiveMenuProperty)?.Close();
            var menu = new AccountCompanionMenu(player);
            if (!menu.AddToWorld())
                return false;
            player.TempProperties.SetProperty(ActiveMenuProperty, menu);
            player.TargetObject = menu;
            player.Out.SendChangeTarget(menu);
            menu._expirationTimer = new ECSGameTimer(menu, _ =>
            {
                menu.Close();
                return 0;
            }, MenuLifetimeMilliseconds);
            player.Out.SendMessage(BuildMenuText(player), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!ReferenceEquals(source, _owner) || string.IsNullOrWhiteSpace(text))
                return false;
            CompanionCommandHandler.Toggle(_owner, text);
            if (!_closed && _owner.ObjectState is eObjectState.Active && _owner.CurrentRegion == CurrentRegion)
            {
                _owner.TargetObject = this;
                _owner.Out.SendChangeTarget(this);
                _owner.Out.SendMessage(BuildMenuText(_owner), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            }
            return true;
        }

        private void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _expirationTimer?.Stop();
            if (ReferenceEquals(_owner?.TempProperties.GetProperty<AccountCompanionMenu>(ActiveMenuProperty), this))
                _owner.TempProperties.RemoveProperty(ActiveMenuProperty);
            if (_owner?.ObjectState is eObjectState.Active && ReferenceEquals(_owner.TargetObject, this))
            {
                _owner.TargetObject = null;
                _owner.Out.SendChangeTarget(null);
            }
            Delete();
        }
    }

    [CmdAttribute("&companion", ePrivLevel.Player,
        "Brings characters from your own account into your group; they keep what they earn",
        "/companion - menu of your characters",
        "/companion <name> - summon or send home",
        "/companion <name> <class> - choose a class for a new character",
        "/companion build <name> [build] - choose its training build",
        "/companion list | dismiss <name|all>")]
    public sealed class CompanionCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (player == null)
                return;

            if (args.Length < 2)
            {
                if (!AccountCompanionMenu.Open(player))
                    DisplayMessage(client, "The companion menu could not open here. Use /companion <name>.");
                return;
            }

            string first = args[1].ToLowerInvariant();
            switch (first)
            {
                case "list":
                    List(player);
                    return;
                case "dismiss":
                case "home":
                    Dismiss(player, args.Length > 2 ? string.Join(' ', args.Skip(2)) : "all");
                    return;
                case "build":
                case "style":
                    Build(player, args);
                    return;
            }

            DbCoreCharacter character = AccountCompanionManager.FindCharacter(player, args[1]);
            if (character == null)
            {
                DisplayMessage(client, $"You have no other character named '{args[1]}'. Type /companion list.");
                return;
            }

            if (args.Length > 2)
            {
                string wanted = string.Join(' ', args.Skip(2));
                eCharacterClass chosen = AccountCompanionManager.PromotionChoices(character)
                    .FirstOrDefault(choice => string.Equals(choice.ToString(), wanted.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase));
                AccountCompanionManager.Promote(player, character, chosen, out string promoted);
                DisplayMessage(client, promoted);
                return;
            }

            Toggle(player, character.Name);
        }

        internal static void Toggle(GamePlayer player, string name)
        {
            DbCoreCharacter character = AccountCompanionManager.FindCharacter(player, name);
            if (character == null)
                return;

            GameBot active = AccountCompanionManager.ActiveFor(player)
                .FirstOrDefault(bot => bot.CompanionCharacterId == character.ObjectId);
            if (active != null)
            {
                AccountCompanionManager.Dismiss(active);
                player.Out.SendMessage($"{character.Name} heads home. Its progress is saved.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            AccountCompanionManager.Summon(player, character, out string message);
            player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            if (AccountCompanionManager.Check(player, character) == AccountCompanionRules.SummonRefusal.NeedsPromotion)
            {
                string choices = string.Join(", ", AccountCompanionManager.PromotionChoices(character));
                if (choices.Length > 0)
                    player.Out.SendMessage($"{character.Name} can become: {choices}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
        }

        private static void List(GamePlayer player)
        {
            var characters = AccountCompanionManager.AccountCharacters(player);
            if (characters.Count == 0)
            {
                player.Out.SendMessage("You have no other characters on this account.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            foreach (DbCoreCharacter character in characters)
            {
                string className = ScriptMgr.FindCharacterClass(character.Class)?.Name ?? "Unknown";
                AccountCompanionRules.SummonRefusal refusal = AccountCompanionManager.Check(player, character);
                string state = AccountCompanionManager.IsSummoned(character.ObjectId) ? "with you"
                    : refusal == AccountCompanionRules.SummonRefusal.None ? "ready"
                    : refusal == AccountCompanionRules.SummonRefusal.GroupFull ? "group full"
                    : AccountCompanionRules.RefusalMessage(refusal, character.Name);
                player.Out.SendMessage($"{character.Name}: level {character.Level} {className} - {state}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
        }

        private static void Dismiss(GamePlayer player, string name)
        {
            var active = AccountCompanionManager.ActiveFor(player)
                .Where(bot => name.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (active.Count == 0)
            {
                player.Out.SendMessage("No matching companion is with you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            foreach (GameBot companion in active)
                AccountCompanionManager.Dismiss(companion);
            player.Out.SendMessage($"Sent home and saved: {string.Join(", ", active.Select(bot => bot.Name))}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void Build(GamePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                player.Out.SendMessage("Usage: /companion build <name> [build]", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            DbCoreCharacter character = AccountCompanionManager.FindCharacter(player, args[2]);
            if (character == null)
            {
                player.Out.SendMessage($"You have no other character named '{args[2]}'.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            AccountCompanionManager.SetStyle(player, character, args.Length > 3 ? args[3] : null, out string message);
            player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
