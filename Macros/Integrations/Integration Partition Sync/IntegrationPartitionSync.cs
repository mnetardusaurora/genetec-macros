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
//                SyncDoors, SyncAreas (Boolean); Apply (Boolean, default false — safe: preview unless ticked);
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

    // Safe by default. A Config Tool Boolean starts FALSE, so when the operator
    // does nothing the macro PREVIEWS (logs what it would add) and writes nothing.
    // The operator must consciously tick Apply to actually add members.
    public bool Apply { get; set; }

    // Optional. If set, one alarm is raised when the run fails. Empty = log-only.
    public Guid FailureAlarm { get; set; }

    public override void Execute()
    {
        MacroLogger.TraceInformation("IntegrationPartitionSync.Execute() started.");
        try
        {
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
                $"Apply={Apply}.");

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

            // Per-type counters initialised for both modes so the summary is uniform.
            var addedByType = new Dictionary<EntityType, int>();
            var errorsByType = new Dictionary<EntityType, int>();
            foreach (EntityType type in selectedTypes)
            {
                addedByType[type] = 0;
                errorsByType[type] = 0;
            }

            // PHASE 2: write. Skipped entirely in preview mode (no transaction opened).
            if (Apply)
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

                            try
                            {
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
                            catch (Exception addEx)
                            {
                                // Per-entity resilience (spec §8/§10): one entity's failure
                                // (e.g. SdkException for missing rights) must not abort the
                                // rest. Count it, log it, continue; the catch keeps the
                                // transaction valid so the successful adds still commit.
                                errorsByType[type]++;
                                MacroLogger.TraceWarning(
                                    $"InsertIntoPartition threw for {type} '{entity.Name}' ({guid}): " +
                                    $"{addEx.Message}. Check macro-user ManagePartitionMemberships rights.");
                            }
                        }
                    }
                });
            }

            // --- Per-type summary (the log IS the debugger for macros) ---
            foreach (EntityType type in selectedTypes)
            {
                int scanned = scannedByType[type];
                int toAdd = toAddByType[type].Count;
                int alreadyPresent = scanned - toAdd;
                if (!Apply)
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

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
