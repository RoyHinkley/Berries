# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Project

## Problem statement

Ordinary duplicate-file tools generally expose DuplicateSets and require the user to decide what to do with individual instances. That works for small isolated duplication, but poorly for accumulated backups, reorganized trees, partial moves, migrations, archives, generated output, repositories, and other real filesystem histories.

Berries treats duplicate Content as evidence about both files and filesystem structure. It builds a virtual Working Portrait of the user's desired Corpus and provides an Explorer in which the user can inspect, Pivot, Exclude, Delete, and Move duplicate instances before any physical filesystem change occurs.

## Objective

Provide a safe, efficient way to eliminate or deliberately remove from consideration unwanted file duplication across large filesystem trees while preserving the Content and organization the user wants.

The system should minimize required user attention without making semantic decisions on the user's behalf.

## Governing principles

1. **Corpus is logical.** It is the material the user intends Berries to work with, not the entire filesystem. Selected roots add material; Exclude subtracts it.

2. **Portrait-first design.** A scan produces an Initial Portrait. The Working Portrait always represents the user's desired Corpus and is deterministically reconstructible from the Initial Portrait plus an ordered sequence of portrait operations.

3. **The Explorer is primary.** Cases are suggested foci, not a mandatory workflow or persistent queue. Berries should identify something likely to reward attention, then let the user navigate and Pivot freely.

4. **Selection has one meaning.** Across projections, a selection always denotes a set of duplicate FileInstances. Higher tree nodes are shorthand for their applicable descendant instances.

5. **Projection is navigation.** Content, DirectoryPair, and BranchPair are different organizations of the same Working Portrait. Pivot changes view/focus, not resolution semantics.

6. **Operations are explicit and positive.** The principal portrait operations are Exclude, Delete, and Move. There is no Keep operation: survival is the absence of a destructive action.

7. **No settlement layer.** There is no Accept/Settle operation. If instances should cease to participate in Berries, Exclude removes them from the Working Portrait without changing the filesystem.

8. **No Apply layer.** Exclude/Delete/Move update the Working Portrait immediately and are Undoable. Execute is the separate physical commitment boundary.

9. **Move preserves source-relative structure.** The user explicitly establishes source/destination scopes. Existing destination organization is authoritative; Berries does not infer renames, invent filenames, or overwrite different Content.

10. **Unique files can constrain duplicate operations.** They need not be resolution targets, but the session retains enough knowledge of them to detect destination collisions and other filesystem constraints.

11. **Analysis serves attention.** Structural relationships are valuable evidence, but exhaustive enumeration is not a goal. Cheap Branch statistics plus targeted counterpart search are preferred when they find useful questions without combinatorial expansion.

12. **Execution is explicit.** No real filesystem modification occurs until Execute. A pre-execution summary describes the approved plan and Content loss; execution attempts the plan and handles actual filesystem failures locally; a post-execution summary reports what happened.

13. **Core remains independent of UI and platform-specific filesystem behavior.**

## Application flow

A conventional application shell contains the root-selection view and the Duplicate Explorer.

For a new session:

    Select Roots
        -> Add / Remove / Scan
        -> Explorer appears with Corpus roots
        -> analysis progressively enriches the session
        -> Suggest Case / Pivot / Navigate / Resolve
        -> Execute when ready

A persistent status bar owns scan/analysis progress. Heavy analysis should be architected so it can move to asynchronous background work without redesigning the application. Controls become available when the data required to support them exists.

A future saved-session path is:

    Load Saved Session
        -> restore session directly
        -> Duplicate Explorer

Save/Load can remain unimplemented until practical use demonstrates value.

## Duplicate Explorer

### Content projection

One pane:

    Content
        full-path FileInstance
        full-path FileInstance
        ...

### DirectoryPair / BranchPair projections

Two equivalent tree panes. Directory nodes organize duplicate FileInstances; selecting a higher node selects the applicable duplicate instances represented beneath it.

### Navigation

Likely navigation commands:

    Back
    Forward
    Pivot
    Suggest Case

`Suggest Case` means "identify something likely to reward attention," not "give me the next item in a required queue."

### Resolution

Likely resolution/selection commands:

    Invert Selection
    Exclude
    Delete
    Move ->
    <- Move
    Undo

