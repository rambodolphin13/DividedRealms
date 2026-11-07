# Sovereign Regalia client art

Private client skins for the Sovereign Regalia sets, installed into the owner's modern 1.130 test client. Each kit adds
new catalog rows and two new skin archives under new IDs; no stock row, model or skin changes. The server items are
pointed at the new objects by the World Workshop edit scripts in `source/tools/sovereign/`.

| Kit | Class | Look | Client IDs | Server edits |
|---|---|---|---|---|
| `tara/` | Tuatha Sovereign (67) | emerald court scale, moon-and-stag roundel, polished like the new epics (A2) | pskin062, mskin071; mskins 2750-2755, pskins 5810-5818, objects 4950-4956 | `tara_regalia_edits.py --update` |
| `camelot/` | Pendragon Sovereign (65) | gilded plate, crimson enamel, red dragon, crowned great helm (B, polish pass 4) | pskin063, mskin072; mskins 2756-2761, pskins 5819-5827, objects 4957-4963 | `camelot_regalia_edits.py --update` |

Both body textures are painted on the Body05 / Legs01 / Arms01 / Gloves06 / Boots06 meshes, so the hauberk and
breastplate items use `Extension 5`.

## Install and rollback

```
python -B tools/sovereign-art/camelot/install_camelot_set.py plan     <client app folder> [--modern-test]
python -B tools/sovereign-art/camelot/install_camelot_set.py install  <client app folder> [--modern-test]
python -B tools/sovereign-art/camelot/install_camelot_set.py rollback <client app folder> <backup folder>
```

(`tara/install_tara_set.py` works the same way.) The installers refuse the real client folders and accept the owner's
modern test client only with `--modern-test`, and only while the game client is closed. `install` writes a backup
(`runtime/backups/client-<set>-set-<time>`) holding the old `gamedata.mpk` and a manifest. `rollback` restores the
catalog only if nothing else changed it since, then removes the two archives. To replace installed art with new
textures: roll back, swap `textures/`, install again.

`regalia_client.py` holds the shared helpers (catalog rows, DDS encoders from `tools/pet-art`, the archive tool from
`tools/asset-tool`).

## Painters

`paint_tara.py` and `paint_camelot.py` are kept for reference: they made `textures/` and the preview sheets. They need
the Stormforged concept pipeline (`paint_v5`, `paint_v7`, `calib`, `render`, `bake_arrays`, `helm_fit` and its mesh
bakes), which is not in the repository, so they don't run from here as they are.
