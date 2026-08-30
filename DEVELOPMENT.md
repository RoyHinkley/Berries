# Berries Development Guide

This document describes the current implementation state of Berries. It is intended to orient development work from the code that exists today, not preserve superseded plans.

Governing semantics are in `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`. `SITUATIONS.md` and `BOUNDARY.md` retain historical/empirical research.

## Solution structure

Current projects:

    Berries.Core
        domain/session model
        duplicate and structural analysis
        portrait queries
        portrait-operation history
        physical Action planning/execution types

    Berries.Projection
        UI-independent Explorer projection construction and querying

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows implementation of that boundary

    Berries.Gui
        Avalonia application shell and interaction orchestration

    Berries.Core.Tests
        synthetic platform-independent tests

Target framework is .NET 10. The GUI is Avalonia and builds as `WinExe`.

There is deliberately no console front end.

## Current application architecture

The principal runtime objects are:

    BerriesEngine
        Corpus creation, Portrait acquisition, duplicate discovery,
        direct Directory analysis

    BerriesApplication
        application-level orchestration and publication of current
        Session / scan / derived analysis results

    BerriesSession
        fixed Initial Portrait
        Working Portrait
        persistent selection
        ordered portrait operations
        current Groups / DuplicateSets
        physical Action list

    ProjectionService / PortraitQueries
        build/query Explorer views without Avalonia dependencies

    BranchStatisticsAnalyzer
        first-class Branch statistics

    BranchCounterpartAnalyzer
        targeted Branch counterpart search and on-demand best-pair search

    ActionPlanExecutor
        physical execution and failure/dependency handling

    MainWindow
        Avalonia presentation and command orchestration

## Current UI

The implemented application has a conventional File menu:

    Select Roots...
    Load Saved Session...      disabled
    Save Session...            disabled
    Execute...
    Exit

Root selection supports:

    Add...
    Remove
    Explore

The Explorer supports these current user-facing projections/terms:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

The code uses internal types such as `DuplicateSet`, `ContentId`, and `FileInstance`; ordinary UI/documentation should prefer Group, file/copy, and shared Groups.

Current toolbar/navigation language includes:

    Back                 present but history not implemented
    Pivot
    Forward              present but history not implemented
    Suggest
    Invert Selected Copies
    Invert All Groups
    Exclude
    Delete
    Move ->
    <- Move
    Undo

## Configuration

`Berries.config` now uses `[exclude]`; `[ignore]` is obsolete.

Current matching semantics:

    no path separator
        match a path component / filename

    path separator present
        match a contiguous normalized path segment

    * and ?
        wildcards

    # or ; at line start
        comments

Configuration matching is applied during initial Portrait acquisition.

## Initial scan pipeline

`BerriesApplication.ScanAsync()` currently orchestrates the full new-session scan sequentially:

    CreateCorpus
        -> BuildInitialPortraitAsync
        -> DiscoverDuplicatesAsync
        -> attach discovered ContentIds
        -> new BerriesSession
        -> RefreshAnalysisAsync
             -> AnalyzeDirectoriesAsync
             -> BranchStatisticsAnalyzer.Analyze
             -> BranchCounterpartAnalyzer.Analyze
        -> return ScanResult

The GUI displays Corpus roots and progress while this work runs. It switches to the normal Group projection after `ScanAsync()` returns.

This means the GUI thread is not synchronously blocked by the computational work, but initial session readiness is still coupled to completion of the full derived-analysis chain.

## Duplicate discovery

Discovery groups files by length and hashes only non-singleton length groups with SHA-256.

Expected content-read failures (`IOException`, `UnauthorizedAccessException`, `SecurityException`) evict the affected file from the established session. Programming errors propagate.

The Windows filesystem implementation uses permissive read sharing (`ReadWrite | Delete`) where practical.

Unique files remain in the Initial/Working Portrait even though they are not shown as duplicate-resolution candidates. The current Move implementation depends on them for destination collision detection.

## Session and portrait operations

`BerriesSession` is now the authoritative mutable session model.

It owns:

    InitialPortrait          fixed
    WorkingPortrait          rebuilt from operation history
    Selection                persistent semantic selection
    Operations               ordered top-level Undo steps
    Actions                  physical filesystem work implied by operations
    DuplicateSets            current Working-Portrait Groups

### Exclude

`Exclude()` records `ExcludePortraitOperation` objects. Excluded files disappear from the Working Portrait but create no physical Action.

### Delete

`Delete()` records `DeletePortraitOperation` objects. Rebuild removes those files and adds `DeleteFileAction` entries.

### Move

`Move()` evaluates each requested source against the current Working Portrait.

It:

- preserves source-relative directory structure beneath the chosen destination scope;
- treats existing same-Content files in the exact computed destination Directory as authoritative;
- reduces already-present content to source Delete;
- detects same-name/different-Content collisions, including collisions with unique files;
- records successful relocations as `MovePortraitOperation`;
- updates selected paths for moved files.

### Undo

Each user command is one top-level operation, using `PortraitOperationBatch` when necessary.

`Undo()` removes the latest top-level operation and calls `Rebuild()`, which reconstructs the Working Portrait, Group membership, Action list, and valid Selection from the fixed Initial Portrait.

