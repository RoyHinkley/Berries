# Berries Development Guide

This document describes the current implementation state of Berries and the experimentally developed reasoning that must be preserved during refinement. Governing semantics are in `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`.

## Solution structure

    Berries.Core
        domain/session model
        Group discovery
        Directory and Branch analysis
        Portrait queries
        portrait-operation history
        physical FileAction execution

    Berries.Projection
        UI-independent Explorer projection construction
        ProjectionState presentation/navigation state

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows implementation

    Berries.Gui
        Avalonia shell and interaction orchestration

    Berries.Core.Tests
        synthetic platform-independent tests

Target framework is .NET 10.

## Vocabulary in code

Current application/code language includes:

    Group
    GroupCount
    GroupedFileCount
    SharedGroupCount
    GroupDiscovery...
    Suggestion / Suggest
    Exclude
    Directory / Branch / DirectoryPair / BranchPair

Narrower technical concepts remain:

    FileInstance
        one filesystem instance at one exact path

    ContentId
        established byte-content identity

    Seed
        a Branch selected as a promising starting point for targeted search

    Counterpart
        a Branch scored relative to a particular Seed

    ProjectionState
        current Explorer presentation/navigation state; not a Case

**Case remains a valid domain concept.** It is an objective bounded set of current-Portrait files containing duplication and considered together for one coherent disposition. The boundary limits disposition authority. The current implementation does not require a persistent runtime `Case` object for every Explorer view.

Do not equate Case with ProjectionState. That was a terminology-cleanup regression and has been reversed.

Situation/Resolution/Disposition classification is not a required runtime workflow, but the underlying semantic research is not meaningless. Situation can describe human context, and disposition remains useful when reasoning about the coherent outcome authorized by a Case.

## Principal runtime objects

### `BerriesEngine`

Owns Corpus normalization, Initial Portrait acquisition, Group discovery, and direct Directory analysis.

### `BerriesApplication`

Owns application orchestration and publishes:

    Corpus
    Session
    Scan
    DirectoryAnalysis
    BranchStatistics
    Suggestions

A portrait operation invalidates the derived analysis objects.

### `BerriesSession`

Owns:

    InitialPortrait
    WorkingPortrait
    Selection
    Operations
    Actions
    Groups

`Rebuild()` replays operations from the Initial Portrait and reconstructs Working Portrait, Groups, Actions, and selection binding.

### `PortraitQueries`

Answers model questions without UI dependencies: Groups, files in structural scopes, breadcrumbs, best Directory Pair, Branch counterpart eligibility, and shared Group counts.

### `ProjectionService` / `ProjectionState`

`ProjectionService` builds UI-independent Explorer representations. `ProjectionState` records the current projection kind, represented files, and applicable one- or two-sided scopes.

This is view/navigation state only. It does not establish a Case or disposition authority.

### `BranchStatisticsAnalyzer`

Computes Branch records including FileCount, DirectoryCount, GroupedFileCount, GroupCount, and GroupedDirectoryCount.

### `BranchPriorityMetrics`

Computes parent-relative Group concentration metrics. Current Seed ranking uses `ExcessConcentratedGroups`.

### `BranchCounterpartAnalyzer`

Uses ranked Seeds to search efficiently for strong Branch relationships. For each Seed it finds and ranks Counterparts, compares the best pair from several candidate Seeds, and emits `BranchPairSuggestion` results.

The winning Suggestion is chosen primarily by Branch Pair score, not Seed rank. The highest-ranked Seed often does not produce the best Branch Pair. This distinction is critical and is covered by `Analyze_SelectsBestPair_NotHighestRankedSeed`.

The analyzer also supports on-demand best-pair search for an explicitly selected Branch. That selected Branch is not conceptually a Seed merely because it is one side of the resulting Branch Pair.

### `FileActionExecutor`

Executes concrete filesystem Actions, continues independent safe work after failures, and performs Move -> Copy/Delete fallback when required.

## Analysis mathematics

### Directory Pair

    SharedGroupCount

This is a factual distinct-Group count. Do not call it leverage.

### Branch Seed

    group retention = child GroupCount / parent GroupCount
    file retention  = child FileCount / parent FileCount
    concentration   = group retention / file retention

    ExcessConcentratedGroups =
        child GroupCount * (1 - 1 / concentration), concentration > 1
        0,                                         otherwise

