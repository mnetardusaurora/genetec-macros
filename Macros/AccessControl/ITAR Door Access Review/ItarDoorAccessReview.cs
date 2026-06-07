// ----------------------------------------------------------------------------
//  ItarDoorAccessReview.cs
//  Scheduled, read-only review that flags non US Person cardholders who hold
//  access to ITAR marked doors, raising one alarm per violating cardholder.
// ----------------------------------------------------------------------------
//  Purpose         Detect every cardholder who is NOT a member of the
//                  designated US Person cardholder group but who can be granted
//                  access to at least one ITAR marked door, and raise a chosen
//                  alarm for each such cardholder. This macro never modifies
//                  access or configuration. It only reads and reports.
//  Trigger         Scheduled (run to completion from a Config Tool scheduled
//                  task). Not a persistent monitor.
//  Category        AccessControl
//  Visibility      Public
//                  NOTE: Public marks this macro as eligible for the public
//                  catalog only AFTER security and IP review. It contains ITAR
//                  relevant detection logic, so it must be reviewed by the ISSM
//                  before live alarms are enabled, and confirmed to carry no
//                  sensitive site content before it is ever published.
//  Platform        Security Center 5.13 (also targets 5.12)
//
//  Reads           Door (and the Door custom field named "ITAR"), AccessPoint,
//                  AccessRule, Schedule, Credential, Cardholder, CardholderGroup,
//                  Partition membership (via the engine), Alarm (the target).
//  Writes          Nothing. Read only. The only outward effect is raising the
//                  chosen alarm when ReportOnlyMode is false.
//  Parameters      UsPersonGroup (Guid, required), AlarmToRaise (Guid, required),
//                  ReportOnlyMode (Boolean, default true),
//                  ActiveCardholdersOnly (Boolean, default false),
//                  PartitionScope (Guid, default Guid.Empty = all sites).
//  Custom fields   Door."ITAR" (exact name match required, case sensitive).
//  Privileges      The executing account must be able to read access control
//                  configuration, read custom fields, read partition membership,
//                  and trigger the chosen alarm. The macro SDK is already
//                  authenticated as admin, so no login handling is needed.
//
//  Compliance      ITAR (22 CFR 120-130). This macro reads ITAR relevant access
//                  entitlement and must be reviewed by the ISSM before live
//                  alarms are enabled. Run with ReportOnlyMode = true first and
//                  review the macro log before setting it false.
//
//  Author          Matthew Netardus
//  Created         2026-06-06
//  Guide           See README.md in this folder.
//
//  VERIFY (needs live 5.13 validation on the Windows target, see README):
//   1. The Door "ITAR" custom field data type (Text, value list, or Boolean)
//      and the exact stored representation of the true value. IsItarDoorValue
//      handles both a Boolean field and a Text/value list field whose value
//      equals ITAR_TRUE_VALUE, but confirm the real type and the exact stored
//      string in Config Tool.
//   2. AccessVerifier.GetAccessResults with door GUIDs passed as access point
//      groups expands to each door's access points, and AccessResult.Granted is
//      the correct signal here (antipassback is not considered by the verifier,
//      which is acceptable for entitlement detection).
//   3. Sdk.GetPartitions(cardholder.Guid) returns the partitions used by the
//      PartitionScope filter, and the customer's partition model matches the
//      single, non hierarchical PartitionScope assumption.
//   4. Cardholder.Credentials enumerates Credential entities whose Guid is the
//      value GetAccessResults expects for the credential parameter.
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Genetec.Sdk;
using Genetec.Sdk.Entities;
using Genetec.Sdk.Queries;
using Genetec.Sdk.Scripting;
using Genetec.Sdk.Scripting.Interfaces.Attributes;
using Genetec.Sdk.Workflows;

// SingleInstance prevents overlapping scheduled runs. Run64Bit gives the access
// verifier headroom on large sites.
[MacroParameters(SingleInstance = true, Run64Bit = true)]
public sealed class ItarDoorAccessReview : UserMacro
{
    // The Door custom field that marks a door as ITAR controlled. Must match the
    // Config Tool field name exactly. Custom field names are case sensitive.
    private const string ITAR_DOOR_FIELD_NAME = "ITAR";

