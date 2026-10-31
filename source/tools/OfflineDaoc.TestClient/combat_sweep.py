# Styles, buffs and item abilities at scale: one character of each class, plus an item bench, while the server runs.
#   python combat_sweep.py <TestClient.exe> <runtime> <findings log> <results.json> [--workers 8] [--items 90]
# The character is fought against real mobs of its home realm. What the game said is kept for a person to judge.
import json
import queue
import random
import re
import subprocess
import sys
import threading
import time

exe, runtime, log, out = sys.argv[1:5]
opts = sys.argv[5:]
workers = int(opts[opts.index('--workers') + 1]) if '--workers' in opts else 8
item_count = int(opts[opts.index('--items') + 1]) if '--items' in opts else 90
table = json.load(open(exe.rsplit('\\', 1)[0] + '\\classes.json', encoding='utf8'))
p = subprocess.Popen([exe, '--runtime', runtime, '--account', 'aisweep', '--shared-log', log], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.DEVNULL, text=True, encoding='utf8')
waiting = {}
lock = threading.Lock()
counter = [0]


def reader():
    for line in p.stdout:
        r = json.loads(line)
        box = waiting.pop(r['id'], None)
        if box:
            box.put(r)


threading.Thread(target=reader, daemon=True).start()


def call(tool, **args):
    box = queue.Queue()
    with lock:
        counter[0] += 1
        i = counter[0]
        waiting[i] = box
        p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": i, "method": "tools/call", "params": {"name": tool, "arguments": args}}) + "\n")
        p.stdin.flush()
    r = box.get(timeout=240)['result']
    text = r['content'][0]['text']
    try:
        return json.loads(text)
    except ValueError:
        return {'error': text} if r.get('isError') else text


HOME = {1: 1, 2: 100, 3: 200}
spots = {}
for realm, region in HOME.items():
    rows = call('db_query', sql=f"select X,Y,Z,Name,Level from Mob where Region={region} and Level between 30 and 45 and (Guild is null or Guild='') "
                                "and RoamingRange>0 and Flags=0 and Name not like '%guard%' limit 3000", limit=3000)
    spots[realm] = rows if isinstance(rows, list) else []

CHATTER = re.compile(r'<Faction>|^\[\w+\]|just left the game|reached level 50|just logged|has entered')


def words(r):
    if not isinstance(r, dict):
        return []
    return [e['Data'].get('text') or '' for e in r.get('events', []) if e['Kind'] == 'message' and not CHATTER.search(e['Data'].get('text') or '')]


def alive(session):
    s = call('status', session=session)
    if isinstance(s, dict) and s.get('dead'):
        call('release', session=session)
        return False
    return True


results = {}


def play(key):
    row = table[key]
    session = re.sub(r'[^a-z]', '', key)[:14]
    res = {'class': row['name'], 'realm': row['realm'], 'styles': {}, 'spells': {}, 'issues': []}
    results[key] = res
    try:
        s = call('login', session=session, name=('Sw' + session).capitalize()[:18], **{'class': key})
        if 'error' in s:
            res['issues'].append(str(s)[:160])
            return
        r = call('prepare', session=session, **{'class': key})
        if isinstance(r, dict) and 'result' in r:
            res['hp'] = r['result']['you']['max_health']
        ab = call('abilities', session=session)
        styles = [x['name'] for x in ab.get('styles', [])]
        spells = [x['name'] for x in ab.get('spells', [])] + [x['name'] for l in ab.get('spell_lines', []) for x in l['spells'] if x['kind'] == 'spell']
        spells = list(dict.fromkeys(reversed(spells)))
        time.sleep(30)
        spot = random.choice(spots[row['realm']])
        call('teleport', session=session, x=spot['X'] + 120, y=spot['Y'], region=HOME[row['realm']])
        time.sleep(3)
        res['area'] = spot['Name'], spot['Level']
        # Styles in a live melee, in order: anything can be refused for a good reason, so every answer is kept.
        for style in styles[-14:]:
            if not alive(session):
                break
            foe = call('nearby', session=session, kind='npc', radius=900, limit=5)
            mob = next((e for e in foe if isinstance(e, dict) and e['realm'] == 0 and not e['guild'] and e['name'] and e['health_pct'] > 0), None) if isinstance(foe, list) else None
            if mob is None:
                spot = random.choice(spots[row['realm']])
                call('teleport', session=session, x=spot['X'] + 120, y=spot['Y'], region=HOME[row['realm']])
                time.sleep(3)
                continue
            call('fight', session=session, who=str(mob['oid']), timeout_s=4)
            call('command', session=session, text='/assist', wait_ms=50) if False else None
            r = call('use_ability', session=session, name=style, wait_ms=2600)
            res['styles'][style] = [t[:90] for t in words(r) if re.search(r'perform|style|requires|must|can only|prepare', t, re.I)][:2]
        call('stop', session=session)
        # Everything cast twice: at a mob, then at yourself when the mob got nothing.
        name = ('Sw' + session).capitalize()[:18]
        for spell in spells[:16]:
            if not alive(session):
                break
            r = call('cast', session=session, spell=spell, who='@mob', wait_ms=3300)
            t = words(r)
            did = [e['Kind'] for e in r.get('events', []) if e['Kind'] in ('cast', 'spell_effect')] if isinstance(r, dict) else []
            if not did:
                r = call('cast', session=session, spell=spell, who=name, wait_ms=3300)
                t += words(r)
                did = [e['Kind'] for e in r.get('events', []) if e['Kind'] in ('cast', 'spell_effect')] if isinstance(r, dict) else []
            res['spells'][spell] = {'did': did[:3], 'said': [x[:100] for x in t if 'attacks you' not in x and 'hits your' not in x][:3]}
    except Exception as e:
        res['issues'].append('exception: ' + repr(e)[:180])
    finally:
        try:
            call('logout', session=session)
        except Exception:
            pass
        print('done', key, len(res['styles']), len(res['spells']), res['issues'][:1], flush=True)


