// ----------------------------------------------------------------------------
//  IntegrationPartitionSync.cs
//  Add-only sync that keeps a chosen partition complete for selected entity types.
// ----------------------------------------------------------------------------
//  Purpose         Ensures every entity of the selected types (Cardholders,
//                  Credentials, Doors, Areas) is a member of a chosen target
//                  partition, so a partition-scoped integration never silently
//                  misses entities. Never removes membership.
//  Trigger         Scheduled (run by a Config Tool scheduled task). Also on demand.
//  Category        Integrations
//  Visibility      Public
//  Version         1.0.0
//  Platform        Security Center 5.13 (also targets 5.12)
//
//  Reads           Entities of the selected types (enumerated by an
//                  EntityConfigurationQuery), and the target partition's Members.
//  Writes          Partition membership (adds only), and only when Apply is true.
//                  One optional alarm instance when a run fails.
//  Parameters      TargetPartition (Guid); SyncCardholders, SyncCredentials,
//                  SyncDoors, SyncAreas (Boolean); Apply (Boolean, default false,
//                  so the default is a safe preview); FailureAlarm (Guid, optional).
//  Custom fields   none
//  Privileges      The run-as user needs ManagePartitionMemberships, or write
//                  access on the target partition, or every add fails.
//
//  Author          Matthew Netardus
//  Created         2026-06-06
//  Guide           See README.md in this folder.
// ----------------------------------------------------------------------------
//
//  Note: InsertIntoPartition is add-only and additive. An entity may belong to
//  multiple partitions at once, so adding to the target never removes it from any
//  other partition. This macro never calls MoveToPartition or RemoveMember.

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

            // PHASE 2: write. Skipped entirely in preview mode (no writes at all).
            if (Apply)
            {
                foreach (EntityType type in selectedTypes)
                {
                    foreach (Guid guid in toAddByType[type])
                    {
                        // Entities were cached by the Phase 1 queries, so GetEntity
                        // here is a cache lookup, not a Directory query.
                        PartitionSupportEntity entity =
                            Sdk.GetEntity(guid) as PartitionSupportEntity;
                        if (entity == null)
                        {
                            MacroLogger.TraceWarning(
                                $"{type} {guid} vanished or is not partition-able; skipping.");
                            errorsByType[type]++;
                            continue;
                        }

                        // ONE transaction per entity, with the catch OUTSIDE the lambda
                        // (God Mode AutoAddDoor precedent). A failure rolls back only this
                        // entity and is counted as an error, so the summary never reports an
                        // add that actually rolled back. A single bulk transaction was
                        // rejected because catching a throw INSIDE the lambda does not
                        // guarantee the remaining adds still commit (see the design notes).
                        try
                        {
                            Sdk.TransactionManager.ExecuteTransaction(() =>
                            {
                                // ADD-ONLY: InsertIntoPartition is additive. It never
                                // removes the entity from any other partition. A false
                                // return is turned into a throw so this transaction rolls
                                // back and the entity is counted as an error, not an add.
                                if (!entity.InsertIntoPartition(TargetPartition))
                                    throw new InvalidOperationException(
                                        "InsertIntoPartition returned false (no write access?).");
                            });
                            addedByType[type]++;
                        }
                        catch (System.Threading.ThreadAbortException)
                        {
                            // The macro engine is stopping this macro, so never swallow it.
                            throw;
                        }
                        catch (Exception addEx)
                        {
                            // Per-entity resilience (see the design notes): one entity's
                            // failure, for example an SdkException for missing rights, must
                            // not abort the rest.
                            errorsByType[type]++;
                            MacroLogger.TraceWarning(
                                $"Failed to add {type} '{entity.Name}' ({guid}): {addEx.Message}. " +
                                "Check macro-user ManagePartitionMemberships rights on this partition.");
                        }
                    }
                }
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
                        $"wouldAdd={toAdd} (preview, no writes, Apply not set).");
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
    // sync must never report "0 to add" when it could not even read the entities.
    // That is exactly the "integration sees partial data" failure this macro
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
        // BEFORE any transaction. Query() throws inside a transaction with
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
            return;  // optional: operator chose log-only

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