    // A door counts as ITAR only when the field value equals this exactly. Any
    // other value (False, Unknown, empty, null) is treated as not ITAR.
    private const string ITAR_TRUE_VALUE = "True";

    // --- Parameters (set in Config Tool on the macro's execution context) ---

    // The cardholder group whose members are US Persons. Members are allowed to
    // hold ITAR door access and are never flagged. Required.
    public Guid UsPersonGroup { get; set; }

    // The alarm raised once per violating cardholder when ReportOnlyMode is false.
    // Required.
    public Guid AlarmToRaise { get; set; }

    // Default true: log each violation that WOULD raise an alarm, but raise
    // nothing. The first production run should use true. Alarms fire only when
    // this is set false.
    public bool ReportOnlyMode { get; set; } = true;

    // Default false: when false, check all cardholders, since an inactive non US
    // Person that still holds ITAR entitlement is a finding. When true, restrict
    // to cardholders whose status state is Active.
    public bool ActiveCardholdersOnly { get; set; } = false;

    // Optional limit to a single partition. Guid.Empty (the default) means check
    // every partition (all sites).
    public Guid PartitionScope { get; set; } = Guid.Empty;

    public override void Execute()
    {
        MacroLogger.TraceInformation("ItarDoorAccessReview.Execute() started.");
        try
        {
            // --- Validate parameters and resolve them to the expected types ---
            if (UsPersonGroup.Equals(Guid.Empty))
            {
                MacroLogger.TraceError(
                    new ArgumentException("UsPersonGroup not set."),
                    "UsPersonGroup parameter is empty. Set it to the US Person cardholder group.");
                return;
            }
            if (AlarmToRaise.Equals(Guid.Empty))
            {
                MacroLogger.TraceError(
                    new ArgumentException("AlarmToRaise not set."),
                    "AlarmToRaise parameter is empty. Set it to the alarm to raise per violation.");
                return;
            }

            CardholderGroup usPersonGroup = Sdk.GetEntity(UsPersonGroup) as CardholderGroup;
            if (usPersonGroup == null)
            {
                MacroLogger.TraceError(
                    new ArgumentException("US Person group not found."),
                    $"No CardholderGroup found for GUID {UsPersonGroup}. Was it deleted, or is the " +
                    "parameter pointing at a different entity type?");
                return;
            }

            Alarm targetAlarm = Sdk.GetEntity(AlarmToRaise) as Alarm;
            if (targetAlarm == null)
            {
                MacroLogger.TraceError(
                    new ArgumentException("Alarm not found."),
                    $"No Alarm found for GUID {AlarmToRaise}. Was it deleted, or is the parameter " +
                    "pointing at a different entity type?");
                return;
            }

            MacroLogger.TraceInformation(
                $"Reviewing against US Person group '{usPersonGroup.Name}'. " +
                $"ReportOnlyMode={ReportOnlyMode}, ActiveCardholdersOnly={ActiveCardholdersOnly}, " +
                $"PartitionScope={(PartitionScope.Equals(Guid.Empty) ? "all" : PartitionScope.ToString())}.");

            // --- Resolve the ITAR door custom field definition ---
            SystemConfiguration sysConfig = Sdk.GetEntity(SdkGuids.SystemConfiguration) as SystemConfiguration;
            if (sysConfig == null)
            {
                MacroLogger.TraceError(
                    new InvalidOperationException("SystemConfiguration not found."),
                    "Cannot access SystemConfiguration to resolve the ITAR custom field.");
                return;
            }

            CustomField itarField =
                sysConfig.CustomFieldService.GetCustomField(ITAR_DOOR_FIELD_NAME, EntityType.Door);
            if (itarField == null)
            {
                // A missing ITAR field on the Door schema is a security relevant
                // configuration error for an ITAR review macro, so fail loudly
                // rather than silently reporting "no ITAR doors".
                MacroLogger.TraceError(
                    new InvalidOperationException("ITAR door field missing."),
                    $"Custom field '{ITAR_DOOR_FIELD_NAME}' is not defined on the Door entity type. " +
                    "Confirm the field name in Config Tool. Schema drift in an ITAR review is a bug.");
                return;
            }

            bool itarFieldIsBoolean = itarField.ValueType == CustomFieldValueType.Boolean;
            MacroLogger.TraceInformation(
                $"ITAR door field resolved. ValueType={itarField.ValueType} " +
                $"(treatedAsBoolean={itarFieldIsBoolean}).");

            // --- Populate the entity cache once, before any verification ---
            // AccessVerifier reads from the cache, so the supporting entities must
            // be loaded first. This mirrors the official AccessVerifier sample's
            // LoadEntities step.
            PopulateSupportingCache();

            // --- Find ITAR doors (guid -> name) ---
            Dictionary<Guid, string> itarDoors = FindItarDoors(itarFieldIsBoolean);
            if (itarDoors.Count == 0)
            {
                MacroLogger.TraceInformation(
                    "No ITAR doors found (no Door has the ITAR field set to the true value). " +
                    "Nothing to review. Exiting cleanly.");
                MacroLogger.TraceInformation("ItarDoorAccessReview.Execute() completed.");
                return;
            }
            List<Guid> itarDoorGuids = itarDoors.Keys.ToList();
            MacroLogger.TraceInformation($"Found {itarDoors.Count} ITAR door(s) to review against.");

            // --- Build the US Person member set (handles nested groups) ---
            HashSet<Guid> usPersonSet = BuildUsPersonMemberSet(usPersonGroup);
            MacroLogger.TraceInformation(
                $"US Person set contains {usPersonSet.Count} cardholder GUID(s) " +
                "(direct and nested group members).");

            // --- Review the population: non US Persons, honoring the filters ---
            DateTime nowUtc = DateTime.UtcNow;
            string operatorContext = BuildOperatorContext();

            List<Guid> cardholderGuids = GetAllCardholderGuids();
            int checkedCount = 0;
            int violationCount = 0;
            int alarmsRaised = 0;
            int alarmsSimulated = 0;
            int errorCount = 0;

            foreach (Guid cardholderGuid in cardholderGuids)
            {
                try
                {
                    Cardholder cardholder = Sdk.GetEntity(cardholderGuid) as Cardholder;
                    if (cardholder == null)
                    {
                        MacroLogger.TraceWarning(
                            $"Cardholder {cardholderGuid} vanished mid-run; skipping.");
                        continue;
                    }

                    // US Persons are allowed to hold ITAR access; skip them.
                    if (IsInUsPersonSet(cardholderGuid, usPersonSet))
                    {
                        continue;
                    }

                    // Optional partition limit.
                    if (!PartitionScope.Equals(Guid.Empty) &&
                        !IsInPartition(cardholderGuid, PartitionScope))
                    {
                        continue;
                    }

                    // Optional active only limit.
                    if (ActiveCardholdersOnly && !IsActiveCardholder(cardholder))
                    {
                        continue;
                    }

                    checkedCount++;

                    // Which ITAR doors (if any) grant this cardholder access.
                    HashSet<Guid> grantingDoorGuids =
                        FindGrantingItarDoors(cardholder, itarDoorGuids, nowUtc);

                    if (grantingDoorGuids.Count == 0)
                    {
                        continue;
                    }

                    violationCount++;

                    // Build the ITAR audit content: who, which door(s), the
                    // execution context as the operator, and a UTC timestamp.
                    List<string> doorLabels = grantingDoorGuids
                        .Select(dg => itarDoors.ContainsKey(dg)
                            ? $"{itarDoors[dg]} ({dg})"
                            : dg.ToString())
                        .ToList();
                    string doorList = string.Join("; ", doorLabels);

                    // The door list reflects the first credential that granted access
                    // (see FindGrantingItarDoors). It names at least one ITAR door this
                    // cardholder can reach, not necessarily all of them.
                    string content =
                        $"ITAR access violation. Cardholder '{cardholder.Name}' " +
                        $"({cardholder.Guid}) is not a US Person and can be granted access to " +
                        $"ITAR door(s), at least: {doorList}. Operator: {operatorContext}. " +
                        $"Detected (UTC): {nowUtc:yyyy-MM-dd HH:mm:ss}Z.";

                    // Always log the violation with full context, in either mode.
                    MacroLogger.TraceWarning("ITAR VIOLATION: " + content);

                    if (ReportOnlyMode)
                    {
                        alarmsSimulated++;
                        MacroLogger.TraceInformation(
                            $"ReportOnlyMode is on. Would have raised alarm {AlarmToRaise} for " +
                            $"cardholder '{cardholder.Name}' ({cardholder.Guid}). No alarm raised.");
                    }
                    else
                    {
                        bool raised = RaiseViolationAlarm(cardholder, grantingDoorGuids, content);
                        if (raised)
                        {
                            alarmsRaised++;
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                }
                catch (Exception perCardholder)
                {
                    // A failure on one cardholder must not abort the rest of the run.
                    errorCount++;
                    MacroLogger.TraceError(perCardholder,
                        $"Review failed for cardholder {cardholderGuid}; continuing with the rest.");
                }
            }

            MacroLogger.TraceInformation(
                $"Review summary: itarDoors={itarDoors.Count}, cardholdersChecked={checkedCount}, " +
                $"violations={violationCount}, alarmsRaised={alarmsRaised}, " +
                $"alarmsSimulated={alarmsSimulated}, errors={errorCount}, " +
                $"mode={(ReportOnlyMode ? "report-only" : "live")}.");
            MacroLogger.TraceInformation("ItarDoorAccessReview.Execute() completed.");
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "ItarDoorAccessReview.Execute() failed.");
        }
    }

    // ------------------------------------------------------------------------
    //  Pure decisions. These accept plain types and are reviewable without the
    //  SDK assemblies present, as required by the spec.
    // ------------------------------------------------------------------------

    // True when the door's ITAR custom field value means ITAR controlled.
    // fieldIsBoolean tells us how to interpret rawValue: as a Boolean field, or
    // as a Text / value list field whose stored value must equal ITAR_TRUE_VALUE.
    private static bool IsItarDoorValue(object rawValue, bool fieldIsBoolean)
    {
        if (rawValue == null)
        {
            return false;
        }

        if (fieldIsBoolean)
        {
            if (rawValue is bool boolValue)
            {
                return boolValue;
            }
            // Some boolean fields surface as a "True"/"False" string. Parse safely.
            bool parsed;
            return bool.TryParse(Convert.ToString(rawValue), out parsed) && parsed;
        }

        // Text or value list field: require an exact match to the true value.
        // VERIFY: the exact stored string of a value list "True" option.
        return string.Equals(Convert.ToString(rawValue), ITAR_TRUE_VALUE, StringComparison.Ordinal);
    }

    // True only when the verifier granted access. Inconclusive and Error are not
    // treated as granted; the caller logs them as verification gaps.
    private static bool IsGranted(AccessResult result)
    {
        return result == AccessResult.Granted;
    }

    // True when the cardholder is a member of the US Person set.
    private static bool IsInUsPersonSet(Guid cardholderGuid, HashSet<Guid> usPersonSet)
    {
        return usPersonSet.Contains(cardholderGuid);
    }

    // ------------------------------------------------------------------------
    //  SDK backed helpers.
    // ------------------------------------------------------------------------

    // Loads the supporting entities AccessVerifier relies on into the cache.
    // Doors and cardholders are loaded separately by their enumeration helpers.
    private void PopulateSupportingCache()
    {
        EntityConfigurationQuery query =
            Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as EntityConfigurationQuery;
        if (query == null)
        {
            MacroLogger.TraceWarning(
                "Could not create the supporting cache query; access checks may be incomplete.");
            return;
        }

        query.EntityTypeFilter.Add(EntityType.AccessPoint);
        query.EntityTypeFilter.Add(EntityType.AccessRule);
        query.EntityTypeFilter.Add(EntityType.Schedule);
        query.EntityTypeFilter.Add(EntityType.Credential);
        query.EntityTypeFilter.Add(EntityType.CardholderGroup);

        QueryCompletedEventArgs result = query.Query();
        if (result == null || !result.Success)
        {
            MacroLogger.TraceWarning(
                "Supporting cache query did not report success; access checks may be incomplete.");
            return;
        }

        MacroLogger.TraceInformation(
            "Loaded supporting entities into the cache (access points, access rules, " +
            "schedules, credentials, cardholder groups).");
    }

    // Enumerates every door, reads its ITAR field, and returns the ITAR doors as
    // guid -> name. The query also caches the doors for later access checks.
    private Dictionary<Guid, string> FindItarDoors(bool itarFieldIsBoolean)
    {
        Dictionary<Guid, string> itarDoors = new Dictionary<Guid, string>();

        EntityConfigurationQuery query =
            Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as EntityConfigurationQuery;
        if (query == null)
        {
            throw new InvalidOperationException(
                "Could not create the door enumeration query; cannot review ITAR access this run.");
        }
        query.EntityTypeFilter.Add(EntityType.Door);

        QueryCompletedEventArgs result = query.Query();
        if (result == null || !result.Success || result.Data == null)
        {
            // An ITAR review must never silently report "no ITAR doors" when it
            // could not even read the doors. Fail loudly.
            throw new InvalidOperationException(
                "Door enumeration query failed; cannot review ITAR access this run.");
        }

        int scanned = 0;
        // The distinct raw ITAR values seen across all doors. Logged once so a
        // mismatch between the real stored value and ITAR_TRUE_VALUE is visible,
        // rather than silently classifying every door as not ITAR.
        HashSet<string> distinctRawValues = new HashSet<string>();
        foreach (DataRow row in result.Data.Rows)
        {
            try
            {
                if (!(row["Guid"] is Guid doorGuid))
                {
                    MacroLogger.TraceWarning("A door row has no usable Guid column; skipping it.");
                    continue;
                }

                Door door = Sdk.GetEntity(doorGuid) as Door;
                if (door == null)
                {
                    MacroLogger.TraceWarning($"Door {doorGuid} vanished mid-run; skipping.");
                    continue;
                }
                scanned++;

                IReadOnlyList<CustomFieldValue> values = door.GetCustomFields();
                CustomFieldValue match =
                    values.FirstOrDefault(fv => fv.CustomField.Name == ITAR_DOOR_FIELD_NAME);
                object rawValue = match == null ? null : match.Value;
                distinctRawValues.Add(rawValue == null ? "<null>" : Convert.ToString(rawValue));

                if (IsItarDoorValue(rawValue, itarFieldIsBoolean))
                {
                    itarDoors[doorGuid] = door.Name;
                    MacroLogger.TraceInformation(
                        $"ITAR door: '{door.Name}' ({doorGuid}), ITAR value '{rawValue}'.");
                }
            }
            catch (Exception perDoor)
            {
                MacroLogger.TraceError(perDoor,
                    "Failed to read the ITAR field on a door; skipping this door.");
            }
        }

        MacroLogger.TraceInformation(
            $"Distinct ITAR field values seen across scanned doors: " +
            $"{string.Join(", ", distinctRawValues.Select(value => "'" + value + "'"))}. " +
            $"A door counts as ITAR when this is '{ITAR_TRUE_VALUE}' (or a Boolean true).");
        MacroLogger.TraceInformation(
            $"Scanned {scanned} door(s); {itarDoors.Count} are ITAR controlled.");
        return itarDoors;
    }

    // Expands the US Person group into a flat set of member cardholder GUIDs.
    // Children may include nested cardholder groups, so this recurses with a
    // cycle guard.
    private HashSet<Guid> BuildUsPersonMemberSet(CardholderGroup usPersonGroup)
    {
        HashSet<Guid> members = new HashSet<Guid>();
        HashSet<Guid> visitedGroups = new HashSet<Guid>();
        ExpandGroupMembers(usPersonGroup, members, visitedGroups);
        return members;
    }

    private void ExpandGroupMembers(
        CardholderGroup group, HashSet<Guid> members, HashSet<Guid> visitedGroups)
    {
        if (group == null)
        {
            return;
        }
        if (!visitedGroups.Add(group.Guid))
        {
            // Already expanded this group; guards against membership cycles.
            return;
        }

        foreach (Guid childGuid in group.Children)
        {
            CardholderGroup childGroup = Sdk.GetEntity(childGuid) as CardholderGroup;
            if (childGroup != null)
            {
                ExpandGroupMembers(childGroup, members, visitedGroups);
            }
            else
            {
                // A non group child is a cardholder member. We only need the GUID,
                // so this holds even if the cardholder is not in the cache.
                members.Add(childGuid);
            }
        }
    }

    // Enumerates every cardholder GUID. The query also caches the cardholders.
    private List<Guid> GetAllCardholderGuids()
    {
        List<Guid> cardholderGuids = new List<Guid>();

        EntityConfigurationQuery query =
            Sdk.ReportManager.CreateReportQuery(ReportType.EntityConfiguration) as EntityConfigurationQuery;
        if (query == null)
        {
            throw new InvalidOperationException(
                "Could not create the cardholder enumeration query; cannot review ITAR access this run.");
        }
        query.EntityTypeFilter.Add(EntityType.Cardholder);

        QueryCompletedEventArgs result = query.Query();
        if (result == null || !result.Success || result.Data == null)
        {
            throw new InvalidOperationException(
                "Cardholder enumeration query failed; cannot review ITAR access this run.");
        }

        foreach (DataRow row in result.Data.Rows)
        {
            if (row["Guid"] is Guid cardholderGuid)
            {
                cardholderGuids.Add(cardholderGuid);
            }
            else
            {
                MacroLogger.TraceWarning("A cardholder row has no usable Guid column; skipping it.");
            }
        }

        MacroLogger.TraceInformation($"Enumerated {cardholderGuids.Count} cardholder(s).");
        return cardholderGuids;
    }

    // Returns the set of ITAR door GUIDs that grant this cardholder access. Stops
    // at the first credential that produces any grant (dedupe per cardholder).
    private HashSet<Guid> FindGrantingItarDoors(
        Cardholder cardholder, List<Guid> itarDoorGuids, DateTime nowUtc)
    {
        HashSet<Guid> grantingDoorGuids = new HashSet<Guid>();

        if (cardholder.Credentials == null)
        {
            // A cardholder with no credential records is a normal state, not an
            // error, so skip cleanly rather than throwing into the error count.
            return grantingDoorGuids;
        }

        foreach (Credential credential in cardholder.Credentials)
        {
            if (credential == null)
            {
                continue;
            }

            try
            {
                // Pass the ITAR door GUIDs as access point groups. The verifier
                // expands each door to its access points and returns one result
                // per access point, with ParentGroup pointing back at the door.
                // The two trailing Guid.Empty values are the (unused) secondary
                // credential and reader parameters.
                IEnumerable<AccessVerifier.AccessPointResult> results =
                    Sdk.AccessVerifier.GetAccessResults(
                        itarDoorGuids, nowUtc, credential.Guid, Guid.Empty, Guid.Empty);

                if (results == null)
                {
                    MacroLogger.TraceWarning(
                        $"Access verifier returned null for cardholder '{cardholder.Name}' " +
                        $"({cardholder.Guid}), credential {credential.Guid}. Treating as no access.");
                    continue;
                }

                bool sawAnyResult = false;
                foreach (AccessVerifier.AccessPointResult apResult in results)
                {
                    sawAnyResult = true;
                    if (IsGranted(apResult.Result))
                    {
                        grantingDoorGuids.Add(apResult.ParentGroup);
                    }
                    else if (apResult.Result == AccessResult.Inconclusive ||
                             apResult.Result == AccessResult.Error)
                    {
                        // Inconclusive can mean a read rights gap on the involved
                        // entities, which could mask a real grant. Log it so the
                        // gap is noticed rather than silently passing.
                        MacroLogger.TraceWarning(
                            $"Access verification gap for cardholder '{cardholder.Name}' " +
                            $"({cardholder.Guid}), credential {credential.Guid}, access point " +
                            $"{apResult.AccessPoint} on door {apResult.ParentGroup}: result " +
                            $"{apResult.Result}.");
                    }
                }

                if (!sawAnyResult)
                {
                    // Zero results can mean the door GUIDs were not accepted as access
                    // point groups (see VERIFY note 2). Surface it rather than silently
                    // treating the cardholder as having no ITAR access.
                    MacroLogger.TraceWarning(
                        $"Access verifier returned no results for cardholder '{cardholder.Name}' " +
                        $"({cardholder.Guid}), credential {credential.Guid}, against " +
                        $"{itarDoorGuids.Count} ITAR door(s). Confirm door GUIDs expand as " +
                        "access point groups on this 5.13 target.");
                }

                if (grantingDoorGuids.Count > 0)
                {
                    // This cardholder is already a violation; no need to check the
                    // remaining credentials.
                    break;
                }
            }
            catch (Exception perCredential)
            {
                MacroLogger.TraceError(perCredential,
                    $"Access check failed for cardholder '{cardholder.Name}' ({cardholder.Guid}), " +
                    $"credential {credential.Guid}; continuing with the next credential.");
            }
        }

        return grantingDoorGuids;
    }

    // True when the cardholder's status state is Active. Status is an access
    // status object, not a plain enum, so null check it first.
    private static bool IsActiveCardholder(Cardholder cardholder)
    {
        return cardholder.Status != null && cardholder.Status.State == CardholderState.Active;
    }

    // True when the entity belongs to the given partition. Partition membership
    // is read from the engine, not from the entity.
    private bool IsInPartition(Guid entityGuid, Guid partitionScope)
    {
        IEnumerable<Guid> partitions = Sdk.GetPartitions(entityGuid);
        if (partitions == null)
        {
            return false;
        }
        foreach (Guid partitionGuid in partitions)
        {
            if (partitionGuid.Equals(partitionScope))
            {
                return true;
            }
        }
        return false;
    }

    // Raises one alarm for a violating cardholder. Returns true on success.
    private bool RaiseViolationAlarm(
        Cardholder cardholder, HashSet<Guid> grantingDoorGuids, string content)
    {
        DynamicAlarmContent alarmContent = new DynamicAlarmContent(content);
        alarmContent.AttachedEntities.Add(cardholder.Guid);
        foreach (Guid doorGuid in grantingDoorGuids)
        {
            alarmContent.AttachedEntities.Add(doorGuid);
        }

        int instanceId = Sdk.AlarmManager.TriggerAlarm(AlarmToRaise, cardholder.Guid, alarmContent);
        if (instanceId == -1)
        {
            MacroLogger.TraceWarning(
                $"TriggerAlarm returned -1 for cardholder '{cardholder.Name}' ({cardholder.Guid}). " +
                "Alarm was not raised. Check the alarm entity and the executing account's rights.");
            return false;
        }

        MacroLogger.TraceInformation(
            $"Raised alarm instance {instanceId} for cardholder '{cardholder.Name}' ({cardholder.Guid}).");
        return true;
    }

    // Describes the execution context as the operator. A scheduled run has no
    // interactive operator, so we record the service account context: who or what
    // instigated the run plus this macro instance, for the audit trail.
    private string BuildOperatorContext()
    {
        return $"scheduled macro (instigator GUID {InstigatorGuid}, macro instance {InstanceGuid})";
    }

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release.
    }
}
