# Berries Analysis Design

This document defines how Berries discovers duplicate Content, derives structural evidence, and identifies promising foci for the Duplicate Explorer. Terminology and semantic invariants are defined in `MODEL.md`; interaction and execution are defined in `WORKFLOW.md`.

## Purpose of analysis

Analysis exists to help the user find duplication likely to reward attention. It does not own the workflow and does not prescribe a mandatory Case queue.

The Duplicate Explorer is the stable interaction surface. Analysis progressively supplies:

    duplicate Content and FileInstances
    directory/branch structure
    structural evidence
    candidate projections/foci
    Suggested Cases

The governing presentation idea remains:

    identify something likely to reward attention

and, when choosing among comparable candidates:

    prefer the smallest comprehensible question with substantial downstream
    simplifying effect

## Generate the Initial Portrait

Allow selection of filesystem directories. Selected directories become Corpus roots.

Normalize roots by canonicalizing paths, removing exact duplicates, and discarding selected roots contained by another selected root.

Scan regular files beneath the roots and record path plus required platform-neutral metadata. The Initial Portrait should retain unique files as well as duplicate candidates. Unique files are not resolution targets, but they can constrain operations such as Move by occupying destination paths.

Unsupported symbolic/special filesystem objects are handled by the platform adapter and are not exposed to Core as ordinary Files.

### Configuration exclusion

`Berries.config` uses an `[exclude]` section. Exclusion is subtraction from the logical Corpus, not a special duplicate-resolution concept.

Current useful matching semantics remain:

    pattern without path separator
        match any path component / filename

    pattern containing / or \
        match a contiguous path segment anywhere in the full path

    * and ?
        wildcard matching

    # or ; at line start
        comment

Configuration exclusions are semantically automatic initial Exclude operations. The implementation may omit/filter matching instances during acquisition to avoid unnecessary downstream work.

The earlier `[ignore]` name is obsolete and need not be retained for compatibility while the format is still under development.

## Enumerate DuplicateSets

Group FileInstances by length. Singleton size groups need no hashing for duplicate detection but remain known to the Initial Portrait.

Hash only non-singleton size groups. The current implementation uses SHA-256. Partition equal hashes into DuplicateSets; singleton hash groups remain unique files.

If a File becomes unavailable while hashing, expected access failures (`IOException`, `UnauthorizedAccessException`, `SecurityException`) can evict it from the working session. Programming failures must propagate normally.

Windows Content reads currently request permissive sharing (`ReadWrite | Delete`) so files open by other applications can often still be hashed.

## No settlement layer

The earlier `DuplicateSettlements` model is no longer part of the intended design.

If FileInstances should not participate in Berries, Exclude removes them from the Working Portrait. Therefore downstream duplicate and structural analysis simply operates on the Working Portrait.

A Content with fewer than two remaining Working-Portrait instances naturally ceases to be a DuplicateSet for duplicate-oriented presentation.

This replaces whole-Content and pairwise settlement bookkeeping with an ordinary portrait transformation.

## File-centric analysis

A Content/DuplicateSet projection is a first-class way to understand widely distributed duplication.

Real-corpus experiments showed that repeated support/template/generated Contents can manufacture large amounts of semantically weak directory/branch evidence. Examples included repository hook samples, DLL/PDB groups, UUID-like generated artifacts, and saved-web-page support files.

The former special early "sprinkled DuplicateSet" checklist was useful experimentally but should not survive as a separate screen. Its lesson is retained: file-centric duplication often deserves attention before container-centric evidence built from it.

The previous cheap phenotype remains potentially useful as a Suggested-Case heuristic:

    same filename for every instance
    one instance per directory
    at least three represented directories

But such candidates should be presented through the ordinary Explorer/Content projection, where Exclude and other normal operations are available.

## Directory records and DirectoryPairs

Directory statistics describe directly contained files only; descendants are not folded into local counts.

Useful values include:

    Path
    FileCount
    DuplicateFileCount
    DuplicateContentCount

DirectoryPair describes two exact directories sharing one or more duplicated Contents directly. `SharedContentCount` counts distinct shared Contents.

DirectoryPairs remain useful derived evidence and diagnostics. They are not required as an independently generated Case population and are not required to discover targeted BranchPairs.

Useful graph diagnostics discovered during experiments include degree, weighted degree, strongest-edge concentration, connected components, density, directional coverage, and Jaccard overlap. Retain these where cheap/useful; they are evidence, not semantic classification.

Same-directory duplicates remain a distinct useful viewpoint because no DirectoryPair is needed to express them.

## Branch records

`BranchStatisticsAnalyzer` derives first-class statistics for duplicate-bearing branches without enumerating BranchPairs:

    Path
    ParentPath
    FileCount
    DirectoryCount
    DuplicateFileCount
    DuplicateContentCount
    DuplicateDirectoryCount

`DuplicateContentCount` is a distinct-Content count across the branch and must not be obtained by summing descendant directory counts.

These values are cheap enough to compute on large corpora and proved useful for finding structurally concentrated duplication.

## Branch seed priority

For a child Branch relative to its immediate parent, define:

    D = child DuplicateContentCount

    C = (child duplicated-Content retention)
        / (child ordinary file retention)

The current successful seed score is:

    D * (1 - 1/C)       when C > 1
    0                   otherwise

