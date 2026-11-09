# Builds classes.json from the server's own class files: what a character of each class needs to pass
# CharacterCreateRequestHandler.IsCharacterValid. Run from this folder after a class or race changes.
import glob
import json
import os
import re

SERVER = os.path.join('..', '..', 'server', 'GameServer')
RACE_IDS = {'Briton': 1, 'Avalonian': 2, 'Highlander': 3, 'Saracen': 4, 'Norseman': 5, 'Troll': 6, 'Dwarf': 7, 'Kobold': 8, 'Celt': 9,
            'Firbolg': 10, 'Elf': 11, 'Lurikeen': 12, 'Inconnu': 13, 'Valkyn': 14, 'Sylvan': 15, 'HalfOgre': 16, 'Frostalf': 17, 'Shar': 18,
            'AlbionMinotaur': 19, 'MidgardMinotaur': 20, 'HiberniaMinotaur': 21, 'Korazh': 19, 'Deifrang': 20, 'Graoch': 21}
# eLivingModel, male
MODELS = {1: 32, 2: 61, 3: 39, 4: 48, 5: 503, 6: 137, 7: 185, 8: 169, 9: 302, 10: 286, 11: 334, 12: 318, 13: 716, 14: 773, 15: 700,
          19: 1395, 20: 1407, 21: 1419}
FEMALE_MODELS = {1: 35, 2: 65, 3: 43, 4: 52, 5: 507, 6: 145, 7: 193, 8: 177, 9: 310, 10: 294, 11: 342, 12: 326, 13: 724, 14: 781, 15: 708}
FEMALE_ONLY = ('Valkyrie', 'Bainshee')      # GlobalConstants.CLASS_GENDER_CONSTRAINTS_DICT
REALMS = {'albion': 1, 'midgard': 2, 'hibernia': 3}
ORDER = ['STR', 'DEX', 'CON', 'QUI', 'INT', 'PIE', 'EMP', 'CHR']   # the create packet's order


def starting_stats():
    text = open(os.path.join(SERVER, 'GlobalConstants.cs'), encoding='utf8', errors='ignore').read()
    stats = {}
    for name, body in re.findall(r'\{\s*eRace\.(\w+),\s*new Dictionary<eStat, int>\(\)\s*\{(.*?)\}\s*\}', text):
        if name in RACE_IDS:
            stats[RACE_IDS[name]] = {k: int(v) for k, v in re.findall(r'eStat\.(\w+),\s*(\d+)', body)}
    return stats


def main():
    stats = starting_stats()
    enum = open(os.path.join(SERVER, 'Enums', 'eCharacterClass.cs'), encoding='utf8', errors='ignore').read()
    ids = {k: int(v) for k, v in re.findall(r'(\w+)\s*=\s*(\d+)', enum)}
    table = {}
    for realm, realm_id in REALMS.items():
        for path in sorted(glob.glob(os.path.join(SERVER, 'playerclasses', realm, 'Class*.cs'))):
            text = open(path, encoding='utf8', errors='ignore').read()
            # (Savage spells the attribute out in full: [CharacterClassAttribute(...)])
            head = re.search(r'\[CharacterClass(?:Attribute)?\(\(int\)eCharacterClass\.(\w+),\s*"([^"]+)"', text)
            if not head:
                continue
            races = []
            block = re.search(r'EligibleRaces\s*=>(.*?);', text, re.S)     # (newer classes hide the list behind a setting)
            if block:
                live = '\n'.join(line.split('//')[0] for line in block.group(1).splitlines())
                races = [RACE_IDS[r] for r in re.findall(r'PlayerRace\.(\w+)', live) if r in RACE_IDS]
            usable = [r for r in races if r in MODELS and r in stats]
            if not usable:
                print('no usable race:', head.group(2), races)
                continue
            race = usable[0]
            base = dict(stats[race])
            picked = []
            for slot in ('Primary', 'Secondary', 'Tertiary'):
                found = re.search(r'm_%sStat\s*=\s*eStat\.(\w+)' % slot.lower(), text)
                if found and found.group(1) not in picked and found.group(1) in base:
                    picked.append(found.group(1))
            for name in ('STR', 'CON', 'DEX', 'QUI', 'INT', 'PIE', 'EMP', 'CHR'):
                if len(picked) < 3 and name not in picked:
                    picked.append(name)
            for name in picked:
                base[name] += 10
            table[head.group(1).lower()] = {     # (the enum name: three realms share the name Mauler)
                'name': head.group(2), 'class_id': ids[head.group(1)], 'realm': realm_id, 'race': race,
                'gender': 1 if head.group(1) in FEMALE_ONLY else 0,
                'model': FEMALE_MODELS[race] if head.group(1) in FEMALE_ONLY else MODELS[race],
                'stats': [base[s] for s in ORDER]}
    with open('classes.json', 'w', encoding='utf8') as out:
        json.dump(table, out, indent=1, sort_keys=True)
    print(len(table), 'classes')


main()
