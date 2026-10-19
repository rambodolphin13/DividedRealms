# OfflineDAoC / Stormforged project handoff

Updated October 18, 2026. This describes the local development installation, not a public release.

## Paste this into another chat

> Continue my OfflineDAoC project in `C:\Users\natex\Desktop\DAoC Offline`. First read `AGENTS.md`, `docs/DEVELOPMENT.md`, `source/server/AGENTS.md`, and `docs/PROJECT-HANDOFF.md`. My active game is `artifacts/stormforged-playable`; preserve its accounts, characters, equipment and world progress. The original `playable` installation is separate. We created an intentionally powerful Midgard class called Stormforged using the classic client's Midgard Mauler slot. Normal RvR is currently restored; the experimental co-op/GvG mode is disabled and cross-realm capital teleport changes have been removed. Keep all Stormforged work. There are many uncommitted and untracked project changes: inspect them and do not reset or overwrite them. Do not start the server automatically or deploy over a running game. Read the handoff for current abilities, source locations, test/deployment commands, known limitations and future goals. Current task: build, test and finish the account companion draft described in `docs/ACCOUNT-COMPANIONS.md`; it has never been compiled.

## Current work: account companions (not built yet)

The owner asked for characters on their own account that can be summoned into the group as bots and
still be logged in and played, keeping their levels, specs and gear. Code is drafted but has **not been
compiled or tested**: the drafting session had no .NET SDK. Read `docs/ACCOUNT-COMPANIONS.md` for the
design, the owner's choices, the code map and the next steps. The owner wants to continue this work in
Claude Code on the PC, where `dotnet` is available. Nothing has been deployed.

Owner's choices: companions auto-train new spec points; they take coin and upgrades like a group member;
Stormforged companions come later; a dismissed companion is saved where it was dismissed.

## Owner's direction and preferences

- A self-contained solo/small-group DAoC experience, with optional co-op rather than a public MMO.
- Aim toward a time capsule of Base + Shrouded Isles just before Trials of Atlantis. This is an aspiration, not a claim that the current world is complete or historically exact.
- Housing/Foundation completeness matters. It has not been comprehensively audited or completed in this work.
- ToA is a favorite expansion and a possible future project; fully working ToA has not been implemented by this customization.
- Persistent account-owned companion characters: drafted (see above), not yet built. Transferring a solo character to a friend's server: not implemented.
- Stormforged is deliberately overpowered. Avoid balancing away requested power without discussing it.
- Later expansion armor appearances are acceptable even with classic/SI gameplay. Classic Thane epic appearance is the fallback.
- Keep real loot/equipment progression and original server features unless a change is requested.

## Current installed state: normal RvR

The latest requested change was to stop the hybrid co-op experiment and return to normal RvR.

Installed settings:

| Setting | Value |
|---|---|
| `hybrid_coop_pvp` | `False` |
| `allow_bg_claim` | `False` |
| `claim_num` | `8` |

The last two values match the pre-experiment installation. Normal server configuration remains in use. Guild-based warfare friendliness, cross-realm PvE friendliness and the hybrid DF access override are inactive.

Both `LiveTeleporter.cs` and `InlandTeleporter.cs` were restored to their pre-change source. The `CoopCapitalTravel.cs` helper and its nine tests were removed. Stock destinations and Realm Exchange remain. No custom capital NPCs or teleport database rows were added by that experiment.

The owner reported that cross-realm bots still rejected invitations and examined as enemies in hybrid mode. Investigation found hardcoded realm checks in `BotGroupInvite.TryInvite`, `GameBot.EnterPlayerLedGroup`, named bot lookup, `PlayerLedPullCoordinator.Available`, and `GameBot.GetExamineMessages`. No fixes to those paths were installed: the owner chose to return to RvR instead. Cross-realm bot hostility is now expected under normal RvR.

Hybrid implementation remains in source, gated off, for possible later work. See `docs/COOP-GUILD-WARFARE.md`. Do not re-enable it implicitly. No new guild reward/blessing system was implemented.

## Workspace and launch locations

Workspace: `C:\Users\natex\Desktop\DAoC Offline`

