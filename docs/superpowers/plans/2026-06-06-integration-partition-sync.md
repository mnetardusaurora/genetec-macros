# Integration Partition Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an add-only Genetec macro that ensures every entity of selected types (Cardholders, Credentials, Doors, Areas) is a member of a chosen target partition, so a partition-scoped 3rd-party integration never silently misses entities.

**Architecture:** A single `UserMacro` (`IntegrationPartitionSync`) triggered by a Config Tool scheduled task. Each run: validate params → snapshot the partition's current members → enumerate all entities of each selected type (failing loud on any read error) → add the missing ones inside one transaction (or just preview them when `ReportOnly`) → write a per-type summary to the macro log → raise one optional alarm only if the run throws. It never removes membership.

**Tech Stack:** C# 7.3 (Security Center 5.13 macro runtime), `Genetec.Sdk` (`UserMacro`, `EntityConfigurationQuery`, `Partition`, `PartitionSupportEntity.InsertIntoPartition`, `TransactionManager`, `AlarmManager`, `MacroLogger`). No local build/test — verification is guide-research + `macro-reviewer` + lab run.

---

## How "verification" works in this repo (read first)

There is **no compiler, no test runner, no local build** for macros. Security
Center compiles the `.cs` at paste time. So every task below replaces
"write/run a test" with the verification this project actually uses:

- **Guide check:** confirm each new SDK call against the Genetec guide via the
  `genetec-guide-researcher` agent (already done once for this design — re-confirm
  any call you add that isn't in the spec's §4 table).
- **Reasoning check:** re-read the diff for null-safety, C# 7.3-only syntax, and
  the project rules in `CLAUDE.md`.
- **Reviewer pass (final):** run the `macro-reviewer` agent, then `pre-deploy-check`.
- **Lab run (final, by the user):** report-only run, read the log, then a real run.

Commit after every task. Stage files explicitly (`git add <path>`) — never
`git add .` — per the repo's commit-hygiene rule. Never stage `.claude/`,
`CLAUDE.md`, or `Guide/`.

---

## File Structure

```
Macros/Integrations/Integration Partition Sync/
  IntegrationPartitionSync.cs   # the entire macro (one focused file)
  README.md                     # operator setup: scheduled task, privilege, params, first-run
```

One file holds the macro because a macro is a single pasted unit by definition.
Responsibilities inside it are split into small private helpers
(`BuildSelectedTypes`, `EnumerateEntityGuids`, `RaiseFailureAlarm`) so each piece
is understandable on its own. The README is operator-facing only.

The final `IntegrationPartitionSync.cs` (target end state — built up across Tasks
1–7) is reproduced in full at the end of this plan under **Appendix A** so the
engineer can cross-check the assembled result.

---

### Task 1: Folder + macro skeleton (header, class, parameters, empty Execute)

**Files:**
- Create: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Create the folder and file with the header, usings, class, parameters, and a logging try/catch skeleton**

```csharp
// IntegrationPartitionSync.cs
//
// Purpose:       Add-only sync that ensures every entity of the selected types
//                (Cardholders, Credentials, Doors, Areas) is a MEMBER of a chosen
//                target partition. Keeps a partition-scoped 3rd-party integration
//                from silently missing entities. Never removes membership.
// Trigger type:  Scheduled (run by a Config Tool scheduled task). Also on demand.
// Required entities: one Partition (target). Optional: one Alarm (failure alarm).
// Required custom fields: none.
// Required parameters: TargetPartition (Guid); SyncCardholders, SyncCredentials,
//                SyncDoors, SyncAreas (Boolean); ReportOnly (Boolean, default true);
//                FailureAlarm (Guid, optional).
// Required privilege: the macro run-as user needs ManagePartitionMemberships
//                (or write access on the target partition), or every add throws.
// Date created:  2026-06-06
//
// NOTE: InsertIntoPartition is ADD-ONLY and additive — an entity may belong to
// multiple partitions at once, so adding to the target never removes it from any
// other partition. This macro never calls MoveToPartition / RemoveMember.

using System;
using System.Collections.Generic;
using System.Data;
using Genetec.Sdk;
using Genetec.Sdk.Scripting;
using Genetec.Sdk.Entities;
using Genetec.Sdk.Queries;
using Genetec.Sdk.Workflows;

public sealed class IntegrationPartitionSync : UserMacro
{
    // The partition to keep complete. Config Tool shows an entity picker.
    public Guid TargetPartition { get; set; }

    // One checkbox per supported entity type.
    public bool SyncCardholders { get; set; }
    public bool SyncCredentials { get; set; }
    public bool SyncDoors { get; set; }
    public bool SyncAreas { get; set; }

    // Safe default: report-only. Logs what it WOULD add and writes nothing.
    // The operator sets this false to actually add members. (Note: in Config Tool
    // a Boolean defaults to false; the README instructs setting ReportOnly = true
    // for the first run. See Task 8.)
    public bool ReportOnly { get; set; }

    // Optional. If set, one alarm is raised when the run fails. Empty = log-only.
    public Guid FailureAlarm { get; set; }

    public override void Execute()
    {
        MacroLogger.TraceInformation("IntegrationPartitionSync.Execute() started.");
        try
        {
            // Body added in later tasks.
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "IntegrationPartitionSync.Execute() failed.");
            // RaiseFailureAlarm(ex) added in Task 7.
        }
    }

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
```

