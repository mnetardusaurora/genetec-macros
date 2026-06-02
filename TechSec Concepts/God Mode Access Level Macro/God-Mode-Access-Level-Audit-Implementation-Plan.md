# God Mode Access Level Audit — Implementation Plan

> **For agentic workers:** This plan builds a single Genetec Security Center macro
> (`.cs` file). There is **no local build, no compiler, and no unit-test harness** —
> Security Center compiles the macro at runtime when pasted into a Macro entity.
> "Verification" therefore means: (a) confirming every SDK call against the guide via
> the `genetec-guide-researcher` agent, (b) running the `macro-reviewer` agent, and
> (c) a **manual lab paste-and-run** by the operator. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** A daily macro that verifies every Door is in a chosen "God Mode" access
rule, raises one alarm per missing door, and (behind a default-off toggle) adds
missing doors to the rule.

**Architecture:** One `UserMacro` subclass, triggered by a Config Tool scheduled
task. Reads all doors via a single cached `EntityConfigurationQuery`; checks each
door's access points against the rule; raises alarms for misses; optionally writes
missing access points into the rule via per-door transactions. Auto-add is gated by
a boolean parameter so the macro can run report-only first.

**Tech Stack:** C# 7.3 (Security Center macro compiler), Genetec SDK 5.13
(`Genetec.Sdk.*`). Single file, no external assemblies.

**Companion spec:** `God-Mode-Access-Level-Audit-Concept.md` (same folder).

---

## Deliverable file

- Create: `TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs`

This is the file the operator copies out of the repo and pastes into a Macro entity
in Config Tool. It deliberately lives in the concept folder (not `Macros/`) so it
sits next to its design docs as a self-contained deliverable.

## How "verification" replaces "tests" in this plan

Because there is no test runner, every task that introduces an SDK call follows this
rhythm instead of red/green TDD:

1. **Confirm the API** — `genetec-guide-researcher` (or the `.claude/knowledge/`
   file already cited) gives the verified signature.
2. **Write the code** for that block.
3. **Review** — at the end, `macro-reviewer` over the whole file, then the
   `pre-deploy-check` skill.
4. **Lab run** — operator pastes into a non-production Security Center and reads the
   macro log. This is the only true execution test.

## Open SDK items this plan must resolve (do not guess these)

These are NOT in `.claude/knowledge/` yet. Each has a dedicated verification step
below; if the researcher returns "not found in guide," stop and raise it rather than
inventing a signature.

- **R1 — Reading query results:** what `EntityConfigurationQuery.Query()` returns and
  how to extract the door GUIDs from it (Task 3).
- **R2 — DoorSide members:** the return types of `DoorSide.Reader`, `.EntrySensor`,
  `.Rex`, `.AccessPointSide` — AccessPoint objects vs GUIDs — and how to get each
  access point's `Guid` (Task 4).
- **R3 — Membership test:** exact usage of `AccessRule.IsMember(Guid)` vs reading
  `AccessRule.RelatedAccessPoints` (Task 4).
- **R4 — Mutation + transaction:** exact shape of
  `Sdk.TransactionManager.ExecuteTransaction(Action)` and
  `AccessPoint.AccessRules.Add(AccessRule)` (Task 6).
- **R5 — §7 membership definition:** which access points constitute "the door is in
  the rule" — resolved empirically in the lab (Task 8), not by reading the guide.

---

## Task 0: Branch setup

**Files:** none (git only).

- [ ] **Step 1: Create a feature branch** (global rule: never work on `main`).

```bash
git checkout -b feature/god-mode-access-audit
```

- [ ] **Step 2: Confirm forbidden paths are ignored** (project rule).

Run: `git status --short && cat .gitignore`
Expected: `.claude/`, `CLAUDE.md`, and `Guide/` do NOT appear as untracked-to-add.
The `TechSec Concepts/` folder may appear — that is fine to commit.

---

## Task 1: Scaffold the macro (header, usings, class, parameters, Execute skeleton)

**Files:**
- Create: `TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs`

**Verified from `.claude/knowledge/`:** `UserMacro` base class, `Execute()`,
`CleanUp()`, parameters as public properties, `MacroLogger.*`, required usings
(`user-macro.md`); on-demand/scheduled shape (`on-demand-macro.md`).