| Location | Purpose |
|---|---|
| `source/server` | .NET 10 server source and NUnit tests |
| `source/tools/stormforged` | Private content seed, guarded classic-client patch, installer and Python tests |
| `artifacts/stormforged-playable` | Active separate test game, containing the owner's saves |
| `artifacts/stormforged-playable/runtime/OfflineDAoC.exe` | Launcher the owner uses |
| `artifacts/stormforged-playable/runtime/data/opendaoc.sqlite3.db` | Played world/account/character database: protect it |
| `playable` | Original separate installation/clean files used by tooling; do not deploy here casually |
| `docs/STORMFORGED.md` | Detailed class notes; some early status/test-count sections are historical |
| `docs/STORMFORGED-ARMOR-OPTIONS.md` | Appearance research only |

The classic client is supported by the Stormforged patch. The Godot/new client has not been wired to the new class. Only one server may use port 10300.

## Stormforged class identity and equipment

- Separate Midgard class, server ID **64**, presented through the classic client's Midgard Mauler slot **61**. It derives from Thane; original Thane and Hibernian Sluaghbinder remain separate.
- Norseman, Troll and Dwarf; normal character leveling through 50.
- Chain, large shields, swords, axes and hammers from level 1.
- New characters receive chain armor, hammer, large shield and the shared personal recall stone. Existing inventories are preserved.
- Sword/Axe/Hammer/Shield/Parry/Stealth automatically follow character level. Spell unlocks are automatic; no need to choose between keeping lines trained.
- Spell lists retain the newest two learned ranks of each custom ability; weapon styles retain their full lists.
- Each ability needs its own spell line: the classic client addresses spells by line and level. Sharing addresses previously caused wrong abilities to cast.
- Primary/secondary/tertiary class stats: STR / PIE / CON.
- Innate bonus per level: +2 to all eight stats, +10 maximum HP, +2 maximum power, capped at level 50. At 50: +100 stats, +500 HP, +100 power, plus ordinary Constitution/Piety contributions. These are separate from item and buff caps, not saved base-stat edits or removable effects.
- Atlas ROG now recognizes class 64 as chain/melee/Piety, including large shields and relevant trained skills. Generic mob drops and group loot for other classes can still produce cloth. Existing drops were not rerolled.
- Shield ability is a single level-1 rank-3 grant; multiple equal-level rank grants previously allowed small-shield selection.

## Current spell kit

- **Stormbolt:** cast-time lightning attack.
- **Thunderclap:** instant single-target lightning, 12-second reuse.
- **Chain Lightning:** instant caster-centered 350-radius PBAoE, 12-second reuse.
- **Stormhammer:** 2.5-second cast, targeted 350-radius AoE with classic Thane hammer visual (effect 3538).
- Custom direct damaging hits restore 50% of actual damage as health and 15% each as power/endurance, bounded by missing resources.
- **Stormheart:** maintained 350-radius damage aura every four seconds, returning 75% damage as health and 25% each as power/endurance per enemy hit. No returns for misses/resists or overkill damage. Multiple targets increase recovery.
- **Thunderforged Ward:** scaling self stats, AF, nine normal resists, damage add and reactive energy damage shield. Works at low levels; full resistance strength by 40. Uses Thane-like icon and private numeric tooltip. Ordinary buff caps still apply. Recast after leveling to refresh cast-time bonuses.
- **Stormsong:** one maintained group effect: speed, health/power/endurance regeneration and damage add. Speed reaches 205% at 40, continues in combat and stealth. Group range 1,500. No repeating activation visual or already-active-effect chat spam. Respects stronger damage adds and normal movement restrictions.
- **Stormrest:** normal targeted heal; caster and recipient must be out of combat.
- Maintained buffs/chants can coexist with stealth; attacking/pulsing hostile damage reveals the caster. Normal detection applies.
- Private class/internal spells cost power without the stock five-endurance casting charge. Melee styles retain normal endurance costs. Do not remove endurance costs globally without a new request.
- Normal buff cancellation was fixed for private effects. Old hotbar spell buttons may need replacing after spell-line changes.

## Commander squad

**Stormbound Elemental**, available at level 5, now summons a tank commander with Mender and Tempest supports automatically.

