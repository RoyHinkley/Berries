# Berries Decision and Execution Workflow

This document defines presentation of Cases, construction of Dispositions, virtual application, ActionPlan compilation, execution safety, and filesystem-boundary requirements. Terminology and semantic invariants are defined in `MODEL.md`; Situation semantics are in `SITUATIONS.md`.

## Present a Case

The UI should expose each Case as one self-contained decision area without requiring the user to understand its formal Case type.

A scrollable list of Case panels is the preferred presentation. Panels need not be fully materialized before scrolling into view; lightweight summaries can be expanded or virtualized as needed.

The active panel body shows only Case data useful to establishing the Disposition, such as:

    concise Case description
    relevant paths/branches
    defining duplicate relationships
    leverage, coverage, concentration, and other useful structural evidence
    Situation selector
    Resolution selector
    projected surviving arrangement
    directory mappings where applicable
    Accept / Apply controls

Additional detail should be available on demand without cluttering the primary presentation. Tooltips, hover details, expandable evidence, and similar secondary surfaces are appropriate for paths, contributing instances, and other context needed only occasionally.

Case information should emphasize the objective duplication pattern that caused the Case to exist rather than unrelated duplication merely lying within its bounds.

Situation identification is optional. Objective evidence may constrain or rank the offered Situations. Once the user selects one, proposed Resolutions are drawn from that asserted Situation; see `SITUATIONS.md`.

When no Situation is selected, Berries may offer generally applicable Resolutions that still address the Case's defining duplication pattern.

Selecting a Resolution adjusts the Case view to show the resulting proposed Disposition and any parameters still required from the user.

Every Resolution implies one Disposition and must supply or solicit every parameter required for that Disposition. Multiple Resolutions may imply the same Disposition.

## Determine the Disposition

The UI presents the desired surviving arrangement whenever possible rather than asking the user to enumerate removals.

A Disposition defines the exact intended filesystem state within Case authority. It does not imply that every File in the Case is modified.

Fundamental rule:

    A Case need not resolve all duplication within its bounds; its Resolution
    must address the duplication pattern that caused the Case to exist.

Other internal duplication can remain untouched for subsequent Cases.

### DuplicateSet Case

Show retained File instances. Multiple instances may be retained. Omitted instances of the same Content are implied removable when the selected Resolution calls for deduplication.

A retain-all Resolution can instead settle every relationship in the DuplicateSet while leaving the Portrait unchanged.

### Single-directory Case

The Resolution addresses Content duplicated internally within that directory. Unique Files and unrelated duplicate relationships need not be changed unless the selected Resolution explicitly requires it.

### DirectoryPair Case

The Resolution addresses the Content relationship directly shared by the two directories. Unique Files are handled according to the selected Situation/Resolution and explicit user choices rather than by a universal delete/retain default.

### BranchPair Case

The Resolution addresses duplicated Content crossing the two effective sides of the BranchPair cut. Duplication wholly internal to either side is not part of the Case's semantic obligation and can remain for later Cases.

Keep/destination relationships can be expressed through directory mappings, for example:

    OldPhotos\Family -> Photos\Family
    OldPhotos\Trips  -> Photos\Travel

A source directory with no accepted special mapping can preserve its relative path beneath the selected destination when that is appropriate to the Resolution.

Removing an empty source directory is a directory-specific choice and can normally default to yes when consistent with the selected Resolution.

A Disposition is complete when every destination/retention decision required by the selected Resolution is unambiguous. A change-producing Disposition cannot be applied until complete and valid.

## Directory mappings

High-level structural Cases can contain useful subordinate directory correspondences.

Derive proposed mappings on demand from DirectoryPair relationships within the selected source/destination branches rather than storing them permanently in BranchPair.

Proposed mappings are fully user-editable. The user can accept, reject, or change mappings piecemeal.

Where a Resolution requires moving material into a destination and no special mapping is accepted, preserve the relative source path beneath the destination as the deterministic fallback when appropriate.

