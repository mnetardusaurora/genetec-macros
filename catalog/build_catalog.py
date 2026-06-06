#!/usr/bin/env python3
"""Build the public macro pages for the catalog site.

This script copies a macro's README into the site only when that macro is marked
Public. The rule is fail-closed: a macro is published only when its guide's
at-a-glance table contains a Visibility row whose value is Public. Anything that
is Restricted, unmarked, or unreadable is skipped. Planning notes under each
macro's development folder are never read or copied.

Run this before building or serving the site:

    python catalog/build_catalog.py
    mkdocs build -f catalog/mkdocs.yml
"""

import re
import shutil
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent
MACROS_DIR = REPO_ROOT / "Macros"
OUTPUT_DIR = SCRIPT_DIR / "docs" / "macros"

VISIBILITY_ROW = re.compile(r"\|\s*\*\*Visibility\*\*\s*\|\s*(?P<value>[^|]+)\|", re.IGNORECASE)


def slugify(text):
    """Turn a folder name into a lowercase, hyphenated, file-safe slug."""
    slug = re.sub(r"[^A-Za-z0-9]+", "-", text).strip("-").lower()
    return slug or "macro"


def first_heading(markdown_text, fallback):
    """Return the text of the first level-one heading, or a fallback."""
    for line in markdown_text.splitlines():
        if line.startswith("# "):
            return line[2:].strip()
    return fallback


def visibility_of(markdown_text):
    """Return 'public', 'restricted', or 'unmarked' for a macro guide."""
    match = VISIBILITY_ROW.search(markdown_text)
    if not match:
        return "unmarked"
    value = match.group("value").strip().lower()
    if "restricted" in value:
        return "restricted"
    if "public" in value:
        return "public"
    return "unmarked"


def main():
    # Start from a clean output folder so a macro that flips back to Restricted
    # cannot leave a stale published page behind.
    if OUTPUT_DIR.exists():
        shutil.rmtree(OUTPUT_DIR)
    OUTPUT_DIR.mkdir(parents=True)

    published = []
    skipped = []

    readmes = sorted(MACROS_DIR.glob("*/*/README.md")) if MACROS_DIR.exists() else []
    for readme in readmes:
        macro_dir = readme.parent
        category = macro_dir.parent.name
        text = readme.read_text(encoding="utf-8")
        status = visibility_of(text)
        title = first_heading(text, macro_dir.name)

        if status != "public":
            skipped.append((category, title, status))
            continue

        category_slug = slugify(category)
        macro_slug = slugify(macro_dir.name)
        target_dir = OUTPUT_DIR / category_slug
        target_dir.mkdir(parents=True, exist_ok=True)
        (target_dir / (macro_slug + ".md")).write_text(text, encoding="utf-8")
        published.append((category, title))

    write_index(published)
    report(published, skipped)


def write_index(published):
    """Write the landing page for the Macros section."""
    lines = ["# Macros", ""]
    if not published:
        lines += [
            "No macros are published yet. New entries appear here as they are",
            "marked Public and reviewed.",
        ]
    else:
        lines += ["Browse the published macros below.", ""]
        by_category = {}
        for category, title in published:
            by_category.setdefault(category, []).append(title)
        for category in sorted(by_category):
            lines.append("## " + category)
            lines.append("")
            for title in sorted(by_category[category]):
                lines.append("- " + title)
            lines.append("")
    (OUTPUT_DIR / "index.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def report(published, skipped):
    """Print a transparent summary so nothing is dropped silently."""
    print("Catalog build summary")
    print("  published: {0}".format(len(published)))
    for category, title in published:
        print("    + [{0}] {1}".format(category, title))
    print("  skipped: {0}".format(len(skipped)))
    for category, title, status in skipped:
        print("    - [{0}] {1} ({2})".format(category, title, status))


if __name__ == "__main__":
    main()
