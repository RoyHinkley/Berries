# Berries Analysis Design

Analysis exists to reduce the user's effort in resolving duplication. It finds promising structure; it does not prescribe a mandatory workflow or infer semantic intent.

Terminology and invariants are in `MODEL.md`; runtime rules are in `ARCHITECTURE.md`; interaction is in `WORKFLOW.md`.

## Primary discovery

Selected roots are normalized to a minimal disjoint Corpus. Berries enumerates accessible regular files, applying configured exclusions, then:

1. groups files by length;
2. hashes only files in non-singleton size groups with SHA-256;
3. establishes `ContentId`s and Groups for identities initially having at least two files;
4. counts files belonging to no Group by physical Directory;
5. prunes those unique `FileInstance`s before constructing the session Portrait.

Group identity is established once for the session. Later portrait operations change membership but do not rediscover Groups. A Group may therefore have two or more, one, or zero current members.

Expected read failures evict the affected file. Programming failures propagate.

Countable discovery phases report completed/total progress and check cancellation inside scaling loops. Filesystem enumeration remains indeterminate because its total is not cheaply known beforehand.

## Directory analysis

A `DirectoryRecord` describes one exact Directory:

    UniqueFileCount
    GroupedFileCount
    GroupCount
    FileCount = UniqueFileCount + GroupedFileCount

`UniqueFileCount` is fixed from primary discovery. `GroupedFileCount` reflects current files belonging to session-stable Groups, including a Group's sole remaining member.

A `DirectoryPair` is two exact Directories currently sharing one or more Groups directly. `SharedGroupCount` is the number of distinct such Groups.

## Branch statistics

A Branch is a Directory plus all descendants. `BranchStatisticsAnalyzer` derives:

    Path
    ParentPath
    UniqueFileCount
    DirectoryCount
    GroupedFileCount
    GroupCount
    GroupedDirectoryCount
    FileCount = UniqueFileCount + GroupedFileCount

Unique counts come from the retained fixed per-Directory population; grouped counts come from current Group membership.

These statistics are deliberately cheaper than constructing every possible Branch Pair.

## Seed priority

For a child Branch relative to its immediate parent:

    D = child GroupCount
    group retention = child GroupCount / parent GroupCount
    file retention  = child FileCount / parent FileCount
    C = group retention / file retention

    ExcessConcentratedGroups = D * (1 - 1/C), C > 1
                               0,              otherwise

This answers only:

    where is it worth looking for a strong relationship?

Seed rank is not final Suggestion order.

## Targeted Counterpart search

Current algorithm:

1. rank eligible Seeds by `ExcessConcentratedGroups`;
2. inspect the top 10 unblocked Seeds in each selection round;
3. for each Seed, find eligible non-nested Counterparts sharing Groups;
4. compute shared Group count, Seed coverage, Counterpart coverage, and Jaccard overlap;
5. score the relationship as:

       score = shared Group count * Jaccard

6. keep the strongest few Counterparts for each Seed;
7. compare the best relationship from every Seed in the window;
8. select the strongest Branch Pair as the next Suggestion;
9. use Seed priority only as a later tie-breaker;
10. block the chosen Seed and Counterpart families and repeat.

The best Branch Pair often does not come from the highest-ranked Seed. This distinction is intentional and test-protected.

Targeted search is part of the product design, not merely a performance shortcut: the useful question is normally local and comprehensible, while exhaustive Branch-Pair enumeration produces large populations of weak or redundant relationships.

## Directory Namesake structure experiment

A Directory Namesake is a recurring Directory leaf name. Name equality alone is weak evidence: conventional names such as `src`, `include`, `lib`, or `images` may occur hundreds of times without implying that their Directories are related.

The stronger structural signal under investigation is concentration of several distinct Namesakes beneath the same collection of Branches. The current experimental analyzer:

1. reconstructs the Directory ancestry implied by the scanned files;
2. retains only Directory names occurring more than once in the Corpus;
3. represents each Branch by the set of Namesake names beneath it;
4. chooses a small number of the globally rarest Namesakes in each Branch as anchors;
5. uses pairs of those anchors as an inverted index into candidate Branch collections rather than enumerating all Branch pairs;
6. reduces each anchor bucket to its deepest non-nested containers;
7. retains collections sharing several Namesakes and ranks them using rarity-weighted shared evidence.

This is deliberately an inspectable research view before becoming a Suggestion source. The objective question is whether the resulting collections are repeatedly recognizable as meaningful related structures across varied Corpora.

MinHash / locality-sensitive hashing remains a separate candidate-generation technique worth preserving. It can cheaply approximate similarity between large feature sets and may replace or complement rare-anchor indexing here, or apply elsewhere to sets of names, relative paths, Group identities, or other structural features.

## Suggestions

Current Suggestions are Branch Pair views. A Suggestion is a promising place to begin looking, not necessarily the final Case boundary. Pair breadcrumbs and Pivot let the user adjust either scope until the relationship is useful and recognizable.

The user is the semantic authority. Repeated identical content can indicate backup, migration, reorganization, staging, intentional deployment, generated output, or many other histories. Analysis may expose the evidence but must not treat byte identity as permission to delete.

## On-demand Branch Pair

When the user requests the best Branch Pair for an explicitly selected Branch, Berries searches non-nested Branches using the same overlap evidence. The selected Branch is simply one side of that request; it is not thereby a Seed in the Suggestion algorithm.

## Dependency-driven analysis

Primary scan returns once Groups, retained unique counts, and the session exist. Derived analysis then runs in the background:

    Working Portrait + Groups + retained unique counts
        -> Directory analysis / Directory Pairs
        -> Branch statistics
        -> Suggestions

Each derived product is generation-bound. A successful Exclude/Delete/Move/Undo advances `PortraitGeneration`, requests cancellation of obsolete analysis, and schedules current-generation work. Stale results cannot publish even if cancellation is delayed.

Known `ContentId`s are not reread or rehashed after virtual portrait operations.

## Analysis invariants

- Content identity is exact; semantic intent is not inferred from it.
- Group identity is session-stable.
- Unique-file population affects structural statistics without retaining unique `FileInstance`s in the session Portrait.
- Group and structural views are complementary.
- Widely repeated low-level Groups can create weak higher-level relationships; raw relationship count alone is not quality.
- Seed priority and Branch Pair quality are distinct measurements.
- Several Seeds are compared before selecting a Suggestion.
- Suggestions are advisory and scope-adjustable.
- Re-analysis operates on the current Working Portrait rather than preserving a global plan for the original one.
- Scaling loops are cancellable and report meaningful progress.