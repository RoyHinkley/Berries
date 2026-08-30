# Berries Analysis Design

This document describes the analysis Berries actually performs today, why that analysis has its present shape, and where the practical design deliberately differs from earlier exhaustive mathematical approaches.

User/domain vocabulary is defined in `MODEL.md`. Interaction and execution are described in `WORKFLOW.md`.

## Purpose

Analysis exists to help the user find duplication likely to reward attention. It does not own the workflow and does not prescribe a mandatory Case queue.

The stable interaction surface is the Duplicate Explorer. Analysis supplies:

    Groups of identical files
    direct Directory evidence
    Branch statistics
    targeted Branch-Pair suggestions
    on-demand structural counterparts

The governing heuristic remains practical rather than exhaustive:

    find something likely to reward attention

and, among comparable candidates:

    prefer a comprehensible question with substantial simplifying effect

## Initial discovery pipeline

The implemented initial discovery chain is intentionally linear because every step is required to establish the session.

### 1. Normalize Corpus roots

Selected roots are canonicalized and reduced to a disjoint set so no retained root is beneath another retained root.

### 2. Acquire the scan Portrait

Enumerate ordinary files beneath the roots and record platform-neutral metadata needed by Core.

`Berries.config` uses an `[exclude]` section. Matching paths are filtered during acquisition. Matching semantics are:

    pattern without a path separator
        match any path component / filename

    pattern containing / or \
        match a contiguous path segment in the normalized full path

    * and ?
        wildcard matching

    # or ; at line start
        comment

Configuration exclusion is operationally equivalent to beginning the session without those files in the working Corpus; no filesystem action is implied.

### 3. Group by length

Files whose length is unique cannot currently be duplicated and require no content hash. Equal-length files become hash candidates.

Unique files remain in the Portrait because they can constrain later operations such as Move destination collision checks.

### 4. Hash candidates

Current content identity uses SHA-256.

Expected read failures (`IOException`, `UnauthorizedAccessException`, `SecurityException`) evict inaccessible files from the established session rather than aborting the entire scan. Programming failures still propagate.

The Windows adapter opens content with permissive sharing (`ReadWrite | Delete`) where possible so ordinary files held open by other applications can still be hashed.

### 5. Establish Groups and the session

Equal hashes form internal `DuplicateSet` objects and user-facing Groups. Content identity is attached to the corresponding Portrait files and `BerriesSession` is created.

At this point Berries has the fixed Initial Portrait, current Working Portrait, Groups, persistent selection state, portrait-operation history, and physical Action list.

## Derived structural analysis

After session construction, current `ScanAsync()` performs three derived analysis stages sequentially.

### Directory analysis

For duplicate-bearing exact directories, Berries derives direct records including values such as:

    Path
    FileCount
    DuplicateFileCount
    DuplicateContentCount

Directory counts describe files directly in the Directory, not descendants.

A `DirectoryPair` represents two exact directories sharing duplicated content directly. `SharedContentCount` is the number of distinct shared Groups.

Directory Pairs are useful evidence and projections. They are not comprehensively promoted into a separate mandatory work queue.

### Branch statistics

`BranchStatisticsAnalyzer` derives first-class statistics for directory-rooted Branches without first enumerating Branch Pairs:

    Path
    ParentPath
    FileCount
    DirectoryCount
    DuplicateFileCount
    DuplicateContentCount
    DuplicateDirectoryCount

`DuplicateContentCount` is a distinct-content count across the entire Branch and must not be obtained by summing descendant Directory counts.

This stage is intentionally cheap enough to run on large real corpora.

### Branch seed priority

`BranchPriorityMetrics` scores a Branch relative to its parent using duplicate-content concentration rather than raw duplicate count alone.

The current preferred family of metric is based on:

    D = child distinct duplicated Content
    C = duplicated-Content retention / ordinary-file retention

with positive priority only when duplicate content is concentrated more strongly than ordinary files. The currently implemented score uses the bounded `D * (1 - 1/C)` form when `C > 1`.

This answers:

    where is it promising to look?

It is not itself a Branch-Pair quality score.

## Targeted Branch counterpart analysis

Earlier versions attempted broad or exhaustive ancestor-pair expansion. Real-corpus experiments demonstrated that this creates large combinatorial populations, including millions of candidate pairs, without proportional user benefit.

The current implementation therefore searches only promising Branch seeds.

`BranchCounterpartAnalyzer`:

