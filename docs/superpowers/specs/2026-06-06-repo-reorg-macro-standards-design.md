# Repository Reorganization & Macro Standards — Design

Date: 2026-06-06
Status: Approved (revised after discovering true branch state)
Branch: feature/repo-reorg-macro-standards (based on main)

## Problem

The repo needs one repeatable structure so every macro is organized and documented
the same way, the code looks professional when pasted into Config Tool, and nothing in
the repo reads as machine-generated. Today there is no standardized guide template, no
standardized macro header, no written human-tone rule, and the two existing macros were
each built ad hoc.

## True repository state (verified 2026-06-06)

The two macros live on separate, unmerged feature branches. Neither is on main.

- `main`: only the empty category placeholders (AccessControl, Alarms, Cardholders,
  Scheduled, Workflow), plus README and LICENSE. No macros.
- `feature/god-mode-access-audit`: the God Mode macro and its README under
  `Macros/Access Levels/God Mode Access Level Macro/`, plus
  `TechSec Concepts/God Mode Access Level Macro/` (concept and implementation plan).
- `feature/integration-partition-sync`: the Integration Partition Sync macro and its
  README under `Macros/Integrations/Integration Partition Sync/`, plus
  `docs/superpowers/{specs,plans}/`.

Because of this, the standards work is split into two kinds of change:

- Cross-cutting standards (macro-independent): templates, repo README, the project
  rules in CLAUDE.md, and the new-macro skill.
- Per-macro conformance (macro-specific): applying the standard header, the full guide
  sections, the development/ folder, and the tone scrub to each macro.

## Chosen strategy

Standards land on main; macros conform on their own branches.

- This branch is based on main and carries only the cross-cutting standards.
- Each macro branch later adopts the standards using a documented conformance checklist
  (this branch produces that checklist; it does not edit the macros).

## Decisions (from brainstorming)

1. Category folders: keep all current ones, including Scheduled, Workflow, and the
   empty placeholders.
2. Planning-doc home: hybrid. Drafting happens centrally in `docs/superpowers/`. When a
   macro ships, its final spec and plan are copied into a `development/` folder beside
   that macro as the permanent shipped record.
3. Human-written tone rule applies to both `.cs` files and `README.md` guides.
4. Guide template: the full standard section set (listed below).
5. The God Mode concept and implementation-plan docs (on the god-mode branch) move out
   of `TechSec Concepts/` into the macro's `development/` folder when that branch
   conforms; `TechSec Concepts/` is flattened to hold only pre-macro idea markdown.

## What lands on this branch (committed to git)

1. `templates/macro-guide-template.md` — the full standard guide template with
   placeholder prompts.
2. `templates/macro-header.txt` — the standard header block.
3. `README.md` (repo landing page) — rewritten to describe the layout standard, the
   per-macro folder contents, the hybrid doc model, the role of `TechSec Concepts/` and
   `docs/superpowers/`, and a pointer to `templates/`.
4. `docs/conformance-checklist.md` — the per-macro conformance steps each macro branch
   follows to adopt the standards.
5. This spec and its implementation plan, under `docs/superpowers/`.

## What lands on this branch (local only, NOT committed)

CLAUDE.md and `.claude/` are gitignored by project rule, so these edits are local to the
developer's working copy and do not travel through git:

6. `CLAUDE.md` — a new "Repository layout and macro deliverables" section codifying the
   standards. Existing SDK rules untouched.
7. `.claude/skills/new-macro/SKILL.md` — updated to enforce the intake order, scaffold
   the per-macro folder (`.cs` plus README from template plus empty `development/`),
   apply the standard header, use `MacroLogger` (not `Logger`), and obey the tone rules.

## Per-macro folder standard

Every macro folder contains exactly three things:

1. One `.cs` file, PascalCase, matching the class name. The paste target for Config Tool.
2. One `README.md`, the standardized guide.
3. A `development/` folder holding the final spec and plan, copied from
   `docs/superpowers/` when the macro ships.

## Standardized guide (README.md) sections, in order

1. Title and one-line summary
2. At-a-glance table (Platform, Macro file, Trigger, Category, Author)
3. Intent
4. Data it touches (every entity, custom field, and parameter read or written, marked read/write)
5. How it works each run (numbered steps)
6. Flowchart (Mermaid `flowchart`)
7. Architecture impact (Mermaid diagram of what it reads from and writes to)
8. Prerequisites
9. Parameters table (name, type, what to set)
10. Import and schedule steps
11. Failure modes (how it fails and how that looks in the log)
12. Risks (security and operational, plus the safe-default posture)
13. Rollback / disabling
14. Log strings worth grepping (table)

## Standardized macro header

An aligned comment block, refining the format already in use, scrubbed of machine-written tells:

```
// ----------------------------------------------------------------------------
//  <ClassName>.cs
//  <One-line plain-language purpose>
// ----------------------------------------------------------------------------
//  Purpose         <full-sentence description of what it does>
//  Trigger         <Scheduled | Event-driven | On-demand, with how it is run>
//  Category        <Macros subfolder>
//  Platform        Security Center 5.13 (also targets 5.12)
//
//  Reads           <entities and fields it reads>
//  Writes          <entities and fields it writes, or "nothing">
//  Parameters      <name (Type): meaning>
//  Custom fields   <name (Type), or "none">
//  Privileges      <run-as privileges required, or "none beyond default">
//
//  Author          Matthew Netardus
//  Created         YYYY-MM-DD
//  Guide           See README.md in this folder.
// ----------------------------------------------------------------------------
```

## Human-written tone rules (apply to .cs and README.md)

- No em dashes or en dashes as punctuation. Use commas, parentheses, or two sentences.
- No "e.g.", "i.e.", or "etc." Write "for example", "that is", or rephrase.
- No "generated by" or co-authoring lines, and no references to code-generation tools.
- No emoji in code or guides.
- Avoid the machine cadence: stacked "moreover/furthermore", over-hedging, decorative dashes.
- Plain, direct sentences. Comments explain why, not what.

## New-macro intake order

The new-macro skill asks these three first, in this order, before anything else:

1. Name (and the class name derived from it)
2. Category (which `Macros/` folder)
3. Trigger (event-driven vs scheduled vs on-demand)

Then purpose, custom fields, parameters. The skill scaffolds the full folder
(`.cs` plus `README.md` from the template plus an empty `development/`), applies the
standard header, uses `MacroLogger`, and obeys the tone rules.

## Per-macro conformance checklist (applied on each macro branch, not here)

For God Mode (feature/god-mode-access-audit):
- Reformat the `.cs` header to the standard block.
- Rewrite the README to the full standard sections.
- Scrub em dashes and "e.g." from the `.cs` and README.
- Create `development/`, move the two TechSec concept docs into it, remove the empty
  TechSec subfolder, leaving `TechSec Concepts/` flat.

For Integration Partition Sync (feature/integration-partition-sync):
- Reformat the `.cs` header to the standard block.
- Rewrite the README to the full standard sections.
- Scrub em dashes and "e.g." from the `.cs` and README.
- Add `development/` and copy the final spec and plan into it when the macro ships.

## Deferred deliverable

A recommendation for publicly sharing the macros via a static catalog website
(GitHub Pages or Cloudflare Pages generated from the standardized READMEs). Produced
after the reorganization is complete and approved. The standardization here is the
precondition that makes a generated catalog possible.

## Out of scope

- Building the public website itself (recommendation only, for now).
- Writing any new macro logic.
- Editing the macros on their own branches (covered by the conformance checklist,
  executed separately).
- Consolidating Access Levels and AccessControl (left to the developer's discretion).
