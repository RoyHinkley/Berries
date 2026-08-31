# Berries Development Guide

This document describes the current implementation state of Berries and the experimentally developed reasoning that must be preserved during refinement. Governing semantics are in `PROJECT.md`, `MODEL.md`, `ARCHITECTURE.md`, `ANALYSIS.md`, and `WORKFLOW.md`.

## Solution structure

    Berries.Core
        domain/session model
        Group discovery
        Directory and Branch analysis
        analysis lifecycle/scheduling
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

The architectural placement rule is **Core whenever possible, Projection only when warranted by presentation-specific meaning, GUI only for interaction and bounded control work**. Corpus-, Portrait-, Group-, Directory-, or Branch-scale computation does not belong in GUI event handlers or GUI helper methods. See `ARCHITECTURE.md` for the full responsiveness, cancellation, and progress contract.

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

Owns Corpus normalization, Initial Portrait acquisition, Group discovery, unique-file accounting/pruning, and direct Directory analysis. Computation belongs here by default when it is domain or factual model work rather than presentation construction.

### `BerriesApplication`

Owns application orchestration, serialized portrait mutation, portrait generation, and the dependency-driven background analysis scheduler. It publishes current-generation values for:

    Corpus
    Session
    Scan
    DirectoryAnalysis
    BranchStatistics
    Suggestions

Completed derived results are retained internally even after becoming stale, but the ordinary public analysis properties expose only results valid for the current portrait generation.

### `AnalysisProduct<T>`

Owns lifecycle state for one derived result:

    latest completed Result
    ResultGeneration
    RunningGeneration
    cancellation for the active run

Validity is derived from generation equality rather than represented by a separately mutable valid/invalid flag. A stale computation may finish, but it can publish only if its generation is still current.

### `BerriesSession`

Owns:

    InitialPortrait
    WorkingPortrait
    Selection
    Operations
    Actions
    Groups
    UniqueFileCountsByDirectory

After initial Group discovery, unique `FileInstance`s are removed from the session Portrait. `UniqueFileCountsByDirectory` retains the fixed number of files in each physical directory that were unique at initial discovery.

Group identity is also fixed by initial discovery. `Rebuild()` replays operations from the Initial Portrait and reconstructs Working Portrait, current Group membership, Actions, and selection binding. A Group may have two or more, one, or zero current members without losing its session identity.

Because Initial Portrait now contains only Group-originating files, its traversals scale with the duplicate-resolution problem rather than the complete scanned corpus.

### `PortraitQueries`

Answers factual model questions without UI dependencies: files in structural scopes, breadcrumbs, best Directory Pair, Branch counterpart eligibility, shared Group counts, and related model queries.

### `ProjectionService` / `ProjectionState`

`ProjectionService` builds UI-independent Explorer representations. It is the correct home for work whose result is inherently presentation-shaped, such as Explorer hierarchy construction, Group display ordering, and projection labels.

`ProjectionState` records the current projection kind, represented files, and applicable one- or two-sided scopes. This is view/navigation state only. It does not establish a Case or disposition authority.

Projection is not a general-purpose overflow layer for work that should have been in Core.

### `BranchStatisticsAnalyzer`

Computes Branch records including `UniqueFileCount`, `DirectoryCount`, `GroupedFileCount`, `GroupCount`, and `GroupedDirectoryCount`.

`FileCount` is derived as:

    FileCount = UniqueFileCount + GroupedFileCount

`GroupedFileCount` means current files belonging to session-stable Groups, including a Group's sole remaining member. A zero-member Group contributes no files.

### `BranchPriorityMetrics`

Computes parent-relative Group concentration metrics. Current Seed ranking uses `ExcessConcentratedGroups` and continues to use total `FileCount`, reconstructed from fixed unique counts plus the current Group-member population.

### `BranchCounterpartAnalyzer`

Uses ranked Seeds to search efficiently for strong Branch relationships. For each Seed it finds and ranks Counterparts, compares the best pair from several candidate Seeds, and emits `BranchPairSuggestion` results.

The winning Suggestion is chosen primarily by Branch Pair score, not Seed rank. The highest-ranked Seed often does not produce the best Branch Pair. This distinction is critical and is covered by `Analyze_SelectsBestPair_NotHighestRankedSeed`.

The analyzer also supports on-demand best-pair search for an explicitly selected Branch. That selected Branch is not conceptually a Seed merely because it is one side of the resulting Branch Pair.

### `FileActionExecutor`

Executes concrete filesystem Actions, continues independent safe work after failures, and performs Move -> Copy/Delete fallback when required.

## Responsiveness and scaling-work rule

Any operation whose runtime can grow materially with user data is reviewed as an architectural concern, not merely a performance concern.

