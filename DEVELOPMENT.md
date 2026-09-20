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
- Ordinary Selection denotes files and persists across ordinary projections. Directory Namesakes has separate projection-local Namesake/Directory selection and must not mutate `BerriesSession.Selection`.
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

`BerriesSelection` owns only the selection-derived Directory state. On every semantic selection change, determine the distinct containing Directories represented by selected files. This is intentionally a tiny analysis: walk selected files, collect distinct parent Directories, and stop as soon as a third distinct Directory is encountered. No filesystem traversal is required.

Expose the result as one immutable `SelectedDirectories` value owned by `BerriesSelection`. It should distinguish the four useful states without retaining more than two paths:

- `NoDir == true`, with `OneDir` and `DirPair` empty: selection is empty / represents zero Directories;
- `OneDir` contains a path: selection represents exactly one Directory;
- `DirPair` contains two paths: selection represents exactly two Directories;
- `NoDir == false`, with `OneDir` and `DirPair` empty: selection represents three or more Directories.

`OneDir` and `DirPair` may be nullable/empty. `BerriesSelection` should replace its `SelectedDirectories` value whenever selection-derived Directory state changes and raise a corresponding event only when the summary value actually changes. This gives contextual-search/navigation consumers a cheap, precise trigger without making them react to selection changes that leave Directory context unchanged.

The GUI combines `SelectedDirectories` with the current projection to make the final inferred-Directory decision:

1. If `OneDir` is present, infer that Directory. This includes one file or multiple files in the same Directory, regardless of which projection/node interactions produced the selection.
2. Otherwise, if `NoDir` is true and the current projection is a Directory or Branch view, infer that view's top-level Directory.
3. Otherwise there is no inferred Directory. Cancel work dependent on the previous inference and disable commands that require one.

If `DirPair` is present there is no single inferred Directory, but those two Directories may be viewed explicitly as a Directory Pair or Branch Pair. Three or more distinct containing Directories provide neither a single inferred Directory nor an explicit pair.

Commands using an inferred Directory should not offer a no-op projection. In particular, when the inferred Directory is already the top-level Directory of the current Directory view, disable the Directory pivot; Branch remains available, and Best Branch Pair becomes available when its contextual counterpart result has been found. Conversely, in a Branch view rooted at the inferred Directory, disable Branch while leaving Directory available.

## Present status — Directory Namesakes

Directory Namesakes has moved from structural research into a bounded production feature. The cheap projection groups recurring Directory leaf names and shows each concrete occurrence. The richer Namesake Structure and Namesake MinHash views remain research artifacts and are not prerequisites for the production feature.

The production Directory Namesakes view deliberately substitutes directory-selection semantics for ordinary file Selection:

- Namesake rows and occurrence rows have separate GUI-local selection state; `BerriesSession.Selection` is untouched.
- Explicit occurrence selection takes precedence over selected Namesakes when concrete Directories are required.
- Invert complements Namesakes when only Namesakes are selected; with occurrence selection it complements occurrences only within the affected Namesakes.
- Groups ignores Namesake selection and opens the default Groups view.
- Directory accepts one or more effective Directories; the multi-Directory implementation scans Groups once rather than once per selected Directory.
- Delete and Move are disabled in Directory Namesakes.
- Exclude resolves grouped files beneath all effective Directories in one pass and reuses the existing session Exclude operation.
- Permanent Exclude also appends equivalent rules to `Berries.config`. A Namesake is emitted as a directory-specific rule such as `/obj/`; a trailing separator now means the match must have a descendant component.
- Best Directory Pair / Best Branch Pair and Branch are currently disabled from Directory Namesakes because their existing commands have different seed semantics; do not silently reinterpret them.
- Suggest remains controlled solely by Suggestion availability.

Large-tree behavior remains virtualized. Expansion state is now stored on `ExplorerNode.IsExpanded` and bound two-way to `TreeViewItem.IsExpanded`; recyclable visual containers must not own logical expansion state.

The existing `SuggestionBox.TakeNext()` contract is cyclic. After all current Suggestions have been seen it starts again at the highest-ranked Suggestion; the stale unit test expecting `null` after exhaustion was updated.

## Near-term work

Keep this section concise, but preserve unresolved design decisions until they are settled.

1. **Finish direct user interaction in Directory Namesakes.** First verify the new node-owned expand/collapse behavior in both Groups and Directory Namesakes. Then diagnose and correct the reported Namesake selection/navigation problems before extending the feature.
2. **Exercise the specialized selection contract.** Test selection isolation from ordinary file Selection, Namesake-only selection, occurrence-only selection, mixed selection precedence, and all Invert cases.
3. **Exercise Exclude end to end.** Verify session Exclude/Undo, permanent Namesake rules such as `/obj/`, path-specific occurrence rules, no-grouped-file cases, and the distinction between `/obj/` and separator-free `obj` on a fresh scan.
4. **Exercise multi-Directory Pivot and scale.** Verify one root per selected Directory, ordinary file semantics after the Pivot, and bounded behavior with high-occurrence Namesakes such as `src`. Preserve the single-pass implementations; avoid Directory-count × Groups/files algorithms.
5. **Resolve remaining Pivot semantics deliberately.** Decide whether explicit two-Directory selection should acquire direct Directory Pair / Branch Pair navigation and whether Branch/Roots have useful meanings from Namesake selection. Existing “Best Pair” commands must not be repurposed accidentally.
6. Contextual counterpart search and ordinary pair construction from `BerriesSelection.SelectedDirectories` remain separate ordinary-selection work.
7. Projection titles should include useful numerical context; decide counts/metrics systematically.
8. Consider whether cheap Directory Namesake evidence should eventually feed Suggestions. Do not require MinHash or structural-coherence research, and do not infer exclusion intent.
9. Add an explicit acquisition setting for excluding zero-length files.
10. Continue real-corpus validation of heterogeneous Suggestion quality and design a common comparison metric based on decision leverage.
11. Revisit Back/Forward only after navigation semantics demonstrate enough value; continue auditing stale-request races.
12. Decide whether session persistence provides enough user value to justify Save/Load.