- All three match owner level and update with owner level changes; no stock Bonedancer cumulative-level restriction.
- Commander: 4x pet-health scaling, 2x melee, at least 35% armor absorption, armor factor at least 2x level per slot, melee threat 8x damage, 350-radius taunt about every five seconds subject to NPC scheduling.
- Keeps instant single-target and area lightning.
- Mender: heals the human owner even when not grouped, prioritizes injured owner, then nearby party/pets; lightning and attack-speed debuff.
- Mender also uses private copies of the strongest installed healer/shaman support buffs: base stats, STR/CON, DEX/QUI, acuity, armor, celerity, regeneration, Frigg and six magical resists. No concentration/power cost; normal buff caps apply. Skips equal/stronger effects and upgrades weaker buffs. Allow time for casts; healing has priority.
- Tempest: lightning and energy-resistance debuff. Support health scaling is 2x.
- Support command/release handling follows commander. Releasing/resummoning replaces missing supports. Resummon after installing changed pet spell data.
- Stock elemental model 114; different sizes, no custom model files.

## Lightning Bomb turrets

- Level 10, ground-targeted stationary repeating turrets, not single-use mines.
- Ranged AoE lightning plus instant 350-radius PBAoE every four seconds.
- Last 120 seconds; no summon recast timer, but three-second cast remains.
- Up to **16 per owner**, including at one ground target. Private bombs bypass the ordinary shared Animist area quota; ordinary Animist limits remain intact.
- Can acquire legal grey-con enemies; stock Animist behavior remains unchanged.
- Normal ground range, visibility, terrain and keep-height validation remains. Summon ground-target range: 1,000.
- **Release Storm Clump:** target one owned turret; releases owned turrets within 500, without releasing commander or another player's summons.

## Heroic abilities

| Ability | Unlock | Behavior |
|---|---|---|
| Stormguard | 10 | Maintained block-triggered lightning AoE; normal per-hit recovery |
| Stormstep | 15 | Instant ground teleport up to **2,000** units plus arrival AoE; 8-second reuse; terrain/LOS checks |
| Second Thunder | 20 | Fatal damage prevention, full resource restore and 500-radius burst; shared 2-minute trigger cooldown |
| Thunder Recall | 20 | Moves owner's live bombs in-region to ground target within 1,000; refreshes their two-minute lifetimes; 5-second reuse |
| Eye of the Storm | 25 | Maintained four-second pull of legal hostile NPCs within 600; skips CC/root-immune targets and checks terrain |
| Ride the Storm | 30 | Nearby owned elemental/bombs echo directly cast class attacks; no recursive echoes |
| Avatar of Thor | 40 | Frost Giant model **918**, 60 seconds, 3-minute reuse; doubled custom lightning damage, casts at most 250 ms, two-second recovery/AoE pulses |

No mounts were added by this work.

## Epic armor status

No epic armor set, rewards or appearance changes have been added.

Research found later-client appearances available for potential private chain items: Midgard Dragonslayer (4026-4030, helm 4072), Possessed (2713-2718), Good chain/scale (2871-2876), ToA mixes, and classic Thane Storm Touched (787-791, coif 834). These still need visual comparison on supported races and a decision on stats/procs/acquisition. Use private templates; do not modify stock Thane-only permissions.

## Main code map

Paths below are relative to the workspace.

| Area | Files |
|---|---|
| Class | `source/server/GameServer/playerclasses/midgard/ClassStormforged.cs` |
| Rules/passives | `GameServer/gameutils/StormforgedRules.cs`, `StormforgedInnate.cs`, `StormforgedTurretRules.cs`, `StormforgedTravel.cs` under `source/server` |
| Spell handlers | `source/server/GameServer/spells/Stormforged/` |
| Effects | `source/server/GameServer/ECS-Effects/StormforgedEffects.cs`, `StormforgedHeroicEffects.cs`, `StormforgedNetworkEffects.cs` |
| Squad | `source/server/GameServer/gameobjects/StormforgedPets.cs` |
| Bomb brain | `source/server/GameServer/ai/brain/Animist/StormforgedBombBrain.cs` |
| Trainer | `source/server/GameServer/trainer/midgard/StormforgedTrainer.cs` |
| Data/tuning | `source/tools/stormforged/seed_stormforged.py` |
| Client/installer | `source/tools/stormforged/patch_client.py`, `install.py`, `test_stormforged.py` |
| Disabled hybrid | `source/server/GameServer/serverrules/HybridCoopRules.cs`, `commands/playercommands/CoopRulesCommand.cs`, `source/tools/stormforged/seed_hybrid_coop.py` |
| Tests | `source/server/Tests/UnitTests/UT_Stormforged*.cs`, `UT_HybridCoopRules.cs` |