Example:

    source: OldPhotos\Trips\d.jpg
    special mapping: Trips -> Travel
    result: Photos\Travel\d.jpg

without that mapping:

    result: Photos\Trips\d.jpg

## Accept and Apply

A Resolution can produce settlement state, filesystem-state change, or both.

If the selected/approved Resolution produces no filesystem change to the Current Portrait, the command is:

    Accept

Accept records the Resolution's settlement semantics. It performs no filesystem Action. The accepted duplicate relationships cease to generate future Cases even though the physical Portrait is unchanged.

If the Resolution changes the Current Portrait, the command is:

    Apply

Apply first validates the Disposition. Expected incompleteness while the user is editing is not itself an error, but Apply requires a complete and realizable Disposition.

Validation includes destination/path collisions. A required destination may be absent or already contain identical Content, but two different Contents may not silently be assigned to the same destination path. Berries must not invent a filename, silently overwrite, or choose one Content over another.

If validation fails, no virtual change is made. Structured validation issues are returned to the Case panel with enough information to solicit only the additional Resolution data needed.

If validation succeeds, the ActionPlan is executed only against the virtual Current Portrait and any settlement consequences of the Resolution are recorded.

The new Current Portrait reflects:

    virtual deletions
    moves/renames
    copies
    resulting DuplicateSet membership
    resulting directory structure

The new unresolved decision state also reflects settlements established by the Resolution.

After Accept or Apply, derived Cases are regenerated from the resulting Portrait plus unresolved duplicate state. The same Case need not be retained as a persistent object; if its defining evidence has been settled or changed, it simply no longer regenerates in the same form.

Undo/redo must therefore restore both the accepted virtual Action sequence and the corresponding settlement state.

## Compile the ActionPlan

Once the Disposition is complete, compile it deterministically into primitive filesystem Actions.

For each required Content/destination:

    if an identical instance already exists at the required destination:
        no copy is needed
    otherwise:
        create the required instance from a valid source
        prefer move when it also satisfies source removal and is safe
        otherwise copy

For each Case instance explicitly excluded by the Disposition:

    remove it from the projected result

Do **not** interpret every Case member omitted from an unrelated subdecision as removable. Removal follows from the selected Disposition, whose semantic scope is determined by the Resolution.

The ActionPlan defines the logical filesystem transformation. Settlements are not Actions and are not compiled into it.

Safety-dependent physical ordering, source protection, cross-filesystem behavior, and temporary storage belong to ExecutionPlan construction.

Optimization of copy versus move, metadata preservation, and directory operations remain implementation details within these rules.

No real filesystem modification occurs while compiling an ActionPlan.

## Refresh after virtual Apply or settlement

Derived analysis is recomputed from the new Current Portrait and unresolved duplicate state as described in `ANALYSIS.md`.

Known virtual operations preserve Content identity:

    delete
        removes a known instance

    move/rename
        changes path only

    copy
        adds another instance of known Content

Settlement removes accepted duplicate relationships from future structural evidence without changing File instances.

Re-reading or rehashing real filesystem Content is unnecessary unless external filesystem change invalidates the session model.

## Keep/drop scoring and user history

Keep/drop scoring is heuristic presentation machinery, not authority.

Scores may influence:

    sorting duplicate instances
    preferred survivor presentation
    Case ordering
    preselection

Scores never directly produce filesystem Actions.

Possible future persistent user information includes:

    explicit directory/branch preferences
    confirmed Situations
    selected Resolutions/Dispositions
    accepted/rejected mappings
    accepted settlements
    user-defined rules
    previous choices in analogous Cases

Such information may later influence Situation ordering, Resolution ordering, Case ordering, keep/drop scoring, and preselection. None is required for the initial implementation.

## Execute

Execute means the user accepts the projected filesystem represented by the Current Portrait and requests real filesystem modification.

