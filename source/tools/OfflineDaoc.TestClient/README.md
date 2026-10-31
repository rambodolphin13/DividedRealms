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

- Each session is an account, `<prefix><session>`: `aiclaudemain`, `aicodexmain`. Characters are created on first
  login, in the classes listed by the `login` tool.
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

## What it can't test

- Anything drawn: graphics, UI, animation, client crashes.
- 1.130 behaviour: it announces 1.127, so `bot_show_as_players` is not sent to it and bots arrive as NPCs.
- Walls: line-of-sight checks always answer "seen", because it has no world geometry. The navmesh gives the ground only.
- Water, climbing and riding movement; walking stops at zone edges where meshes don't join.
- `status.unknown_packets` lists packets nothing decodes yet.
