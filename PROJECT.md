# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Application Project Plan

## Problem statement

Ordinary duplicate-file tools generally expose duplicate sets and
require the user to decide what to do with individual instances. That
works well for small, isolated duplication, but poorly for accumulated
backups, reorganized trees, partial moves, migrations, archives, staging
areas, and similar real-world filesystem histories.

The central problem is to recognize and present high-leverage structural 
relationships among duplicated content so fewer human decisions are needed
to remove the undesired duplicates while retaining the useful ones.

## Objective

Provide a safe, efficient way to eliminate unwanted file duplication
across large filesystem trees while preserving the content and
organization the user wants.

The program identifies high-leverage relationships among duplicated
content and presents them as coherent cases requiring few decisions
rather than requiring the user to resolve duplicates individually. It 
uses those decisions to construct a projected filesystem state that can 
be reviewed and revised freely before any changes are made to disk.

The system should minimize the amount of user attention required without
making irreversible decisions on the user’s behalf.

## Key characteristics

1.  Structural relationships among directories and branches are
    first-class evidence, not merely presentation around duplicate sets.

2.  Cases are ranked primarily by leverage: the number of distinct
    duplicated contents that can potentially be addressed by one or 
	a few related user decisions.

3.  User decisions are applied first to a virtual filesystem portrait.
    The user can inspect, revise, undo, and redo the projected result
    before any real filesystem changes occur.

4.  The UI primarily portrays the desired surviving arrangement.
    Removal is normally implied by what the user elects to keep and
    where it should remain.

5.  The system does not perform filesystem changes based solely on
	programmatic analysis or inferences. User approval is required.

A useful presentation heuristic is to ask the smallest comprehensible question
with the greatest downstream simplifying effect. High-leverage structural Cases
serve this goal; fine-grained Duplicate-set Cases remain available when broader
structure does not resolve the duplication.

## Platform and architecture

Implementation platform:

    C#
    .NET
    Avalonia

The analysis engine is entirely independent of the UI.

Filesystem access is isolated behind platform-agnostic interfaces.
Filesystem- specific behavior (NTFS, ext4, APFS, etc.) belongs only in
filesystem adapter implementations.

A console front end should be possible without changing the analysis
engine.

Architectural test:

    If Core cannot be exercised from a console program against a synthetic
    portrait, a platform or UI concern has leaked across a boundary.

Tentative solution decomposition:

    Berries.Core
        Domain model
        Portrait
        Duplicate analysis
        Directory / pair / scope analysis
        Cases
        Situations and resolutions
        Dispositions and disposition validation
        Action-plan compilation
        Execution planning

    Berries.FileSystem.Abstractions
        Platform-neutral filesystem model and operations

    Berries.FileSystem.Windows
        Windows/NTFS implementation

    Berries.FileSystem.<other>
        Future filesystem/platform implementations

    Berries.Gui
        Avalonia UI

    Berries.Console
        Optional console/diagnostic front end

## Core terminology

These terms are carefully-defined architectural vocabulary and should be used consistently
in code and documentation.

-Corpus
The set of disjoint filesystem trees selected for analysis.

-Corpus root
A filesystem directory selected to contribute its tree to the corpus. No corpus root is a descendant of another corpus root.

-File
One filesystem instance of content at a particular path. A File is an instance, not the byte content itself.

- Content
The byte sequence contained by a file. Content identity is established sufficiently for the program’s purposes
by the duplicate-detection process.

-Initial portrait
The modeled state of the corpus when it was scanned. It records the real
filesystem state from which the user begins constructing projected Portraits
and provides the known source state used by execution validation.

-Portrait
A modeled filesystem state. The initial portrait is obtained from disk. Subsequent portraits are
derived by applying virtual actions to the preceding one.

-Current portrait
The portrait produced by the currently accepted sequence of virtual
actions. It represents the filesystem state the user is presently designing.

-Duplicate
A file whose content is identical to at least one other file in the same
portrait.

-Duplicate set
All file instances in a portrait having one identical content identity,
when at least two such instances exist. A duplicate set therefore represents exactly one distinct duplicated
content.

-Directory
For duplicate-analysis statistics, a Directory record describes files
directly contained by that directory only. Descendants are not folded
into its local counts. The filesystem directory/tree itself may of course contain descendants.

-DirectoryPair
An unordered pair of distinct directories having one or more distinct
duplicated contents directly represented in both directories. DirectoryPair describes local/direct shared content only.

-Scope
A directory and all of its descendants.

-ScopePair
An unordered pair of distinct directory-rooted scopes for which descendant
DirectoryPairs provide shared-content leverage. A ScopePair can exist even when its two root directories have no
directly shared files.

