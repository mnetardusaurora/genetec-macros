// ----------------------------------------------------------------------------
//  AreaToPartitionReconciliation.cs
//  Add-only sweep that keeps each mapped partition complete for the entities
//  living inside a mapped area's subtree.
// ----------------------------------------------------------------------------
//  Purpose         For each configured area-to-partition mapping, ensures every
//                  in-scope entity inside that area's subtree (the area's own
//                  members plus the members of all descendant sub-areas) is a
//                  member of the mapped partition. Membership changes are
//                  additive only: the macro never removes an entity from any
//                  partition. A clean system produces no changes (idempotent).
//  Trigger         Scheduled. Run by a Config Tool scheduled task, off-hours.
//                  The macro does no scheduling of its own.
//  Category        Integrations
//  Visibility      Public
//  Platform        Security Center 5.13 (also targets 5.12)
//
//  Reads           Area subtree membership (Area.AllDoors, Area.Cameras,
//                  Area.Zones, and Area.CaptiveAreas for recursion), and each
//                  mapped Partition's current Members.
//  Writes          Partition membership (adds only), and only when
//                  ReportOnlyMode is false. Never removes membership.
//  Parameters      ReportOnlyMode (Boolean, default true): when true, logs every
//                  change it would make and writes nothing.
//                  Area01..Area50 / Partition01..Partition50 (Guid pairs): the
//                  area-to-partition mappings. An unused slot leaves either GUID
//                  empty. This block is the ONLY place the mapping count is set.
//  Custom fields   none (mappings come from parameters)
//  Privileges      The run-as user needs rights to modify partition membership
//                  (ManagePartitionMemberships, or write access on each mapped
//                  partition), or every add fails. The SDK is already
//                  authenticated as admin, so no login handling is required.
//
//  Compliance      Partition reconciliation alters entity visibility boundaries
//                  across multi-site scope. This is a write operation. Recommend
//                  ISSM awareness before enabling live writes (ReportOnlyMode
//                  false).
//
//  Author          Matthew Netardus
//  Created         2026-06-06
//  Guide           See README.md in this folder.
// ----------------------------------------------------------------------------
//
//  VERIFY list (confirm against the installed 5.13 reference on the Windows
//  target before enabling live writes):
//   1. Run64Bit is appropriate and enabled in this environment.
//   2. Reflecting over the macro's own public Guid properties is permitted in
//      the macro sandbox. The guide documents no reflection restriction and
//      Config Tool itself reads parameters by reflection, so this is expected
//      to work. If it does not, fall back to an explicit pair list (the count
//      then lives in two places). See DiscoverMappings.
//   3. The MacroParameters attribute lives in
//      Genetec.Sdk.Scripting.Interfaces.Attributes and exposes SingleInstance
//      and Run64Bit. (Confirmed in project knowledge; reconfirm on target.)
//   4. Area member enumeration (AllDoors, Cameras, Zones) and sub-area
//      traversal (CaptiveAreas) behave as the Reference Guide (p. 924 to 927)
//      describes. Confirmed against the indexed 5.13 reference; reconfirm on
//      the target. See CollectSubtreeMembers.
//   5. Sdk.GetEntity resolves an area-member GUID in a scheduled run without a
//      prior caching query. The canonical AddBookmark example resolves a
//      parameter GUID this way, so this is expected. See ProcessMapping.
//   6. InsertIntoPartition is additive: it adds to the target partition without
//      removing the entity from any other partition. The guide documents the
//      verb "Inserts" plus separate RemoveFromPartition and MoveToPartition
//      methods, so additive behavior is strongly supported but not stated
//      verbatim. Confirm with a live before-and-after check. See ProcessMapping.
//   7. "Readers" are not a documented Area member type in 5.13. In-scope types
//      default to doors, cameras, and zones. See InScopeMemberSelectors.
//   8. ReportOnlyMode reads TRUE in Config Tool before the first run. The safe
//      default is set in the constructor below, but confirm Config Tool surfaces
//      it as true (some platforms force Booleans to false). If it shows false,
//      tick it true before applying, since a false value writes live.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Genetec.Sdk;
using Genetec.Sdk.Scripting;
using Genetec.Sdk.Scripting.Interfaces.Attributes;
using Genetec.Sdk.Entities;

