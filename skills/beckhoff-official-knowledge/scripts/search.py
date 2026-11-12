#!/usr/bin/env python3
import argparse
import os
import json
import re
from pathlib import Path


def normalize(value):
    return re.sub(r"[^0-9a-z\u4e00-\u9fff]+", " ", value.lower()).strip()


def score(record, phrase, terms):
    fields = {
        "name": normalize(record.get("name", "")),
        "path": normalize(record.get("path", "")),
        "title": normalize(record.get("title", "")),
        "subject": normalize(record.get("subject", "")),
        "preview": normalize(record.get("preview", "")),
    }
    weights = {"name": 14, "path": 10, "title": 9, "subject": 5, "preview": 2}
    total = 0
    for field, text in fields.items():
        if phrase and phrase in text:
            total += weights[field] * 5
        total += sum(weights[field] * text.count(term) for term in terms if term)
    return total


def snippet(text, terms, length=280):
    compact = " ".join((text or "").split())
    lowered = compact.lower()
    positions = [lowered.find(term) for term in terms if lowered.find(term) >= 0]
    start = max(0, min(positions) - 80) if positions else 0
    value = compact[start : start + length]
    return ("..." if start else "") + value + ("..." if start + length < len(compact) else "")


def main():
    parser = argparse.ArgumentParser(description="Search the indexed Beckhoff PDF library.")
    parser.add_argument("query", nargs="+", help="Model number or technical search terms")
    parser.add_argument("--limit", type=int, default=10)
    parser.add_argument("--root", type=Path, default=os.environ.get("BECKHOFF_LIBRARY_ROOT"), required=not bool(os.environ.get("BECKHOFF_LIBRARY_ROOT")))
    parser.add_argument(
        "--index",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "references" / "index.jsonl",
    )
    args = parser.parse_args()

    query = " ".join(args.query)
    phrase = normalize(query)
    terms = [term for term in phrase.split() if len(term) > 1]
    hits = []
    with args.index.open(encoding="utf-8") as stream:
        for line in stream:
            record = json.loads(line)
            value = score(record, phrase, terms)
            if value:
                hits.append((value, record))
    hits.sort(key=lambda item: (-item[0], item[1]["path"].lower()))

    output = []
    for value, record in hits[: max(1, args.limit)]:
        output.append(
            {
                "score": value,
                "path": str(args.root / record["path"]),
                "pages": record.get("pages"),
                "title": record.get("title") or record.get("name"),
                "snippet": snippet(record.get("preview", ""), terms),
                "index_error": record.get("error", ""),
            }
        )
    print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
