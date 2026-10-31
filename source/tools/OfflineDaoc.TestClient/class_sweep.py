# Plays one character of every class through its abilities, several at a time, and keeps what the game said.
#   python class_sweep.py <TestClient.exe> <runtime> <findings log> <results.json> [--classes a,b,c] [--workers 6]
# Run with the server up. It reports what happened; deciding what is wrong is left to whoever reads the results.
import json
import queue
import re
import subprocess
import sys
import threading
import time

exe, runtime, log, out = sys.argv[1:5]
opts = sys.argv[5:]
only = opts[opts.index('--classes') + 1].split(',') if '--classes' in opts else None
workers = int(opts[opts.index('--workers') + 1]) if '--workers' in opts else 6
table = json.load(open(exe.rsplit('\\', 1)[0] + '\\classes.json', encoding='utf8'))
table.update({k: table.get(k, {}) for k in []})

p = subprocess.Popen([exe, '--runtime', runtime, '--account', 'aisweep', '--shared-log', log], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.DEVNULL, text=True, encoding='utf8')
waiting = {}
lock = threading.Lock()
counter = [0]


def reader():
    for line in p.stdout:
        r = json.loads(line)
        waiting.pop(r['id']).put(r)


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


# Low level mobs near each realm's start: the point is what abilities do, not surviving.
HOME = {1: (1, 'Camelot Hills'), 2: (100, 'Vale of Mularn'), 3: (200, "Lough Derg")}
spots = {}
for realm, (region, _) in HOME.items():
    rows = call('db_query', sql=f"select X,Y,Z,Name,Level from Mob where Region={region} and Level between 8 and 20 and (Guild is null or Guild='') "
                                "and RoamingRange>0 and Name not like '%guard%' limit 400")
    spots[realm] = rows if isinstance(rows, list) else []
    print('spots', realm, len(spots[realm]), flush=True)

NOTABLE = re.compile(r"not implemented|not found|invalid|error|can't|cannot|must |unable|no effect|resist|fail|nothing|need |don't|isn't|not ", re.I)
results = {}


def messages(r):
    if not isinstance(r, dict):
        return []
    return [e['Data'].get('text') or '' for e in r.get('events', []) if e['Kind'] == 'message'
            and '<Faction>' not in (e['Data'].get('text') or '') and not re.match(r'^\[\w+\]', e['Data'].get('text') or '')]


def play(key):
    row = table[key]
    session = re.sub(r'[^a-z]', '', key)[:14]
    name = ('Sw' + session).capitalize()[:18]
    res = {'class': row['name'], 'realm': row['realm'], 'issues': [], 'said': {}}
    results[key] = res

    def note(lines):
        for t in lines:
            c = re.sub(r'\d+', '#', t)[:90]
            res['said'][c] = res['said'].get(c, 0) + 1

    try:
        s = call('login', session=session, name=name, **{'class': key})
        if 'error' in s or not isinstance(s, dict):
            res['issues'].append('login: ' + str(s)[:160])
            return
        r = call('prepare', session=session, **{'class': key})
        if 'error' in r:
            res['issues'].append('prepare: ' + str(r)[:160])
            return
        kit = r['result']['kit']
        res['worn'] = sum(1 for k in kit if k.get('worn'))
        res['unworn'] = [k['slot'] for k in kit if not k.get('worn')]
        res['specs'] = [(x['spec'], x['level']) for x in r['result']['specs']]
        res['hp'] = r['result']['you']['max_health']
        note(messages(r))
        a = call('abilities', session=session)
        spells = [x['name'] for x in a.get('spells', [])]
        lines = a.get('spell_lines', [])
        lined = [x['name'] for l in lines for x in l['spells'] if x['kind'] == 'spell']
        styles = [x['name'] for x in a.get('styles', [])]
        res.update(spells=len(spells), line_spells=len(lined), lines=[l['name'] for l in lines], styles=len(styles),
                   abilities=len(a.get('abilities', [])))
        time.sleep(30)                                   # the PvP invulnerability timer after login
        spot = spots[row['realm']][hash(key) % max(1, len(spots[row['realm']]))]
        call('teleport', session=session, x=spot['X'] + 150, y=spot['Y'], region=HOME[row['realm']][0])
        time.sleep(2)
        r = call('fight', session=session, who='@mob', timeout_s=25)
        if isinstance(r, dict) and 'result' in r:
            res['fight'] = r['result']['outcome']
            note(messages(r))
        if call('status', session=session).get('dead'):
            call('release', session=session)
        # The strongest few of everything: each name once, newest (highest level) first.
        picks = list(dict.fromkeys(reversed(spells + lined)))[:12]
        for spell in picks:
            r = call('cast', session=session, spell=spell, who='@mob', wait_ms=3200)
            if isinstance(r, dict) and 'events' in r:
                note(messages(r))
                res.setdefault('cast', {})[spell] = {'did': [e['Kind'] for e in r['events'] if e['Kind'] in ('cast', 'spell_effect', 'interrupt')][:3],
                                                     'said': [t[:110] for t in messages(r) if 'attacks you' not in t and 'hits your' not in t][:4]}
            else:
                res['issues'].append(f'cast {spell}: {str(r)[:110]}')
            if call('status', session=session).get('dead'):
                call('release', session=session)
                break
        for style in list(reversed(styles))[:4]:
            r = call('use_ability', session=session, name=style, wait_ms=600)
            if isinstance(r, dict) and 'events' in r:
                note(messages(r))
        res['unknown'] = call('status', session=session).get('unknown_packets', '') if False else ''
    except Exception as e:                                # keep the sweep going: one class must not stop the rest
        res['issues'].append('exception: ' + repr(e)[:200])
    finally:
        try:
            call('logout', session=session)
        except Exception:
            pass
        print('done', key, 'worn', res.get('worn'), 'spells', res.get('spells'), res.get('line_spells'), 'styles', res.get('styles'), res['issues'][:1], flush=True)


keys = [k for k in sorted(table) if (only is None or k in only)]
todo = queue.Queue()
for k in keys:
    todo.put(k)


def worker():
    while True:
        try:
            k = todo.get_nowait()
        except queue.Empty:
            return
        play(k)


threads = [threading.Thread(target=worker) for _ in range(workers)]
for t in threads:
    t.start()
for t in threads:
    t.join()
json.dump(results, open(out, 'w', encoding='utf8'), indent=1)
p.stdin.close()
p.wait(timeout=30)
print('finished', len(results))
