# Berries Development Guide

This document describes the current implementation state of Berries. It is intended to orient development work from the code that exists today, not preserve superseded plans.

Governing semantics are in `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`. `SEMANTIC-RESEARCH.md` and `BOUNDARY.md` retain empirical research without defining runtime workflow.

## Solution structure

    Berries.Core
        domain/session model
        Group discovery
        Directory and Branch analysis
        Portrait queries
        portrait-operation history
        physical Action planning/execution

    Berries.Projection
        UI-independent Explorer projection construction

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows implementation of that boundary

    Berries.Gui
        Avalonia application shell and interaction orchestration

    Berries.Core.Tests
        synthetic platform-independent tests

Target framework is .NET 10. The GUI is Avalonia and builds as `WinExe`. There is deliberately no console front end.

## Vocabulary in code

The code now follows current application language wherever the concepts coincide:

    Group
    GroupCount
    GroupedFileCount
    SharedGroupCount
    GroupDiscovery...
    Suggest
    Exclude
    Directory / Branch / DirectoryPair / BranchPair

Two lower-level names intentionally remain:

    FileInstance
        one filesystem file instance at one exact path

    ContentId
        established byte-content identity

They are narrower technical concepts rather than alternative product vocabulary. `Group` is the current collection of at least two FileInstances having one ContentId.

The obsolete classification/acceptance framework has been removed from compiled code. There are no runtime model types for semantic classification, no separate acceptance state, and no exhaustive structural-pair analysis path.

## Principal runtime objects

### `BerriesEngine`

Owns:

    Corpus normalization
    Initial Portrait acquisition
    Group discovery
    direct Directory analysis

Important public operations:

    CreateCorpus
    BuildInitialPortraitAsync
    DiscoverGroupsAsync
    AnalyzeDirectoriesAsync

### `BerriesApplication`

Owns application-level orchestration and publishes:

    Corpus
    Session
    Scan
    DirectoryAnalysis
    BranchStatistics
    Counterparts

A portrait operation invalidates all three derived analysis objects.

### `BerriesSession`

Owns:

    InitialPortrait
    WorkingPortrait
    Selection
    Operations
    Actions
    Groups

Exclude/Delete/Move add one top-level operation per user command. `Rebuild()` replays operations from the Initial Portrait and reconstructs Working Portrait, Groups, Actions, and selection binding.

### `PortraitQueries`

Answers model questions without UI dependencies:

    Groups / GroupsForSelection
    grouped files in Directory / Branch / Corpus Roots
    files in scope
    breadcrumbs
    best Directory Pair
    Branch counterpart eligibility
    shared Group count

### `ProjectionService`

Builds UI-independent Explorer representations from `PortraitQueries`.

### `BranchStatisticsAnalyzer`

Computes Branch records:

    FileCount
    DirectoryCount
    GroupedFileCount
    GroupCount
    GroupedDirectoryCount

### `BranchPriorityMetrics`

Computes parent-relative Group concentration metrics. The current seed ranking uses `ExcessConcentratedGroups`.

### `BranchCounterpartAnalyzer`

Performs targeted Branch relationship discovery and on-demand best-pair search. It does not enumerate every Branch Pair.

### `ActionPlanExecutor`

Executes the concrete filesystem Actions produced by the Working Portrait. It attempts independent work after failures and performs Move -> Copy/Delete fallback when required.

### `MainWindow`

Owns Avalonia presentation and command orchestration. Functionality is split across partial-class files for navigation, projection construction, selection, portrait commands, context menus, and progress handling.

## Current UI

File menu:

    Select Roots...
    Load Saved Session...      disabled
    Save Session...            disabled
    Execute...
    Exit

Explorer toolbar:

    Back                       present, history not implemented
    Pivot
    Forward                    present, history not implemented
    Suggest
    Invert
    Exclude
    Delete
    Move -> / <- Move
    Undo

Current projections:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

## Current initial scan path