Core integration also touches enum/class/client mappings, starter equipment, spell identity/cancellation, stat/HP/mana calculators, speed calculation, ROG, pet spell handling and other shared hooks. Inspect `git status`/diff rather than assuming all work lives in new files.

Reserved private spell range currently used: **62000-62239**; tooltip identities use **20600 + spell ID - 62000**. Package currently seeds **172 spells**. Pet templates: commander 60171001, bomb 60171002, Mender 60171003, Tempest 60171004. Mender native-buff copies use 62220-62236. Trainer: Stormkeeper, beside Frode in Jordheim.

Important implementation trap: actual `GamePlayer` does **not** implement bot-facing `IGamePlayer`. Code supporting both humans and bots must handle both explicitly. This previously affected innate bonuses and turret limits.

## Build, test and deploy

Read repository instructions first. Use PowerShell in the workspace. Keep builds separate from the installed game. The .NET tools may require the environment's approved escalation.

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD 'artifacts/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD 'artifacts/nuget-packages'
dotnet test source/server/Tests/Tests.csproj -c Release --no-restore
python -m unittest discover -s source/tools/stormforged -p test_stormforged.py
```

Focused tests: add `--filter 'FullyQualifiedName~UT_Stormforged'`. Check actual test results, not only PowerShell's last-command exit code when redirecting logs and then calling `Get-Content`.

Deployment requires stopped game, launcher and server. Never start the server automatically. Installer checks processes, checkpoints SQLite, creates a complete replacement backup and hash manifest, seeds private content transactionally, copies all three server DLL/PDB locations and verifies hashes.

```powershell
# Preview only
python source/tools/stormforged/install.py artifacts/stormforged-playable

# Install normal class updates, preserving current settings
python source/tools/stormforged/install.py artifacts/stormforged-playable --apply

# Explicitly restore normal RvR and original keep-claim defaults
python source/tools/stormforged/install.py artifacts/stormforged-playable --disable-coop-pvp --apply
```

`--enable-coop-pvp` exists but is not currently wanted. Never use it without a new request.

Do not restore an old played database merely to change a setting. The installer provides guarded rollback, but refuses if files/database changed after installation; manual recovery then needs to preserve newer progress. Backups, played databases, accounts, credentials and personal logs must not be committed or published.

The source server includes upstream development changes beyond the sealed 0.36 package. Do not describe this as a minimal binary-only patch to that release.

## Latest verification and backup

Latest installed backup: `artifacts/stormforged-playable/stormforged-backup-20261018-195904`.

- **2,907 server tests passed**, after removing nine capital-travel tests with that feature.
- **19 Python tooling tests passed**, including RvR-setting restoration.
- Logs: `artifacts/restore-rvr-tests.log`, `artifacts/restore-rvr-tools-tests.log`.
- Installed file hashes and SQLite integrity verified.
- All rows in Account, DOLCharacters, Inventory, PlayerXEffect, Keep, Guild, GuildRank and Teleport verified unchanged against the immediate pre-install backup.
- Only the intended mode/claim settings were restored; no progress reset.
- Installed server SHA-256: `5e68a77e282c67713fffb80058731a56346b76d1baea62411552a16948858a15`.
- Installed classic client SHA-256: `01d6a77803cbbc94487c8263fffdc105958856a07d39316347d9da0a0ef61df9`.

The owner has played and confirmed successive improvements, including working class creation, bombs, heroics, ROG/shield corrections and healer/passive work. Automated tests do not replace full in-game acceptance. The final RvR rollback has been installed and verified on disk; it has not been separately confirmed in-game by the owner yet.

## Known limits and reasonable next work

- No current implementation request remains outstanding after the RvR rollback. Follow the owner's next instruction.
- Epic appearance selection and a private epic set are a natural next project; research only so far.
- Base/SI completeness, housing, encounter/quest coverage and historical accuracy need an explicit audit; do not claim completion percentages without evidence.
- Account-alternate companions and cross-server character transfer are future designs, not delivered features.
- No autonomous Stormforged population generation or dedicated player-companion class AI was added.
- Hybrid bot recruitment/presentation needs more work if the experiment is resumed. Autonomous bots retain realm-based campaign goals; no guild campaign planner exists.
- Full ToA and new guild reward buffs remain future work.
- Detailed class docs contain early test counts and initial acceptance wording that are historical. This handoff's current-state section supersedes those old status paragraphs.
- Preserve unrelated uncommitted work. No commit, push or public release was requested as part of this handoff.
