# God Mode Access Level Audit — Concept

**Status:** Concept (approved design, not yet implemented)
**Date:** 2026-05-31
**Target platform:** Genetec Security Center 5.13 (also targets 5.12)
**Author:** TechSec

---

## 1. Problem

We maintain an access rule we informally call **"God Mode"** that is supposed to
grant access to **every door in the system**. When a new door is onboarded,
someone has to remember to add it to God Mode. If they forget, God Mode silently
stops being complete, and we don't find out until someone needs it.

We want a daily automated check that catches any door missing from God Mode,
alerts an operator, and (optionally) fixes it automatically.

## 2. Terminology — important SDK correction

In everyday speech we call this an **"access level."** The Security Center 5.13
SDK has **no `AccessLevel` entity**. The concept "a set of doors that a credential
can open on a schedule" is modeled by the **`AccessRule`** entity
(`Genetec.Sdk.Entities.AccessRule`, verified Reference Guide p. 888). Everywhere
below, "God Mode" means a specific **Access Rule**.

A second non-obvious point drives the whole design:

> An access rule's membership is **not** stored as a list of doors. It is stored
> as a list of **access points** (`AccessRule.RelatedAccessPoints`, a read-only
> `ReadOnlyCollection<Guid>` of access-point GUIDs — Reference Guide pp. 888, 891).

A door is an access-point *group*: it has two sides (`DoorSideIn`, `DoorSideOut`),
and each side carries access points (Reader, REX, entry sensor). So the question
"is this door in God Mode?" really means "are this door's access points in the
rule?" — and adding a door means adding its access points.

## 3. Goal and non-goals

**Goal:** Once per day, verify every door's access points are present in the
chosen God Mode access rule. For each door that is missing (fully or partially):
raise one alarm naming the door, and — when enabled — add the door's access
points to the rule.

**Non-goals (deliberately out of scope):**
- No per-door exclusions. "God Mode" means literally every door, no exceptions.
  (See *Future extensions* for how an exclusion flag could be added later.)
- No email/SMS notification — alarms only.
- No *removing* doors from the rule. The macro only adds.
- No cardholder or credential changes.
- No auto-creating the alarm or the access rule — both must already exist and are
  chosen by the user.

## 4. Behavior summary

| Aspect | Decision |
|--------|----------|
| Trigger | Daily, via a **Config Tool scheduled task** (no internal loop). |
| On missing door | Raise **one alarm per missing door**, context = the door's name. |
| Auto-fix | Add the door's access points to the rule — **gated by a toggle**, default **off**. |
| Exclusions | None. Every door must be in the rule. |
| Alarm cap | None (per operator choice). Mitigated by running report-only first. |

## 5. User-set parameters (public properties on the macro)

The Security Center macro framework exposes public properties of supported types
(`Guid`, `String`, `Boolean`, `Int32`, `DateTime`) as configurable fields in
Config Tool. For `Guid` properties it shows an entity picker.

| Parameter | Type | Purpose |
|-----------|------|---------|
| `GodModeAccessRule` | `Guid` | The access rule that must contain every door. Config Tool shows an entity picker. |
| `MissingDoorAlarm` | `Guid` | The alarm entity to trigger once per missing door. |
| `EnableAutoAdd` | `Boolean` | **Default `false`.** `false` = report-only (alarm + log). `true` = also add missing doors to the rule. |

## 6. Execution flow

`Execute()` runs once per scheduled trigger. The entire body is wrapped in
`try/catch`; any uncaught exception is logged with `MacroLogger.TraceError`.

1. **Log entry.**
2. **Validate parameters.** Both GUIDs must be non-empty. Resolve
   `Sdk.GetEntity(GodModeAccessRule) as AccessRule` and
   `Sdk.GetEntity(MissingDoorAlarm) as Alarm`; null-check each. On bad input, log
   a clear error and return — never crash.
3. **Enumerate all doors with a single cached query.** Use one
   `EntityConfigurationQuery` (`ReportType.EntityConfiguration`) filtered to
   `EntityType.Door`, with related data downloaded so each door's sides and
   access points are cached. This query runs **before** any transaction.
