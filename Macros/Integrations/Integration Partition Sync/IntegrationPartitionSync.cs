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
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "IntegrationPartitionSync.Execute() failed.");
            // RaiseFailureAlarm(ex) added in Task 7.
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

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