- [ ] **Step 1: Write the file scaffold.**

```csharp
// GodModeAccessLevelAudit.cs
//
// Purpose:       Daily audit that every Door is present in a chosen "God Mode"
//                access rule. Raises one alarm per missing door, and (when
//                EnableAutoAdd is true) adds the missing door's access points
//                to the rule.
// Trigger type:  Scheduled (run daily by a Config Tool scheduled task). Also
//                runs on demand.
// Required entities: one Access Rule ("God Mode"), one Alarm. Both pre-existing.
// Required custom fields: none.
// Required parameters: GodModeAccessRule (Guid), MissingDoorAlarm (Guid),
//                EnableAutoAdd (Boolean, default false).
// Date created:  2026-05-31
//
// NOTE: "Access level" in Security Center is modeled by the AccessRule entity.
// Rule membership is a list of ACCESS POINT GUIDs, not doors. See the concept doc.

using System;
using System.Collections.Generic;
using Genetec.Sdk;
using Genetec.Sdk.Scripting;
using Genetec.Sdk.Entities;
using Genetec.Sdk.Queries;
using Genetec.Sdk.Workflows;

public sealed class GodModeAccessLevelAudit : UserMacro
{
    // The access rule that must contain every door. Config Tool shows a picker.
    public Guid GodModeAccessRule { get; set; }

    // The alarm to raise once per missing door.
    public Guid MissingDoorAlarm { get; set; }

    // Default off: report-only (alarm + log). Set true to also add missing doors.
    public bool EnableAutoAdd { get; set; }

    public override void Execute()
    {
        MacroLogger.TraceInformation("GodModeAccessLevelAudit.Execute() started.");
        try
        {
            // Parameter validation     -> Task 2
            // Enumerate all doors      -> Task 3
            // Check each door + alarm  -> Task 4 + Task 5
            // Auto-add (if enabled)    -> Task 6
            // Summary                  -> Task 7
            MacroLogger.TraceInformation("GodModeAccessLevelAudit.Execute() completed.");
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "GodModeAccessLevelAudit.Execute() failed.");
        }
    }

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
```

- [ ] **Step 2: Sanity-check the scaffold** against `user-macro.md`: class is
  `sealed`, inherits `UserMacro`, `Execute()` is `public override void`, body wrapped
  in try/catch, entry+exit logging present, three parameters are public properties of
  supported types (`Guid`, `Guid`, `Boolean`). No `MacroParameters` attribute needed
  (defaults `SingleInstance=false`, `KeepRunningAfterExecute=false` are correct for a
  scheduled one-shot).

- [ ] **Step 3: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: scaffold God Mode access level audit macro"
```

---

## Task 2: Parameter validation and entity resolution

**Files:**
- Modify: `GodModeAccessLevelAudit.cs` (inside `Execute()`, replacing the
  "Parameter validation" comment).

**Verified from `.claude/knowledge/`:** `Guid.Empty` check + `Sdk.GetEntity(guid) as T`
+ null-check + `MacroLogger.TraceError` early-return pattern (`on-demand-macro.md`,
`sdk-engine.md`). `AccessRule` cast (`Genetec.Sdk.Entities.AccessRule`, concept §11).
`Alarm` cast (`Genetec.Sdk.Entities.Alarm`, concept §11).

- [ ] **Step 1: Insert validation at the top of the `try` block.**

```csharp
// --- Validate parameters ---
if (GodModeAccessRule.Equals(Guid.Empty))
{
    MacroLogger.TraceError(
        new ArgumentException("GodModeAccessRule not set."),
        "GodModeAccessRule parameter is empty. Set it to the God Mode access rule.");
    return;
}
if (MissingDoorAlarm.Equals(Guid.Empty))
{
    MacroLogger.TraceError(
        new ArgumentException("MissingDoorAlarm not set."),
        "MissingDoorAlarm parameter is empty. Set it to the alarm to raise.");
    return;
}

AccessRule godModeRule = Sdk.GetEntity(GodModeAccessRule) as AccessRule;
if (godModeRule == null)
{
    MacroLogger.TraceError(
        new ArgumentException("God Mode rule not found."),
        $"No AccessRule found for GUID {GodModeAccessRule}. Was it deleted?");
    return;
}

