# Berries Semantic Research

This document retains useful human interpretations discovered during early research. It is not a runtime classification scheme and does not define required workflow.

The current Explorer allows the user to Pivot, select files, and apply Exclude/Delete/Move directly. These scenarios remain valuable because they describe recognizable filesystem histories and the natural actions those histories often suggest.

## Research rule

A useful semantic scenario should correspond to one or more natural, fully specified user actions or outcomes. If a label does not help the user decide what to do, it is not useful merely because Berries can detect some structural phenotype.

Filesystem evidence can make a scenario plausible; it generally cannot prove intent. The user remains the authority.

## Unneeded copies

Meaning:

Multiple files have identical content and one or more copies have no independent reason to remain.

Most naturally observed in:

    Group

Natural actions:

    select unwanted copies -> Delete
    remove irrelevant material from Berries -> Exclude
    leave all copies unchanged

Examples:

    accidental copy
    renamed residue
    obsolete generated/exported output

## Move residue

Meaning:

Material formerly located under one Branch now belongs under another, but old copies remain.

Most naturally observed in:

    Directory Pair
    Branch Pair

Natural action:

    select old-side files -> Move toward current side

The explicit source/destination scopes define correspondence. Berries need not infer an abstract directory mapping beforehand.

## Reorganization

Meaning:

Two Branches contain substantially overlapping Groups, but their internal directory organizations differ.

Most naturally observed in:

    Branch Pair

Natural action:

Use a sequence of explicit Moves at useful boundaries. For example:

    OldPhotos\Trips -> Photos\Travel
    OldPhotos       -> Photos

The first operation handles the renamed/reorganized subtree; the second handles the remainder.

## Backup

Meaning:

One tree intentionally duplicates material in another for recovery or preservation.

Most naturally observed in:

    Directory Pair
    Branch Pair

Natural outcomes/actions:

    retain both sides
    retire selected backup copies
    Exclude the backup from Berries consideration

Unique material in a backup can be important. The current model does not treat unique files as ordinary resolution targets.

## Migration

Meaning:

Material is being transferred from an old location or organization to a new one, but the transfer is incomplete or the old location continued to accumulate files.

Most naturally observed in:

    Branch Pair

Natural action:

Move selected old-side grouped files toward the new side while preserving explicit source-relative structure.

Unique files can indicate unfinished migration, but general unique-file migration is outside the current duplicate-oriented scope.

## Snapshot

Meaning:

A tree is an intentionally retained copy of another tree, or of an earlier state of it, for temporary preservation.

Natural outcomes/actions:

    keep the snapshot
    retire grouped snapshot copies
    Exclude the snapshot from current analysis

The semantic difference from a backup is human intent and expected lifetime, not byte identity.

## Archive

Meaning:

A tree is an intentionally retained historical or preservation-oriented collection related to current material.

Natural outcomes/actions vary:

    retain archive duplication
    remove current copies already represented in the archive
    remove archive copies represented elsewhere
    Exclude the archive from current Berries work

Archive-only files may be especially important. Berries' current duplicate-oriented operations do not imply that unique archive content is expendable.

## Staging / import area

Meaning:

A temporary collection contains files that may already have been incorporated elsewhere.

Examples:

    camera import directory
    phone import directory
    scanner output bucket
    temporary intake directory

Natural action:

Delete staging-side files already represented in the intended retained location. Unique staging files remain untouched by ordinary Group operations.

## Downloads

Meaning:

A download area contains files that have already been deliberately retained elsewhere.

Natural actions:

    delete selected download-side copies
    Exclude persistent download-support material from Berries

Downloads remains a useful human scenario because downloaded copies are often intentionally expendable after filing, even though the underlying Group evidence alone cannot prove that.

## Mirror

Meaning:

Two locations are intentionally expected to contain corresponding content, possibly directionally.

Natural outcomes/actions can include:

    retain the mirror
    repair selected duplicate-side residue with explicit Move/Delete operations
    Exclude one side from Berries when duplication is intentionally maintained

Full synchronization of unique files in both directions would be a broader filesystem-management feature and is outside the present scope.

## Other observations retained

### Generated output

Generated/build material can create highly repeated Groups and large amounts of structural noise. This is often better handled by configuration `[exclude]` than by teaching Berries application-specific semantics.

### Template/shared resource

Identical resource copies may be independently required. Repetition is evidence, not permission to delete.

### Intentional deployment

Distributed identical copies may be required by an application or deployment structure. Again, Group identity alone does not establish expendability.

### Aggregation residue

A broad collection may contain material copied from several source locations. Depending on the boundaries visible in the Explorer, this can often be handled as a sequence of ordinary Move or Delete operations rather than requiring a separate product concept.

## Empirical direction

These scenarios should continue to influence testing, examples, and future Suggest heuristics only when they produce demonstrably useful user choices.

Berries should not reconstruct a mandatory semantic-classification layer unless real Explorer use shows a concrete need that direct projections and operations cannot satisfy.