The two effective sides of a ScopePair are always disjoint. If one scope root
is a descendant of the other, traversal of the ancestor side stops at the
descendant root and omits that entire subtree. The descendant side is traversed
normally. Canonical pair ordering has no semantic relationship to ancestry.
A ScopePair with identical roots is invalid.

-Bounded
Having membership determined unambiguously by specified criteria.

-Case
A bounded set of files in the current portrait, containing at least one
duplicate, considered together for one coherent disposition.

A case is objective and program-discovered. A Situation is not required
to discover a Case, but it can alter its presentation.

A case is not necessarily composed only of duplicated files. Structural
cases may contain unique files within their bounds because those files
can be relevant to a common disposition.

The case boundary limits disposition authority. Duplicate instances
outside the case may provide context or evidence, but remain unchanged
unless they are independently brought under disposition authority.

Contextual question:

    Case
        What files are under consideration?

-Situation
The user’s semantic interpretation of a case: what the user believes is
going on.

A Situation is merely explanatory user input and is not required. Its 
purpose is to refine the system-suggested potential resolutions and, 
if possible, identify disposition parameters. It need not be provable
from the filesystem portrait.

Contextual question:

    Situation
        What does the user say is going on?

A useful Situation evokes one or more potentially appropriate Resolutions that can be
mapped to fully parameterized Dispositions.

-Resolution
A situation-aware, user-facing description of a proposed solution.

A Resolution exists to express an operational outcome naturally in the
language of the selected Situation.

Different Resolutions, including Resolutions belonging to different
Situations, may produce the same Disposition.

Contextual question:

    Resolution
        Given what is going on, what's a natural way to express
		a particular disposition?

-Disposition
The precise desired placement and retention of the files within a Case.

A Disposition is the operational meaning of an accepted Resolution. It
defines the desired surviving arrangement, including approved directory
mappings and destinations where applicable.

It is a desired state, not a sequence of filesystem operations.

Once the keep/destination arrangement is completely specified, any
removals are implied: an instance within the Case boundary but not 
required is to be removed.

Contextual question:

    Disposition
        What outcome does the user want?
        Which content instances/locations should remain or appear, and where?

-Directory mapping
A user-approved relationship between a source directory and a
destination directory within a structural Disposition.

Example:

    OldPhotos\Family  -> Photos\Family
    OldPhotos\Trips   -> Photos\Travel

Mappings can be suggested from observed DirectoryPair relationships and
edited piecemeal by the user.

-Action
A primitive filesystem operation used to implement an Action Plan.

Tentative primitive operations are:

    delete
    copy
    move/rename

Directory creation/removal and metadata handling may be explicit actions
or well-defined helpers; this remains an implementation detail.

-Action Plan
A deterministic logical transformation, expressed in primitive filesystem
Actions, that implements a fully specified Disposition. Its ordering is
sufficient to define and apply the virtual transformation, but need not be the
literal order used for real filesystem execution.

Contextual question:

    ActionPlan
        What filesystem operations produce the desired outcome?

-Disposition validation
Validation of a proposed Disposition before it is applied to the virtual
Portrait. Validation detects collisions, ambiguities, missing parameters, and
other conditions that prevent the proposed desired state from being
unambiguously realized. Validation reports structured issues back to the Case
panel; it does not invent a solution.

-Execution Plan
A safe physical realization of the accumulated Action Plans against the real
filesystem. The Execution Plan may reorder or expand logical Actions when the
result is equivalent, in order to protect required Content and accommodate
filesystem constraints such as cross-filesystem moves.

-Execution cache
Temporary verified storage used by the Execution Plan to secure required
Content before an operation could destroy or invalidate the source instance on
which later work depends. The cache is execution machinery, not part of the
Portrait or Disposition, and is removed after successful execution.

-Leverage
The number of distinct duplicated contents potentially addressed by
resolving a Case.

Leverage measures reduction in duplicate-content decisions, not bytes
saved.

-Hidden
A Case presentation state indicating that the user has dismissed 
the case without requiring an Action Plan. Hidden cases are omitted
from the working list unless they are requested to be shown.

Hidden applies to the case as a whole and is not a filesystem state; 
it does not affect the inclusion of the same files in other Cases.

## Conceptual chain

The central semantic/operational decision chain is:

    Case
        What files are under consideration?

    Situation
        What does the user believe is going on?

    Resolution
        How can an appropriate outcome be described naturally in that
        Situation?

    Disposition
        What exact surviving arrangement does the user want?

    ActionPlan
        What primitive filesystem operations produce that arrangement?

