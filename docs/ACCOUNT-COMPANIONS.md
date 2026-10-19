# Account companions

Status, October 18, 2026: **compiles; unit tests pass (9 companion tests, 2916 in the full suite).
Not yet checked against a played database or in game.** Code review fixed one bug: a summon that
failed partway (region refused, group refused, double summon) saved the character at the owner's
position. Now only a companion that joined the group is ever saved.

## What it does

Make extra characters on your own account, then bring them into your group from inside the game. A
summoned character fights as a party bot, like a `/spawn` companion: it follows, assists, heals and
counts for Bring A Friend. It is still a real character:

- It wears its own equipment and carries its own bags (its normal inventory rows; nothing is generated).
- It earns experience as a group member, at the player XP rate, and levels up.
- It spends new specialization points automatically along a bot build that fits what it has
  already trained. It never lowers a line, and it never touches lines outside its build.
- It takes a dropped item only when it is an upgrade it will wear; the old piece goes into its
  backpack. The group's autosplit loot setting must be on.
- Coin needs nothing extra: money is shared per realm across the account, so the owner's normal
  pickup already fills the same wallet.
- When dismissed, removed from the group, or when you log out, it is saved where it stands
  (at its bind point if it was dead). It also saves after every level and every two minutes if
  anything changed. Log in as that character and you continue from there.

## Commands

| Command | Effect |
|---|---|
| `/companion` | Clickable menu of your other characters in your realm; click to summon or send home |
| `/companion <name>` | Summon, or send home if it is already with you |
| `/companion <name> <class>` | Choose the class for a new character (characters start as a base class, e.g. Viking) |
| `/companion build <name> [build]` | List or choose its training build (e.g. Healer: MendHealer, AugHealer, PacHealer) |
| `/companion list` | Your characters, their level and whether they can come |
| `/companion dismiss <name or all>` | Send home and save |

## Rules and limits

- Same realm only (normal RvR is on). A character cannot be summoned while you play it.
- The class must have bot AI: every Classic + Shrouded Isles class. **Stormforged cannot be a
  companion yet** (owner's choice: its AI is a separate job).
- Characters start as base classes. Choosing the class with `/companion <name> <class>` works at any
  level, unlike a trainer's level-5 promotion, and gives no trainer gifts.
- Companions count as group members for XP. As with any real group, if a monster is grey to any
  member, nobody gets XP: a much higher-level companion stops its owner's XP.
- No realm points, realm ranks or death penalty for companions yet.
- Companions are not re-summoned automatically when you log back in.
- Bard/Minstrel/Skald companions may get a free training instrument, and assassins free poison
  coats, from the shared bot code; those land in the character's real inventory.

## Code map

| File | Role |
|---|---|
| `source/server/GameServer/bots/AccountCompanions/AccountCompanionRules.cs` | Pure rules: spec points, autotrain refunds, spending, stat gains, levels, spec merge, refusal order |
| `.../AccountCompanions/GameBot.AccountCompanion.cs` | `partial class GameBot`: build from a `DbCoreCharacter`, XP, auto-train, upgrades, saving |
| `.../AccountCompanions/AccountCompanionManager.cs` | Summon, dismiss, class choice, build choice, loot hand-off |
| `.../AccountCompanions/AccountCompanionRecord.cs` | New table `account_companions` (saved build plan only) |
| `source/server/GameServer/commands/playercommands/CompanionCommand.cs` | `/companion` and its popup menu |
| `source/server/Tests/UnitTests/UT_AccountCompanion.cs` | Rule tests |

Edits to existing files:
- `GameBot.cs`: now `partial`; `GainExperience` routes companions; `Delete` saves first; no endgame
  gear regeneration; `SaveToDatabase` saves the character.
- `AbstractServerRules.cs`: companions count and earn as group members; no autonomous kill credit.
- `AtlasMobLoot.cs`: companions are loot participants (their classes get drop rolls) and keep ownership.
- `Group.cs`: `TryPickUpItem` offers upgrades to companions first.
- `progress-policy.json`: `account_companions` is saved progress (transferred, and emptied in public packages).

Design notes: a companion keeps `IsTemporaryGroupHelper = true` for party behavior, with
`IsAccountCompanion = true` overriding every path that discards or regenerates a `/spawn` helper.
It writes into the `DbCoreCharacter` cached on the owner's `Account.Characters`, the same object the
character screen loads, so the login never sees stale data.

## Next steps (for the Claude Code session)

1. ~~Build and run the tests~~ (done October 18: build clean, all tests pass).
2. Small known wording issue: the group message says "takes X and equips it" even when the item had
   to stay in the companion's backpack.
3. Review the save path against a copy of the played database: summon, gain a level, dismiss,
   then compare DOLCharacters and Inventory rows.
4. Deploy only with the game stopped, using the normal installer backup.
5. In-game check: create a Midgard alt, `/companion <name> Healer`, `/companion <name>`, play a
   while, `/companion dismiss all`, log in as the alt and confirm level, specs, gear and position.
