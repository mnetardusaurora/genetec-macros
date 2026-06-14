# Design: Macro guide versioning, removal-impact, troubleshooting, and a plainer website voice

Date: 2026-06-12
Author: Matthew Netardus
Status: Approved for planning

## Problem

The macro guides and the public catalog do three things less well than they should:

1. A website visitor cannot tell what a macro does to their system if they remove
   it. The guides explain how to roll back, but not what stops happening or what
   silently lapses as a consequence.
2. There is no troubleshooting help. When a macro behaves unexpectedly, the reader
   has only the failure-modes table and a list of log strings, with no map from an
   observed symptom to a likely cause and a fix.
3. There is no version history. When a macro changes, a visitor has no way to know
   which version they are looking at or what changed since the last one.

Separately, the catalog's landing and About pages read as polished and slightly
impersonal. The author wants them to sound like a person who has spent a career in
electronic security sharing ideas, hoping other teams use them and collaborate on
improvements.

## Scope

In scope:

- Update `templates/macro-guide-template.md` with three new sections and a version
  row.
- Update `templates/macro-header.txt` with a Version field.
- Document the versioning policy and add checklist items in
  `docs/conformance-checklist.md`.
- Rewrite `catalog/docs/index.md` and `catalog/docs/about.md` in a plainer voice and
  add real contact details.
- Retrofit all four existing macros (READMEs and `.cs` headers) to the new standard.

Out of scope:

- No change to `catalog/build_catalog.py`. The version row and changelog are plain
  Markdown that the build copies verbatim. Only the Visibility row drives publishing,
  and that is unchanged.
- No change to `mkdocs.yml` `site_description`. It is already plain.
- No version bump for the four existing macros beyond their first release. Adding
  these guide sections is a documentation change, not a macro behavior change.

## Design

### 1. New README sections (standard, in the template)

These are added to `templates/macro-guide-template.md` so every future macro guide
carries them.

**a. Version row** in the at-a-glance table, placed near the top so a visitor sees
it first:

```
| **Version** | 1.0.0 |
```

**b. `## If this macro is removed`**, a standalone section placed immediately after
`Rollback / disabling`. It covers consequences, not procedure. Four labeled bullets:

```
## If this macro is removed

If the Macro entity is deleted or left disabled:

- **What stops happening:** <the recurring action this macro performed>.
- **What keeps working:** <core Security Center behavior that does not depend on this macro>.
- **What breaks or silently lapses:** <any check, sync, or alarm that no longer runs, and why that matters>.
- **What to do instead:** <the manual fallback or alternative, or state that none is needed>.
```

**c. `## Troubleshooting`**, a symptom table placed just before
`Log strings worth grepping` so its "What to do" column can point at those strings:

```
## Troubleshooting

If the macro behaves unexpectedly, find the symptom below.

| Symptom (what you see) | Likely cause | What to do |
|------------------------|--------------|------------|
| <observed behavior> | <likely cause> | <fix, and the log string to confirm it> |
```

**d. `## Changelog`**, placed at the very bottom, newest version first. Dash-free
date format per the project tone rules:

```
## Changelog

Newest version first. The version here matches the Version field in the macro header
and the Version row in the table above.

### Version 1.0.0 (YYYY-MM-DD)

- First release.
```

### 2. Version in the `.cs` header

Add one line to the identity block in `templates/macro-header.txt`, between
Visibility and Platform:

```
//  Visibility      <Public | Restricted>
//  Version         <MAJOR.MINOR.PATCH>
//  Platform        Security Center 5.13 (also targets 5.12)
```

### 3. Versioning policy (documented in the conformance checklist)

Semantic versioning, MAJOR.MINOR.PATCH:

- **MAJOR** for a breaking change: a new required parameter, changed behavior, or a
  new write to the system.
- **MINOR** for a new optional capability that does not break an existing setup.
- **PATCH** for a bugfix or internal change with no behavior change for the operator.

Rules:

- The README Version row, the `.cs` header Version field, and the top Changelog
  entry must always match.