- [ ] **Step 2: Reasoning check**

Confirm against `CLAUDE.md` and spec §4:
- Inherits `Genetec.Sdk.Scripting.UserMacro`, overrides `Execute()` and `CleanUp()`. ✓ (VERIFIED in CLAUDE.md)
- Parameters are public properties of allowed types (`Guid`, `bool`). ✓
- `Execute()` body is wrapped in try/catch and logs entry. ✓
- C# 7.3 only; explicit types; header comment present. ✓

- [ ] **Step 3: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: scaffold Integration Partition Sync macro skeleton"
```

---

### Task 2: Parameter validation + selected-type list

**Files:**
- Modify: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Add the validation block at the top of the `try` in `Execute()`**

Replace the `// Body added in later tasks.` line with:

```csharp
            // --- Validate parameters ---
            if (TargetPartition.Equals(Guid.Empty))
            {
                MacroLogger.TraceError(
                    new ArgumentException("TargetPartition not set."),
                    "TargetPartition parameter is empty. Set it to the partition to keep complete.");
                return;
            }

            Partition partition = Sdk.GetEntity(TargetPartition) as Partition;
            if (partition == null)
            {
                MacroLogger.TraceError(
                    new ArgumentException("Partition not found."),
                    $"No Partition found for GUID {TargetPartition}. Was it deleted?");
                return;
            }

            List<EntityType> selectedTypes = BuildSelectedTypes();
            if (selectedTypes.Count == 0)
            {
                MacroLogger.TraceWarning(
                    "No entity types selected (all Sync* parameters false). Nothing to do.");
                return;
            }

            MacroLogger.TraceInformation(
                $"Syncing partition '{partition.Name}'. Types=[{string.Join(",", selectedTypes)}]. " +
                $"ReportOnly={ReportOnly}.");
```

- [ ] **Step 2: Add the `BuildSelectedTypes` helper method (after `Execute()`, before `CleanUp()`)**

```csharp
    // Translates the four checkbox parameters into the list of EntityType values
    // to scan. EntityType enum values verified: Cardholder=7, Credential=9,
    // Door=11, Area=5 (Ref Guide p. 292).
    private List<EntityType> BuildSelectedTypes()
    {
        var types = new List<EntityType>();
        if (SyncCardholders) types.Add(EntityType.Cardholder);
        if (SyncCredentials) types.Add(EntityType.Credential);
        if (SyncDoors) types.Add(EntityType.Door);
        if (SyncAreas) types.Add(EntityType.Area);
        return types;
    }
```

- [ ] **Step 3: Guide check**

`Sdk.GetEntity(guid) as Partition` — `Partition` is `Genetec.Sdk.Entities.Partition`
(VERIFIED Ref Guide p. 1206); the `as` cast is the standard `GetEntity(...) as
<EntityClass>` pattern. `EntityType.Cardholder/Credential/Door/Area` VERIFIED
Ref Guide p. 292. No new unverified calls.

