# Offline DAoC test client

Plays the game with no window, so Claude, Codex or a saved scenario can log in as a real character and test on the
real server. It speaks the normal client protocol from the new client's `DaocClient.cs`, so the server runs the same
code paths as for a player. `GodotShim.cs` lets that file compile without the engine.

## Use

The server must already be running; the client never starts or stops it.

```
OfflineDaoc.TestClient --runtime <install>\runtime --account aiclaude     # MCP tools on stdio
OfflineDaoc.TestClient --runtime <install>\runtime --scenario smoke.json [--verbose]
OfflineDaoc.TestClient --runtime <install>\runtime --check                # no server needed
```

Build with `dotnet build -c Release -o ..\..\..\artifacts\test-client` from this folder (from `Source`, adjust the
there (`.mcp.json` for Claude, `~/.codex/config.toml` for Codex). Scenarios are JSON lists of tool calls with `expect` and `forbid` text, and exit non-zero on a failure.

## Accounts

- Each session is an account, `<prefix><session>`: `aiclaudemain`, `aicodexmain`. The client makes the row itself,
  because auto creation stops at 20 accounts from one address and the server reads the flags at login. Characters
  are created on first login, in the classes listed by the `login` tool. Session names are letters only.
- A session name in use by another program is refused (`TestConfig.ClaimAccount`): two agents on one account keep
  dropping each other.
- `prepare` gives a character its level, specs and the best armor and weapon for its class. Artifacts stay out.
- Accounts stay at player level. `GamePlayer.IsAttackable` is false above it, so a true GM can't be hit and combat
  tests would prove nothing. GM commands are granted one by one in `SinglePermission` (`TestConfig.GrantedCommands`).
  Admin commands stay out. Write the full command name: the short forms only resolve for real GMs.
- Passwords are random and kept in `%LOCALAPPDATA%\OfflineDAoC\test-client-accounts.json`, never in the install.
  A committed password would be a GM login on a world hosted for friends.
- `progress-policy.json` clears `Account` and `SinglePermission` in release packages.

## Mind

- **Tests happen in the real world.** Test accounts get `Account.IsTester`, which keeps them out of the server firsts
  (`AutonomousLivingServer.MayClaimFirst`). A `GameServer.dll` older than that check lets `/player level 50` claim a
  first that belonged to the bots: stay below 50 there, or delete the row from `offline_server_firsts` with the server
  stopped. That table is cached, so deleting it while the server runs does nothing.
- `/mob`, `/item` and `/npc` can save world rows. World fixes still go through World Workshop.
- After a dropped client the character is linkdead for 60 seconds. `login` waits that out.
- `/player level` leaves Experience at 0, so the next XP change, a kill or a death's loss, lifts it to level 50's floor
  (`GamePlayer.GainExperience`), about 170 billion, and that buys five Master Levels at once. Players levelling
  normally never see it. A prepared character is ML5 after its first kill.
- Use the `release` tool: during the countdown `/release` is a toggle and cancels auto-release.
- Attacks fail during the PvP invulnerability timer after login. Wait about 30 seconds, away from aggressive mobs.
- Testers share a log with `report_finding` and `read_findings` (`--shared-log`, one JSON object a line).
- `db_query` runs one read-only SELECT, to find what to test.

## What it can't test

- Anything drawn: graphics, UI, animation, client crashes.
- 1.130 behaviour: it announces 1.127, so `bot_show_as_players` is not sent to it and bots arrive as NPCs.
- Walls: line-of-sight checks always answer "seen", because it has no world geometry. The navmesh gives the ground only.
- Climbing and riding movement. Swimming is only what the `swim` tool says, because Atlantis zones hold no water
  level. Boats can be summoned but not steered. `ground_target` sets a ground target for area spells. Walking stops at zone edges where
  meshes don't join.
- `status.unknown_packets` lists packets nothing decodes yet.
