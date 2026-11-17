# Crafting pass 2026-11-17: correctness fixes (one patch)

Combined from the 11 audits in `DAoC Offline\Research\Audits\Crafting-Pass-2026-11-17` (see SUMMARY.md there) by
`combine.py`. Owner-decision files are left out (Legendary bonuses, Dragonsworn materials, furniture sizes, odd
models, unique dye ingredients). 3,239 edits -> 3,236 row edits; 9 duplicate edits between armorcraft and tailoring dropped.

What it fixes: ~40 non-armor/non-weapon products, wrong-realm and wrong-item recipes, recipe 16117 with no ingredients,
arrow/bolt flags, AF/level/absorb on ~400 armor pieces, spellcraft stat gems +2..+29 and Spectral Guard gems,
329 missing client recipes, Bountycraft armor types and wrong products, dead material conversions, horse armor
realm themes, 48 bardings, placeable furniture, potion use levels, Mid/Hib siege materials, NF material merchants,
free smith's hammer, 30c Hibernian bars.

Checked on a scratch copy of the test world (16:22): apply OK, then the client recipe check went from 525 missing,
6 realm-refused, 1 empty to 196 missing (Fletching 111 and Weaponcraft 85 with no product item or live stats),
0 realm-refused, 0 empty; undo restored the row counts exactly. Not booted or played.

Apply (owner OK, server stopped): `cli.py --profile modern plan crafting-pass-2026-11-17.edits.json --out ...patch.json`
then `apply`. Record the undo id in the issue list.