- [ ] **Step 4: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: validate target partition and build selected-type list"
```

---

### Task 3: Entity enumeration helper (fail loud on read failure)

**Files:**
- Modify: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Add the `EnumerateEntityGuids` helper (after `BuildSelectedTypes`)**

```csharp
    // Returns the GUIDs of every entity of the given type. Mirrors the God Mode
    // macro's enumeration: synchronous EntityConfigurationQuery that also caches
    // the entities so later GetEntity calls resolve from cache.
    //
    // FAIL LOUD: if the query cannot run or does not succeed, THROW. A security
    // sync must never report "0 to add" when it could not even read the entities —
    // that is exactly the "integration sees partial data" failure this macro
    // exists to prevent. The throw is caught by Execute() and (optionally) alarmed.
    private List<Guid> EnumerateEntityGuids(EntityType type)
    {
        var guids = new List<Guid>();
        var query = Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration)
            as EntityConfigurationQuery;
        if (query == null)
        {
            throw new InvalidOperationException(
                $"Could not create EntityConfigurationQuery for {type}.");
        }
        query.EntityTypeFilter.Add(type);

        // Synchronous: blocks, returns results, caches the entities. Must run
        // BEFORE any transaction — Query() throws inside a transaction with
        // pending updates (lesson from the God Mode macro).
        QueryCompletedEventArgs result = query.Query();
        if (result == null || !result.Success || result.Data == null)
        {
            throw new InvalidOperationException(
                $"Entity enumeration query failed for {type}; aborting sync this run.");
        }

        foreach (DataRow row in result.Data.Rows)
        {
            guids.Add((Guid)row["Guid"]);
        }

        MacroLogger.TraceInformation($"Enumerated {guids.Count} {type} entity(ies).");
        return guids;
    }
```

- [ ] **Step 2: Guide check**

`Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as
EntityConfigurationQuery` and `query.EntityTypeFilter.Add(EntityType.X)` —
VERIFIED (Dev Guide; identical to the working God Mode macro
`GetAllDoorGuids`). `result.Data.Rows` / `row["Guid"]` — matches the God Mode
macro (spec §11 item 3: confirm the column name is `"Guid"` during the lab run).

- [ ] **Step 3: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: add fail-loud entity enumeration by type"
```

---

### Task 4: Phase 1 — snapshot members and compute the add-list

**Files:**
- Modify: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Append the member snapshot + enumeration loop inside the `try`, right after the "Syncing partition…" log line from Task 2**

```csharp
            // Snapshot the partition's current members once. Members is a
            // ReadOnlyCollection<Guid> (Ref Guide p. 1210). We diff against this
            // so re-runs skip entities already present (no redundant writes/events).
            HashSet<Guid> members = new HashSet<Guid>(partition.Members);

            // PHASE 1: enumerate EVERYTHING before opening any transaction.
            var toAddByType = new Dictionary<EntityType, List<Guid>>();
            var scannedByType = new Dictionary<EntityType, int>();
            foreach (EntityType type in selectedTypes)
            {
                List<Guid> all = EnumerateEntityGuids(type);
                scannedByType[type] = all.Count;

                var toAdd = new List<Guid>();
                foreach (Guid guid in all)
                {
                    if (!members.Contains(guid))
                        toAdd.Add(guid);
                }
                toAddByType[type] = toAdd;
            }
```

- [ ] **Step 2: Guide check**

`partition.Members` → `ReadOnlyCollection<Guid>` VERIFIED (Ref Guide p. 1210).
`new HashSet<Guid>(partition.Members)` is valid C# 7.3. No new SDK calls.

- [ ] **Step 3: Reasoning check**

All enumeration (Phase 1) completes before any transaction opens (Task 5), so the
"Query() throws inside a transaction" hazard cannot occur. `toAddByType` and
`scannedByType` are keyed by the same `selectedTypes` used everywhere else.