This can be interpreted as duplicated Content concentrated beyond what would be expected from the Branch's share of parent files. It is bounded above by D and intentionally becomes zero at C = 1.

Real-corpus tests repeatedly promoted known meaningful structures, including moved/copy trees such as LTSpiceXVII and recognizable broad organizational problems.

Seed score answers only:

    where is it promising to look?

It is not a BranchPair score.

## Targeted BranchPair counterpart discovery

Comprehensive BranchPair enumeration is suspended and is not required by the intended architecture. Exhaustive ancestor Cartesian expansion produced combinatorial populations (including tens of millions of pairs on large corpora) without proportional user value.

Instead, use promising Branch seeds and search directly for counterparts sharing duplicated Content.

Current experimental algorithm:

1. Rank eligible Branch seeds by `D * (1 - 1/C)`.
2. Consider the top 10 eligible seeds in a selection round.
3. For each seed, find its best non-nested counterpart from unresolved/working DuplicateSet Content overlap.
4. Rank the seed/counterpart relationship by:

       shared distinct Content * Jaccard overlap

5. Select the highest pair score among the top-10 seed window.
6. Cull/block both selected branch roots and their descendants from later seed selection.
7. Repeat to obtain a compact shortlist.

The best pair can originate from seed rank 2, 6, or another position rather than rank 1. Therefore seed score and pair score must remain conceptually separate:

    seed score
        promising place to search

    pair score
        promising relationship actually found

For diagnostics, retaining the top few counterpart candidates per seed is useful.

### Boundary precision

Branch-level shared/Jaccard scoring can occasionally prefer a slightly broader parent over a child that feels like the more natural semantic boundary when the two contain almost identical duplicate Content. This is mathematically legitimate and may yield the same eventual disposition.

Do not add boundary heuristics merely to perfect such examples unless Explorer use demonstrates that the distinction affects user resolution.

Direct `DirectoryPair.SharedContentCount` between exact candidate roots is a cheap diagnostic but experiments showed it is not a strong enough discriminator to drive BranchPair ranking.

## Suggested Cases / foci

A Case is best treated as a suggested Explorer focus rather than a persistent object in a global list.

The useful viewpoints currently are:

    Content-centric
        one duplicated Content and its instances

    same-directory
        internal duplicate instances in one directory

    BranchPair/container-centric
        duplicated Content distributed between two branches

DirectoryPair is naturally a narrow BranchPair projection.

`Suggest Case` should become available when enough derived analysis exists to produce a useful suggestion. It need not wait for every possible analysis stage to complete.

A suggestion establishes a focus and projection. The user can Pivot or navigate away; Berries does not force the user to resolve the suggested Case.

## Projection data

### Content projection

Organize as:

    Content
        full-path FileInstance
        full-path FileInstance
        ...

Do not collapse leaves to directories. Multiple same-Content instances can exist in one directory, and every selectable leaf must retain exact FileInstance semantics.

### Pair projections

DirectoryPair and BranchPair projections use two equivalent tree panes. Higher directory nodes represent the set of applicable duplicate FileInstances beneath them.

Unique files are generally not displayed in these duplicate-resolution trees, but remain known to the session for operational constraints such as Move collisions.

## Derived analysis after portrait operations

Exclude/Delete/Move immediately transform the Working Portrait. Derived duplicate and structural analysis can therefore become stale.

Invalidate/recompute affected data such as:

    DuplicateSet membership
    Directory records and DirectoryPairs
    Branch records and seed scores
    targeted counterpart candidates
    Suggested Cases

Known Content does not need to be reread or rehashed merely because a virtual operation changes location or membership.

Correct incremental invalidation is desirable, but broad recomputation from in-memory session data is acceptable initially if performance is good enough.

The longer-term architecture should permit heavy analysis to run asynchronously. Background completion should update capability/availability (for example enabling Suggest Case) without taking control of the Explorer or unexpectedly changing the user's current focus.

## Scan and progress

The next UI should avoid a monolithic blocking "Scan then show results" architecture even if early implementation still invokes stages sequentially.

The Explorer can begin with the selected corpus roots while analysis proceeds. A persistent status bar should report the active stage and progress within that stage.

Natural stages include:

    filesystem enumeration / portrait acquisition
    size grouping
    hashing candidate files
    DuplicateSet construction
    directory analysis
    Branch statistics
    suggestion/counterpart analysis

Exact progress granularity is an implementation matter, but stage boundaries should remain explicit so later asynchronous orchestration does not require redesigning the analysis engine.

## Empirical lessons retained

Real-corpus work has established several practical principles:

- low-level repeated Content can generate misleadingly large higher-level structure;
- file-centric and container-centric questions are complementary, not competing universal Case types;
- Branch statistics are cheap and useful before any BranchPair construction;
- targeted counterpart search is dramatically cheaper and often more useful than exhaustive BranchPair enumeration;
- seed quality and pair quality are different measurements;
- broad structural boundaries need not be mathematically perfected if the same useful resolution remains available;
- application-managed/generated/repository trees can create substantial duplication evidence, which is one reason configurable Corpus exclusion is useful;
- the program should discover promising attention targets, not attempt to infer semantic truth autonomously.

These findings should guide implementation without hard-coding application-specific special cases.