Important distinctions:

    Case is objective and discovered by the program.

    Situation is semantic and optionally asserted by the user.

    Resolution is semantic and selected by the user.

    Disposition is the precise desired state.

    ActionPlan is the logical implementation of the desired transformation.

    ExecutionPlan is the safe physical realization of accumulated ActionPlans
    and is outside the semantic decision chain.

A Situation exists to evoke useful Resolutions. A Resolution,
together with a directory mapping if necessary, selects and
fully parameterizes a Disposition.

## Initial implementation scope for situations

The first implementation does NOT need heuristic Situation inference.

For a Case, the UI can simply offer the Situations applicable to that
Case type, in a sensible static order. The user chooses one, leaves it
unspecified, or works from the offered Resolutions.

Later versions may rank or narrow Situations using path names, known
filesystem locations, metadata, case structure, user history, or other
evidence.

This is polishing, not a prerequisite for the core architecture.

Cases should be presented within a self-contained decision panel
within a scrollable list of such panels.

## Situation catalogue

The catalogue is pragmatic and may not be mathematically complete. 
Situations may be added, merged, or refined as experience reveals 
useful user concepts.

-- Unneeded copy

Situation text:

    It looks like these are unneeded copies of the same content.

Meaning:

    Multiple instances contain identical content and one or more instances
    have no independent reason to remain.

Applicable Case types:

    Duplicate-set Case only.

Natural Resolutions:

    Keep this instance.
    Keep these instances.
    Keep all as-is.

Disposition information:

    Selected surviving instance(s). Other Case instances are implicitly
    removable.

Examples subsumed here:

    Accidental copy
    Rename residue
    Simple obsolete copy
    Obsolete generated/exported output

At structural scale, more informative Situations normally apply.

-- Move residue

Situation text:

    It looks like D has moved to C, but duplicate files remain at D.

Meaning:

    Material formerly located under one scope now belongs under another, but
    duplicate instances remain at the former location.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Former location = D
    Current location = C

Natural Resolutions:

    Merge the former location into the current location.
    Leave the relationship unchanged.

The reverse orientation should also be available when plausible.

The approved mapping supplies the detailed Disposition.

-- Reorganization

Situation text:

    It looks like D has been reorganized under C.

or:

    It looks like the material under D is now organized differently under C.

Meaning:

    Two scopes represent substantially overlapping content, but their internal
    directory organizations differ.

Applicable Case types:

    Primarily ScopePair.

    A simple DirectoryPair may reduce to Move residue.

Semantic roles:

    Former organization
    Preferred/current organization

Natural Resolutions:

    Merge the former organization into the preferred organization using the
    proposed mappings.

    Leave the organizations as they are.

The reverse orientation should also be available.

Observed descendant DirectoryPairs can suggest non-identity mappings
such as:

    OldPhotos\Family -> Photos\Family
    OldPhotos\Trips  -> Photos\Travel

Move residue and Reorganization are related but worth retaining
separately because “I moved this” and “I reorganized this” are natural,
meaningfully different user interpretations.

-- Backup

Situation text:

    It looks like E is a backup of D.

Meaning:

    E intentionally duplicates material in D for recovery or preservation.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Primary = D
    Backup = E

Natural Resolutions:

    Keep the backup.
    Retire the backup.

If the backup is retired, unique content in it may intentionally be
lost. Any such loss must be highlighted before Execute.

The reverse orientation may apply.

Backup is intentional duplication; preserving duplication is therefore a
valid completed outcome.

-- Migration

Situation text:

    It looks like D is being migrated to C.

Meaning:

    Content is being transferred from an old location or organization to a new
    one, but the migration is incomplete or the old location has continued to
    accumulate content.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Old/source = D
    New/destination = C

Natural Resolutions:

    Complete the migration from D to C.
    Leave the migration incomplete for now.

The reverse orientation may apply.

Migration differs from Move residue because unique files remaining on
the old side may represent unfinished migration rather than expendable
residue.

Migration may overlap structurally with Reorganization while still
evoking a different natural Resolution.

-- Snapshot

Situation text:

    It looks like E is a temporary snapshot of D.

Meaning:

    E is an intentionally retained copy of D, or of some prior state of D,
    created for temporary preservation rather than permanent backup.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Working/current = D
    Snapshot = E

Natural Resolutions, suggested order:

    Retire the snapshot.
    Keep the snapshot for now.

An alternate, less likely interpretation is that the snapshot represents
the desired state and the working tree represents a temporary excursion.
That can lead to a rollback-like Resolution.