// SingleInstance = true so two scheduled runs can never overlap and double-sweep.
// Run64Bit = true for memory headroom at enterprise entity counts. See VERIFY 1 and 3.
[MacroParameters(SingleInstance = true, Run64Bit = true)]
public sealed class AreaToPartitionReconciliation : UserMacro
{
    // ------------------------------------------------------------------------
    //  Configuration parameters (set in Config Tool)
    // ------------------------------------------------------------------------

    // Default true. A Config Tool Boolean starts FALSE, but this property is
    // initialized to true in the constructor below so the first run previews.
    // When true, the macro logs every change it WOULD make and commits nothing.
    // Live writes occur only when an operator consciously sets this false.
    public bool ReportOnlyMode { get; set; }

    // The area-to-partition mapping block. This is the ONLY place the mapping
    // count is set. To add a mapping slot, add one AreaNN/PartitionNN pair at
    // the tail. To remove one, delete a pair at the tail. The Execute logic
    // discovers these pairs by reflection (see DiscoverMappings) and never
    // references the count anywhere. Leave both GUIDs of an unused slot empty.
    public Guid Area01 { get; set; }  public Guid Partition01 { get; set; }
    public Guid Area02 { get; set; }  public Guid Partition02 { get; set; }
    public Guid Area03 { get; set; }  public Guid Partition03 { get; set; }
    public Guid Area04 { get; set; }  public Guid Partition04 { get; set; }
    public Guid Area05 { get; set; }  public Guid Partition05 { get; set; }
    public Guid Area06 { get; set; }  public Guid Partition06 { get; set; }
    public Guid Area07 { get; set; }  public Guid Partition07 { get; set; }
    public Guid Area08 { get; set; }  public Guid Partition08 { get; set; }
    public Guid Area09 { get; set; }  public Guid Partition09 { get; set; }
    public Guid Area10 { get; set; }  public Guid Partition10 { get; set; }
    public Guid Area11 { get; set; }  public Guid Partition11 { get; set; }
    public Guid Area12 { get; set; }  public Guid Partition12 { get; set; }
    public Guid Area13 { get; set; }  public Guid Partition13 { get; set; }
    public Guid Area14 { get; set; }  public Guid Partition14 { get; set; }
    public Guid Area15 { get; set; }  public Guid Partition15 { get; set; }
    public Guid Area16 { get; set; }  public Guid Partition16 { get; set; }
    public Guid Area17 { get; set; }  public Guid Partition17 { get; set; }
    public Guid Area18 { get; set; }  public Guid Partition18 { get; set; }
    public Guid Area19 { get; set; }  public Guid Partition19 { get; set; }
    public Guid Area20 { get; set; }  public Guid Partition20 { get; set; }
    public Guid Area21 { get; set; }  public Guid Partition21 { get; set; }
    public Guid Area22 { get; set; }  public Guid Partition22 { get; set; }
    public Guid Area23 { get; set; }  public Guid Partition23 { get; set; }
    public Guid Area24 { get; set; }  public Guid Partition24 { get; set; }
    public Guid Area25 { get; set; }  public Guid Partition25 { get; set; }
    public Guid Area26 { get; set; }  public Guid Partition26 { get; set; }
    public Guid Area27 { get; set; }  public Guid Partition27 { get; set; }
    public Guid Area28 { get; set; }  public Guid Partition28 { get; set; }
    public Guid Area29 { get; set; }  public Guid Partition29 { get; set; }
    public Guid Area30 { get; set; }  public Guid Partition30 { get; set; }
    public Guid Area31 { get; set; }  public Guid Partition31 { get; set; }
    public Guid Area32 { get; set; }  public Guid Partition32 { get; set; }
    public Guid Area33 { get; set; }  public Guid Partition33 { get; set; }
    public Guid Area34 { get; set; }  public Guid Partition34 { get; set; }
    public Guid Area35 { get; set; }  public Guid Partition35 { get; set; }
    public Guid Area36 { get; set; }  public Guid Partition36 { get; set; }
    public Guid Area37 { get; set; }  public Guid Partition37 { get; set; }
    public Guid Area38 { get; set; }  public Guid Partition38 { get; set; }
    public Guid Area39 { get; set; }  public Guid Partition39 { get; set; }
    public Guid Area40 { get; set; }  public Guid Partition40 { get; set; }
    public Guid Area41 { get; set; }  public Guid Partition41 { get; set; }
    public Guid Area42 { get; set; }  public Guid Partition42 { get; set; }
    public Guid Area43 { get; set; }  public Guid Partition43 { get; set; }
    public Guid Area44 { get; set; }  public Guid Partition44 { get; set; }
    public Guid Area45 { get; set; }  public Guid Partition45 { get; set; }
    public Guid Area46 { get; set; }  public Guid Partition46 { get; set; }
    public Guid Area47 { get; set; }  public Guid Partition47 { get; set; }
    public Guid Area48 { get; set; }  public Guid Partition48 { get; set; }
    public Guid Area49 { get; set; }  public Guid Partition49 { get; set; }
    public Guid Area50 { get; set; }  public Guid Partition50 { get; set; }

