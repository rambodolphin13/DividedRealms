"""Combine the per-class spell fix drafts of 2026-11-11 into one World Workshop edits file.

The drafts came from the reference comparison of our classes with live 1.130a (dr agent sort spells: rows judged
"fix"), were written per class by dr agent draft spells and tried on sandboxes by dr agent testdrafts (no drafted
change broke a spell; Research\\Audits\\Spell-Gaps-Sorted-20261111\\drafts\\TESTED.md). This script only merges them:
one edit per table row, the "set" values of every draft that touches the row joined. Two drafts that set the same
column of the same row to different values stop the script.

    python make_class_spell_fixes_edits.py <drafts folder> <out edits.json>
"""
import json
import sys
from pathlib import Path


def main(drafts, out):
    merged, order, sources, wheres, other = {}, [], {}, {}, []
    files = sorted(Path(drafts).glob('*/*.edits.json'))
    for f in files:
        for e in json.loads(f.read_text(encoding='utf-8')):
            if set(e) == {'table', 'where', 'set'}:
                # A "set where" edit (a draft that named its rows by a column): kept whole, keyed by its condition.
                k = (e['table'], 'where:' + json.dumps(e['where'], sort_keys=True))
                wheres[k] = e['where']
            elif set(e) == {'table', 'key', 'set'}:
                k = (e['table'], str(e['key']))
            else:
                # copy / insert / delete: kept as written, after the set edits (a copied row may be set by a later draft).
                if e not in other:
                    other.append(e)
                continue
            if k not in merged:
                merged[k] = {}
                order.append(k)
            for col, val in e['set'].items():
                if col in merged[k] and merged[k][col] != val:
                    raise SystemExit(f'{f}: {k} {col} = {val!r}, but {sources[k, col]} set {merged[k][col]!r}')
                merged[k][col] = val
                sources[k, col] = f.parent.name
    edits = [{'table': t, 'where': wheres[t, key], 'set': merged[t, key]} if (t, key) in wheres else
             {'table': t, 'key': key, 'set': merged[t, key]} for t, key in order]
    edits = other + edits      # new rows first, so a set on a copied row finds it
    Path(out).write_text(json.dumps(edits, indent=1, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'{len(edits)} edits ({len(other)} copy/insert/delete) from {len(files)} draft files -> {out}')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