- [ ] **Step 4: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: snapshot partition members and compute missing entities"
```

---

### Task 5: Phase 2 — add missing members inside one transaction

**Files:**
- Modify: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Append the write phase inside the `try`, after the Phase 1 loop**

```csharp
            // Per-type counters initialised for both modes so the summary is uniform.
            var addedByType = new Dictionary<EntityType, int>();
            var errorsByType = new Dictionary<EntityType, int>();
            foreach (EntityType type in selectedTypes)
            {
                addedByType[type] = 0;
                errorsByType[type] = 0;
            }

            // PHASE 2: write. Skipped entirely in ReportOnly (no transaction opened).
            if (!ReportOnly)
            {
                // One transaction for all adds: faster for bulk writes and rolls
                // back automatically if it throws (Dev Guide p. 116). Entities were
                // cached by the Phase 1 queries, so GetEntity here is a cache lookup.
                Sdk.TransactionManager.ExecuteTransaction(() =>
                {
                    foreach (EntityType type in selectedTypes)
                    {
                        foreach (Guid guid in toAddByType[type])
                        {
                            PartitionSupportEntity entity =
                                Sdk.GetEntity(guid) as PartitionSupportEntity;
                            if (entity == null)
                            {
                                MacroLogger.TraceWarning(
                                    $"{type} {guid} vanished or is not partition-able; skipping.");
                                errorsByType[type]++;
                                continue;
                            }

                            // ADD-ONLY: InsertIntoPartition is additive — it never
                            // removes the entity from any other partition.
                            bool ok = entity.InsertIntoPartition(TargetPartition);
                            if (ok)
                            {
                                addedByType[type]++;
                            }
                            else
                            {
                                errorsByType[type]++;
                                MacroLogger.TraceWarning(
                                    $"InsertIntoPartition returned false for {type} '{entity.Name}' " +
                                    $"({guid}). Check macro-user ManagePartitionMemberships rights " +
                                    "on this partition.");
                            }
                        }
                    }
                });
            }
```

- [ ] **Step 2: Guide check (the most important one in this plan)**

- `(Sdk.GetEntity(guid) as PartitionSupportEntity).InsertIntoPartition(Guid)` →
  `bool`; throws `SdkException` if not logged on / no write access — VERIFIED
  Ref Guide p. 1212. Add-only/additive semantics VERIFIED (Ref Guide
  `MoveToPartition` remarks + `Entity.GetPartitions` p. 1072).
- `Sdk.TransactionManager.ExecuteTransaction(Action)` — VERIFIED Dev Guide p. 116.
- **Spec §11 item 2 to confirm:** that Cardholder, Credential, Door, and Area each
  derive from `PartitionSupportEntity` so the cast is non-null for all four. If the
  `macro-reviewer` or lab run shows `entity == null` for a valid Credential, switch
  that path to `partition.AddMember(entity as IPartitionSupport)` (also VERIFIED,
  Ref Guide p. 1207). Note this contingency for the reviewer.
- **Confirm `GetEntity` inside `ExecuteTransaction` is acceptable** (it is a cached
  read, not a query). Flag to `genetec-guide-researcher` if any doubt; if it must
  move out, resolve entities into a `List<PartitionSupportEntity>` during Phase 1
  and iterate that inside the transaction instead.

- [ ] **Step 3: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: add missing entities to partition in one transaction (add-only)"
```

---

### Task 6: Per-type summary logging

**Files:**
- Modify: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Append the summary block inside the `try`, after the Phase 2 block, then the completion log**

```csharp
            // --- Per-type summary (the log IS the debugger for macros) ---
            foreach (EntityType type in selectedTypes)
            {
                int scanned = scannedByType[type];
                int toAdd = toAddByType[type].Count;
                int alreadyPresent = scanned - toAdd;
                if (ReportOnly)
                {
                    MacroLogger.TraceInformation(
                        $"[{type}] scanned={scanned} alreadyPresent={alreadyPresent} " +
                        $"wouldAdd={toAdd} (report-only, no writes).");
                }
                else
                {
                    MacroLogger.TraceInformation(
                        $"[{type}] scanned={scanned} alreadyPresent={alreadyPresent} " +
                        $"added={addedByType[type]} errors={errorsByType[type]}.");
                }
            }

            MacroLogger.TraceInformation("IntegrationPartitionSync.Execute() completed.");
```

- [ ] **Step 2: Reasoning check**

In report-only mode `addedByType`/`errorsByType` stay zero and are not printed, so
the numbers can't mislead. `alreadyPresent = scanned - toAdd` is correct because
`toAdd` is exactly the subset of scanned GUIDs not already in `members`.

- [ ] **Step 3: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: log per-type sync summary"
```

---

### Task 7: Optional failure alarm

**Files:**
- Modify: `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`

- [ ] **Step 1: Call the alarm helper from the existing `catch` in `Execute()`**

Replace the `catch` block's comment line so it reads:

```csharp
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "IntegrationPartitionSync.Execute() failed.");
            RaiseFailureAlarm(ex);
        }