Alarm missingDoorAlarm = Sdk.GetEntity(MissingDoorAlarm) as Alarm;
if (missingDoorAlarm == null)
{
    MacroLogger.TraceError(
        new ArgumentException("Alarm not found."),
        $"No Alarm found for GUID {MissingDoorAlarm}. Was it deleted?");
    return;
}

MacroLogger.TraceInformation(
    $"Auditing rule '{godModeRule.Name}'. EnableAutoAdd={EnableAutoAdd}.");
```

- [ ] **Step 2: Confirm `AccessRule.Name` and `Alarm` resolve cleanly.** `Name` is on
  the `Entity` base class (used throughout the guide). If `macro-reviewer` later flags
  it, fall back to logging the GUID only.

- [ ] **Step 3: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: validate parameters and resolve rule and alarm entities"
```

---

## Task 3: Enumerate all doors via one cached query

**Files:**
- Modify: `GodModeAccessLevelAudit.cs` (add a private helper + call it from `Execute()`).

**Why a single query (concept §8, Dev Guide p. 23):** looping `Sdk.GetEntity()` over
uncached doors can crash the Directory. One `EntityConfigurationQuery` caches them all.

- [ ] **Step 1: VERIFY R1 — how to read query results.** Invoke
  `genetec-guide-researcher`:

  > "In Security Center 5.13, after
  > `var q = Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as
  > EntityConfigurationQuery; q.EntityTypeFilter.Add(EntityType.Door); q.Query();` —
  > what does `Query()` return? How do I get the list of matching entity GUIDs from
  > the result (e.g. a DataTable, a Guid column name, or a results collection)? Is
  > there a property like `EntityConfigurationQuery.Data` or a returned
  > `QueryCompletedEventArgs`? Provide verbatim signatures + page numbers. Also
  > confirm whether `EntityTypeFilter` or `EntityTypes` is the correct property name
  > on EntityConfigurationQuery 5.13."

  Record the confirmed result-reading API. **If not found, stop and report.**

**R1 result (VERIFIED):** `Query()` returns a `QueryCompletedEventArgs` with a
`bool Success` and a `System.Data.DataTable Data`; read each GUID via
`(Guid)row["Guid"]`. `EntityTypeFilter` is the correct filter property. Synchronous
`Query()` is correct in a macro (no `await`). **This requires `using System.Data;`**,
which the scaffold does not yet have.

- [ ] **Step 2a: Add `using System.Data;`** to the file's using block (after
  `using System.Collections.Generic;`).

- [ ] **Step 2b: Write the door-enumeration helper** (verified shape):

```csharp
private List<Guid> GetAllDoorGuids()
{
    var doorGuids = new List<Guid>();
    var query = Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration)
        as EntityConfigurationQuery;
    query.EntityTypeFilter.Add(EntityType.Door);

    // Synchronous: blocks, returns results, and caches the doors. Run BEFORE any
    // transaction — Query() throws inside a transaction with pending updates (§6).
    QueryCompletedEventArgs result = query.Query();
    if (result != null && result.Success)
    {
        foreach (DataRow row in result.Data.Rows)
        {
            doorGuids.Add((Guid)row["Guid"]);
        }
    }
    else
    {
        MacroLogger.TraceWarning("Door enumeration query did not succeed.");
    }

    MacroLogger.TraceInformation($"Enumerated {doorGuids.Count} door(s).");
    return doorGuids;
}
```

- [ ] **Step 3: Call it from `Execute()`** (after parameter validation):

```csharp
List<Guid> doorGuids = GetAllDoorGuids();
```

- [ ] **Step 4: Review** — re-read the confirmed R1 notes and confirm the helper's
  result-reading lines match exactly (column name, return type). Fix discrepancies.

