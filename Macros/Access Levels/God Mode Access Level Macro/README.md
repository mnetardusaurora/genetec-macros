# God Mode Access Level Audit

A Genetec Security Center macro that checks, on a schedule, that **every door in
the system is included in a chosen access rule** (the one we call "God Mode"). If a
door is missing — for example, because someone onboarded it and forgot to add it —
the macro raises an alarm naming that door, and can optionally add the door to the
rule automatically.

- **Platform:** Security Center 5.13 (also targets 5.12)
- **Macro file:** `GodModeAccessLevelAudit.cs` (in this folder)
- **Trigger:** a Config Tool **Scheduled task** (see *Scheduling* below)
- **Design docs:** `TechSec Concepts/God Mode Access Level Macro/` (concept +
  implementation plan)

> **Terminology note.** What we call "God Mode" is, in SDK terms, an **Access Rule**.
> Security Center 5.13 has no "Access Level" entity. Rule membership is stored as a
> list of **access points** (a door has two sides, each with reader/REX/sensor access
> points), not whole doors — which is why the macro audits a door's access points.

---

## What it does, each run

1. Reads every Door in the system (one cached query — safe on large systems).
2. For each door, checks whether its access points are listed in the God Mode rule's
   membership (`RelatedAccessPoints`). The check **fails closed**: if a door is not in
   the rule, it is flagged — no exceptions.
3. For each missing door, raises **one alarm** whose description names the door, and
   attaches the door so it appears in the Alarm Monitoring task.
4. If `EnableAutoAdd` is on, adds each missing door's access points to the rule inside
   a per-door database transaction (one failure doesn't block the others).
5. Logs a summary: doors scanned, doors missing, whether auto-add ran.

If it cannot even read the list of doors, it **fails loudly** (logs an error) rather
than silently reporting "nothing missing."

---

## Prerequisites

Before importing, make sure these exist in Security Center:

- The **access rule** that should contain every door (your "God Mode" rule).
- An **alarm** entity to raise for missing doors. Set its recipients so the right
  operators see it in Alarm Monitoring.

The macro needs **no custom fields**.

---

## Importing into Config Tool

1. Open **Config Tool → System → Macros** (or **Tasks → Macros**).
2. Create a new **Macro** entity (e.g. name it *God Mode Access Level Audit*).
3. Open its **Properties / source** tab and **paste the entire contents of
   `GodModeAccessLevelAudit.cs`**.
4. Apply. Security Center compiles the macro on apply — fix any reported compile
   errors before continuing (there should be none).

### Set the parameters

On the macro's default execution context, set:

| Parameter | Type | What to set it to |
|-----------|------|-------------------|
| `GodModeAccessRule` | Guid | Pick your God Mode access rule in the entity browser. |
| `MissingDoorAlarm` | Guid | Pick the alarm to raise for missing doors. |
| `EnableAutoAdd` | Boolean | **Leave `false` at first** (report-only). Turn on later. |

---

## Scheduling (how to make it run daily)

Security Center runs a macro on a schedule through a **Scheduled task** — that is
where you choose when it runs.

1. Open **Config Tool → Tasks → Scheduled tasks** (the action scheduler).
2. Click **+ Scheduled task**, give it a name (e.g. *Daily God Mode Audit*).
3. Set **Action = Run a macro**, and select the *God Mode Access Level Audit* macro.
4. Set the **Recurrence** to when you want it to run — for a daily check, choose a
   daily recurrence at an **off-peak time** (e.g. 02:00). This start time is the
   "beginning of the time range" at which the audit runs.
5. **Apply.**

> The macro does **not** schedule itself. The SDK has no event for "a schedule's time
> range started," so the supported, recommended way to control timing is this
> Scheduled task. Pick the schedule/time here.

---

## Recommended rollout

1. **Report-only first.** With `EnableAutoAdd = false`, run the macro on demand and
   read the macro log. Confirm the door count is right and that missing doors are
   flagged and alarmed.
2. **Confirm the access-point mapping (one-time).** Manually add one door to the rule
   in Config Tool, re-run, and confirm that door stops being flagged. The per-missing-
   door log line shows two signals per access point — `ruleList=` (the rule's own
   membership) and `apRules=` (the access point's view) — so you can see exactly what
   the rule contains.
3. **Then enable auto-add.** Set `EnableAutoAdd = true`. Run, and confirm in Config
   Tool that auto-added doors land in the rule **the same way a hand-added door does**,
   and that re-running no longer flags them.
4. **Schedule it** (above) once you're confident.

---

## What an operator sees

For each missing door, an alarm instance appears in **Alarm Monitoring** with the
context: `God Mode is missing door: <door name> (<guid>)`, and the door entity
attached. One alarm is raised per missing door.

---

## Known limitations

- **Auto-add write behavior should be lab-confirmed before production.** The SDK guide
  does not document whether adding a rule on the access-point side is reflected on the
  rule side. The audit is safe regardless (it fails closed and will keep alarming a
  door that was not truly added), but confirm step 3 above in a lab before trusting
  auto-add in production.
- **No exclusions.** Every door is expected in the rule; there is no per-door opt-out.
- **The door query has no timeout** (the SDK's query call doesn't offer one). It runs
  once per scheduled run at an off-peak time, so this is low risk, but be aware on very
  large or unresponsive systems.

---

## Rollback / disabling

- **Stop it running:** disable the **Scheduled task** (Config Tool → Tasks → Scheduled
  tasks → set inactive), and/or set the **Macro** entity's state to Disabled.
- **Undo an auto-add:** the macro never removes doors. If a run added something it
  shouldn't have, remove those access points from the rule manually in Config Tool.

---

## Log strings worth grepping

| Meaning | String |
|---------|--------|
| Run started / finished | `Execute() started.` / `Execute() completed.` |
| Doors read | `Enumerated N door(s).` |
| A missing door | `MISSING: door '<name>'` (includes `ruleList=` / `apRules=`) |
| Alarm raised | `Raised alarm instance` |
| Auto-add result | `Auto-added door` / `Failed to auto-add door` |
| Run failed (audit could not complete) | `Execute() failed.` |
