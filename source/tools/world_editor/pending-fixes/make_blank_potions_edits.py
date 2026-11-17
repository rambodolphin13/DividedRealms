"""Writes the World Workshop edits that give the blank alchemy potions their spells (owner decision 2026-11-17).

    python -I make_blank_potions_edits.py <out.edits.json>

The 2026-11-13 item pass (DR-433) zeroed SpellID on Draught/Cauldron of Supremacy, Omni-Healing, Omni-Regen, Tenacity
and Restoration because no live numbers exist. The patch notes say what each does; the owner approved these values:
- Supremacy: Str/Con/Dex/Acuity +62 (level 50 base-buff cap), Str/Con and Dex/Qui +93 (spec cap), +10% heat, cold,
  matter, body, spirit and energy; 2 hours (owner); 30 s item reuse (live 1.122). Built like Draught of Heroism
  (parent buff + MultipleSubSpells), same effect groups, so it never stacks with Heroism or the single draughts.
- Omni-Healing: Superior Elixir of Instant Healing / Power / Endurance in one drink.
- Omni-Regen: the Combined Regens spell (AllRegenBuff).
- Tenacity: endurance cost -25% (Saint's Tenacity value), 20 minutes, self.
- Restoration: removes PvE and RvR resurrection illness (SickHeal handler).
The six crafting essences (none sold or dropped) go on the three high-end craft lists that sell Otherworldly Ore, at
its price of 1 gold.
"""
import json
import sys

SUPREMACY, OMNI_HEAL, OMNI_REGEN, TENACITY, RESTORATION = 870001, 870002, 870003, 870004, 870006
USED_BY = {
    SUPREMACY: ['lvcr652', 'lvcr4101', 'lvcr7650', 'lvcr9978', 'lvcr14650', 'lvcr16358'],
    OMNI_HEAL: ['lvcr648', 'lvcr4102', 'lvcr7646', 'lvcr9979', 'lvcr14646', 'lvcr16359'],
    OMNI_REGEN: ['lvcr647', 'lvcr4103', 'lvcr7645', 'lvcr9980', 'lvcr14645', 'lvcr16360'],
    TENACITY: ['lvcr649', 'lvcr4105', 'lvcr7647', 'lvcr9982', 'lvcr14647', 'lvcr16362'],
    RESTORATION: ['lvcr651', 'lvcr7649', 'lvcr14649'],
}
TWO_HOURS = 7200
ESSENCES = ['lvci_essence_of_supremacy', 'lvci_essence_of_omni_healing', 'lvci_essence_of_omni_regen',
            'lvci_essence_of_tenacity', 'lvci_essence_of_celerity', 'lvci_essence_of_restoration']
MERCHANT_SLOTS = {'AlbExpensiveTrade': (2, 20), 'MidExpensiveTrade': (2, 20), 'HibExpensiveCraftComponents': (4, 22)}
SUPREMACY_TEXT = ('Draught of Supremacy: Strength, Constitution, Dexterity and Acuity +62, Strength/Constitution and '
                  'Dexterity/Quickness +93, and +10% resistance to heat, cold, matter, body, spirit and energy '
                  'for 2 hours.')


def spell(copy_of, spell_id, **values):
    values['SpellID'] = spell_id
    values['PackageID'] = 'OfflineBlankPotions'
    return {'table': 'Spell', 'copy': copy_of, 'key': f'dr-blank-potion-{spell_id}', 'set': values}


def main(out):
    edits = [spell('dr-live-potion-8110', SUPREMACY, Name='Draught of Supremacy', Value=62.0, Duration=TWO_HOURS,
                   Description=SUPREMACY_TEXT)]
    for source, sid, name, value in [
        ('dr-live-potion-8112', 870011, 'Supremacy: Fortitude', 62.0),
        ('dr-live-potion-8113', 870012, 'Supremacy: Dexterity', 62.0),
        ('dr-live-potion-8114', 870013, 'Supremacy: Enlightenment', 62.0),
        ('dr-live-potion-8115', 870014, 'Supremacy: Might', 93.0),
        ('dr-live-potion-8116', 870015, 'Supremacy: Deftness', 93.0),
    ]:
        edits.append(spell(source, sid, Name=name, Value=value, Duration=TWO_HOURS, Description=SUPREMACY_TEXT))
    edits.append(spell('1091', 870016, Name='Supremacy: Resistance', Value=10.0, Duration=TWO_HOURS, Target='Self',
                       Range=0, Radius=0, Power=0, CastTime=0.0, Concentration=0, Pulse=0, Frequency=0,
                       SharedTimerGroup=0, Description='+10% resistance to heat, cold, matter, body, spirit and energy.'))
    edits.append(spell('31048', OMNI_HEAL, Name='Draught of Omni-Healing',
                       Description='Restores health, power and endurance at once.'))
    edits.append(spell('31052', OMNI_REGEN, Name='Draught of Omni-Regen',
                       Description='Increases health, power and endurance regeneration.'))
    edits.append(spell('1715', TENACITY, Name='Draught of Tenacity', Target='Self', Range=0, Radius=0, Duration=1200,
                       Power=0, Concentration=0, Pulse=0, Frequency=0, CastTime=3.0, SharedTimerGroup=2,
                       Description='Reduces the endurance your attacks cost by 25% for 20 minutes.'))
    edits.append(spell('31048', RESTORATION, Name='Draught of Restoration', Type='SickHeal', Value=0.0, Damage=0.0,
                       Description='Removes resurrection illness.'))

    param = 870101
    for parent, children in ((SUPREMACY, [870011, 870012, 870013, 870014, 870015, 870016]),
                             (OMNI_HEAL, [31050, 31049])):
        for child in children:
            edits.append({'table': 'SpellXCustomValues', 'insert': {
                'CustomParamID': param, 'SpellID': parent, 'KeyName': 'MultipleSubSpells', 'Value': str(child)}})
            param += 1

    for sid in USED_BY:
        for line in ('Item Effects', 'Potions'):
            key = f'dr-blank-potion-{line.split()[0].lower()}-{sid}'
            edits.append({'table': 'LineXSpell', 'key': key,
                          'insert': {'LineXSpell_ID': key, 'LineName': line, 'SpellID': sid, 'Level': 1}})
    for sid, items in USED_BY.items():
        for item in items:
            values = {'SpellID': sid}
            if sid == SUPREMACY:
                values['CanUseEvery'] = 30
            edits.append({'table': 'ItemTemplate', 'key': item, 'set': values})

    for essence in ESSENCES:
        edits.append({'table': 'ItemTemplate', 'key': essence, 'set': {'Price': 10000}})
    for merchant, (page, first) in MERCHANT_SLOTS.items():
        for i, essence in enumerate(ESSENCES):
            key = f'dr-blank-potion-{merchant}-{essence}'
            edits.append({'table': 'MerchantItem', 'key': key, 'insert': {
                'MerchantItem_ID': key, 'ItemListID': merchant, 'ItemTemplateID': essence,
                'PageNumber': page, 'SlotPosition': first + i}})

    with open(out, 'w', encoding='utf-8') as f:
        json.dump(edits, f, indent=1)
    print(len(edits), 'edits ->', out)


if __name__ == '__main__':
    main(*sys.argv[1:])