- [ ] **Step 5: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: enumerate all doors via a single cached configuration query"
```

---

## Task 4: Per-door membership check with diagnostic logging

**Files:**
- Modify: `GodModeAccessLevelAudit.cs` (add helpers + the main loop in `Execute()`).

This task produces the **report-only diagnostic** core: for each door it logs which
access points it found and whether each is in the rule. This is what resolves R5
(§7) in the lab — the log tells us exactly which access points represent membership.

- [ ] **Step 1: VERIFY R2 + R3.** Invoke `genetec-guide-researcher`:

  > "Security Center 5.13. (R2) For `Door.DoorSideIn` / `Door.DoorSideOut` (type
  > `DoorSide`), what are the exact return types of `DoorSide.Reader`,
  > `DoorSide.EntrySensor`, `DoorSide.Rex`, and `DoorSide.AccessPointSide` — are they
  > `AccessPoint` objects or `Guid`s? How do I get each access point's `Guid`? Can any
  > be null? (R3) On `AccessRule`, confirm the exact signature of `IsMember(Guid)` and
  > the type of `RelatedAccessPoints`. Verbatim signatures + page numbers."

  Record confirmed types. **If not found, stop and report.**

**R2 result (VERIFIED):** `DoorSide.Reader`, `.Rex`, `.EntrySensor` are `AccessPoint`
objects (each has `.Guid` via the `Entity` base). `DoorSide.AccessPointSide` is an
**enum**, not an access point — do NOT use it as a membership GUID. `DoorSide` is a
nested type, `Door.DoorSide`. Nullability is undocumented → null-check each member.
**R3 result (VERIFIED):** `IsMember(Guid)` exists but the guide does not confirm it
targets access points specifically (a rule also has cardholders). `RelatedAccessPoints`
(`ReadOnlyCollection<Guid>`) is *explicitly* the rule's access-point GUIDs — so we use
it as the unambiguous source of truth instead of `IsMember`.

- [ ] **Step 2: Write a helper that collects a door's `AccessPoint` objects.** Returns
  the actual access-point objects (not just GUIDs) so Task 6 can call `.AccessRules.Add`
  on them directly — no `GetEntity`-by-GUID needed. Null-guard every member.

```csharp
// Returns the AccessPoint objects on both sides of the door, each with a label,
// so the diagnostic log shows exactly what we are checking. The objects come from
// the cached Door graph, so no extra GetEntity calls are made.
private List<KeyValuePair<string, AccessPoint>> GetDoorAccessPoints(Door door)
{
    var points = new List<KeyValuePair<string, AccessPoint>>();
    AddSidePoints(points, "In", door.DoorSideIn);
    AddSidePoints(points, "Out", door.DoorSideOut);
    return points;
}

private void AddSidePoints(
    List<KeyValuePair<string, AccessPoint>> points,
    string sideLabel,
    Door.DoorSide side)
{
    if (side == null) return;
    // Reader/Rex/EntrySensor are AccessPoint objects (R2). Each may be null on a
    // partially configured door. AccessPointSide is an enum and is NOT included.
    if (side.Reader != null)
        points.Add(new KeyValuePair<string, AccessPoint>(sideLabel + ":Reader", side.Reader));
    if (side.Rex != null)
        points.Add(new KeyValuePair<string, AccessPoint>(sideLabel + ":Rex", side.Rex));
    if (side.EntrySensor != null)
        points.Add(new KeyValuePair<string, AccessPoint>(sideLabel + ":EntrySensor", side.EntrySensor));
}
```

- [ ] **Step 3: Write the main per-door loop in `Execute()`** (after `GetAllDoorGuids`).
  In this task it only *detects and logs* — alarms (Task 5) and auto-add (Task 6) plug
  into the marked spots. Membership is checked against a `HashSet` built once from the
  rule's `RelatedAccessPoints`.

```csharp
// The rule's access points, captured once. RelatedAccessPoints is the verified,
// unambiguous membership list (R3).
var ruleAccessPoints = new HashSet<Guid>(godModeRule.RelatedAccessPoints);

var missingDoorGuids = new List<Guid>();
int scanned = 0;