Backup and Snapshot may produce similar Dispositions but rank them
differently:

    Backup
        Redundancy is expected to persist.

    Snapshot
        Redundancy is expected to be temporary.

-- Archive

Situation text:

    It looks like E is an archive of D.

Meaning:

    E is an intentionally retained historical or preservation-oriented
    collection related to D.

    Duplication between D and E may be desirable. Unique archive content may
    be especially important to preserve.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Working/current = D
    Archive = E

Natural Resolutions may include:

    Update the archive.
        Add material missing from the archive while retaining the working
        instances.

    Remove archived items from the working side.
        Remove working instances already represented in the archive.

    Update the archive, then remove archived items from the working side.

    Remove duplicates from the archive.
        Remove archive instances represented on the working side while
        preserving archive-only content.

    Delete the archive.
        This may intentionally destroy unique content.

    Keep as-is.

Internal duplication wholly within the archive should normally be
exposed as separate Cases.

Applying one archive Resolution need not hide the Case. Further useful
work on the relationship may remain. The user can Accept it later.

Archive demonstrates that a useful Disposition may intentionally create
or preserve duplication.

-- Staging / Import

Situation text:

    It looks like E is a staging or import area for D.

Meaning:

    E is a temporary collection whose contents may have been copied or
    incorporated into D.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Collection/destination = D
    Staging/import area = E

Natural Resolution:

    Remove from E the items already represented in D.

Unique files in E remain untouched.

The Resolution deliberately stops at deduplication. Organizing or
importing unique staging content is outside the initial application
scope.

Examples:

    Camera import folder
    Phone import folder
    Scanner output bucket
    Temporary intake folder
    Manually collected staging directory

-- Downloads

Situation text:

    It looks like E is a download/staging area for D.

Meaning:

    E contains downloaded files, some of which are already represented
    elsewhere and therefore may no longer need to remain in E.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Filed/retained location = D
    Downloads = E

Natural Resolutions:

    Remove from E the files already represented in D.

    Remove E files duplicated anywhere else in the portrait.

For the second Resolution, external duplicate instances provide evidence
that the E instance is redundant. Those external instances remain
outside Disposition authority and are not modified.

Unique files in E remain untouched.

Downloads is retained separately from Staging / Import because a
downloaded instance is commonly expendable once identical content has
been deliberately retained elsewhere.

-- Mirror

Situation text:

    It looks like D and E are intended to mirror each other.

or, for a directional mirror:

    It looks like E is intended to mirror D.

Meaning:

    D and E are intended to contain corresponding content. Differences between
    them represent divergence from that intended relationship.

Applicable Case types:

    DirectoryPair
    ScopePair

Semantic roles:

    Mirror side D
    Mirror side E

For a directional mirror:

    Authoritative side
    Mirrored side

Natural Resolutions:

    Make E match D.
        Add to E what is missing there.
        Remove from E what is absent from D.

    Make D match E.

    Synchronize D and E.
        Preserve content found on either side by adding it to the other.

    Keep as-is.

Mirror differs from Backup because correspondence/symmetry is part of
its semantics.

In a backup:

    Missing backup content may mean the backup needs updating.
    Extra backup content is not necessarily wrong.

In a mirror:

    Missing or extra content represents asymmetry.

Making one side authoritative may intentionally destroy unique content
on the other side. Such loss must be highlighted before Execute.

### Rejected or subsumed Situation candidates

These scenarios are retained here so useful reasoning is not lost.

Accidental copy Subsumed by Unneeded copy.

Rename residue Subsumed by Unneeded copy.

Download residue Developed into Downloads.

Generated output Retained as an example under Unneeded copy. It is real
but difficult to infer reliably from ordinary filesystem evidence.

Template / shared resource Rejected as a distinct Situation. True
templates normally become modified derivatives and cease to be
duplicates. Identical shared-resource copies may be independently
required.

Intentional deployment Rejected as a distinct Situation. Distributed
identical copies may be independently required by applications or
directory structures. Duplicate detection alone provides insufficient
evidence that any is expendable.

Intentional copies Not retained as a generic Situation. Meaningful
intentional duplication is better represented by specific Situations
such as Backup, Snapshot, Archive, and Mirror.

Intentional mirror Developed into Mirror.

Aggregation residue Real scenario, but not a distinct Situation.
Pairwise exposure reduces it naturally to Move residue or
Reorganization. A sufficiently broad ScopePair may encompass several
original source locations.

## Application Design plan

### Generate the initial portrait

Allow selection of filesystem directories.

Selected directories become Corpus roots.

Corpus roots must be disjoint; no selected root may be a descendant of
another.

