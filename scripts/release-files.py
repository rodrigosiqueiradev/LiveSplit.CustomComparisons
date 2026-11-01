#!/usr/bin/env python3
"""Turns CHANGELOG.md into the files a release needs.

    python3 scripts/release-files.py 0.2.0 OUT_DIR

Writes, into OUT_DIR:
  update.LiveSplit.CustomComparisons.xml  every version in the changelog, newest first,
                                           in the format LiveSplit's updater reads
  release-notes.md                         this version's changelog lines

Fails if the changelog has no section for the version, so a release can't go out without one.
"""

import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr

DLL = "LiveSplit.CustomComparisons.dll"


def read_changelog(path: Path) -> list[tuple[str, list[str]]]:
    sections: list[tuple[str, list[str]]] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        heading = re.match(r"^##\s+v?(\d+\.\d+\.\d+)\s*$", line)
        if heading:
            sections.append((heading.group(1), []))
        elif sections and line.startswith("- "):
            sections[-1][1].append(line[2:].strip())
    return sections


def version_key(version: str) -> tuple[int, ...]:
    return tuple(int(part) for part in version.split("."))


def update_xml(sections: list[tuple[str, list[str]]]) -> str:
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<updates>"]
    for version, changes in sorted(sections, key=lambda s: version_key(s[0]), reverse=True):
        lines.append(f"  <update version={quoteattr(version)}>")
        lines.append("    <files>")
        # Downloaded from the release (UpdateURL + path), saved into LiveSplit's Components folder.
        lines.append(f'      <file path="{DLL}" localPath="Components/{DLL}" status="changed" />')
        lines.append("    </files>")
        lines.append("    <changelog>")
        lines.extend(f"      <change>{escape(change)}</change>" for change in changes)
        lines.append("    </changelog>")
        lines.append("  </update>")
    lines.append("</updates>")
    return "\n".join(lines) + "\n"


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    version, out = sys.argv[1].lstrip("v"), Path(sys.argv[2])
    sections = read_changelog(Path(__file__).resolve().parent.parent / "CHANGELOG.md")
    changes = dict(sections).get(version)
    if not changes:
        print(f"CHANGELOG.md has no '## {version}' section with '- ' lines. Add one, then tag again.", file=sys.stderr)
        return 1

    out.mkdir(parents=True, exist_ok=True)
    (out / "update.LiveSplit.CustomComparisons.xml").write_text(update_xml(sections), encoding="utf-8")
    (out / "release-notes.md").write_text("".join(f"- {c}\n" for c in changes), encoding="utf-8")
    print(f"Release files for {version} written to {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
