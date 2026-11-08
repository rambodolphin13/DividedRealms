# Sovereign Regalia client art

Private client skins for the Sovereign Regalia sets, installed into the owner's modern 1.130 test client. Each kit adds
new catalog rows and two new skin archives under new IDs; no stock row, model or skin changes. The server items are
pointed at the new objects by the World Workshop edit scripts in `source/tools/sovereign/`.

| Kit | Class | Look | Client IDs | Server edits |
|---|---|---|---|---|
| `tara/` | Tuatha Sovereign (67) | emerald court scale, moon-and-stag roundel, polished like the new epics (A2) | pskin062, mskin071; mskins 2750-2755, pskins 5810-5818, objects 4950-4956 | `tara_regalia_edits.py --update` |
| `storm/` | Storm Sovereign (64) | Stormforged v20 Rune-Warlord plate and storm cloak (polish passes 2026-11-04) | pskin061, mskin070; mskins 2740-2745, pskins 5790-5797, objects 4929-4933, 4937 | `storm_regalia_edits.py` |
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

## Releases

All three kits are in the release client patch (`source/tools/make_client_patch.py` `regalia_entries`, 2026-11-08): the
launcher adds the same rows and archives to every player's client copy, and to a copy an older release set up. The release
world must then point the Tara and Camelot items at the new objects: run `tara_regalia_edits.py --update` and
`camelot_regalia_edits.py --update` (World Workshop) on the release's staging world (the Storm items already name theirs).
DR-dev-0.1d shipped Tara and Camelot on stock art, and the Storm Regalia on objects 4929-4942 that no release client had;
`build_modern_test_package.py` now refuses an item on a private model the patch doesn't add (`private_models_without_art`).

`storm/` is taken from the Stormforged concept kit (`DAoC Offline\Stormforged-armor-concepts\v20-polished-set`): its final
textures and the rows its installers wrote for the pieces the Storm Regalia uses, checked against the test client (every
row and texture the same; the classic DXT3 cloak differs only in compression bits, from an older Pillow). The concept kit's
painters are not in the repository. **Left out:** the winged helm (4942, skin pskins 5800): its painter recolours the stock
ToA Winged Helm (Mid)'s feather drawing, so the skin isn't only our art. Release worlds draw the stock helm 2224 instead
(`PRIVATE_MODELS`); repaint the wings without the stock texture to ship it. Also left out: the Storm Crown, wolf cloaks and
wolf helm (4934-4936, 4941), which no item uses (custom meshes and fig3map edits). The Camelot helm keeps the stock full
plate helm's eye slot and breathing holes (its near-black texels, darkened: `keep_openings` in `paint_camelot.py`).

`regalia_client.py` holds the shared helpers (catalog rows, skin archives and the whole build, used by both installers and
the client patch; DDS encoders from `tools/pet-art`, the archive tool from `tools/asset-tool`).

## Painters

`tara/paint_tara.py` and `camelot/paint_camelot.py` paint `textures/` and the preview sheets. They paint in 3D body
space on mesh bakes, using the pipeline in `pipeline/` (the Stormforged concept pipeline: `paint_sf`, `paint_sf_parts`,
`paint_v5` Canvas with painted bevels, `paint_v7` helpers, `calib` mesh bakes, `render` preview renderer, `bake_arrays`,
`helm_fit`). The pipeline's own helpers come from `tools/pet-art` and `source/new-client/pipeline`.

```
python tools/sovereign-art/pipeline/fetch_meshes.py           # once: meshes and the stock helm skin from the client
python -u tools/sovereign-art/camelot/paint_camelot.py B      # about 15 minutes; Tara: tara/paint_tara.py A2
python tools/sovereign-art/camelot/paint_camelot.py B only=render   # previews again from existing textures
```

Everything they write goes to `tools/sovereign-art/.work/` (git-ignored; `SOVEREIGN_ART_WORK` moves it): `nif/` and
`tex/` from the fetch, `bakes/` (rebuilt from the meshes on first use, about 30 s), and `tara/out_A2/`,
`camelot/out_B/` with the painted textures and the preview sheets. Copy a finished `out_*/` into the kit's `textures/`
to install it. The client folder is `OFFLINE_DAOC_CLIENT`, else the owner's modern test client; it is only read.

Checked 2026-11-07: the fetched meshes and rebuilt bakes are identical to the ones the installed art was painted from,
and both painters reproduce the committed textures from the repository.