foreach (Guid doorGuid in doorGuids)
{
    Door door = Sdk.GetEntity(doorGuid) as Door;   // cached by Task 3's query
    if (door == null)
    {
        MacroLogger.TraceWarning($"Door {doorGuid} vanished mid-run; skipping.");
        continue;
    }
    scanned++;

    List<KeyValuePair<string, AccessPoint>> points = GetDoorAccessPoints(door);
    if (points.Count == 0)
    {
        MacroLogger.TraceWarning(
            $"Door '{door.Name}' ({doorGuid}) has no access points; skipping.");
        continue;
    }

    bool fullyInRule = true;
    foreach (KeyValuePair<string, AccessPoint> ap in points)
    {
        bool inRule = ruleAccessPoints.Contains(ap.Value.Guid);
        MacroLogger.TraceInformation(
            $"Door '{door.Name}' AP[{ap.Key}]={ap.Value.Guid} inRule={inRule}");
        if (!inRule) fullyInRule = false;
    }

    if (!fullyInRule)
    {
        MacroLogger.TraceWarning(
            $"MISSING: door '{door.Name}' ({doorGuid}) is not fully in God Mode.");
        missingDoorGuids.Add(doorGuid);
        // Task 5: raise alarm here.
    }
}
```

- [ ] **Step 4: Review** against R2/R3 notes — fix member accessors and `IsMember`
  usage to match confirmed signatures.

- [ ] **Step 5: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: detect doors missing from the rule with per-access-point logging"
```

---

## Task 5: Raise one alarm per missing door

**Files:**
- Modify: `GodModeAccessLevelAudit.cs` (add a helper + call it at the "Task 5" spot).

**Verified (concept §11, Ref pp. 2875–2876, 1059–1060; Dev p. 119):**
`Sdk.AlarmManager.TriggerAlarm(Guid alarm, Guid sourceEntityGuid, DynamicAlarmContent)`
returns an `int` instance id (`-1` on failure). `new DynamicAlarmContent(string)` sets
the operator-visible context; `.AttachedEntities.Add(Guid)` associates the door.

- [ ] **Step 1: Add the alarm helper.**

```csharp
private void RaiseMissingDoorAlarm(Door door)
{
    var content = new DynamicAlarmContent(
        $"God Mode is missing door: {door.Name} ({door.Guid})");
    content.AttachedEntities.Add(door.Guid);

    int instanceId = Sdk.AlarmManager.TriggerAlarm(
        MissingDoorAlarm, door.Guid, content);

    if (instanceId == -1)
        MacroLogger.TraceWarning(
            $"TriggerAlarm returned -1 for door '{door.Name}' ({door.Guid}).");
    else
        MacroLogger.TraceInformation(
            $"Raised alarm instance {instanceId} for door '{door.Name}'.");
}
```

- [ ] **Step 2: Call it** from the loop, replacing the `// Task 5: raise alarm here.`
  comment:

```csharp
RaiseMissingDoorAlarm(door);
```

- [ ] **Step 3: Confirm `DynamicAlarmContent` and `AlarmManager.TriggerAlarm` are in
  `Genetec.Sdk.Entities` / `Genetec.Sdk.Workflows`** (already in the usings from
  Task 1). No new using needed.

