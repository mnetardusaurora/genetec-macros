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
