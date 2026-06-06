# Integration Partition Sync

Keeps a **partition** complete for selected entity types so a 3rd-party
integration whose Security Center account is scoped to that partition always sees
the full set of entities. **Add-only:** it never removes anything from any
partition.

## What it does each run

1. Reads the current members of the **target partition**.
2. Enumerates every entity of the **selected types** (Cardholders, Credentials,
   Doors, Areas).
3. Adds the ones that are missing — but only if **Apply** is ticked. Otherwise it
   just logs what it *would* add (a safe preview).
4. Writes a per-type summary to the macro log; raises one alarm only if the run
   fails (optional).

## Parameters

| Parameter | What to set |
|-----------|-------------|
| `TargetPartition` | The partition to keep complete (entity picker). **Required.** |
| `SyncCardholders` / `SyncCredentials` / `SyncDoors` / `SyncAreas` | Tick the types this integration consumes. |
| `Apply` | **Leave unticked for a safe preview** (logs what it would add, writes nothing). Tick it only when you want the macro to actually add members. |
| `FailureAlarm` | Optional. Pick an Alarm entity to be raised if a run fails. Leave empty for log-only. |

## Prerequisites

- **Privilege:** the user the Macro role runs as must have **Manage partition
  memberships** (or explicit write access on the target partition). Without it,
  every add fails and the per-type `errors` count will be non-zero.
- **Schedule:** create a **Scheduled task** in Config Tool to run this macro on
  your cadence (e.g. nightly, off-peak). The macro has no internal timer.

## First run (do this once)

1. Set `TargetPartition` and tick the entity types.
2. Leave `Apply` unticked. Run the macro on demand.
3. Open the macro log. Confirm the `[Type] scanned=… wouldAdd=…` lines look right.
4. Tick `Apply` and run again. The log should now show `added=…`.
5. Attach the macro to the scheduled task.

## Reading the log

- `scanned` — how many entities of that type exist (that the macro's account can see).
- `alreadyPresent` — already members of the partition.
- `wouldAdd` (preview) / `added` — newly added members.
- `errors` — adds that failed (usually a missing **Manage partition memberships**
  privilege).

> **Caveat:** `scanned` reflects only the entities the macro's run-as account can
> see. If that account is itself partition-scoped, counts can be lower than the
> true total. Run the macro as an account with broad read access.
