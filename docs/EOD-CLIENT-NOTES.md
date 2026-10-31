# What we took from eod-client

Survey of [eveofdarkness.org](https://eveofdarkness.org/) and
[joej/eod-client](https://github.com/joej/eod-client), 2026-10-31. joej is building a GPL-3.0 Godot client,
and tests it against OpenDAoC. He studied this project's asset pipeline
(`docs/offlinedaoc-asset-pipeline.md` in his repository), so its NIF findings on collision nodes, scale
and doors started here and add nothing new.

The opcode table is in [reference/daoc-opcodes-1.130.json](reference/daoc-opcodes-1.130.json). Use it to
name a packet from `UNHANDLED_CLIENT_PACKET` in the warn log. Its "guess" and "unknown" rows are not facts.

## Applied

| Finding | Change |
|---|---|
| A standing 1.130 client sent position packets up to 6.4 s apart on official servers. | Soft link death limit raised from 5 s to 15 s. Before, a network client could lose /quit, a trade or its place in a house. Loopback clients were already exempt. |
| A stuck appeal from the client's dialog is rejected. | Stuck appeals skip the five-word check. |
| The crash packet carries more than the DLL name and uptime. | The whole packet is logged. Its last 12 bytes should be the position, per joej. |
| `0x1D`, `0x42`, `0x43`, `0xAC` and `0xB8` are world entry and exit notices that official servers do not answer. | They are no longer reported as unhandled. |

Separately, `logconfig.xml` drops PacketProcessor messages at Info and below, so `UNHANDLED_CLIENT_PACKET`
never reached a log. It is now a warning.

## Not affected

- **Stale database pool:** this is a MySQL connection pool problem. We use SQLite with pooling off.
- **0xA1 client crash (his E1):** his notes leave the cause open. Our test server logs since 27 October hold
  no client crash report, so it does not appear to happen here.
- **NPC health above 100:** `GameObject.HealthPercent` clamps to 0-100 here, and whether that covers his
  1.127 capture is unknown. It does not affect play.
- **Grass density masks:** the new client already scatters ground clutter.

## Left alone

- **Retail world entry packets** (`0xA6`, `0x24`, `0x50`, `0x5F`, `0x59`, and others): nobody knows what they
  mean, the game works without them, and a wrong packet can crash the client.
- **Status command bug:** his post does not name the command, so it could not be checked here.
- **Quit lockout (his L9):** a client that disconnects right after /quit may lock the account for 60 seconds.
  Read from his notes only; not tried here.
- **Character screen oddities (C1-C8):** they do not matter on a private server.
- **RSA key transport for 1.130:** his route needs a client patched with a new key, and ours already runs
  without encryption.

## Open

Frontier keep shapes name their textures with `!` tokens such as `!main_wall`. The client picks the file by
realm and keep level (`alb_mainwall_lvl2.dds` and so on). Our new client pipeline has no such table.
