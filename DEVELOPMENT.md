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

For several good Seeds, finds strong non-nested Counterparts and emits promising Branch Pair candidates. Its Seed/Counterpart search is analysis machinery; Suggestion ordering is owned separately.

### `SuggestionBox`

Accepts analyzer-independent Suggestion candidates, owns comparison/ranking policy, deduplication, seen/current state, and highest-ranked-unseen dispensing for the current portrait generation. Future analyzers may submit concurrently without knowing about one another.

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
        -> analyzer candidates
        -> SuggestionBox

A successful Exclude/Delete/Move/Undo rebuilds the Working Portrait, advances generation, makes older derived products stale, requests cancellation of obsolete work, and schedules the chain for the new generation.

## Explorer responsiveness

The GUI uses virtualized `TreeView` item panels. Do not remove virtualization from large Explorer trees: logical projections may contain many thousands of roots, and realizing all of them destroys responsiveness even when projection computation is cheap.

Groups GUI nodes are constructed/published incrementally in bounded batches and cached for the current Working Portrait. Navigation is generation-owned: only the newest request may publish visible state.

Timing instrumentation around navigation/projection phases is intentionally retained. It is low-cost diagnostic infrastructure and should normally remain available in debug output.

## Analysis mathematics

Seed concentration for child vs immediate parent:

    group retention = child GroupCount / parent.GroupCount
    file retention  = child FileCount / parent.FileCount
    C = group retention / file retention

    ExcessConcentratedGroups = child GroupCount * (1 - 1/C), C > 1
                               0,                              otherwise

Counterpart relationship:

    score = shared Group count * Jaccard overlap

Each Branch Pair search round examines the top 10 eligible Seeds, finds each Seed's best Counterpart, then chooses the strongest pair across that window. Seed priority is only a later tie-breaker. Chosen Seed/Counterpart families are blocked before the next round. SuggestionBox currently reproduces the Branch Pair presentation ordering independently; heterogeneous comparison remains to be designed.

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

## Inferred Directory

Some context-sensitive pivots/searches need one Directory as their Seed. Treat this as a general **inferred Directory** operation rather than embedding different inference rules in each command.

`BerriesSelection` owns only the selection-derived Directory state. On every semantic selection change, determine the distinct containing Directories represented by selected files. This is intentionally a tiny analysis: walk selected files, collect distinct parent Directories, and stop as soon as a third distinct Directory is encountered. No filesystem traversal is required. Expose this result directly so all consumers use the same interpretation of selection.

The GUI combines that selection-derived state with the current projection to make the final inferred-Directory decision:

1. If the selection represents files from exactly one distinct containing Directory, infer that Directory. This includes one file or multiple files in the same Directory, regardless of which projection/node interactions produced the selection.
2. Otherwise, if the selection is empty and the current projection is a Directory or Branch view, infer that view's top-level Directory.
3. Otherwise there is no inferred Directory. Cancel work dependent on the previous inference and disable commands that require one.

The same `BerriesSelection` analysis also detects the exactly-two-Directory case. If selected files belong to exactly two distinct containing Directories, there is no single inferred Directory, but those two Directories may be viewed explicitly as a Directory Pair or Branch Pair. Three or more distinct containing Directories provide neither a single inferred Directory nor an explicit pair.

Commands using an inferred Directory should not offer a no-op projection. In particular, when the inferred Directory is already the top-level Directory of the current Directory view, disable the Directory pivot; Branch remains available, and Best Branch Pair becomes available when its contextual counterpart result has been found. Conversely, in a Branch view rooted at the inferred Directory, disable Branch while leaving Directory available.

## Near-term work

Keep this section concise, but preserve unresolved design decisions until they are settled.

- Contextual counterpart search: infer a current Directory Seed using the contract above. Start low-priority Core searches opportunistically; cancel immediately when the inferred Directory changes or disappears. Reuse the Branch Counterpart search machinery rather than maintaining a second Branch Pair algorithm. Slice long work finely enough for effectively immediate cancellation. Enable Best Directory Pair / Best Branch Pair only after a current-seed result exists and the resulting pair differs from the current view.
- Pair construction from selection: when `BerriesSelection` reports exactly two distinct containing Directories, allow viewing them as either a Directory Pair or Branch Pair.
- Projection titles should include useful numerical context; decide the appropriate counts/metrics for every projection type rather than adding ad-hoc title data.
- Add a Suggestion analyzer for repeated Directory names. Develop what constitutes a useful same-name Directory Case, including occurrence count, duplicate contribution, likely Exclude disposition, ranking, and possible persistent exclusion.
- Persistent exclusions: retain simple path-pattern configuration. Consider an unobtrusive `Exclude always` path that writes an expressible exclusion rule for the user; ordinary Exclude should remain simple. README must prominently explain that configuration exists, why early exclusions matter, and give advisable syntax examples such as `/LICENSE` for any file or Directory named `LICENSE`.
- Add an explicit acquisition setting for excluding zero-length files; empty content is a compelling exception to pathname-only exclusion because all empty files collapse into one analytically uninformative Group.
- Toolbar/action organization: separate Invert from disposition buttons; group Move with Exclude/Delete. Keep Undo conceptually separate pending final layout.
- Continue real-corpus validation of heterogeneous Suggestion quality and design a common comparison metric based on decision leverage rather than producer-supplied scores.
- Revisit whether Suggest and Pivot remain separate navigation concepts once viewed-case Back/Forward semantics are clear. Back/Forward stays deferred until its desired behavior has demonstrated utility.
- Continue auditing navigation paths for stale-request races.
- Decide whether session persistence provides enough user value to justify Save/Load.