`BerriesApplication.ScanAsync()` currently performs:

    normalize Corpus
        -> acquire Portrait
        -> DiscoverGroupsAsync
        -> attach ContentIds to grouped files
        -> construct BerriesSession
        -> RefreshAnalysisAsync
             -> Directory analysis
             -> Branch statistics
             -> targeted counterpart analysis
        -> return ScanResult

This entire path is awaited before the Groups projection becomes ready.

The discovery front end is fundamentally one Corpus-dependent chunk: once the Corpus is unchanged, its observed Portrait and established ContentIds are treated as fixed session truth. Berries does not rehash because virtual portrait operations occur or because the external filesystem may have drifted.

## Current portrait-operation path

Exclude/Delete/Move/Undo are run through `BerriesApplication`.

After a command changes the operation history:

    derived analysis objects are set to null
    visible projection is refreshed immediately from BerriesSession
    GUI starts RefreshAnalysisAsync in background
    old refresh work is cancelled when another portrait command starts
    completion restores analysis-dependent capabilities

This is the first implemented form of analysis invalidation/background refresh.

## Current analysis mathematics

### Directory Pair

Strength is simply:

    SharedGroupCount

No separate generic leverage abstraction is retained.

### Branch seed

For a child Branch relative to its parent:

    group retention = child GroupCount / parent GroupCount
    file retention  = child FileCount / parent FileCount
    concentration   = group retention / file retention

    ExcessConcentratedGroups =
        child GroupCount * (1 - 1 / concentration), concentration > 1
        0,                                         otherwise

### Branch relationship

Targeted search uses:

    shared Group count
    seed coverage
    counterpart coverage
    Jaccard overlap

    score = shared Group count * Jaccard

Selection examines a top-10 seed window, chooses the strongest actual relationship, blocks both chosen Branch families, and repeats.

This practical approach replaced exhaustive ancestor-Cartesian Branch Pair construction because the latter produced combinatorial growth on real corpora.

## Move implementation

`BerriesSession.Move()` operates entirely on the Working Portrait.

For each requested source file:

1. verify it is still present and lies within the source scope;
2. preserve its source-relative parent path beneath destination scope;
3. detect the same ContentId directly in the computed destination Directory;
4. reduce to Delete when content is already correctly present there;
5. otherwise use the source filename;
6. report same-name/different-content collisions without modifying either file;
7. update persistent selection for successful modeled moves.

Move does not rename arbitrarily and does not overwrite different content.

## Execute implementation

The GUI calculates pre-execution content loss, asks for approval, then calls `ActionPlanExecutor`.

Executor behavior:

    DeleteFileAction -> delete
    CopyFileAction   -> ensure parent, copy
    MoveFileAction   -> ensure parent, try Move;
                        on IOException, Copy then Delete

I/O/authorization failures are recorded. Independent later work continues.

## Configuration

`Berries.config` uses `[exclude]` only.

Matching:

    no separator     -> any path component / filename
    separator        -> contiguous full-path segment
    * / ?            -> wildcards
    # / ;            -> comments

The parser and tests use Exclude terminology consistently.

## Tests retained after cleanup

Active tests cover:

    Corpus/Portrait acquisition
    Group discovery and file-access failures
    configuration Exclude
    Directory statistics and Directory Pairs
    Branch statistics
    Branch priority metrics
    BerriesSession portrait operations / Undo / Move
    filesystem abstraction and execution behaviors

Tests for removed experimental models were deleted rather than translated into current terminology.

## Immediate architectural work

The next design problem remains the analysis lifecycle discussed immediately before this terminology cleanup.

The initial discovery chunk is required whenever the Corpus changes and can thereafter remain stable for the session. Derived results have different prerequisites and invalidators:

    Working Portrait + Groups
        -> Directory analysis
        -> Branch statistics

    Directory analysis + Branch statistics + Groups
        -> counterpart analysis

The current implementation still recomputes these as one sequential `RefreshAnalysisAsync()` operation. The intended next step is to define explicit validity/prerequisite state and demand-driven background scheduling without constructing an over-general analysis framework.