```

- [ ] **Step 2: Add the `RaiseFailureAlarm` helper (after `EnumerateEntityGuids`)**

```csharp
    // Optional: raise ONE alarm when a run fails. No-op when FailureAlarm is empty
    // (log-only mode). Mirrors the God Mode macro's alarm pattern.
    private void RaiseFailureAlarm(Exception ex)
    {
        if (FailureAlarm.Equals(Guid.Empty))
            return;  // optional — operator chose log-only

        try
        {
            var content = new DynamicAlarmContent(
                $"Integration Partition Sync failed: {ex.Message}");
            content.AttachedEntities.Add(TargetPartition);

            int instanceId = Sdk.AlarmManager.TriggerAlarm(
                FailureAlarm, TargetPartition, content);

            if (instanceId == -1)
                MacroLogger.TraceWarning("Failure alarm TriggerAlarm returned -1.");
            else
                MacroLogger.TraceInformation(
                    $"Raised failure alarm instance {instanceId}.");
        }
        catch (Exception alarmEx)
        {
            // Never let alarm-raising mask the original failure.
            MacroLogger.TraceError(alarmEx, "Failed to raise the failure alarm.");
        }
    }
```

- [ ] **Step 3: Guide check**

`new DynamicAlarmContent(string)`, `content.AttachedEntities.Add(Guid)`, and
`Sdk.AlarmManager.TriggerAlarm(Guid, Guid, DynamicAlarmContent)` returning `int`
— VERIFIED by reuse: identical to the shipped God Mode macro's
`RaiseMissingDoorAlarm`.

- [ ] **Step 4: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs"
git commit -m "feat: raise one optional alarm on run failure"
```

---

### Task 8: Operator README

**Files:**
- Create: `Macros/Integrations/Integration Partition Sync/README.md`

- [ ] **Step 1: Write the README**

````markdown
# Integration Partition Sync

Keeps a **partition** complete for selected entity types so a 3rd-party
integration whose Security Center account is scoped to that partition always sees
the full set of entities. **Add-only:** it never removes anything from any
partition.

## What it does each run

1. Reads the current members of the **target partition**.
2. Enumerates every entity of the **selected types** (Cardholders, Credentials,
   Doors, Areas).
3. Adds the ones that are missing (or, in report-only mode, just logs what it
   *would* add).
4. Writes a per-type summary to the macro log; raises one alarm only if the run
   fails (optional).

## Parameters

| Parameter | What to set |
|-----------|-------------|
| `TargetPartition` | The partition to keep complete (entity picker). **Required.** |
| `SyncCardholders` / `SyncCredentials` / `SyncDoors` / `SyncAreas` | Tick the types this integration consumes. |
| `ReportOnly` | **Tick this for the first run.** Logs what it would add, writes nothing. Untick to actually add members. |
| `FailureAlarm` | Optional. Pick an Alarm entity to be raised if a run fails. Leave empty for log-only. |

## Prerequisites

- **Privilege:** the user the Macro role runs as must have **Manage partition
  memberships** (or explicit write access on the target partition). Without it,
  every add fails and the per-type `errors` count will be non-zero.
- **Schedule:** create a **Scheduled task** in Config Tool to run this macro on
  your cadence (e.g. nightly, off-peak). The macro has no internal timer.

## First run (do this once)

1. Set `TargetPartition` and tick the entity types.
2. Tick `ReportOnly`. Run the macro on demand.
3. Open the macro log. Confirm the `[Type] scanned=… wouldAdd=…` lines look right.
4. Untick `ReportOnly` and run again. The log should now show `added=…`.
5. Attach the macro to the scheduled task.

## Reading the log