## Projection layer

`Berries.Projection` is now a real architectural boundary and should remain separate from GUI controls.

`ProjectionService` currently supplies:

    DirectoryAsync
    BranchAsync
    CorpusRootsAsync
    Groups
    GroupsForSelection
    Group
    SharedGroupCountAsync
    structural lookup/navigation helpers

The GUI converts these projection models into `ExplorerNode` trees.

Do not move ordinary projection/query logic back into Avalonia event handlers unless the behavior is genuinely presentation-specific.

## Navigation

Current Pivot operations include:

    Corpus Roots
    Group
    Containing Directory
    Branch
    Best Directory Pair
    Best Branch Pair
    Current Suggested Branch Pair

Directory/Branch pair panes maintain independent structural breadcrumbs.

Back and Forward controls exist in XAML but have not yet acquired navigation-history implementation.

## Structural analysis

### Directory analysis

The engine computes duplicate-bearing `DirectoryRecord` objects and `DirectoryPair` evidence.

### Branch statistics

`BranchStatisticsAnalyzer` computes first-class Branch statistics without enumerating all Branch Pairs.

### Branch priority

`BranchPriorityMetrics` contains parent-relative measures. Current seed selection uses the concentration form based on distinct duplicated Content and duplicate-content retention versus ordinary-file retention.

### Targeted counterparts

`BranchCounterpartAnalyzer` is the active container-centric discovery mechanism.

The application currently requests:

    seedLimit = 25
    counterpartLimit = 5

and publishes the result as `BerriesApplication.Counterparts`.

Comprehensive Branch-Pair enumeration is intentionally not part of the active design; prior experiments demonstrated unacceptable combinatorial cost.

`FindBestBranchPairAsync()` performs an on-demand best-counterpart search for a selected Branch using current Branch statistics and Groups.

## Analysis invalidation and background refresh

Any session command that changes the top-level portrait-operation count calls `InvalidateAnalysis()`, which currently clears all three derived products:

    DirectoryAnalysis
    BranchStatistics
    Counterparts

The invalidation model is intentionally coarse.

After a GUI portrait command:

1. cancel an older background analysis refresh if one exists;
2. apply the session command;
3. refresh the visible projection immediately;
4. re-enable ordinary Explorer interaction;
5. start a cancellable background `RefreshAnalysisAsync()`;
6. update capabilities when the current refresh finishes.

This background-generation mechanism is implemented in `MainWindow.PortraitCommands.cs`.

Initial scan still awaits the same derived analysis synchronously from the caller's perspective before returning.

## Transitional analysis residue

`DuplicateSettlements` remains in Core and in several analyzer signatures from the earlier Accept/Settle experimental model.

Current application behavior does **not** use settlement semantics. `RefreshAnalysisAsync()` creates a new empty `DuplicateSettlements` for each refresh and never accepts content or pairs. Therefore it is currently a compatibility parameter with no filtering effect.

Do not describe Accept/Settle as an application feature. Removing this residue from analyzer APIs is legitimate cleanup when convenient, but documentation should reflect the code until that happens.

Older `Case` classes/report formatters also remain in parts of the tree from experimental work. The active Explorer does not require a persistent Case queue or Situation->Resolution->Disposition workflow.

## Execute

Physical Execute is implemented.

The GUI enables Execute when `session.Actions.Count > 0`.

Before approval it reports:

    planned Action count
    Groups with no surviving physical file after the plan

`ActionPlanExecutor` then attempts the Actions. It supports dependency-safe execution, including copy-before-delete behavior where a physical move cannot be completed directly.

Independent work can continue after local failure. Results distinguish completed, skipped-dependent, and failed Actions. The GUI presents a failure summary when necessary.

There is no global pre-execution rescan/reconciliation pass.

## Persistence

Save/Load menu items are present but disabled. No current persistence format or schema should be treated as implemented.

When persistence work eventually begins, derive its representation from the runtime session model rather than resurrecting earlier speculative schemas.

## Tests

The test project contains coverage for current Core/session behavior, duplicate discovery, filesystem boundaries, structural analysis, projection-relevant query semantics, operations, Move behavior, and execution.

When changing architecture, prefer synthetic tests that exercise Core without Avalonia or Windows-specific assumptions.

Particularly important invariants to preserve include:

- deterministic Working-Portrait rebuild from Initial Portrait plus operations;
- one user command per Undo step;
- Exclude produces no physical Action;
- Group membership follows the current Working Portrait;
- Move preserves relative paths and destination-authoritative semantics;
- same-name/different-Content collisions leave the source unchanged;
- unique files can block Move destinations;
- failed execution prerequisites suppress dependent destructive work;
- exhaustive Branch-Pair generation must not accidentally return as a dependency of normal analysis.

## Known incomplete or deliberately deferred work

Current code intentionally does not yet provide:

    Save/Load
    Back/Forward navigation history
    general rename
    general unique-file maintenance
    automatic Situation classification
    learned/persistent resolution rules
    fine-grained analysis invalidation
    independent lazy scheduling of every derived analysis product

The last item is the active architectural area immediately under study: the current initial-scan chain and post-operation background refresh are functional, but derived analyses are still managed as one coarse validity unit.