# Item abilities: a sample of everything with a use or proc.
def bench():
    session = 'itembench'
    res = {'items': {}, 'issues': []}
    results['_items'] = res
    try:
        call('login', session=session, name='Switembench', **{'class': 'armsman'})
        call('prepare', session=session, **{'class': 'armsman'})
        rows = call('db_query', limit=6000, sql="select i.Id_nb,i.Name,i.Item_Type,i.Level,i.SpellID,i.SpellID1,s.Name sname,s.Type stype from ItemTemplate i "
                    "join Spell s on s.SpellID=i.SpellID where i.Level between 1 and 51 and i.Item_Type between 10 and 37 and i.Id_nb not like '% %'")
        random.Random(3).shuffle(rows)
        seen = set()
        picks = []
        for r in rows:
            if r['stype'] not in seen or len(picks) < item_count // 2:
                seen.add(r['stype'])
                picks.append(r)
            if len(picks) >= item_count:
                break
        time.sleep(30)
        for item in picks:
            r = call('command', session=session, text='/item create ' + item['Id_nb'], wait_ms=500)
            inv = call('inventory', session=session)
            mine = [i for i in inv if i['name'].endswith(item['Name']) and i['slot'] >= 40] if isinstance(inv, list) else []
            if not mine:
                res['items'][item['Name']] = {'type': item['stype'], 'said': ['not created'] + [t[:80] for t in words(r)][:1]}
                continue
            call('move_item', session=session, **{'from': mine[0]['slot'], 'to': item['Item_Type']})
            time.sleep(0.4)
            r = call('use_item', session=session, slot=item['Item_Type'])
            res['items'][item['Name']] = {'type': item['stype'], 'spell': item['sname'], 'said': [t[:100] for t in words(r)][:3],
                                          'did': [e['Kind'] for e in r.get('events', []) if e['Kind'] in ('cast', 'spell_effect')][:2] if isinstance(r, dict) else []}
            time.sleep(10.3)                              # the charged item lockout
    except Exception as e:
        res['issues'].append('exception: ' + repr(e)[:180])
    finally:
        try:
            call('logout', session=session)
        except Exception:
            pass
        print('done items', len(res['items']), res['issues'][:1], flush=True)


todo = queue.Queue()
for k in sorted(table):
    todo.put(k)


def worker():
    while True:
        try:
            k = todo.get_nowait()
        except queue.Empty:
            return
        play(k)
        json.dump(results, open(out, 'w', encoding='utf8'), indent=1)


threads = [threading.Thread(target=worker) for _ in range(workers)] + [threading.Thread(target=bench)]
for t in threads:
    t.start()
for t in threads:
    t.join()
json.dump(results, open(out, 'w', encoding='utf8'), indent=1)
p.stdin.close()
p.wait(timeout=30)
print('finished', len(results))