- A pure guide or prose edit does not bump the version. The version tracks macro
  behavior only.
- The first shipped release of a macro is 1.0.0.

New checklist items under the Guide section of `docs/conformance-checklist.md`:

- The at-a-glance table has a Version row matching the `.cs` header Version field.
- The guide has an `If this macro is removed` section.
- The guide has a `Troubleshooting` section.
- The guide has a `Changelog` section whose newest entry matches the Version row.

### 4. Website voice rewrite

`catalog/docs/index.md` landing pitch is rewritten to a plainer first-person voice:
the author has worked in electronic security their whole career, kept building small
macros to fix recurring problems, and shares the general ones. The polished closing
tagline is removed. A new closing section invites collaboration and bug reports and
gives the GitHub handle and email.

`catalog/docs/about.md` is rewritten to soften the resume. It drops the specific
Director-level titles and employer names in favor of "spent my whole career in
electronic security and access control." The opening line reads "My name is Matthew
Netardus." The empty `Get in touch` placeholder is filled with real contact details.

Contact details used on both pages:

- GitHub: https://github.com/mnetardusaurora (handle `mnetardusaurora`)
- Email: mnetardus@auroranexus.ai

The `index.md` "How to use one" step 1 is updated to also point readers at the new
troubleshooting notes.

### 5. Retrofit the four existing macros

All four are existing first releases, so each is set to Version 1.0.0, dated from its
`.cs` `Created` header:

| Macro | Folder | Version | Date |
|-------|--------|---------|------|
| ITAR Door Access Review | `Macros/AccessControl/ITAR Door Access Review` | 1.0.0 | 2026-06-06 |
| God Mode Access Level Audit | `Macros/Access Levels/God Mode Access Level Macro` | 1.0.0 | 2026-05-31 |
| Area-to-Partition Reconciliation | `Macros/Integrations/Area-to-Partition Reconciliation` | 1.0.0 | 2026-06-06 |
| Integration Partition Sync | `Macros/Integrations/Integration Partition Sync` | 1.0.0 | 2026-06-06 |

For each macro:

- Add `Version 1.0.0` to the `.cs` header identity block.
- Add the Version row to the README at-a-glance table.
- Add an `If this macro is removed` section with consequences specific to that macro.
- Add a `Troubleshooting` table with symptoms specific to that macro, reusing its
  existing log strings.
- Add a `Changelog` with a single `### Version 1.0.0 (date)` / `First release.` entry.

Two README shapes exist and both are handled:

- God Mode, Integration Partition Sync, and Area-to-Partition Reconciliation follow
  `templates/macro-guide-template.md` section for section. The new sections slot into
  the standard positions.
- ITAR Door Access Review has a richer custom structure (At a glance, Known
  limitations, and so on). It gets the same four additions adapted to its structure:
  the Version row in its At a glance table, the removal and troubleshooting sections
  near its Rollback section, and the Changelog at the bottom.

## Tone constraints (apply to every file touched)

- No em dashes or en dashes as punctuation. Use commas, parentheses, or two
  sentences.
- No "e.g.", "i.e.", or "etc.".
- No mention of AI or code generation.
- No emoji.
- No company, customer, or site names in macros or guides. (The About page is not a
  macro or guide, and per the approved decision it now names no employers anyway.)

## Verification

- Run the existing tone check over every touched macro folder:
  `grep -rnP '\x{2014}|\x{2013}|\be\.g\.|\bi\.e\.|\betc\.' "Macros/<Category>/<Macro Display Name>"`
  Expected: no output.
- Run `python catalog/build_catalog.py` and confirm all four macros still publish and
  the new sections appear in `catalog/docs/macros/...`.
- Confirm each macro's three version locations (README row, `.cs` header, top
  changelog entry) read 1.0.0.

## Out-of-scope follow-ups

- The author may optionally add the versioning policy to `CLAUDE.md` later. `CLAUDE.md`
  is gitignored in this repo, so the committed source of truth is the conformance
  checklist and the template comments.