- `scanned` — how many entities of that type exist (that the macro's account can see).
- `alreadyPresent` — already members of the partition.
- `wouldAdd` (report-only) / `added` — newly added members.
- `errors` — adds that failed (usually a missing **Manage partition memberships**
  privilege).

> **Caveat:** `scanned` reflects only the entities the macro's run-as account can
> see. If that account is itself partition-scoped, counts can be lower than the
> true total. Run the macro as an account with broad read access.
````

- [ ] **Step 2: Commit**

```bash
git add "Macros/Integrations/Integration Partition Sync/README.md"
git commit -m "docs: add operator README for Integration Partition Sync"
```

---

### Task 9: Final verification (reviewer + guide + pre-deploy)

**Files:** none modified (verification only).

- [ ] **Step 1: Run the `macro-reviewer` agent** on
  `Macros/Integrations/Integration Partition Sync/IntegrationPartitionSync.cs`.
  Resolve every BLOCKER and WARN. Pay attention to the two contingencies flagged
  in Task 5 Step 2 (Credential cast; `GetEntity` inside the transaction).

- [ ] **Step 2: Run the `pre-deploy-check` skill** for the macro. It re-verifies
  every SDK call via `genetec-guide-researcher`, parses the header prerequisites,
  and emits a manual test checklist. Address anything it raises.

- [ ] **Step 3: Confirm spec §11 open items are closed:**
  - `as Partition` cast compiles.
  - all four types cast to `PartitionSupportEntity` (or the `AddMember` fallback is in place).
  - the enumeration GUID column is `"Guid"`.
  - README documents the run-as scope caveat. ✓ (done in Task 8)

- [ ] **Step 4: Hand the manual test checklist to the user for a lab run.** The
  user runs report-only, then a real run in the lab, and reports the log back. Only
  then is the macro "Ready for production" per `pre-deploy-check`.

- [ ] **Step 5: Finish the branch.** Use the `superpowers:finishing-a-development-branch`
  skill to choose merge / PR / cleanup for `feature/partition-membership-sync`.

---

## Appendix A — assembled `IntegrationPartitionSync.cs` (cross-check target)

This is the end state after Tasks 1–7. Use it to verify the assembled file; build
it up task-by-task rather than pasting it all at once.

```csharp
// IntegrationPartitionSync.cs
//
// Purpose:       Add-only sync that ensures every entity of the selected types
//                (Cardholders, Credentials, Doors, Areas) is a MEMBER of a chosen
//                target partition. Keeps a partition-scoped 3rd-party integration
//                from silently missing entities. Never removes membership.
// Trigger type:  Scheduled (run by a Config Tool scheduled task). Also on demand.
// Required entities: one Partition (target). Optional: one Alarm (failure alarm).
// Required custom fields: none.
// Required parameters: TargetPartition (Guid); SyncCardholders, SyncCredentials,
//                SyncDoors, SyncAreas (Boolean); ReportOnly (Boolean, default true);
//                FailureAlarm (Guid, optional).
// Required privilege: the macro run-as user needs ManagePartitionMemberships
//                (or write access on the target partition), or every add throws.
// Date created:  2026-06-06
//
// NOTE: InsertIntoPartition is ADD-ONLY and additive — an entity may belong to
// multiple partitions at once, so adding to the target never removes it from any
// other partition. This macro never calls MoveToPartition / RemoveMember.

using System;
using System.Collections.Generic;
using System.Data;
using Genetec.Sdk;
using Genetec.Sdk.Scripting;
using Genetec.Sdk.Entities;
using Genetec.Sdk.Queries;
using Genetec.Sdk.Workflows;

public sealed class IntegrationPartitionSync : UserMacro
{
    public Guid TargetPartition { get; set; }

    public bool SyncCardholders { get; set; }
    public bool SyncCredentials { get; set; }
    public bool SyncDoors { get; set; }
    public bool SyncAreas { get; set; }

    public bool ReportOnly { get; set; }

    public Guid FailureAlarm { get; set; }

    public override void Execute()
    {
        MacroLogger.TraceInformation("IntegrationPartitionSync.Execute() started.");
        try
        {
            if (TargetPartition.Equals(Guid.Empty))
            {
                MacroLogger.TraceError(
                    new ArgumentException("TargetPartition not set."),
                    "TargetPartition parameter is empty. Set it to the partition to keep complete.");
                return;
            }

            Partition partition = Sdk.GetEntity(TargetPartition) as Partition;
            if (partition == null)
            {
                MacroLogger.TraceError(
                    new ArgumentException("Partition not found."),
                    $"No Partition found for GUID {TargetPartition}. Was it deleted?");
                return;
            }

            List<EntityType> selectedTypes = BuildSelectedTypes();
            if (selectedTypes.Count == 0)
            {
                MacroLogger.TraceWarning(
                    "No entity types selected (all Sync* parameters false). Nothing to do.");
                return;
            }

            MacroLogger.TraceInformation(
                $"Syncing partition '{partition.Name}'. Types=[{string.Join(",", selectedTypes)}]. " +
                $"ReportOnly={ReportOnly}.");

            HashSet<Guid> members = new HashSet<Guid>(partition.Members);

            var toAddByType = new Dictionary<EntityType, List<Guid>>();
            var scannedByType = new Dictionary<EntityType, int>();
            foreach (EntityType type in selectedTypes)
            {
                List<Guid> all = EnumerateEntityGuids(type);
                scannedByType[type] = all.Count;

                var toAdd = new List<Guid>();
                foreach (Guid guid in all)
                {
                    if (!members.Contains(guid))
                        toAdd.Add(guid);
                }
                toAddByType[type] = toAdd;
            }

            var addedByType = new Dictionary<EntityType, int>();
            var errorsByType = new Dictionary<EntityType, int>();
            foreach (EntityType type in selectedTypes)
            {
                addedByType[type] = 0;
                errorsByType[type] = 0;
            }

            if (!ReportOnly)
            {
                Sdk.TransactionManager.ExecuteTransaction(() =>
                {
                    foreach (EntityType type in selectedTypes)
                    {
                        foreach (Guid guid in toAddByType[type])
                        {
                            PartitionSupportEntity entity =
                                Sdk.GetEntity(guid) as PartitionSupportEntity;
                            if (entity == null)
                            {
                                MacroLogger.TraceWarning(
                                    $"{type} {guid} vanished or is not partition-able; skipping.");
                                errorsByType[type]++;
                                continue;
                            }

                            bool ok = entity.InsertIntoPartition(TargetPartition);
                            if (ok)
                            {
                                addedByType[type]++;
                            }
                            else
                            {
                                errorsByType[type]++;
                                MacroLogger.TraceWarning(
                                    $"InsertIntoPartition returned false for {type} '{entity.Name}' " +
                                    $"({guid}). Check macro-user ManagePartitionMemberships rights " +
                                    "on this partition.");
                            }
                        }
                    }
                });
            }

            foreach (EntityType type in selectedTypes)
            {
                int scanned = scannedByType[type];
                int toAdd = toAddByType[type].Count;
                int alreadyPresent = scanned - toAdd;
                if (ReportOnly)
                {
                    MacroLogger.TraceInformation(
                        $"[{type}] scanned={scanned} alreadyPresent={alreadyPresent} " +
                        $"wouldAdd={toAdd} (report-only, no writes).");
                }
                else
                {
                    MacroLogger.TraceInformation(
                        $"[{type}] scanned={scanned} alreadyPresent={alreadyPresent} " +
                        $"added={addedByType[type]} errors={errorsByType[type]}.");
                }
            }

            MacroLogger.TraceInformation("IntegrationPartitionSync.Execute() completed.");
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "IntegrationPartitionSync.Execute() failed.");
            RaiseFailureAlarm(ex);
        }
    }

    private List<EntityType> BuildSelectedTypes()
    {
        var types = new List<EntityType>();
        if (SyncCardholders) types.Add(EntityType.Cardholder);
        if (SyncCredentials) types.Add(EntityType.Credential);
        if (SyncDoors) types.Add(EntityType.Door);
        if (SyncAreas) types.Add(EntityType.Area);
        return types;
    }

    private List<Guid> EnumerateEntityGuids(EntityType type)
    {
        var guids = new List<Guid>();
        var query = Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration)
            as EntityConfigurationQuery;
        if (query == null)
        {
            throw new InvalidOperationException(
                $"Could not create EntityConfigurationQuery for {type}.");
        }
        query.EntityTypeFilter.Add(type);

        QueryCompletedEventArgs result = query.Query();
        if (result == null || !result.Success || result.Data == null)
        {
            throw new InvalidOperationException(
                $"Entity enumeration query failed for {type}; aborting sync this run.");
        }

        foreach (DataRow row in result.Data.Rows)
        {
            guids.Add((Guid)row["Guid"]);
        }

        MacroLogger.TraceInformation($"Enumerated {guids.Count} {type} entity(ies).");
        return guids;
    }

    private void RaiseFailureAlarm(Exception ex)
    {
        if (FailureAlarm.Equals(Guid.Empty))
            return;

        try
        {
            var content = new DynamicAlarmContent(
                $"Integration Partition Sync failed: {ex.Message}");
            content.AttachedEntities.Add(TargetPartition);

            int instanceId = Sdk.AlarmManager.TriggerAlarm(
                FailureAlarm, TargetPartition, content);

            if (instanceId == -1)
                MacroLogger.TraceWarning("Failure alarm TriggerAlarm returned -1.");
            else
                MacroLogger.TraceInformation(
                    $"Raised failure alarm instance {instanceId}.");
        }
        catch (Exception alarmEx)
        {
            MacroLogger.TraceError(alarmEx, "Failed to raise the failure alarm.");
        }
    }

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
```