- [ ] **Step 4: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: raise one alarm per missing door with door name as context"
```

---

## Task 6: Auto-add missing doors (toggle-gated, per-door transaction)

**Files:**
- Modify: `GodModeAccessLevelAudit.cs` (add a helper + call it after the loop).

**Concept §6/§9:** runs only if `EnableAutoAdd`; one transaction per door so one
failure (e.g. partition permissions) is isolated; runs AFTER all queries.

- [ ] **Step 1: VERIFY R4.** Invoke `genetec-guide-researcher`:

  > "Security Center 5.13. Confirm verbatim: (a) `Sdk.TransactionManager` property and
  > the exact signature of `ExecuteTransaction(Action)` — does it auto commit and
  > roll back on exception? (b) `AccessPoint.AccessRules` property type
  > (`AccessRuleCollection`) and `AccessRuleCollection.Add(AccessRule)` and
  > `.Contains(AccessRule)` signatures. (c) Given an access-point GUID, the correct
  > way to obtain the `AccessPoint` entity (`Sdk.GetEntity(guid) as AccessPoint`?) and
  > whether modifying `accessPoint.AccessRules` must happen inside the transaction.
  > Page numbers please."

  Record confirmed signatures. **If not found, stop and report.**

**R4 result (VERIFIED):** `Sdk.TransactionManager.ExecuteTransaction(Action)` auto
creates, commits, and rolls back on exception (Ref pp. 2960–2961). `AccessPoint.AccessRules`
is an `AccessRuleCollection` with `Add(AccessRule)` (void), `Contains(AccessRule)` (bool).
Mutation must be inside the transaction; `ReportQuery.Query()` must NOT run inside it
(our queries already ran in Task 3). The `AccessPoint` objects come from
`GetDoorAccessPoints` (Task 4) so no `GetEntity` call is needed.

- [ ] **Step 2: Add the auto-add helper.** It takes the rule as a parameter (the
  `godModeRule` local in `Execute()`), iterates the door's `AccessPoint` objects, and
  adds the rule to any that don't already have it — inside one transaction per door.

```csharp
private void AutoAddDoor(Door door, AccessRule rule)
{
    try
    {
        List<KeyValuePair<string, AccessPoint>> points = GetDoorAccessPoints(door);
        Sdk.TransactionManager.ExecuteTransaction(() =>
        {
            foreach (KeyValuePair<string, AccessPoint> ap in points)
            {
                if (!ap.Value.AccessRules.Contains(rule))   // per R4
                    ap.Value.AccessRules.Add(rule);         // per R4 (returns void)
            }
        });
        MacroLogger.TraceInformation(
            $"Auto-added door '{door.Name}' ({door.Guid}) to God Mode.");
    }
    catch (Exception ex)
    {
        MacroLogger.TraceError(ex,
            $"Failed to auto-add door '{door.Name}' ({door.Guid}). " +
            "Check macro-user write rights on this door's partition.");
    }
}
```

- [ ] **Step 3: Call it after the detection loop** (so all queries finish first):

```csharp
if (EnableAutoAdd && missingDoorGuids.Count > 0)
{
    MacroLogger.TraceInformation(
        $"Auto-add enabled. Adding {missingDoorGuids.Count} door(s).");
    foreach (Guid doorGuid in missingDoorGuids)
    {
        Door door = Sdk.GetEntity(doorGuid) as Door;
        if (door != null) AutoAddDoor(door, godModeRule);
    }
}
else if (!EnableAutoAdd && missingDoorGuids.Count > 0)
{
    MacroLogger.TraceInformation(
        "Auto-add disabled (report-only). No changes written.");
}
```

- [ ] **Step 4: Review** against R4 notes — confirm `ExecuteTransaction`,
  `AccessRules.Add/Contains`, and the `AccessPoint` cast match exactly. Confirm no
  `Query()` call occurs inside the transaction (concept §6 ordering rule).

- [ ] **Step 5: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: optionally auto-add missing doors via per-door transactions"
```

---

## Task 7: Summary logging and whole-file review

**Files:**
- Modify: `GodModeAccessLevelAudit.cs` (add summary log before the completion line).

- [ ] **Step 1: Add a summary log** at the end of the `try` block:

```csharp
MacroLogger.TraceInformation(
    $"Audit summary: scanned={scanned}, missing={missingDoorGuids.Count}, " +
    $"autoAdd={(EnableAutoAdd ? "on" : "off")}.");
```

- [ ] **Step 2: Run `macro-reviewer`** over the full `GodModeAccessLevelAudit.cs`.
  Expected verdict format: a tagged list (BLOCKER / WARN / NIT) and a one-line verdict.
  Resolve every BLOCKER and re-run until none remain.

- [ ] **Step 3: Re-confirm every SDK call** appears in the concept §11 table or in a
  recorded researcher result (R1–R4). Any call not traceable to a verified source must
  be re-verified or removed.

- [ ] **Step 4: Commit.**

```bash
git add "TechSec Concepts/God Mode Access Level Macro/GodModeAccessLevelAudit.cs"
git commit -m "feat: add audit summary logging"
```

---

## Task 8: Pre-deploy verification and staged lab rollout

