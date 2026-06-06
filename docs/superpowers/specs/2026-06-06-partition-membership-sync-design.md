# Partition Membership Sync — Macro Design

**Status:** Design / approved (no code yet)
**Author:** Matthew Netardus
**Date created:** 2026-06-06
**Audience:** Beginner-friendly. SDK facts are tagged **[VERIFIED]** (confirmed from
the Genetec guides in this repo, with page numbers) or **[VERIFY]** (must be
confirmed before/while coding).

---

## 1. Purpose & goal

Some 3rd-party integrations (e.g. the Helix → Foundry connector) authenticate to
Security Center with a service account that is scoped to a single **partition**.
A partition is a security boundary: the account sees *exactly* the entities that
are members of that partition — nothing else. If an entity of a type the
integration cares about is **not** a member of that partition, the integration
silently never sees it. That is the same "silent partial results" failure the
Helix concept doc calls out as a top risk.

This macro is the defensive counterpart. For a chosen **target partition** and a
chosen set of **entity types**, every run ensures that every entity of those
types is a member of the partition. It is **add-only**: it never removes
membership, so it cannot hide entities from other users or break another
integration.

> **One sentence:** keep a partition's membership complete for the selected
> entity types, so a partition-scoped integration always sees the full set.

---

## 2. Scope & non-goals

**In scope**
- A scheduled / on-demand macro (`UserMacro`) that scans all entities of the
  selected types and adds any that are missing from the target partition.
- Operator-selectable entity types via Config Tool checkboxes:
  **Cardholders, Credentials, Doors, Areas**.
- A safe **report-only** default (preview what it would add, write nothing).
- A per-run summary in the macro log; one **optional** alarm on failure.

**Out of scope (YAGNI)**
- **Removal / full reconcile.** The macro never removes membership. (The user
  chose add-only; the dangerous `MoveToPartition` API is explicitly avoided.)
- **Per-change alarms.** Routine adds are logged, not alarmed.
- **Event-driven / real-time** addition of new entities. This is a batch sweep
  driven by a Config Tool scheduled task (project rule: no internal loops).
- **Entity types beyond the four.** Adding a new type later is a small, deliberate
  code edit, not a config change.

---

## 3. Glossary

| Term | Meaning |
|------|---------|
| **Partition** | A Security Center security boundary. A user/integration granted access to a partition sees only the entities that are *members* of it. |
| **Member** | An entity that belongs to a partition. `Partition.Members` is the list of member GUIDs. **[VERIFIED — Ref Guide p. 1210]** |
| **Add-only** | The macro only ever *adds* members; it never removes. An entity can belong to multiple partitions at once, so adding to the target never removes it from others. **[VERIFIED — Ref Guide, `MoveToPartition` remarks + `Entity.GetPartitions` p. 1072]** |
| **Report-only** | A mode where the macro logs what it *would* add but performs no writes. The safe default. |
| **Run-as user** | The Security Center user the macro engine runs as. Must hold `ManagePartitionMemberships` (or write access on the partition) for writes to succeed. |

---

## 4. SDK surface (verified before design)

All confirmed from the guides in `Guide/` (text layer cached in
`.claude/knowledge/_raw/`) by the `genetec-guide-researcher` agent on 2026-06-06.

| Need | API | Tag |
|------|-----|-----|
| The partition entity | `Genetec.Sdk.Entities.Partition`, obtained via `Sdk.GetEntity(guid) as Partition` | class **[VERIFIED — Ref Guide p. 1206]**; the `as Partition` cast is the standard `GetEntity(...) as <EntityClass>` pattern **[VERIFY cast]** |
| Read current members | `partition.Members` → `ReadOnlyCollection<Guid>` | **[VERIFIED — Ref Guide p. 1210]** |
| Add a member (add-only) | `(entity as PartitionSupportEntity).InsertIntoPartition(Guid partition)` → `bool`; throws `SdkException` if not logged on or no write access | **[VERIFIED — Ref Guide p. 1212]** |
| Multi-partition membership | An entity may belong to multiple partitions; `InsertIntoPartition` is additive and does **not** remove from others. `MoveToPartition` is the only "move" op and is **NOT used**. | **[VERIFIED — Ref Guide, `MoveToPartition` remarks; `Entity.GetPartitions` p. 1072]** |
| Enumerate entities by type | `Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as EntityConfigurationQuery`, then `query.EntityTypeFilter.Add(EntityType.X)` | **[VERIFIED — Dev Guide]** |
| Entity type enum values | `EntityType.Cardholder` (7), `EntityType.Credential` (9), `EntityType.Door` (11), `EntityType.Area` (5) | **[VERIFIED — Ref Guide p. 292]** |
| Write wrapper | `Sdk.TransactionManager.ExecuteTransaction(Action)` — auto commit, auto rollback on exception. Used **per entity** (one add per transaction) so a failure rolls back only that entity. | **[VERIFIED — Dev Guide p. 116]** |
| Required privilege | `SdkPrivilege.ManagePartitionMemberships` on the run-as user, or write access on the partition | **[VERIFIED — Ref Guide]** |