Scan every file beneath the Corpus roots and record its path and
relevant platform-neutral metadata. Filesystem-specific metadata, if
useful, is obtained through the filesystem abstraction.

At this point Content identity is generally unknown.

### Enumerate duplicate sets

Enumerate all files.

Group files by size.

Files in singleton-size groups require no hashing for duplicate
detection and remain in the Portrait as unique files.

For each non-singleton size group:

    Prefix-hash files using a fixed-size prefix.
    Tentative prefix size: approximately 1 MiB; final value is empirical.

    Partition by prefix hash.

    Files in singleton prefix groups require no further hashing.

    Full-hash survivors.

    Partition by full hash into exact DuplicateSets.

Each DuplicateSet represents one distinct Content identity with at least
two File instances in the CurrentPortrait.

Unique files remain fully represented even if a complete hash was never
needed to establish that they cannot have a duplicate.

Hash choice and collision policy are implementation details, but the
resulting Content identity must be sufficiently reliable for destructive
planning.

### Build the Directory dictionary

Include directories represented by files in DuplicateSets.

Directory statistics describe directly contained files only.

Key:

    canonical directory identity/path

Maintain at least:

    Path

    ContentCount
        Number of distinct contents directly in the directory.

    DuplicateCount
        Number of distinct duplicated contents directly in the directory.

Each distinct duplicated Content contributes 1 regardless of how many
instances of that Content occur directly in the same directory.

A directory is a candidate Single-directory Case whenever at least one
Content has more than one File instance directly in that directory. External
instances of the same Content do not suppress the Case; overlapping structural
Cases and subsequent refresh/order naturally determine when the internal
duplication is presented.

Ancestor/branch information is not folded into Directory local counts.

### Build the sparse DirectoryPair dictionary

DirectoryPair describes direct/local shared content between two
directories.

For each DuplicateSet:

    Determine the distinct parent directories represented by its instances.

    For every unordered pair of distinct represented directories:

        key = CanonicalPair(directoryA, directoryB)

        DirectoryPair[key].SharedContentCount += 1

Therefore:

    DirectoryPair(A,B).SharedContentCount

is the number of distinct Content identities occurring directly in both
A and B.

Store each unordered pair once.

    SharedContentCount(A,B) ## SharedContentCount(B,A)

No DirectoryPair exists for directories with no directly shared
duplicated Content.

Coverage, if later useful, is derived:

    Coverage(A by B)
        = SharedContentCount(A,B) / ContentCount(A)

    Coverage(B by A)
        = SharedContentCount(A,B) / ContentCount(B)

Coverage is not currently a primary leverage metric.

### Build the sparse ScopePair dictionary

ScopePair describes recursive shared-content leverage between two
directory-rooted scopes.

A Scope rooted at A contains A and all descendants.

For each DirectoryPair (D, E, sharedCount):

    for each ancestor-or-self A of D:
        for each ancestor-or-self B of E:

            if A ## B:
                continue

            key = CanonicalPair(A, B)

            If one candidate root is a descendant of the other, apply the
            effective-side exclusion rule before accumulating: reject the
            contribution when both DirectoryPair endpoints lie inside the
            descendant subtree. Only a relationship crossing between the two
            effective ScopePair sides contributes leverage.

            ScopePair[key].ApproximateLeverage += sharedCount

A ScopePair may therefore exist even when its root directories have no
directly shared files.

Example:

    A\Family <-> B\Family
    A\Trips  <-> B\Travel

can induce:

    ScopePair(A,B)

even when DirectoryPair(A,B) does not exist.

ApproximateLeverage is the sum of SharedContentCount from all contributing
DirectoryPairs crossing between the two effective ScopePair sides. For
non-overlapping scopes these are the ordinary recursive scopes. For nested
roots, the descendant subtree is omitted from traversal of the ancestor side.

ApproximateLeverage can over-count one Content identity when that
identity appears in multiple contributing descendant DirectoryPairs.

Use ApproximateLeverage as ScopePair leverage in the initial
implementation.

Exact ScopePair leverage can later be computed by counting distinct
duplicated Contents represented across both effective sides. Do not implement
this refinement unless empirical testing shows that approximation error
materially changes Case ordering. Numerical error by itself is not
important; ranking quality is.

Do not retain contributing DirectoryPairs in ScopePair. Rediscover
relevant DirectoryPairs on demand when mappings are required.

### Construct candidate Cases

Cases are objective, program-discovered bounded sets of Files in the
CurrentPortrait.

Situations are not needed to discover Cases.

