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
using System.Data;
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
            List<Guid> doorGuids = GetAllDoorGuids();
            // The rule's access points, captured once. RelatedAccessPoints is the
            // verified, unambiguous membership list.
            HashSet<Guid> ruleAccessPoints = new HashSet<Guid>(godModeRule.RelatedAccessPoints);

            var missingDoorGuids = new List<Guid>();
            int scanned = 0;

            foreach (Guid doorGuid in doorGuids)
            {
                Door door = Sdk.GetEntity(doorGuid) as Door;   // cached by the door query
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
                var apDetail = new List<string>();
                foreach (KeyValuePair<string, AccessPoint> ap in points)
                {
                    bool inRule = ruleAccessPoints.Contains(ap.Value.Guid);
                    apDetail.Add($"{ap.Key}={ap.Value.Guid} inRule={inRule}");
                    if (!inRule)
                    {
                        fullyInRule = false;
                    }
                }

                if (!fullyInRule)
                {
                    // Full per-access-point detail is logged only for missing doors,
                    // so the log stays readable on large systems while still showing
                    // exactly which access points are absent (used to reconcile which
                    // access points define membership).
                    MacroLogger.TraceWarning(
                        $"MISSING: door '{door.Name}' ({doorGuid}) not fully in God Mode. " +
                        $"Access points: {string.Join("; ", apDetail)}");
                    missingDoorGuids.Add(doorGuid);
                    RaiseMissingDoorAlarm(door);
                }
            }
            if (EnableAutoAdd && missingDoorGuids.Count > 0)
            {
                MacroLogger.TraceInformation(
                    $"Auto-add enabled. Adding {missingDoorGuids.Count} door(s).");
                foreach (Guid doorGuid in missingDoorGuids)
                {
                    Door door = Sdk.GetEntity(doorGuid) as Door;
                    if (door != null)
                    {
                        AutoAddDoor(door, godModeRule);
                    }
                }
            }
            else if (!EnableAutoAdd && missingDoorGuids.Count > 0)
            {
                MacroLogger.TraceInformation(
                    "Auto-add disabled (report-only). No changes written.");
            }
            MacroLogger.TraceInformation(
                $"Audit summary: scanned={scanned}, missing={missingDoorGuids.Count}, " +
                $"autoAdd={(EnableAutoAdd ? "on" : "off")}.");
            MacroLogger.TraceInformation("GodModeAccessLevelAudit.Execute() completed.");
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "GodModeAccessLevelAudit.Execute() failed.");
        }
    }

    private List<Guid> GetAllDoorGuids()
    {
        var doorGuids = new List<Guid>();
        var query = Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration)
            as EntityConfigurationQuery;
        if (query == null)
        {
            MacroLogger.TraceError(
                new InvalidOperationException("EntityConfigurationQuery unavailable."),
                "Could not create the door enumeration query.");
            return doorGuids;
        }
        query.EntityTypeFilter.Add(EntityType.Door);

        // Synchronous: blocks, returns results, and caches the doors. Run BEFORE any
        // transaction — Query() throws inside a transaction with pending updates.
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
        // Reader/Rex/EntrySensor are AccessPoint objects. Each may be null on a
        // partially configured door. AccessPointSide is an enum and is NOT included.
        if (side.Reader != null)
            points.Add(new KeyValuePair<string, AccessPoint>(sideLabel + ":Reader", side.Reader));
        if (side.Rex != null)
            points.Add(new KeyValuePair<string, AccessPoint>(sideLabel + ":Rex", side.Rex));
        if (side.EntrySensor != null)
            points.Add(new KeyValuePair<string, AccessPoint>(sideLabel + ":EntrySensor", side.EntrySensor));
    }

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

    private void AutoAddDoor(Door door, AccessRule rule)
    {
        try
        {
            List<KeyValuePair<string, AccessPoint>> points = GetDoorAccessPoints(door);
            Sdk.TransactionManager.ExecuteTransaction(() =>
            {
                foreach (KeyValuePair<string, AccessPoint> ap in points)
                {
                    if (!ap.Value.AccessRules.Contains(rule))
                        ap.Value.AccessRules.Add(rule);
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

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
