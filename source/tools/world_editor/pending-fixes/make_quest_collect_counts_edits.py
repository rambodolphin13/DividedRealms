"""DR-269: data quests whose turn-in step takes one item although earlier steps handed out several of it.

A collect step (StepType 10 Collect / 11 CollectFinish) reads its CollectItemTemplate entry; "id;N" (N >= 2) makes
the giver take N (QuestDeliveryItems.CollectEntry). The classic quests give one item per earlier kill/search step
(StepItemTemplates, "id" or "id;drop chance") and never set N, so the player hands in one and keeps the rest. N here is the number of steps
since the previous collect of the same item whose step item is that item.

Usage: python make_quest_collect_counts_edits.py <out.edits.json>   (reads the modern test world through cli.py sql)
"""
import json, subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CLI = HERE.parent / "cli.py"
COLLECT = {10, 11}


def rows():
    out = subprocess.run([sys.executable, str(CLI), "--profile", "modern", "sql", "--limit", "100000",
                          "SELECT ID, Name, StepType, CollectItemTemplate, StepItemTemplates FROM DataQuest"],
                         capture_output=True, text=True, check=True).stdout
    return json.loads(out[out.index("["):])


def plan(row):
    types = [int(t) if t.strip().isdigit() else -1 for t in (row["StepType"] or "").split("|")]
    collects = (row["CollectItemTemplate"] or "").split("|")
    items = (row["StepItemTemplates"] or "").split("|")
    changed, notes = list(collects), []
    since = 0
    for step, kind in enumerate(types):
        if kind in COLLECT and step < len(collects):
            entry = collects[step].strip()
            if entry and ";" not in entry:
                given = sum(1 for s in range(since, step) if s < len(items) and items[s].split(";")[0].strip().lower() == entry.lower())
                if given >= 2:
                    changed[step] = f"{entry};{given}"
                    notes.append(f"step {step + 1}: {entry} x{given}")
            since = step + 1
    return ("|".join(changed), notes) if notes else (None, [])


def main(out_path):
    edits, report = [], []
    for row in rows():
        new, notes = plan(row)
        if new is not None:
            edits.append({"table": "DataQuest", "key": str(row["ID"]), "set": {"CollectItemTemplate": new}})
            report.append(f'{row["ID"]} {row["Name"]}: ' + "; ".join(notes))
    Path(out_path).write_text(json.dumps(edits, indent=1) + "\n", encoding="utf-8")
    print("\n".join(report))
    print(f"{len(edits)} quests")


if __name__ == "__main__":
    main(sys.argv[1])