Candidate Case classes and exact bounds:

    Duplicate-set Case
        All File instances belonging to one DuplicateSet. Always available as
        the finest-grained fallback.

    Single-directory Case
        All Files directly contained by a directory that contains at least one
        Content more than once directly. Unique Files in the directory are
        included.

    DirectoryPair Case
        All Files directly contained by either directory in the pair, including
        unique Files.

    ScopePair Case
        All Files encountered by traversing both effective ScopePair sides,
        including unique Files. If the roots overlap, the descendant subtree is
        omitted from traversal of the ancestor side.

A structural Case therefore includes the Files within its defined bounds, not
merely the duplicated Files that caused the Case to be discovered.

The duplicate relationship supplies the reason for considering the Case;
unique Files within the bounds may affect its eventual Disposition.

Cases may overlap. Applying one Case can therefore change or invalidate
other Cases.

### Compute leverage

Core leverage:

    Number of distinct duplicated Contents potentially addressed by resolving
    the Case.

Initial metrics:

    Duplicate-set Case
        leverage = 1

    Single-directory Case
        leverage = number of distinct Contents having more than one File
        instance directly in the directory

    DirectoryPair Case
        leverage = SharedContentCount

    ScopePair Case
        leverage = ApproximateLeverage

Bytes are not part of leverage. The objective is to reduce unwanted
duplication and human decision workload, not primarily to reclaim
storage.

### Order Cases

Rank primarily by descending leverage.

Goal:

    Present the Cases capable of resolving the greatest number of duplicate-
    content decisions with one user assessment.

Later ranking may incorporate user history, Situation likelihood,
keep/drop scores, or other heuristics.

The user must be able to ignore the ranking and browse/filter/sort Cases
by other useful characteristics.

### Present a Case

The UI should expose each Case as one self-contained decision area
without requiring the user to understand the formal Case type.

A scrollable list of Case panels is the preferred presentation.

Panels need not be fully materialized before scrolling into view.
Lightweight summaries can be expanded or virtualized as needed.

The active panel body shows Case data applicable to the case type and
malleable parameters for the selected Disposition (files, 
directories, directory mapping, etc.). These elements are 
intended to be adjusted by the user to specify Disposition
details.

The active panel has a distinctly recognizable header containing a 
concise display of Case information and case controls that remain 
present regardless of Disposition.

Panels show only information applicable to the Case and
useful to establishing the Disposition:

	Case information: description, relevant paths/scopes, 
		duplicates and/or duplicate set counts, coverage/leverage, etc.
    Situation selector
    Resolutions selector
    Projected surviving arrangement
		Directory mappings
		Directories in scope but omitted from the retained Disposition
			may be individually tagged for deletion if left empty
    Accept/Apply command (may appear in header); the label is determined by
        whether the current valid Disposition changes the CurrentPortrait

Situations applicable to the Case type are presented in a sensible static order.
Defer implementing heuristic Situation narrowing to a later revision.
Situation identification/selection is always optional.

Regardless of whether a Situation is selected, the program offers plausible 
Resolutions for the Case. When no Resolution is selected, the current portrait 
of the Case is shown. Selecting a resolution adjusts the Case view to show the
result of the Disposition, with a directory mapping if needed to indicate
required Disposition parameters.

Resolutions are presented in user-facing semantic language.
Suggested Resolutions and ranking depend on:
    Case
    selected Situation, if any
	
	Future versions may additionally use:
		user history/preferences
		heuristic Situation confidence
		path/folder preferences

Every Resolution implies a single Disposition and must supply 
or solicit every parameter required by that Disposition.
Multiple Resolutions may imply the same Disposition.

### Determine the Disposition

The UI presents the desired surviving arrangement whenever possible,
rather than prompting the user to enumerate removals.

Conceptually, a Disposition partitions affected File instances/content
into desired destinations. Case membership defines what is under disposition
authority; it does not imply that every member should receive the same default
treatment. Proposed Resolutions may establish different case-specific defaults
for duplicated and unique Files, while leaving the user able to revise the
result.

For a simple DuplicateSet Case:
	A list of retained file instances / are shown; multiple instances may be 
	added to those initially selected by the Resolution
		Keep:
			path A
			path B
	Omitted Case instances of that Content are implied "to be removed".
For a DirectoryPair Case:
	The desired contents for the two Directories are listed.
	Unique files are handled as specified by user choice:
		specify retained, specify omitted,
		retain all, delete all)
For a ScopePair Case:
    Keep / destination relationships are expressed by mappings:
        C\Family -> B\Family
        C\Trips  -> B\Travel

A source directory with no accepted special mapping can retain a
deterministic relative-path mapping beneath the chosen destination when
that is appropriate to the selected Resolution.