**Gotchas captured for the build:**
- Entities must be cached by the `EntityConfigurationQuery` before `GetEntity`
  works on them. **[VERIFIED — Dev Guide]**
- `Query()` throws inside a transaction with pending updates → run **all
  enumeration queries first**, then open one transaction. (Lesson reused from
  the God Mode macro.)
- `InsertIntoPartition` returns `false` (or throws) when the run-as user lacks
  rights — count and log these per type; do not let one failure abort the rest.

---

## 5. Placement & files

```
Macros/Integrations/Integration Partition Sync/
  IntegrationPartitionSync.cs   # the macro (class IntegrationPartitionSync)
  README.md                     # operator setup: scheduled task, privileges, params
```

`Macros/Integrations/` is a new sub-folder (the user requested it). The macro
entity is named **Integration Partition Sync**; the class is
`IntegrationPartitionSync` and the file is `IntegrationPartitionSync.cs`. The
README mirrors the God Mode macro's operator-facing README style.

---

## 6. Parameters (Config Tool fields)

Macro parameters are public properties. Allowed types are `Boolean`, `String`,
`Int32`, `DateTime`, `Guid` (project rule). Each boolean renders as a checkbox;
each `Guid` renders as an entity picker.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `TargetPartition` | `Guid` | empty | The partition to keep complete. Entity picker. Required. |
| `SyncCardholders` | `bool` | false | Include Cardholder entities. |
| `SyncCredentials` | `bool` | false | Include Credential entities. |
| `SyncDoors` | `bool` | false | Include Door entities. |
| `SyncAreas` | `bool` | false | Include Area entities. |
| `Apply` | `bool` | **false** | **Safe by default.** Unticked = preview only: log what *would* be added, write nothing. Tick it to actually add members. |
| `FailureAlarm` | `Guid` | empty | Optional. If set, raise this alarm once when the run fails. Empty = log-only. |

**Design note:** one checkbox per type was chosen over a comma-separated string
for a clear, typo-proof Config Tool UI. The supported set is small and stable;
adding a new type is a deliberate code edit.

**Why `Apply` and not `ReportOnly`:** a Config Tool `Boolean` always starts
`false`. A `ReportOnly` flag would therefore be `false` (i.e. *writes enabled*)
until an operator remembered to tick it — the first run would write, the opposite
of safe-by-default. Inverting to `Apply` makes the zero-value the safe value: do
nothing and the macro only previews. The operator must consciously tick `Apply`
to make changes.

---

## 7. Execution flow

```
Execute():
  log "started"
  try:
    # --- validate ---
    if TargetPartition is empty:           log error, return
    partition = Sdk.GetEntity(TargetPartition) as Partition
    if partition is null:                  log error (deleted?), return

    selectedTypes = [] built from the four booleans
        SyncCardholders -> EntityType.Cardholder
        SyncCredentials -> EntityType.Credential
        SyncDoors       -> EntityType.Door
        SyncAreas       -> EntityType.Area
    if selectedTypes is empty:             log warning "nothing selected", return

    members = HashSet<Guid>(partition.Members)     # one read, used for skip-check

    # --- PHASE 1: enumerate (ALL queries before ANY transaction) ---
    toAddByType = {}
    for type in selectedTypes:
        guids = EnumerateEntityGuids(type)         # EntityConfigurationQuery
                                                   # query failure -> THROW (fail loud)
        toAddByType[type] = [g for g in guids if g not in members]

    # --- PHASE 2: write (skipped entirely unless Apply) ---
    if not Apply:
        for type, list in toAddByType:  log "would add {count} {type}"
    else:
        for type, list in toAddByType:
            for guid in list:
                pse = Sdk.GetEntity(guid) as PartitionSupportEntity
                if pse is null:  log warning (vanished), count error, continue
                # ONE entity per transaction, catch OUTSIDE the lambda (God Mode
                # AutoAddDoor precedent). A failure rolls back only this entity and
                # is honestly counted as an error — never reported as added.
                try:
                    ExecuteTransaction(() =>
                        if not pse.InsertIntoPartition(TargetPartition):
                            throw  # force rollback so a false return counts as error
                    )
                    count added
                catch (ThreadAbortException): rethrow      # engine stop — never swallow
                catch (addEx):                             # per-entity resilience (§8)
                    count error, log warning               # one failure must not abort the rest

    # --- summary ---
    for type:  log "scanned={n} alreadyPresent={p} added/would-add={a} errors={e}"
    log "completed"
  catch (ex):
    MacroLogger.TraceError(ex, "IntegrationPartitionSync.Execute() failed.")
    if FailureAlarm is set:  RaiseFailureAlarm(ex.Message)

CleanUp():
  # no persistent resources or event subscriptions
```

**Helper `EnumerateEntityGuids(EntityType)`** mirrors the God Mode macro's
`GetAllDoorGuids`: create the `EntityConfigurationQuery`, add the type filter,
run synchronously, and **throw** if the query returns null / not-success / null
data (so a read failure never masquerades as "0 to add").