4. **For each door (reading from cache only):**
   - Determine the door's membership access points (its two sides — see §7).
   - Ask the rule whether it contains them (`AccessRule.IsMember(accessPointGuid)`
     / check against `RelatedAccessPoints`).
   - If the door is **not fully** in the rule:
     - Trigger the alarm:
       `Sdk.AlarmManager.TriggerAlarm(MissingDoorAlarm, doorGuid,
       new DynamicAlarmContent("God Mode is missing door: <name> (<guid>)"))`,
       and attach the door entity (`DynamicAlarmContent.AttachedEntities.Add(doorGuid)`)
       so the operator sees it in the Alarm Monitoring task.
     - Log a warning with the door name + GUID.
     - If `EnableAutoAdd`, remember this door for the write step.
5. **If `EnableAutoAdd` and there are missing doors:** for each remembered door,
   add its missing access points to the rule via
   `accessPoint.AccessRules.Add(GodModeAccessRule)`, inside a **per-door**
   transaction (`Sdk.TransactionManager.ExecuteTransaction(...)`). Per-door
   transactions isolate failures — a door in a partition the macro user can't
   write fails alone and the others still succeed. Log each success/failure.
6. **Summary log:** total doors scanned, number missing, alarms raised, doors
   added (or "auto-add disabled").

### Critical ordering rule

All reads/queries happen in step 3–4, **before** the transactions in step 5. The
SDK throws if `ReportQuery.Query()` runs inside a transaction that has pending
updates (Reference Guide p. 2960).

## 7. Open item — exact membership definition (resolve in lab, not in code review)

We must define *precisely which access points* constitute "the door is in the
rule," because auto-add has to write the **same** set that Config Tool writes when
a human adds a door — otherwise hand-configured and macro-configured doors look
different.

**Plan:** the default working definition is the door's two sides
(`DoorSideIn` / `DoorSideOut`). Before enabling writes, confirm empirically:

1. In a lab system, manually add one door to the rule in Config Tool.
2. Dump that rule's `RelatedAccessPoints` and note exactly which access-point
   GUIDs appear for that door.
3. Make detection and auto-add match that exact set.

A door that is only **partially** present is treated as missing; auto-add adds the
missing access points.

### 7a. Which *side* of the relationship is authoritative (also lab-resolved)

The rule↔access-point link can be read from two ends: `AccessRule.RelatedAccessPoints`
(the rule side) and `AccessPoint.AccessRules` (the access-point side, the only
**writable** end). The SDK guide does **not** document whether these stay in sync —
i.e., whether adding the rule via `AccessPoint.AccessRules.Add(rule)` makes that access
point appear in `RelatedAccessPoints` (verified "not found in guide", 2026-06-01).

**The audit decision FAILS CLOSED on the rule side.** A door's access point counts as
covered **only if it appears in `RelatedAccessPoints`** — the rule's own membership
view. The access-point signal (`apRules`) is logged for diagnostics but never trusted
for the verdict, because trusting the writable mirror could let a door that is not
actually in the rule's effective membership pass the audit (a fail-open hole that would
defeat the macro's purpose). Each missing-door log line prints both signals
(`ruleList=… apRules=…`). Consequence: a door that was auto-added but does **not** show
up on the rule side will keep alarming — which is correct, because it signals the write
did not achieve rule membership. The lab run (Task 8) reveals which side Config Tool
populates when a human adds a door and confirms whether `AccessRules.Add` actually
updates `RelatedAccessPoints`; only then is auto-add trustworthy.

## 8. Deployment & processing impact

**Where it runs.** Macros run on the **Directory server** (Developer Guide p. 133);
the Directory instantiates them and a macro-agent process
(`GenetecMacroAgent32.exe`, p. 145) hosts execution. There is **no documented way
to pin a macro to a different server** — its host is whichever server currently
owns the Directory role.

**Failover.** We run Directory failover. This macro runs on the **active**
Directory and follows the Directory role through a failover. Because it is a
single `.cs` file (no external assembly), the "copy the assembly to the failover
Directory" requirement (p. 146) does not apply — there's nothing extra to deploy.
The scripting guide does **not** document what happens to an *in-flight* macro
during failover; for a once-a-day job this is low risk, and the per-door
transaction design means an interrupted run leaves completed doors added and
re-detects the rest on the next run.

**Processing cost.**
- *Steady state* (rule already complete): one bulk cached query + cheap in-memory
  membership checks + zero alarms + zero transactions. Negligible; run it at an
  off-peak hour via the scheduled task.