Before modifying disk:

    perform a lightweight preflight for known relevant changes, missing sources,
    and destination conflicts

    compute a synopsis of planned changes

    identify irrecoverable Content loss:
        Content present in the Initial Portrait for which the final projected
        Portrait contains zero surviving instances

    highlight irrecoverable loss distinctly from harmless deletion of redundant
    instances

    require final user approval

Then derive and execute an ExecutionPlan from the accumulated ActionPlans.

The ExecutionPlan may reorder or expand logical Actions when doing so preserves the same final Portrait and increases safety.

Execution safety invariant:

    Before any operation that could destroy or invalidate a required source,
    a verified usable instance of that Content must already exist at a secured
    final destination or in the execution cache.

Duplicate identity alone is not authority to delete. Files in application-managed, repository-managed, generated, cached, deployed, or otherwise structurally significant locations may require stronger validation or explicit authorization even when identical Content exists elsewhere.

Use the execution cache only when useful. Straightforward redundant-instance deletion need not incur cache I/O when a retained source can be validated immediately before deletion and the instance is otherwise authorized for removal.

For relocation, multiple destinations, pathname dependencies, or other fragile source relationships:

    choose a valid source instance

    if the source is expendable and the cache is on the same filesystem,
    move it into the cache; otherwise copy it

    verify cached Content before relying on it

    use the cached instance for required placements

    treat cross-filesystem moves as copy followed by later source removal

    delay destructive removals until required destinations are secured

    delete the cache after successful execution

    retain the cache after failure when recovery or diagnosis still requires it

Validate sources as they are staged or relied upon and validate destinations before placing Content. Immediately before destructive removal, establish that required surviving Content is secured.

Record completed physical operations durably enough to diagnose partial execution or failure.

Execution summary should report:

    completed operations
    exceptions
    conflicts
    failures
    discrepancies between projected and actual filesystem state

## Filesystem abstraction requirements

Core must not depend on Windows path syntax, drive letters, NTFS file IDs, alternate data streams, ACLs, reparse points, case-insensitive path behavior, or other filesystem-specific assumptions.

The abstraction may grow richer semantics when justified, including:

    path identity/comparison
    parent-directory traversal
    directory enumeration
    file size and required timestamps
    Content reading
    create/copy/move/delete
    directory create/remove
    metadata preservation policy
    symbolic links / reparse points / filesystem links
    hard links
    path conflicts
    case sensitivity
    cross-filesystem moves
    atomicity guarantees

The initial contract assumes only the least common capabilities needed by the application:

    hierarchical directories
    named regular File instances
    file length
    readable ordinary file Content as a byte stream
    basic create/copy/move/delete operations

Content identity is defined solely by the bytes returned by the ordinary file Content stream. Filesystem-specific metadata is not part of duplicate identity in the initial implementation.

Symbolic links, reparse points, filesystem aliases, and similar symbolic objects are outside the initial model. Directory traversal must not follow them; adapters ignore them rather than exposing them to Core as ordinary Files or directories.

Hard links receive no special treatment initially. If the adapter enumerates multiple directory entries as ordinary Files, Core may model them as separate instances with identical Content.

Sparse Files are treated according to their logical byte streams. Alternate data streams, resource forks, extended attributes, ACLs, ownership, and similar metadata are ignored for duplicate identity.

The filesystem adapter decides what Core can safely regard as a regular File or directory. Core should not grow a taxonomy of every platform's exotic filesystem objects.

## Deferred refinements

Deliberately deferred until experience demonstrates value:

    heuristic Situation inference/ranking beyond current empirical work
    sophisticated persistent user learning
    general-purpose filesystem reorganization beyond duplicate-motivated Resolutions
    aggressive incremental recomputation optimization
    formal/mathematically complete Situation or Resolution enumeration
    generic recognition of application-managed / non-destructive regions

The architecture should permit these additions without requiring them now.