**Files:** none (process + the operator's lab system).

This is where R5 (§7 — the exact membership access-point set) is resolved, and where
the only real execution test happens.

- [ ] **Step 1: Run the `pre-deploy-check` skill** against
  `GodModeAccessLevelAudit.cs`. Resolve anything it flags. Expected end verdict:
  "Ready for lab".

- [ ] **Step 2: Lab — report-only run.** Operator pastes the file into a Macro entity
  in a **non-production** Security Center, sets `GodModeAccessRule` and
  `MissingDoorAlarm`, leaves `EnableAutoAdd = false`, and runs it. Read the macro log.
  Confirm: door count looks right; per-access-point `inRule=` lines appear; missing
  doors are listed; one alarm per missing door shows in Alarm Monitoring with the door
  name; **no** config was changed.

- [ ] **Step 3: Resolve R5 (§7) and R6 (§7a).** In the lab, manually add one known door
  to the rule in Config Tool. Re-run report-only. Each missing-door log line now shows
  both signals per access point: `ruleList=` (the rule's `RelatedAccessPoints`) and
  `apRules=` (the access point's `AccessRules`). Note which signal(s) flipped to `true`
  for the door you added — that reveals **which side Config Tool populates** and **which
  access points** it uses (e.g. only `:Reader`, or `:Reader`+`:EntrySensor`). If the
  macro checks points Config Tool does NOT use, narrow `GetDoorAccessPoints` to the
  confirmed set. Record the finding in the concept doc §7 / §7a.

- [ ] **Step 4: Lab — auto-add run.** Set `EnableAutoAdd = true` on a small test rule
  missing a couple of doors. Run. Confirm in Config Tool that the doors are now in the
  rule and that their membership matches a hand-added door exactly (from Step 3).
  Confirm the log shows per-door auto-add success lines.

- [ ] **Step 5: Negative paths.** Verify: empty `GodModeAccessRule` logs a clear error
  and returns; a deleted rule/alarm GUID logs "not found"; a door in a partition the
  macro user can't write logs the permission error and the other doors still process.

- [ ] **Step 6: Production rollout.** Create a Config Tool **scheduled task** to run the
  macro daily at an off-peak hour with `EnableAutoAdd = false`. Watch for a few days,
  fix any backlog manually, then flip `EnableAutoAdd = true`. Record the lab result in
  `.claude/knowledge/deployment-log.md` per the `pre-deploy-check` workflow.

- [ ] **Step 7: Hand off the file.** The operator copies
  `GodModeAccessLevelAudit.cs` out of the repo into Config Tool. (Optional cleanup:
  also place a copy under `Macros/AccessControl/` to match repo convention.)

---

## Self-review against the spec

- **Coverage:** every concept section maps to a task — parameters §5→T2; flow §6→T2–T7;
  §7 open item→T8/S3; deployment/perf §8→T3 (cached query) + T6 (per-door tx) + T8/S6
  (off-peak schedule); risks §9→T6 (isolated tx), T8/S2 (report-only first), T8/S5
  (negative paths); verified API §11→cited per task.
- **No silent guesses:** the four unverified APIs (R1–R4) each have a researcher step
  before their code; R5 is resolved empirically in T8. If any returns "not found,"
  the plan says stop.
- **Type consistency:** `godModeRule` (AccessRule), `missingDoorAlarm` (Alarm),
  `GetDoorAccessPoints` (used in T4 and T6), `GetAllDoorGuids`, `missingDoorGuids` —
  names are consistent across tasks. T6 Step 2 flags the one scope decision
  (`godModeRule` local vs parameter) explicitly so it isn't left ambiguous.

---

## Post-implementation refinements (applied 2026-06-01)

After the build, the `macro-reviewer` gate and an extra guide lookup (R6) produced three
changes to the as-built `GodModeAccessLevelAudit.cs` that supersede the literal Task 3/4
snippets above. The macro file is the source of truth for the final code.

1. **Two-signal membership check (R6 / concept §7a).** The guide does not confirm that
   `AccessRule.RelatedAccessPoints` and `AccessPoint.AccessRules` stay in sync. The
   detection loop checks **both** ends (`inRuleList || inApRules`) so an auto-added door
   is never re-alarmed, and logs both signals per missing access point.
2. **Log volume trimmed to misses.** Per-access-point detail is logged only for doors
   flagged missing, keeping the log readable at scale.
3. **Null-guard on the query cast.** `GetAllDoorGuids()` returns early with a logged
   error if `CreateReportQuery(...) as EntityConfigurationQuery` is null.