1. ranks eligible Branch seeds;
2. considers a bounded top seed window;
3. finds strong non-nested counterpart Branches sharing Groups with each seed;
4. scores actual relationships using shared distinct duplicated content and overlap/Jaccard information;
5. chooses a compact set of candidates while culling already represented Branch families;
6. retains a few top counterparts per seed for diagnostics and UI use.

The current application calls this analyzer with a seed limit of 25 and counterpart limit of 5. These are implementation parameters, not semantic constants.

The important distinction is:

    seed score
        promising place to search

    counterpart/pair score
        promising relationship actually found

The best relationship need not originate from the highest-ranked seed.

## Suggest and on-demand counterpart search

The implemented `Suggest` command cycles through the compact analyzed Branch-Pair suggestions in `BerriesApplication.Counterparts`.

A selected Branch can also request its best current counterpart on demand through `FindBestBranchPairAsync()`. That search uses the established Branch statistics and current Groups without requiring exhaustive Branch-Pair enumeration.

Directory-Pair pivoting similarly selects the best direct Directory Pair for the chosen Directory from the current Directory analysis.

## Projections and analysis

`Berries.Projection` builds UI-independent views over the current `BerriesSession`:

    Groups
    Directory
    Branch
    Corpus Roots
    shared-Group counts for pair views

Directory-Pair and Branch-Pair views are composed from those projection/query services plus analyzed pair choices.

Projection construction operates on the current Working Portrait and current Groups; it does not require a separate persistent Case object.

## Portrait changes and invalidation

Exclude/Delete/Move/Undo can change Group membership and all higher structural evidence.

Current behavior is deliberately broad and simple:

    any portrait-operation history change
        -> DirectoryAnalysis = null
        -> BranchStatistics = null
        -> Counterparts = null

Known Content is not reread or rehashed merely because a virtual operation changes membership or location.

After a portrait command, the GUI:

1. cancels any previous background analysis refresh;
2. applies/rebuilds the Working Portrait;
3. refreshes the current visible projection immediately;
4. starts a cancellable background `RefreshAnalysisAsync()`;
5. republishes capability state when that generation finishes.

This prevents expensive structural analysis from blocking ordinary portrait editing after the session exists.

The current implementation does not yet maintain independent validity state per analysis product; invalidation is all-or-nothing for the three derived results.

## Initial-scan orchestration

Initial scan remains more monolithic than post-operation refresh.

`BerriesApplication.ScanAsync()` currently performs:

    normalize roots
    acquire Portrait
    discover Groups
    construct BerriesSession
    RefreshAnalysisAsync()
        directory analysis
        Branch statistics
        targeted counterparts
    return ScanResult

The GUI shows the Corpus view and progress while this runs, but does not switch to the normal Group projection until `ScanAsync()` completes.

This is current behavior, not a requirement that every future analysis remain sequential.

## Transitional `DuplicateSettlements`

The codebase still contains `DuplicateSettlements` and analyzer parameters from an earlier Accept/Settle design.

The current application does not expose settlement semantics. Each derived-analysis refresh creates a new empty `DuplicateSettlements` solely to satisfy those older analyzer signatures. No user operation adds accepted content or accepted pairs, so the object has no filtering effect in current application behavior.

The product model is therefore correctly described as having no user-facing settlement layer even though the transitional type remains in Core.

## Same-directory and file-centric duplication

Real-corpus experiments showed that widely repeated low-level content can manufacture large amounts of weak higher-level structure: generated files, template/support material, build artifacts, repository support files, saved-web assets, and similar content can produce many apparent Directory and Branch relationships.

That finding remains important, but the earlier special distributed-DuplicateSet settlement screen is no longer part of the current application.

Today such material is handled through ordinary Group/structural exploration and Exclude when the user decides it does not belong in the working Corpus.

Same-directory duplication also remains naturally visible through Group and Directory projections without requiring a special Case pipeline.

## Performance lessons retained

The development experiments established several durable principles:

- hash only equal-size candidates;
- first-class Branch statistics are much cheaper than exhaustive Branch-Pair construction;
- seed quality and pair quality are different measurements;
- targeted counterpart search is sufficient to surface meaningful real structures in large corpora;
- repeated low-level content can create misleading higher-level structural evidence;
- exact mathematical completeness is not the goal when it creates large combinatorial cost without improving user decisions;
- analysis should surface promising attention targets, not infer semantic truth autonomously.

These empirical findings take precedence over older planning documents that assumed comprehensive pair enumeration or Situation classification as central mechanics.
