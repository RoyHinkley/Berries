# Berries Situation Catalogue — Retained Semantic Research

This document preserves semantic research from earlier Berries design work. It is **not** the current required application workflow.

The present Explorer lets the user work directly with Groups, Directories, Branches, Directory Pairs, Branch Pairs, and the explicit operations Exclude/Delete/Move. It does not require the user to classify a Situation, choose a named Resolution, or compile a Disposition before acting.

The terminology below is retained because the research remains useful for understanding recurring real-world causes of duplication and may inform future suggestion/explanation features. Current domain and UI terminology is defined in `MODEL.md` and `WORKFLOW.md`.

## Historical vocabulary

### Situation

A human explanation for why a duplication pattern exists: backup, migration, reorganization, staging residue, and so forth.

Filesystem evidence can make a Situation plausible but generally cannot prove it. The user remains the semantic authority.

### Resolution

In the earlier research model, a natural user intention appropriate to a Situation, such as retiring a backup or completing a migration.

### Disposition

In the earlier research model, a fully parameterized operational realization of a Resolution.

The current Explorer no longer exposes this Situation -> Resolution -> Disposition chain. Where the user's intent is already clear, ordinary Exclude/Delete/Move expresses it directly.

## Governing research criterion

The most useful surviving criterion is:

    A useful Situation evokes one or more natural resolutions that can be
    mapped to fully specified operations/outcomes.

This remains a useful test for whether a semantic label is worth preserving, even though those labels are not required by the live UI.

## Unneeded copy

Meaning:

Multiple files contain identical content and one or more copies have no independent reason to remain.

Typical examples:

    accidental copy
    rename residue
    simple obsolete copy
    obsolete generated/exported output

Natural historical resolutions:

    retain one or more chosen copies
    leave all copies unchanged

Current Explorer expression:

    inspect the Group
    select expendable copies
    Delete or Exclude as appropriate

At larger structural scale, a more informative Situation often explains the duplication better.

## Move residue

Meaning:

Material formerly located under one Branch now belongs under another, but duplicate files remain at the former location.

Historical semantic roles:

    former location
    current location

Natural historical resolution:

    merge/retire the former location into the current location

Current Explorer expression:

    inspect an appropriate Branch Pair or Directory Pair
    establish the intended source/destination scopes
    Move in the intended direction

The current Move operation, rather than a separate Merge command, handles destination-authoritative duplicate collapse.

## Reorganization

Meaning:

Two Branches contain substantially overlapping Groups but their internal directory organization differs.

Historical semantic roles:

    former organization
    preferred/current organization

Natural historical resolution:

    move material from the former organization into explicitly chosen
    corresponding scopes in the preferred organization

Current Explorer expression:

A reorganization may require several explicit Move operations at different scopes. Berries does not infer that differently named directories correspond merely because they share content.

## Backup

Meaning:

One Branch intentionally duplicates material in another for recovery or preservation.

Historical semantic roles:

    primary
    backup

Natural historical outcomes:

    retain the backup
    retire the backup

Current Explorer implication:

Intentional duplication is legitimate. Berries should not assume that duplicated backup files are expendable. If the user chooses to retire material, Delete/Move operations express that decision and Execute summarizes any physical content loss.

## Migration

Meaning:

Content is being transferred from an old location or organization to a new one, but the migration is incomplete or the old side has continued to accumulate files.

Historical semantic roles:

    old/source
    new/destination

Natural historical resolution:

    complete the migration

Migration differs from simple Move residue because unique files on the old side may represent unfinished migration rather than expendable residue. The current duplicate-oriented Explorer does not perform general unique-file migration automatically.

## Snapshot

Meaning:

One Branch is an intentionally retained copy of another Branch, or of a prior state of it, created for temporary preservation.

Historical semantic roles:

    working/current
    snapshot

Natural historical outcomes:

    retire the snapshot
    retain it for now

The key semantic distinction from Backup is expectation: backup redundancy may be long-lived, while snapshot redundancy is often temporary.

## Archive

Meaning:

One Branch is an intentionally retained historical or preservation-oriented collection related to another working collection.

Historical semantic roles:

    working/current
    archive

Potential historical resolutions included updating the archive, removing already archived working copies, retiring duplicate archive copies, or deleting the archive.

Current boundary:

Berries can use duplicate evidence to support explicit Move/Delete decisions, but it is not a general archive synchronizer. Unique archive content is not an ordinary duplicate-resolution target and must not be silently removed.

## Staging / Import

Meaning:

A temporary collection contains files that may already have been incorporated elsewhere.

Examples:

    camera import directory
    phone import directory
    scanner output bucket
    temporary intake directory
    manually collected staging directory

Natural duplicate-resolution behavior:

    remove staging copies already represented in the retained collection

Current Explorer expression:

Use Group or structural views to identify those duplicates and Delete/Exclude them. Unique staging files remain outside ordinary duplicate cleanup.

## Downloads

Meaning:

A downloads/staging area contains files already represented elsewhere and the download copy may no longer be useful.

Natural duplicate-resolution behavior:

    remove download copies already retained elsewhere

Downloads remains semantically distinct from generic staging because a downloaded copy is commonly expendable once an identical file has deliberately been retained elsewhere.

## Mirror

Meaning:

Two Branches are intended to correspond, either symmetrically or with one authoritative side.

Historical resolutions included making one side match the other or synchronizing both sides.

Current boundary:

Berries is not presently a general synchronization tool. Duplicate evidence may still help the user identify redundant mirror copies, but unique-file propagation/deletion needed for full mirroring lies outside current duplicate-resolution scope.

## Rejected or subsumed candidates

These ideas are retained so earlier reasoning is not lost.

**Accidental copy** — subsumed by Unneeded copy.

**Rename residue** — subsumed by Unneeded copy.

**Download residue** — developed into Downloads.

**Generated output** — a real source of duplicate noise, but filesystem evidence alone does not prove generated copies are expendable. Persistent `[exclude]` configuration or interactive Exclude is often more appropriate than semantic inference.

**Template / shared resource** — not retained as a distinct Situation. Identical copies may be independently required.

**Intentional deployment** — not retained as a distinct Situation. Distributed identical copies can be required by application structure.

**Intentional copies** — better represented by specific semantic contexts such as Backup, Snapshot, Archive, and Mirror.

**Aggregation residue** — real but not retained as a separate Situation. Structural exploration may reduce it to several explicit Move/reorganization operations.

## Empirical lessons

The catalogue remains useful as a record of semantic possibilities, but current Berries development established several stronger product rules:

- duplicate identity is objective; the reason for duplication usually is not;
- the user should not be forced to classify a Situation before acting;
- practical structural analysis should suggest attention, not pretend to infer semantic truth;
- Exclude/Delete/Move are sufficient to express many intentions directly;
- application-specific artifact knowledge should not be hard-coded merely to manufacture semantic confidence;
- a Situation is worth retaining only when it corresponds to recognizable, useful user intentions.

Future semantic features should build on these lessons without reintroducing a mandatory classification workflow.