Removing an empty source directory is a directory-specific decision and
should normally default to yes.

A Disposition is complete when every required destination/retention
decision needed by the selected Resolution is unambiguous. A change-producing
Disposition cannot be applied until it is complete and valid.

#### Directory mappings
High-level structural Cases can contain useful subordinate directory
correspondences.

Example:

    OldPhotos\Family -> Photos\Family
    OldPhotos\Trips  -> Photos\Travel

Derive initial (proposed) mappings on demand from DirectoryPair relationships
contained within the selected source/destination scopes; they are not stored
in ScopePair.

Proposed mappings are fully user-editable. The user can accept, reject, or 
change mappings piecemeal.

Where a Resolution requires moving material into a destination and no
special mapping is accepted for a source directory, preserve its
relative source path beneath the destination as the deterministic
fallback.

Example:

    OldPhotos\Trips\d.jpg

with:

    Trips -> Travel

becomes:

    Photos\Travel\d.jpg

without that special mapping:

    Photos\Trips\d.jpg

### Accept and Apply

If the selected/approved Disposition produces no change to the
CurrentPortrait, the command is:

    [Accept]

Accept performs no filesystem Action and marks the Case Hidden.

If the Disposition changes the CurrentPortrait, the command is:

    [Apply]

[Apply] first calls ValidateDisposition(). Expected incompleteness while the
user is still editing a proposed Resolution is not itself an error. When the
user attempts Apply, however, the Disposition must be complete and realizable.

Validation includes destination/path collisions: a required destination may be
absent or already contain identical Content, but two different Contents may not
be assigned to the same destination path. The program must not silently
overwrite, invent a filename, or choose one Content over another.

If validation fails, no virtual change is made. Structured validation issues are
returned to the Case panel with enough information to solicit only the
additional Resolution data needed to resolve them. The revised Disposition is
then validated again. This validation mechanism is intentionally general so
future ambiguity or realizability checks can use the same path.

If validation succeeds, Apply executes the ActionPlan only against the virtual
CurrentPortrait.

It produces a new CurrentPortrait reflecting:

    virtual deletions
    moves/renames
    copies
    resulting DuplicateSet membership
    resulting directory structure

Apply does not automatically hide the Case. The changed portrait may
still support useful work involving the same relationship.

A Show Hidden facility allows accepted Cases to be revisited.

Undo/redo can be implemented by changing the accepted virtual Action
sequence and regenerating the CurrentPortrait.

### Compile the ActionPlan

Once the Disposition is complete, compile it deterministically into
primitive filesystem Actions.

The essential rule is driven by the desired destinations:

    For each required Content/destination:
        If an identical instance is already present at the required
        destination, no copy is needed.

        Otherwise create the required instance using a valid source instance,
        preferably by move when that also satisfies removal of the source,
        otherwise by copy.

    For each Case instance not required by the Disposition:
        Remove it from the projected result.

The ActionPlan defines the logical transformation. Safety-dependent physical
ordering, source protection, cross-filesystem behavior, and temporary storage
belong to ExecutionPlan construction rather than to the semantic Disposition.

The exact optimization of copy versus move, metadata preservation, and
directory operations remain implementation exercises within these rules.

No real filesystem modification occurs while compiling the ActionPlan.

### Refresh derived analysis after Apply

Cases and structural indexes are derived data.

After a virtual Apply, invalidate/recompute affected portions of:

    DuplicateSet memberships
    Duplicate counts
    Directory records
    DirectoryPair relationships
    ScopePair relationships
    Candidate Cases
    Leverage rankings

Do not reread the filesystem or rehash Contents.

Known virtual operations preserve Content identity:

    delete
        removes a known instance

    move/rename
        changes path only

    copy
        adds another instance of known Content

Content identities established during the initial scan remain valid 
unless the real filesystem changes outside the application.

Correct incremental invalidation is desirable, but a simpler broader
recomputation from the in-memory Portrait is acceptable initially if
performance permits. Re-reading file contents is the expensive operation
to avoid.

### Keep/drop scoring and user history

Keep/drop scoring is heuristic presentation machinery, not authority.

Scores may influence:

    sorting duplicate instances
    default/preferred survivor presentation
    ranking
    preselection

Scores never directly result in filesystem Actions; they are only
used to adjust Case presentations.

Possible future persistent user information includes:

    explicit folder/branch preferences
    confirmed Situations
    selected Resolutions/Dispositions
    accepted/rejected mappings
    user-defined rules
    previous choices in analogous Cases

Such information may later influence:

    Situation ordering
    Resolution ordering
    Case ordering
    keep/drop scoring