Seed priority means "worth investigating," not "best Case" or "best Branch Pair."

### Counterpart / Branch Pair score

For a Seed and candidate Counterpart:

    shared Group count
    Seed coverage
    Counterpart coverage
    Jaccard overlap

    score = shared Group count * Jaccard

Each Seed's Counterparts are ranked by this relationship score.

### Suggestion selection

Each round examines the top 10 eligible Seeds, computes the best Counterpart relationship for each, then selects the strongest Branch Pair across that Seed window. Seed rank is only a later tie-breaker.

After selection, the chosen Seed and Counterpart families are blocked and the process repeats to produce a compact set of structurally distinct Suggestions.

## Historical leverage and present ranking intent

Early design used **leverage** to mean work accomplished per user question, initially quantified as duplicate file instances within a Case. This was a useful conceptual starting point but not a sufficient presentation metric.

Two lessons superseded a single generic Leverage field:

1. exact leverage could be expensive, and cheaper measures often preserved the ranking that mattered;
2. maximum duplicate coverage could favor broad, difficult-to-recognize scopes over nearby narrower scopes that produced much clearer human decisions.

Therefore current code should use names that describe the actual quantity (`SharedGroupCount`, `ExcessConcentratedGroups`, relationship `Score`) rather than labeling unlike numbers `Leverage`.

The higher-level goal remains to prioritize useful, comprehensible Cases that accomplish substantial work per user decision.

## Explorer-first behavior

The application deliberately does not implement a wizard queue of Cases. Suggestions are starting points. Pair breadcrumbs and Pivot allow the user to broaden/narrow scopes and follow recognizable structure.

This is not a fallback for weak analysis; it is part of the intended division of labor. Statistics find promising structure. The user supplies semantic recognition.

## Empirical result to preserve

Real-corpus R&D showed that resolving a small number of useful structural Cases can reduce very large duplicate problem sets extremely quickly. Datasets with tens of thousands of duplicate instances could often be potentially resolved with roughly a handful of Case-level decisions.

This is the reason to:

- find promising local structure rather than enumerate all Branch Pairs;
- compare several Seeds before choosing a Suggestion;
- re-analyze after portrait changes;
- prefer comprehensibility over a slightly larger raw coverage count;
- avoid designing a complete global resolution plan up front.

Treat unexplained complexity in this algorithm as experimental evidence until its purpose is understood; the current code is the surviving result of substantial empirical refinement.

## Current initial scan path

`BerriesApplication.ScanAsync()` currently performs:

    normalize Corpus
        -> acquire Portrait
        -> DiscoverGroupsAsync
        -> attach ContentIds
        -> construct BerriesSession
        -> RefreshAnalysisAsync
             -> Directory analysis
             -> Branch statistics
             -> Suggestion discovery
        -> return ScanResult

This path is currently awaited before the Groups projection becomes ready.

## Portrait-operation path

After Exclude/Delete/Move/Undo:

    derived analysis objects are invalidated
    visible projection refreshes immediately from BerriesSession
    GUI starts RefreshAnalysisAsync in background
    old refresh work is cancelled when another portrait command starts
    completion restores analysis-dependent capabilities

## Unique files: deliberately unresolved

The Portrait retains unique files. They constrain Move destinations and participate in statistics such as `FileCount`, which in turn affects Seed concentration. Earlier structural Case definitions also allowed unique files within Case bounds.

Whether unique files should remain Case members is a separate design question. Do not remove them, their counts, or their influence on ranking as a side effect of terminology cleanup.

## Tests retained after cleanup

Active tests cover Corpus/Portrait acquisition, Group discovery, Exclude, Directory analysis, Branch statistics, Branch priority, Session operations/Undo/Move, filesystem execution, and the critical distinction between Seed rank and winning Branch Pair score.

## Immediate architectural work after terminology cleanup

The next design problem remains analysis lifecycle/dependency management.

The Corpus-dependent discovery front end can remain stable for a session. Derived results have different prerequisites and invalidators:

    Working Portrait + Groups
        -> Directory analysis
        -> Branch statistics

    Directory analysis + Branch statistics + Groups
        -> Suggestion discovery

The current implementation recomputes these through one sequential `RefreshAnalysisAsync()`. The next step is an explicit, simple validity/prerequisite model and demand-driven background scheduling, without constructing an over-general analysis framework.
