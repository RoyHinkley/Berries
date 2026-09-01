# Berries Development Guide

This is the short implementation map for continuing development. Governing semantics are in `MODEL.md`; architecture in `ARCHITECTURE.md`; analysis in `ANALYSIS.md`; interaction and execution in `WORKFLOW.md`.

## Solution structure

    Berries.Core
        domain/session model, discovery, analysis, queries
        portrait operations and analysis scheduling
        filesystem-action planning/execution contracts

    Berries.Projection
        UI-independent Explorer projections and ProjectionState

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows implementation

    Berries.Gui
        Avalonia shell, navigation, ExplorerNode construction and binding

    Berries.Core.Tests
        synthetic platform-independent tests

Target framework: .NET 10.

Placement rule: **lowest reusable layer that naturally owns the work**. Cost does not determine ownership. See `ARCHITECTURE.md`.

## Principal runtime objects

### `BerriesEngine`

Normalizes the Corpus, acquires the Initial Portrait, discovers Groups, records/prunes initially unique files, and performs direct Directory analysis.

### `BerriesApplication`

Owns session orchestration, serialized portrait mutation, `PortraitGeneration`, and dependency-driven background analysis. Decision-facing derived products are exposed only when valid for the current generation.

### `AnalysisProduct<T>`

Tracks a derived result, its generation, any running generation, and cancellation. Generation equality determines validity; stale results cannot publish.

### `BerriesSession`

Owns `InitialPortrait`, `WorkingPortrait`, selection, ordered portrait operations, planned Actions, session-stable Groups, and fixed `UniqueFileCountsByDirectory`.

`Rebuild()` deterministically replays portrait operations. Group identity persists even when current membership falls to one or zero.

### `PortraitQueries`

Answers factual structural/model questions without UI dependencies.

### `ProjectionService`

Builds UI-independent presentation models. Ordinary Groups and Corpus Roots projections are cached by `WorkingPortrait`. Concurrent construction is serialized where necessary. Corpus Roots is prewarmed after the initial Groups view is published.

Selection-dependent projection work must not use a portrait-only cache.

### `ProjectionState`

Records current Explorer presentation/navigation state. It is not a Case and grants no disposition authority.

### `BranchStatisticsAnalyzer`

Computes Branch population and Group statistics. Total population is:

    FileCount = UniqueFileCount + GroupedFileCount

where unique counts are fixed from primary discovery and grouped counts reflect current members of session-stable Groups.

### `BranchPriorityMetrics`

Ranks promising Seeds using parent-relative Group concentration (`ExcessConcentratedGroups`). Seed priority answers where to search, not which Branch Pair is best.

### `BranchCounterpartAnalyzer`

For several good Seeds, finds strong non-nested Counterparts and chooses Suggestions by actual relationship score. The best pair often does not come from the highest-ranked Seed; preserve that distinction.

### `FileActionExecutor`

Executes physical Actions, continues independent safe work after failures, and performs Move -> Copy/Delete fallback when required.

## Runtime path

Primary session establishment:

    normalize Corpus
        -> acquire files
        -> size-group candidates
        -> hash candidates
        -> establish Groups / ContentIds
        -> record unique counts and prune unique FileInstances
        -> construct BerriesSession
        -> establish portrait generation
        -> schedule derived analysis
        -> publish Groups view
        -> prewarm Corpus Roots projection

Derived analysis:

    Working Portrait + Groups + retained unique counts
        -> Directory analysis / Directory Pairs
        -> Branch statistics
        -> Suggestions

A successful Exclude/Delete/Move/Undo rebuilds the Working Portrait, advances generation, makes older derived products stale, requests cancellation of obsolete work, and schedules the chain for the new generation.

## Explorer responsiveness

The GUI uses virtualized `TreeView` item panels. Do not remove virtualization from large Explorer trees: logical projections may contain many thousands of roots, and realizing all of them destroys responsiveness even when projection computation is cheap.

Groups GUI nodes are constructed/published incrementally in bounded batches and cached for the current Working Portrait. Navigation is generation-owned: only the newest request may publish visible state.

Timing instrumentation around navigation/projection phases is intentionally retained. It is low-cost diagnostic infrastructure and should normally remain available in debug output.

## Analysis mathematics

Seed concentration for child vs immediate parent:

    group retention = child GroupCount / parent GroupCount
    file retention  = child FileCount / parent FileCount
    C = group retention / file retention

    ExcessConcentratedGroups = child GroupCount * (1 - 1/C), C > 1
                               0,                              otherwise

Counterpart relationship:

    score = shared Group count * Jaccard overlap

Each Suggestion round examines the top 10 eligible Seeds, finds each Seed's best Counterpart, then chooses the strongest pair across that window. Seed priority is only a later tie-breaker. Chosen Seed/Counterpart families are blocked before the next round.

## Current invariants worth protecting

- Group identity is established once per session; operations change membership, not identity.
- Initially unique `FileInstance`s are pruned after their per-Directory counts are retained.
- Selection always denotes files and persists across projections.
- Projection is navigation, not Case/disposition authority.
- Exclude/Delete/Move change the Working Portrait immediately; there is no Apply state.
- No physical filesystem modification occurs before Execute.
- Analysis publication is generation-validated.
- Navigation publication is navigation-generation-validated.
- Scaling work is cancellable and reports meaningful progress.
- Large Explorer populations are virtualized.
- Cache keys must include every state dimension on which the result depends.

## Near-term work

Keep this section short and remove completed items rather than preserving a development diary.

- continue auditing navigation paths for stale-request races;
- continue real-corpus validation of Suggestion quality;
- decide whether Suggestions should eventually combine/cull Groups, Directories, Directory Pairs, and Branch Pairs;
- implement navigation history only when its desired semantics are clear;
- decide whether session persistence provides enough user value to justify Save/Load.