None of this is required for the first implementation.

### Execute

[Execute] means the user accepts the projected filesystem represented by the
CurrentPortrait and requests real filesystem modification.

Before modifying disk:

    Perform a lightweight preflight sufficient to detect known relevant
    changes, missing sources, and destination conflicts before beginning work.

    Compute a synopsis of planned changes.

    Identify irrecoverable Content loss:
        Content present in the InitialPortrait for which the final projected
        Portrait contains zero surviving instances.

    Highlight such loss distinctly from harmless deletion of redundant
    instances.

Require final user approval after preflight and synopsis.

Then derive and execute an ExecutionPlan from the accumulated ActionPlans. The
ExecutionPlan may reorder or expand logical Actions when doing so preserves the
same final Portrait and increases safety.

Execution safety invariant:

    Before any operation that could destroy or invalidate a required source,
    a verified usable instance of that Content must already exist either at a
    secured final destination or in the execution cache.

Use the execution cache only when useful. Straightforward redundant-instance
deletions need not incur cache I/O when a retained source can be validated
immediately before deletion. For relocation, multiple destinations, pathname
dependencies, or other fragile source relationships:

    Choose a valid source instance.

    If the source is expendable and the cache is on the same filesystem, move
    it into the cache; otherwise copy it.

    Verify the cached Content before relying on it.

    Use the cached instance for required placements, moving it on its final use
    when a same-filesystem move is possible and copying otherwise.

    Treat cross-filesystem moves as copy operations followed by later removal
    of the source when safe.

    Delay destructive removals until required destinations for the affected
    Content are secured.

    Delete the execution cache after successful execution. If execution fails
    after Content has been staged, retain the cache until recovery/diagnosis no
    longer requires it.

Validate sources as they are staged or relied upon and validate destinations
before placing Content. Immediately before a destructive removal, establish
that the required surviving Content is actually secured. This execution-time
dependency validation is the primary safety mechanism; it avoids requiring a
complete Corpus rescan immediately before Execute.

Record completed physical operations durably enough to diagnose partial
execution or failure.

Execution summary presentation:

    completed operations
    exceptions
    conflicts
    failures
    open discrepancies between projected and actual filesystem

### Filesystem abstraction requirements

Core must not depend on Windows path syntax, drive letters, NTFS file
IDs, alternate data streams, ACLs, reparse points, case-insensitive path
behavior, or any other filesystem-specific assumption.

The filesystem abstraction may eventually define richer semantics as needed,
while the initial Core contract remains deliberately smaller. Potential areas
include:

    path identity and comparison
    directory enumeration
    file size
    timestamps used by the application
    stream/content reading
    create/copy/move/delete
    directory create/remove
    metadata preservation policy
    symbolic links / reparse points / filesystem links
    hard links
    path conflicts
    case sensitivity
    cross-filesystem moves
    atomicity guarantees where available

The initial implementation deliberately assumes only the least common
filesystem capabilities needed by the application:

    hierarchical directories
    named regular File instances at paths
    file length
    readable ordinary file content as a byte stream
    basic create/copy/move/delete operations

Content identity is defined solely by the bytes returned by the ordinary file
content stream. Filesystem-specific features are not part of duplicate identity
in the initial implementation.

Symbolic links, reparse points, filesystem aliases, and similar symbolic
objects are outside the initial model. Directory traversal must not follow
them; adapters ignore them rather than exposing them to Core as ordinary Files
or Directories.

Hard links receive no special treatment initially. If the adapter enumerates
multiple directory entries as ordinary files, Core may model them as separate
File instances with identical Content. Sparse files are treated as ordinary
files according to their logical byte streams. Alternate data streams, resource
forks, extended attributes, ACLs, ownership, and other extended metadata are
ignored for duplicate identity. Unix special files and anything that cannot be
safely exposed as an ordinary readable regular file are ignored.

The filesystem adapter is responsible for deciding what Core can safely regard
as a regular File or Directory. Core should not grow a taxonomy of each
platform's exotic filesystem objects. Future adapters/features may extend the
model deliberately, but unsupported filesystem features must not silently
acquire Windows-specific semantics.

### Deferred refinements

The following are deliberately deferred until experience demonstrates
value:

    Heuristic Situation inference/ranking.

    Exact ScopePair leverage refinement.

    Sophisticated persistent user learning.

    General-purpose filesystem reorganization beyond duplicate-motivated
    Resolutions.

    Aggressive incremental recomputation optimization.

    Formal/mathematically complete enumeration of Situations or Resolutions.

The architecture should permit these additions without requiring them
now.