Context menus may duplicate common operations. Selection-first interaction is preferred over tool-first paint semantics.

## Move in one paragraph

Move maps selected duplicate instances from a source scope to a destination scope while preserving each source instance's relative directory path. Within each computed destination directory, an existing instance of the same Content means the work is already done there: retain the destination's existing name and delete the source. If no same-Content instance exists, use the source filename. A same-name/same-Content collision again reduces to source deletion; a same-name/different-Content collision is flagged immediately and that source is left untouched while the rest of the Move proceeds. Rename and general unique-file reorganization are outside scope.

## Analysis strategy

Duplicate discovery remains size-grouping followed by hashing of non-singleton size groups.

The useful analysis viewpoints are:

    Content-centric / DuplicateSet
    same-directory duplication
    BranchPair / container-centric duplication

DirectoryPair remains useful direct evidence and a narrow projection but need not be an independent Case-generation system.

Branch statistics are computed independently of BranchPair enumeration. The current useful seed score is:

    D * (1 - 1/C)

where D is distinct duplicated Content in the child Branch and C is duplicated-Content retention divided by ordinary file retention relative to the parent.

Promising BranchPairs are found by targeted counterpart search. Current experimental selection considers the top 10 eligible seeds, finds each seed's best counterpart, scores the pair as shared distinct Content times Jaccard overlap, chooses the best pair, culls both selected branch families, and repeats.

Comprehensive BranchPair enumeration is suspended; large-corpus experiments showed combinatorial cost without corresponding user benefit.

## Execution model

The Working Portrait is a virtual design. Portrait operations produce an Action Plan only where physical work is required:

    Exclude -> no filesystem Action
    Delete  -> deletion Action(s)
    Move    -> move/copy/delete Action(s) as required

Before Execute, show a summary of intended work and identify Content that will have no surviving instance in the final working Corpus. Do not interrupt ordinary Delete operations with last-instance warnings.

After approval, attempt the Action Plan without a global filesystem revalidation/rescan. Atomic operations can fail regardless of preflight. Preserve dependency safety: for example, a cross-device Move implemented as Copy then Delete must omit the Delete if Copy fails. Independent safe work can continue. The post-execution summary reports successes, failures, skipped dependent work, and exceptions.

## Persistence direction

A saved session, if implemented, should be one serializable session object containing all data required to resume directly. JSON is preferred and should normally be compressed because Portraits can be large.

Loading restores the saved session; it does not reconcile it with the filesystem. A fresh view of disk is obtained by starting a New Session and scanning.

## Design documents

### [MODEL.md](MODEL.md)

Authoritative terminology and invariants: Corpus, Initial/Working Portrait, FileInstance/Content, Exclude/Delete/Move, projections, selection, Actions, Execute, and persistence semantics.

### [ANALYSIS.md](ANALYSIS.md)

Duplicate discovery, directory/branch evidence, Branch seed metrics, targeted counterpart search, Suggested Cases, projection data, progress/background-analysis direction, and retained empirical findings.

### [WORKFLOW.md](WORKFLOW.md)

Application flow, Explorer interaction, selection/navigation, Exclude/Delete/Move semantics, Undo, Save/Resume direction, and execution/reporting behavior.

### [SITUATIONS.md](SITUATIONS.md)

Retains the Situation catalogue and semantic research. Situation classification is optional and is not a prerequisite for ordinary Explorer operation.

### [BOUNDARY.md](BOUNDARY.md)

Empirical investigation of the practical problem boundary and application-managed/generated material. It is research context rather than governing UI workflow.

### [DEVELOPMENT.md](DEVELOPMENT.md)

Describes current implementation state and the delta between today's code and the governing design.

## Platform and architecture

Implementation platform:

    C#
    .NET
    Avalonia

Solution decomposition remains:

    Berries.Core
        domain model, Portrait, duplicate/structural analysis,
        session operations, planning/execution contracts

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem adapter

    Berries.Gui
        Avalonia desktop UI and orchestration

    Berries.Core.Tests
        platform-independent tests using synthetic Portrait/filesystem data

Architectural test:

    If Core cannot be exercised by a simple test harness against synthetic
    data, a platform or UI concern has leaked across a boundary.