The required pattern is:

    place factual/domain computation in Core whenever possible
    use Projection only for inherently presentation-shaped computation
    expose an asynchronous API to the GUI
    accept CancellationToken
    check cancellation inside the scaling loop
    report a meaningful user-facing phase
    report completed/total whenever the total is practical to know
    leave the GUI thread to bounded presentation/control updates

A `Task.Run` added in GUI code is not a substitute for putting the work in the correct project.

Determinate progress is preferred when the work population is already available or cheaply countable. Indeterminate progress is appropriate when obtaining a total would duplicate or materially increase the work, such as open-ended filesystem enumeration.

Core/Projection own the wording and counts that describe their work. GUI code renders those reports in the status bar; it should not hard-code one phase name for a multi-phase operation.

Cancellation is for responsiveness and avoiding wasted work. Generation validation remains the correctness mechanism for background analysis: stale results cannot publish even if cancellation is delayed.

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

The terminal `Suggestions` product should remain conceptually broader than the current Branch-Pair-only producer. Future work may fold Groups, Directories, Directory Pairs, and Branch Pairs together and cull diminishing-return Suggestions before presentation.

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

`BerriesApplication.ScanAsync()` orchestrates the primary scan while `BerriesEngine` performs the scaling computation:

    normalize Corpus
        -> acquire complete Portrait
        -> DiscoverGroupsAsync
            -> size-group candidates
            -> hash candidates
            -> construct Groups
            -> attach ContentIds
            -> count unique files by physical Directory
            -> prune unique FileInstances
        -> construct BerriesSession
        -> establish a new portrait generation
        -> schedule derived analysis
        -> return ScanResult

The countable discovery phases report phase-specific progress and are cooperatively cancellable. Filesystem enumeration reports observed scan counts but remains indeterminate because a total is not cheaply known in advance.

The Groups projection can therefore become usable as soon as primary discovery is complete. Directory/Branch/Suggestion work continues in the background.

## Derived analysis dependency path

The current dependency chain is:

    Working Portrait + Groups + retained unique counts
        -> Directory analysis / Directory Pairs
        -> Branch statistics
        -> Suggestions

The scheduler advances only when prerequisites for the current portrait generation are valid. The current chain is linear, but lifecycle management is product-based so future independent products do not require a monolithic `RefreshAnalysisAsync()` sequence.

`RefreshAnalysisAsync()` remains as an awaitable synchronization point. It no longer owns the analysis lifecycle; ordinary analysis scheduling is automatic.

## Portrait-operation path

Exclude/Delete/Move/Undo are serialized by `BerriesApplication`. A successful portrait mutation:

    rebuilds the Working Portrait
    increments PortraitGeneration
    makes existing derived products stale by generation mismatch
    requests cancellation of obsolete work
    schedules current-generation analysis

Analyzers run against captured Portrait/Group references for one generation. A later portrait mutation does not modify those captured objects. Cancellation avoids wasted work; correctness does not depend on prompt cancellation because a result can publish only when its generation is still current.

The GUI does not own a second background-analysis scheduler or wait for obsolete analysis to finish before starting a portrait operation. It observes progress/product publication and updates capabilities.

## Unique-file representation

Unique files participate concretely only through initial discovery. Once Groups have been established:

- unique files are counted per physical Directory;
- their `FileInstance`s are removed from the session Portrait;
- fixed `UniqueFileCount` statistics preserve their influence on Directory/Branch population measures;
- current files belonging to discovered Groups remain concrete and continue to respond to Exclude/Delete/Move/Undo;
- Group identity persists even when current membership reaches one or zero.

This preserves the empirically developed Seed denominator while reducing long-lived memory use and repeated Portrait traversal cost.

## Tests

Active tests cover Corpus/Portrait acquisition, Group discovery, Exclude, Directory analysis, Branch statistics, Branch priority, Session operations/Undo/Move, filesystem execution, the critical distinction between Seed rank and winning Branch Pair score, and generation-aware `AnalysisProduct<T>` publication/cancellation behavior.

## Pending analysis work

The lifecycle/scheduler foundation is now present. Important follow-on work includes:

- empirical validation of memory and traversal improvements from unique pruning;
- continued audit of scaling loops for cancellation/progress granularity under large real corpora;
- deciding whether any derived analyses can run independently/concurrently rather than through the current dependency chain;
- deciding the final scheduler wake-up strategy if the current event-triggered drain model proves insufficient;
- folding Groups, Directories, Directory Pairs, and Branch Pairs into a common Suggest sequence;
- culling Suggestions when diminishing returns make the ordinary Explorer/Groups view a better fishing ground.