---

## 8. Error handling & safety

- The whole body of `Execute()` is wrapped in `try`/`catch`; the full exception
  is logged with `MacroLogger.TraceError(ex, ...)`.
- **Fail loud on read failure:** if any enumeration query fails, throw — never
  continue to a misleading "0 added" summary. A half-working sync recreates the
  exact "integration sees partial data" risk this macro exists to prevent.
- **Safe by default (`Apply` unticked):** with no operator action the first run is
  a no-op preview that writes nothing. The operator ticks `Apply` once the preview
  looks right. (See §6 for why the flag is `Apply`, not `ReportOnly`.)
- **Add-only guarantee:** the only mutating call is `InsertIntoPartition`. The
  macro never calls `MoveToPartition`, `RemoveMember`, or `RemoveFromPartition`,
  so it can never remove an entity from any partition.
- **Per-entity resilience (truthful counts):** each entity is added in its **own**
  `ExecuteTransaction`, with the `try`/`catch` **outside** the lambda — the God
  Mode `AutoAddDoor` precedent. A `false` return is turned into a throw so it rolls
  back and is counted as an error (never reported as added); a thrown `SdkException`
  rolls back only that one entity and is counted/logged. Successful entities each
  commit independently, so one failure cannot poison the batch or abort the run.
  The catch rethrows `ThreadAbortException` so an engine stop is never swallowed.
  (A single bulk transaction was rejected: catching a throw *inside* the lambda
  does not guarantee the transaction still commits, which could make the summary
  report adds that actually rolled back — unacceptable for a security sync.)
- **Skip-already-present:** entities already in `partition.Members` are skipped,
  so re-runs are cheap and don't fire redundant change events.

---

## 9. Prerequisites (documented in the README)

1. **Run-as privilege:** the macro engine's user must hold
   `ManagePartitionMemberships`, or have write access on the target partition.
   Without it, every `InsertIntoPartition` throws `SdkException`.
2. **Scheduled task:** create a Config Tool scheduled task to run the macro on
   the desired cadence (e.g. nightly, off-peak). The macro has no internal timer.
3. **Optional alarm:** if `FailureAlarm` is used, pre-create an Alarm entity and
   select it in the parameter.
4. **First run:** leave `Apply` unticked, run on demand, read the log summary,
   confirm the "would add" counts look right, then tick `Apply` and run again.

---

## 10. Failure modes & how they appear in the log

| Failure | Cause | Appears as |
|---------|-------|-----------|
| Nothing happens, "nothing selected" | No type checkbox ticked | Warning line, clean return |
| `SdkException` per entity | Run-as user lacks `ManagePartitionMemberships` | Per-entity warnings + non-zero `errors` count; FailureAlarm if the whole run throws |
| Run throws, alarm raised | Enumeration query failed (DB/Directory issue) | `TraceError` with stack + single failure alarm |
| Partition not found | `TargetPartition` deleted/empty | Clear error line, early return |
| Counts look low | Run-as user can't *see* some entities (its own partition scope) | `scanned` lower than expected — documented caveat in README |

---

## 11. Open items — status after macro-reviewer pass (2026-06-06)

1. ✅ **RESOLVED.** `Sdk.GetEntity(guid) as Partition` — `Partition` confirmed
   (Ref p. 1206); standard cast pattern.
2. ✅ **RESOLVED.** All four target types derive from `PartitionSupportEntity`:
   `Cardholder` and `Credential` directly; `Door` and `Area` via
   `AccessPointGroup : PartitionSupportEntity`. The `as PartitionSupportEntity`
   cast is non-null for each, so the `AddMember` fallback is not needed.
3. ✅ **RESOLVED.** GUID column is `"Guid"` — same as the working God Mode macro.
   (Confirm once more in the lab run as cheap insurance.)
4. ✅ **RESOLVED.** README documents the run-as scope caveat (§10) in Task 8.
5. ✅ **RESOLVED (per-entity transaction).** Resilience now uses one
   `ExecuteTransaction` **per entity** with the `try`/`catch` **outside** the
   lambda (God Mode `AutoAddDoor` precedent), so a failed add rolls back only that
   entity and is counted as an error — the summary never reports an add that
   actually rolled back. The earlier "catch inside one bulk transaction" approach
   was rejected as unverified.
6. ⏳ **LAB CHECK.** Confirm first-apply runtime is acceptable at your scale
   (per-entity transactions mean one commit per added entity; the bulk of a daily
   run only adds *new* entities because already-present ones are skipped). If a
   very large first apply is too slow, batch the adds, keeping each batch's
   `try`/`catch` outside its `ExecuteTransaction`.

---

*SDK identifiers tagged **[VERIFIED]** were confirmed from the guides in `Guide/`
on 2026-06-06 with page numbers. Items tagged **[VERIFY]** must be confirmed while
coding, consistent with this project's rule never to ship a guessed SDK surface.*