    public AreaToPartitionReconciliation()
    {
        // Safe by default: the first production deployment runs in report-only
        // mode and writes nothing until an operator sets ReportOnlyMode false.
        ReportOnlyMode = true;
    }

    // ------------------------------------------------------------------------
    //  In-scope entity types (the editable constant list)
    // ------------------------------------------------------------------------
    //
    // Each entry selects one of an Area's typed member collections to sweep.
    // To add or drop an in-scope type, add or remove one line here. An Area
    // exposes its contents as separate typed ReadOnlyCollection<Guid>
    // properties, not as a single member list (Reference Guide p. 925), so the
    // in-scope set is expressed as collection selectors rather than an enum.
    //
    // Defaults: doors (AllDoors covers perimeter and captive doors), cameras,
    // and zones. "Readers" are intentionally absent: readers are not a
    // documented Area member type in 5.13 (they belong to their parent door).
    // See VERIFY 7. To sweep door-side or floor access points instead, add a
    // selector for area.CaptiveAccessPoints after confirming its return type.
    private static readonly Func<Area, ReadOnlyCollection<Guid>>[] InScopeMemberSelectors =
    {
        area => area.AllDoors,
        area => area.Cameras,
        area => area.Zones,
    };

    public override void Execute()
    {
        MacroLogger.TraceInformation("AreaToPartitionReconciliation.Execute() started.");
        try
        {
            List<AreaPartitionMapping> mappings = DiscoverMappings();

            MacroLogger.TraceInformation(
                $"ReportOnlyMode={ReportOnlyMode}. Populated mappings={mappings.Count}.");

            if (mappings.Count == 0)
            {
                MacroLogger.TraceWarning(
                    "No populated mappings found. Set at least one AreaNN/PartitionNN pair.");
                MacroLogger.TraceInformation("AreaToPartitionReconciliation.Execute() completed.");
                return;
            }

            int mappingsProcessed = 0;
            int totalEvaluated = 0;
            int totalAdditions = 0;
            int totalErrors = 0;

            // One member set per partition, shared across mappings and seeded
            // from Partition.Members on first use. If two mappings target the
            // same partition, the second sees the first's additions, so an
            // entity is never re-attempted or double-counted within a run.
            Dictionary<Guid, HashSet<Guid>> memberCache = new Dictionary<Guid, HashSet<Guid>>();

            foreach (AreaPartitionMapping mapping in mappings)
            {
                // Cooperative cancellation: let an operator stop a long sweep
                // cleanly between mappings instead of relying on a hard abort.
                if (MacroAbortToken.IsCancellationRequested)
                {
                    MacroLogger.TraceWarning(
                        "Abort requested. Stopping before the remaining mappings.");
                    break;
                }

                // Per-mapping isolation: one mapping's failure must not abort the
                // others. Catch, log with context, and continue.
                try
                {
                    MappingResult result = ProcessMapping(mapping, memberCache);
                    totalEvaluated += result.Evaluated;
                    totalAdditions += result.Additions;
                    totalErrors += result.Errors;
                    mappingsProcessed++;
                }
                catch (System.Threading.ThreadAbortException)
                {
                    // The macro engine is stopping this macro. Never swallow it.
                    throw;
                }
                catch (Exception mappingEx)
                {
                    totalErrors++;
                    MacroLogger.TraceError(
                        mappingEx,
                        $"Mapping slot {mapping.Index} (area {mapping.AreaGuid} to " +
                        $"partition {mapping.PartitionGuid}) failed. Continuing with the rest.");
                }
            }

            string addedWord = ReportOnlyMode ? "simulated" : "made";
            MacroLogger.TraceInformation(
                $"Completion summary: mappingsProcessed={mappingsProcessed}/{mappings.Count}, " +
                $"entitiesEvaluated={totalEvaluated}, additions{addedWord}={totalAdditions}, " +
                $"errors={totalErrors}.");

            MacroLogger.TraceInformation("AreaToPartitionReconciliation.Execute() completed.");
        }
        catch (Exception ex)
        {
            MacroLogger.TraceError(ex, "AreaToPartitionReconciliation.Execute() failed.");
        }
    }

