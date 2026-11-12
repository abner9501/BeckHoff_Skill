#!/usr/bin/env python3
import argparse
import os
import json
from pathlib import Path

from pypdf import PdfReader


def clean(value):
    return " ".join(str(value or "").replace("\x00", " ").split())


def main():
    parser = argparse.ArgumentParser(description="Index the local Beckhoff PDF library.")
    parser.add_argument("--root", type=Path, default=os.environ.get("BECKHOFF_LIBRARY_ROOT"), required=not bool(os.environ.get("BECKHOFF_LIBRARY_ROOT")))
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "references" / "index.jsonl",
    )
    parser.add_argument("--preview-pages", type=int, default=1)
    parser.add_argument(
        "--resume",
        action="store_true",
        help="Keep valid existing records and append PDFs that are not indexed yet.",
    )
    args = parser.parse_args()

    files = sorted(args.root.rglob("*.pdf"), key=lambda p: str(p).lower())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    indexed_paths = set()
    existing_errors = 0
    if args.resume and args.output.exists():
        valid_lines = []
        with args.output.open(encoding="utf-8") as existing:
            for line_number, line in enumerate(existing, 1):
                try:
                    record = json.loads(line)
                    indexed_paths.add(record["path"])
                    existing_errors += bool(record.get("error"))
                    valid_lines.append(json.dumps(record, ensure_ascii=False))
                except (json.JSONDecodeError, KeyError, TypeError):
                    print(f"Ignoring invalid existing record at line {line_number}", flush=True)
        with args.output.open("w", encoding="utf-8", newline="\n") as stream:
            for line in valid_lines:
                stream.write(line + "\n")

    pending = [pdf for pdf in files if str(pdf.relative_to(args.root)) not in indexed_paths]
    errors = existing_errors
    mode = "a" if args.resume else "w"
    with args.output.open(mode, encoding="utf-8", newline="\n") as stream:
        for number, pdf in enumerate(pending, len(indexed_paths) + 1):
            record = {
                "path": str(pdf.relative_to(args.root)),
                "folder": str(pdf.parent.relative_to(args.root)),
                "name": pdf.stem,
                "bytes": pdf.stat().st_size,
                "title": "",
                "subject": "",
                "preview": "",
                "pages": None,
                "error": "",
            }
            try:
                reader = PdfReader(str(pdf), strict=False)
                metadata = reader.metadata or {}
                record["title"] = clean(metadata.get("/Title"))
                record["subject"] = clean(metadata.get("/Subject"))
                record["pages"] = len(reader.pages)
                chunks = []
                for page in reader.pages[: max(0, args.preview_pages)]:
                    chunks.append(page.extract_text() or "")
                record["preview"] = clean(" ".join(chunks))[:6000]
            except Exception as exc:
                errors += 1
                record["error"] = clean(f"{type(exc).__name__}: {exc}")[:500]
            stream.write(json.dumps(record, ensure_ascii=False) + "\n")
            if number % 100 == 0 or number == len(files):
                print(f"Indexed {number}/{len(files)} PDFs; errors={errors}", flush=True)
    print(f"Wrote {args.output}")


if __name__ == "__main__":
    main()