- *First run / drift*: cost scales with the **number of missing doors** — one alarm
  (and, if enabled, one transaction) each. A large backlog on day one is the
  "alarm flood" risk; mitigate by running **report-only first**, fixing big
  backlogs manually, then enabling auto-add for ongoing drift (usually 0–2 doors).

**Scalability rule baked into the design (Developer Guide p. 23):** repeatedly
calling `Sdk.GetEntity()` for uncached entities *can crash the Directory* on large
systems. The macro therefore pulls doors and their access points in **one
`EntityConfigurationQuery`** and reads only from cache — never a per-door
`GetEntity` loop.

No numeric CPU/memory limits are documented; the guidance is qualitative ("keep
the number of running macros under control," p. 133). One daily macro is well
within it.

## 9. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Auto-add re-adds a door someone *intended* to exclude | Accepted: policy is "every door, no exclusions." Revisit via the Future-extensions exclusion flag if policy changes. |
| Alarm flood on first run | Run report-only first; fix large backlog manually before enabling auto-add. |
| Macro user lacks write rights on a door's partition | Per-door transaction fails in isolation; logged by door name; other doors still processed. |
| Auto-add writes the wrong access-point set | Resolve the §7 open item in lab before enabling writes. |
| Heavy enumeration harming the Directory | Single cached `EntityConfigurationQuery`; no per-door `GetEntity`. |
| Failover mid-run | Low risk for a daily job; interrupted run self-heals next day. |

## 10. Future extensions (not built now)

- **Per-door exclusion flag:** a custom field (e.g. `ExcludeFromGodMode = true`)
  read during the loop to skip a door from both the alarm and auto-add. Would let
  policy carve out genuinely sensitive doors without editing the macro.
- **Alarm cap per run** to hard-stop floods.
- **Summary email** in addition to per-door alarms.

## 11. Verified SDK surface (for the implementer)

All confirmed against the Security Center 5.13 guides.

| Capability | Verified API | Source |
|-----------|--------------|--------|
| Access-rule entity | `Genetec.Sdk.Entities.AccessRule : PartitionSupportEntity` | Ref p. 888 |
| Rule membership (read-only) | `AccessRule.RelatedAccessPoints` → `ReadOnlyCollection<Guid>` (access-point GUIDs) | Ref pp. 888, 891 |
| Membership test | `AccessRule.IsMember(Guid)` / `IsMember(Entity)` | Ref pp. 889–890 |
| Enumerate doors | `Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as EntityConfigurationQuery`, add `EntityType.Door` | Dev p. 136; perf guidance Dev p. 23 |
| Door → access points | `Door.DoorSideIn` / `DoorSideOut` (`DoorSide`) → `Reader` / `EntrySensor` / `Rex` / `AccessPointSide` | Ref pp. 1054, 1056 |
| Add door to rule (mutation) | `AccessPoint.AccessRules.Add(AccessRule)` (`AccessRuleCollection`) | Ref pp. 887, 893–894 |
| Transactions for writes | `Sdk.TransactionManager.ExecuteTransaction(Action)` (auto commit/rollback) | Ref pp. 246, 2958–2961 |
| Trigger alarm | `Sdk.AlarmManager.TriggerAlarm(Guid alarm, Guid sourceEntityGuid, DynamicAlarmContent)` → returns instance id, `-1` on failure | Ref pp. 2875–2876; Dev p. 119 |
| Alarm context/attachments | `new DynamicAlarmContent(string context)`; `.AttachedEntities.Add(Guid)`; shown in Alarm Monitoring | Ref pp. 1059–1060 |
| Entity type values | `Door = 11`, `AccessRule = 2`, `AccessPoint = 1`, `Alarm = 3` | Ref pp. 292–293 |
| Logging | `MacroLogger.TraceInformation/TraceWarning/TraceError` | KB / Ref p. 2793 |

**Not found in guide (do not assume):** any `AccessLevel` type; any mutating method
on `AccessRule` directly (mutation goes through `AccessPoint.AccessRules`); a
`TriggerAlarm`-specific threading/transaction requirement; failover transfer
semantics for an in-flight macro; **whether `AccessRule.RelatedAccessPoints` and
`AccessPoint.AccessRules` are kept in sync** (the macro checks both ends defensively —
see §7a).