    // ------------------------------------------------------------------------
    //  Mapping discovery (the count-in-one-place requirement)
    // ------------------------------------------------------------------------

    // Reads this macro's own public Guid properties by reflection and hands the
    // raw name-to-value pairs to PairMappings for parsing. Reflection is the
    // same mechanism Config Tool uses to read parameters (see VERIFY 2).
    //
    // The name-prefix guard here also keeps us off the public Guid properties
    // that UserMacro itself inherits (InstanceGuid, MacroGuid, InstigatorGuid):
    // none of those start with "Area" or "Partition", so they are never read.
    private List<AreaPartitionMapping> DiscoverMappings()
    {
        Dictionary<string, Guid> guidProperties = new Dictionary<string, Guid>();

        foreach (PropertyInfo property in
                 GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(Guid) || !property.CanRead)
                continue;

            bool isMappingProperty =
                property.Name.StartsWith("Area", StringComparison.Ordinal) ||
                property.Name.StartsWith("Partition", StringComparison.Ordinal);
            if (!isMappingProperty)
                continue;

            guidProperties[property.Name] = (Guid)property.GetValue(this);
        }

        return PairMappings(guidProperties);
    }

    // Pairs AreaNN with PartitionNN by numeric suffix. Plain types only (string
    // and Guid), so this method is reviewable and testable without any SDK
    // assemblies present. Pairs with either GUID empty are unused slots and are
    // dropped here, so callers only ever see populated mappings.
    private List<AreaPartitionMapping> PairMappings(IDictionary<string, Guid> guidProperties)
    {
        Dictionary<int, Guid> areas = new Dictionary<int, Guid>();
        Dictionary<int, Guid> partitions = new Dictionary<int, Guid>();

        foreach (KeyValuePair<string, Guid> entry in guidProperties)
        {
            int index;
            if (TryParseIndexed(entry.Key, "Area", out index))
                areas[index] = entry.Value;
            else if (TryParseIndexed(entry.Key, "Partition", out index))
                partitions[index] = entry.Value;
        }

        List<AreaPartitionMapping> mappings = new List<AreaPartitionMapping>();
        foreach (KeyValuePair<int, Guid> areaEntry in areas)
        {
            Guid partitionGuid;
            if (!partitions.TryGetValue(areaEntry.Key, out partitionGuid))
                continue;

            // Skip unused slots: either GUID empty means no mapping configured.
            if (areaEntry.Value == Guid.Empty || partitionGuid == Guid.Empty)
                continue;

            mappings.Add(new AreaPartitionMapping(areaEntry.Key, areaEntry.Value, partitionGuid));
        }

        // Deterministic order makes the log read in slot order.
        mappings.Sort((left, right) => left.Index.CompareTo(right.Index));
        return mappings;
    }

    // Returns true and the parsed suffix when propertyName is prefix + integer,
    // for example ("Area07", "Area") yields 7. Anything else returns false.
    private static bool TryParseIndexed(string propertyName, string prefix, out int index)
    {
        index = 0;
        if (!propertyName.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string suffix = propertyName.Substring(prefix.Length);
        if (suffix.Length == 0)
            return false;

        return int.TryParse(suffix, out index);
    }

    // ------------------------------------------------------------------------
    //  Per-mapping processing
    // ------------------------------------------------------------------------

    private MappingResult ProcessMapping(
        AreaPartitionMapping mapping, Dictionary<Guid, HashSet<Guid>> memberCache)
    {
        Area area = Sdk.GetEntity(mapping.AreaGuid) as Area;
        if (area == null)
        {
            MacroLogger.TraceWarning(
                $"Area {mapping.AreaGuid} (slot {mapping.Index}) not found or not an Area. " +
                "Skipping this mapping.");
            return MappingResult.Empty;
        }

        Partition partition = Sdk.GetEntity(mapping.PartitionGuid) as Partition;
        if (partition == null)
        {
            MacroLogger.TraceWarning(
                $"Partition {mapping.PartitionGuid} (slot {mapping.Index}) not found or not a " +
                "Partition. Skipping this mapping.");
            return MappingResult.Empty;
        }

        // Gather every in-scope entity in the area's subtree before touching the
        // partition: the area's own members plus, recursively, the members of
        // every descendant sub-area. The HashSet dedups an entity that appears
        // in more than one sub-area. Doing all reads first keeps queries out of
        // the write transactions (Reference Guide TransactionManager remarks).
        HashSet<Guid> inScope = new HashSet<Guid>();
        HashSet<Guid> visitedAreas = new HashSet<Guid>();
        CollectSubtreeMembers(mapping.AreaGuid, area, inScope, visitedAreas);

        // Known members for this partition, shared across mappings and seeded
        // once from Partition.Members (a ReadOnlyCollection<Guid>, Ref Guide
        // p. 1210). Contains() makes the run idempotent: an entity already
        // present is skipped, no write, no event.
        HashSet<Guid> knownMembers = GetOrBuildMemberSet(memberCache, mapping.PartitionGuid, partition);

        Guid targetPartitionGuid = mapping.PartitionGuid;
        int additions = 0;
        int errors = 0;

        foreach (Guid entityGuid in inScope)
        {
            // Cooperative cancellation between entities for a long live sweep.
            if (MacroAbortToken.IsCancellationRequested)
            {
                MacroLogger.TraceWarning(
                    $"Abort requested while processing slot {mapping.Index}. " +
                    "Stopping this mapping early.");
                break;
            }

            if (knownMembers.Contains(entityGuid))
                continue;  // already a member, nothing to do

            // Resolve the entity in both modes so the preview counts only
            // entities that could actually be added (mirrors the live path).
            PartitionSupportEntity entity = Sdk.GetEntity(entityGuid) as PartitionSupportEntity;
            if (entity == null)
            {
                MacroLogger.TraceWarning(
                    $"Entity {entityGuid} vanished or does not support partitions. Skipping.");
                errors++;
                continue;
            }

            if (ReportOnlyMode)
            {
                MacroLogger.TraceInformation(
                    $"[ReportOnly] Would add '{entity.Name}' ({entityGuid}) to partition " +
                    $"'{partition.Name}'.");
                // Treat as known so a later mapping to the same partition does
                // not also report it.
                knownMembers.Add(entityGuid);
                additions++;
                continue;
            }

            // One transaction per entity, catch OUTSIDE the lambda. A failure
            // rolls back only this entity and is counted as an error, so the
            // summary never reports an add that actually rolled back.
            try
            {
                Sdk.TransactionManager.ExecuteTransaction(() =>
                {
                    // ADD-ONLY: InsertIntoPartition is additive (see VERIFY 6). It
                    // never removes the entity from any other partition. A false
                    // return is turned into a throw so this transaction rolls back
                    // and the entity is counted as an error, not an add.
                    if (!entity.InsertIntoPartition(targetPartitionGuid))
                        throw new InvalidOperationException(
                            "InsertIntoPartition returned false (no write access?).");
                });
                // Record the add so a later mapping to the same partition skips it.
                knownMembers.Add(entityGuid);
                additions++;
                MacroLogger.TraceInformation(
                    $"Added '{entity.Name}' ({entityGuid}) to partition '{partition.Name}'.");
            }
            catch (System.Threading.ThreadAbortException)
            {
                throw;
            }
            catch (Exception addEx)
            {
                errors++;
                MacroLogger.TraceWarning(
                    $"Failed to add '{entity.Name}' ({entityGuid}) to partition " +
                    $"'{partition.Name}': {addEx.Message}. Check the macro user's " +
                    "ManagePartitionMemberships rights on this partition.");
            }
        }

        string addedWord = ReportOnlyMode ? "wouldAdd" : "added";
        MacroLogger.TraceInformation(
            $"[slot {mapping.Index}] area='{area.Name}' partition='{partition.Name}' " +
            $"evaluated={inScope.Count} {addedWord}={additions} errors={errors}.");

        return new MappingResult(inScope.Count, additions, errors);
    }

    // Walks the area subtree, accumulating in-scope member GUIDs. Recurses into
    // direct sub-areas via Area.CaptiveAreas. The visitedAreas set guards against
    // a cycle in the area hierarchy so recursion always terminates.
    //
    // VERIFY 4: Area.AllDoors, Area.Cameras, Area.Zones, and Area.CaptiveAreas
    // are documented (Reference Guide p. 924 to 927). Reconfirm on the target.
    private void CollectSubtreeMembers(
        Guid areaGuid, Area area, HashSet<Guid> accumulator, HashSet<Guid> visitedAreas)
    {
        if (area == null)
            return;
        if (!visitedAreas.Add(areaGuid))
            return;  // already walked this area, cycle guard

        foreach (Func<Area, ReadOnlyCollection<Guid>> selector in InScopeMemberSelectors)
        {
            ReadOnlyCollection<Guid> members = selector(area);
            if (members == null)
                continue;
            foreach (Guid memberGuid in members)
                accumulator.Add(memberGuid);
        }

        ReadOnlyCollection<Guid> childAreas = area.CaptiveAreas;
        if (childAreas == null)
            return;

        foreach (Guid childAreaGuid in childAreas)
        {
            if (visitedAreas.Contains(childAreaGuid))
                continue;

            Area childArea = Sdk.GetEntity(childAreaGuid) as Area;
            if (childArea == null)
            {
                MacroLogger.TraceWarning(
                    $"Sub-area {childAreaGuid} could not be resolved. Skipping its subtree.");
                continue;
            }

            CollectSubtreeMembers(childAreaGuid, childArea, accumulator, visitedAreas);
        }
    }

    // Returns the known-member set for a partition, building it once from
    // Partition.Members and caching it so mappings that share a partition also
    // share (and update) one set across the run.
    private static HashSet<Guid> GetOrBuildMemberSet(
        Dictionary<Guid, HashSet<Guid>> cache, Guid partitionGuid, Partition partition)
    {
        HashSet<Guid> set;
        if (!cache.TryGetValue(partitionGuid, out set))
        {
            set = new HashSet<Guid>(partition.Members);
            cache[partitionGuid] = set;
        }
        return set;
    }

    // ------------------------------------------------------------------------
    //  Small plain-type carriers
    // ------------------------------------------------------------------------

    private struct AreaPartitionMapping
    {
        public readonly int Index;
        public readonly Guid AreaGuid;
        public readonly Guid PartitionGuid;

        public AreaPartitionMapping(int index, Guid areaGuid, Guid partitionGuid)
        {
            Index = index;
            AreaGuid = areaGuid;
            PartitionGuid = partitionGuid;
        }
    }

    private struct MappingResult
    {
        public readonly int Evaluated;
        public readonly int Additions;
        public readonly int Errors;

        public MappingResult(int evaluated, int additions, int errors)
        {
            Evaluated = evaluated;
            Additions = additions;
            Errors = errors;
        }

        public static MappingResult Empty
        {
            get { return new MappingResult(0, 0, 0); }
        }
    }

    protected override void CleanUp()
    {
        // No persistent resources or event subscriptions to release. This macro
        // runs to completion and holds nothing open between runs.
    }
}
