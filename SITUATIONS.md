# Berries Situation Catalogue

This document defines the user-facing Situation catalogue and its relationship to Cases, Resolutions, and Dispositions. Core terminology and invariants are in `MODEL.md`.

## Situation rules

A Situation is the asserted semantic context of a Case. It explains the Case's defining duplication pattern; it is not required for Case discovery and need not be provable from filesystem evidence.

Berries may use objective Case characteristics to constrain or rank the Situations it offers. It should avoid presenting Situations that could not plausibly have produced the Case at hand.

Once the user identifies a Situation, Berries treats that Situation as the operative semantic context for proposing Resolutions. It does not compete with the user's assertion by presenting unrelated Resolutions merely because another explanation is conceivable.

A useful Situation evokes one or more natural Resolutions that can be mapped to fully parameterized Dispositions.

A Resolution must address the duplication pattern that caused the Case to exist. It may legitimately leave unrelated internal duplication untouched for other Cases.

Situation identification is optional. A user may work directly from applicable Resolutions when that is sufficient.

The catalogue is pragmatic rather than mathematically complete. Situations may be added, merged, split, reordered, or refined as real-case experience reveals useful user concepts.

## Unneeded copy

Situation text:

    It looks like these are unneeded copies of the same content.

Meaning:

Multiple instances contain identical Content and one or more instances have no independent reason to remain.

Applicable Case types:

    DuplicateSet Case only

Natural Resolutions:

    Keep this instance.
    Keep these instances.
    Keep all as-is.

Disposition information:

Selected surviving instance(s). Other Case instances are implicitly removable.

Examples subsumed here:

    accidental copy
    rename residue
    simple obsolete copy
    obsolete generated/exported output

At structural scale, more informative Situations normally apply.

## Move residue

Situation text:

    It looks like D has moved to C, but duplicate files remain at D.

Meaning:

Material formerly located under one branch now belongs under another, but duplicate instances remain at the former location.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Former location = D
    Current location = C

Natural Resolutions:

    Merge the former location into the current location.
    Leave the relationship unchanged.

The reverse orientation should also be available when plausible. Approved directory mappings supply detailed Disposition parameters.

## Reorganization

Situation text:

    It looks like D has been reorganized under C.

or:

    It looks like the material under D is now organized differently under C.

Meaning:

Two branches represent substantially overlapping Content, but their internal directory organizations differ.

Applicable Case types:

    Primarily BranchPair
    A simple DirectoryPair may reduce to Move residue

Semantic roles:

    Former organization
    Preferred/current organization

Natural Resolutions:

    Merge the former organization into the preferred organization using
    proposed mappings.
    Leave the organizations as they are.

The reverse orientation should also be available.

Observed descendant DirectoryPairs can suggest non-identity mappings such as:

    OldPhotos\Family -> Photos\Family
    OldPhotos\Trips  -> Photos\Travel

Move residue and Reorganization remain separate because "I moved this" and "I reorganized this" are natural and meaningfully different semantic contexts.

## Backup

Situation text:

    It looks like E is a backup of D.

Meaning:

E intentionally duplicates material in D for recovery or preservation.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Primary = D
    Backup = E

Natural Resolutions:

    Keep the backup.
    Retire the backup.

If the backup is retired, unique Content in it may intentionally be lost. Such loss must be highlighted before Execute.

The reverse orientation may apply. Backup is intentional duplication, so preserving duplication is a valid completed outcome.

## Migration

Situation text:

    It looks like D is being migrated to C.

Meaning:

Content is being transferred from an old location or organization to a new one, but the migration is incomplete or the old location has continued to accumulate Content.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Old/source = D
    New/destination = C

Natural Resolutions:

    Complete the migration from D to C.
    Leave the migration incomplete for now.

The reverse orientation may apply.

Migration differs from Move residue because unique Files remaining on the old side may represent unfinished migration rather than expendable residue. Migration may overlap structurally with Reorganization while evoking different natural Resolutions.

## Snapshot

Situation text:

    It looks like E is a temporary snapshot of D.

Meaning:

E is an intentionally retained copy of D, or of some prior state of D, created for temporary preservation rather than permanent backup.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Working/current = D
    Snapshot = E

Natural Resolutions, suggested order:

    Retire the snapshot.
    Keep the snapshot for now.

An alternate interpretation is that the snapshot represents the desired state and the working tree represents a temporary excursion; this can lead to a rollback-like Resolution.

Backup and Snapshot may produce similar Dispositions but differ semantically:

    Backup
        Redundancy is expected to persist.

    Snapshot
        Redundancy is expected to be temporary.

## Archive

Situation text:

    It looks like E is an archive of D.

Meaning:

E is an intentionally retained historical or preservation-oriented collection related to D. Duplication between D and E may be desirable. Unique archive Content may be especially important to preserve.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Working/current = D
    Archive = E

Natural Resolutions may include:

    Update the archive.
        Add material missing from the archive while retaining working instances.

    Remove archived items from the working side.
        Remove working instances already represented in the archive.

    Update the archive, then remove archived items from the working side.

    Remove duplicates from the archive.
        Remove archive instances represented on the working side while
        preserving archive-only Content.

    Delete the archive.
        This may intentionally destroy unique Content.

    Keep as-is.

Internal duplication wholly within the archive should normally be exposed as separate Cases. Applying one Archive Resolution need not hide the Case; further useful work on the relationship may remain.

Archive demonstrates that a useful Disposition may intentionally create or preserve duplication.

## Staging / Import

Situation text:

    It looks like E is a staging or import area for D.

Meaning:

E is a temporary collection whose contents may have been copied or incorporated into D.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Collection/destination = D
    Staging/import area = E

Natural Resolution:

    Remove from E the items already represented in D.

Unique Files in E remain untouched. The Resolution deliberately stops at deduplication; organizing or importing unique staging Content is outside the initial application scope.

Examples:

    camera import directory
    phone import directory
    scanner output bucket
    temporary intake directory
    manually collected staging directory

## Downloads

Situation text:

    It looks like E is a download/staging area for D.

Meaning:

E contains downloaded Files, some of which are already represented elsewhere and may no longer need to remain in E.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Filed/retained location = D
    Downloads = E

Natural Resolutions:

    Remove from E the Files already represented in D.
    Remove E Files duplicated anywhere else in the Portrait.

For the second Resolution, external duplicate instances provide evidence that the E instance is redundant. Those external instances remain outside Disposition authority and are not modified.

Unique Files in E remain untouched.

Downloads remains distinct from Staging / Import because a downloaded instance is commonly expendable once identical Content has been deliberately retained elsewhere.

## Mirror

Situation text:

    It looks like D and E are intended to mirror each other.

or, for a directional mirror:

    It looks like E is intended to mirror D.

Meaning:

D and E are intended to contain corresponding Content. Differences between them represent divergence from the intended relationship.

Applicable Case types:

    DirectoryPair
    BranchPair

Semantic roles:

    Mirror side D
    Mirror side E

For a directional mirror:

    Authoritative side
    Mirrored side

Natural Resolutions:

    Make E match D.
        Add to E what is missing there.
        Remove from E what is absent from D.

    Make D match E.

    Synchronize D and E.
        Preserve Content found on either side by adding it to the other.

    Keep as-is.

Mirror differs from Backup because correspondence/symmetry is part of its semantics.

In a Backup, missing backup Content may mean the backup needs updating and extra backup Content is not necessarily wrong. In a Mirror, missing or extra Content represents asymmetry.

Making one side authoritative may intentionally destroy unique Content on the other side. Such loss must be highlighted before Execute.

## Rejected or subsumed candidates

These scenarios are retained so useful reasoning is not lost.

**Accidental copy** — subsumed by Unneeded copy.

**Rename residue** — subsumed by Unneeded copy.

**Download residue** — developed into Downloads.

**Generated output** — retained as an example under Unneeded copy. It is real, but ordinary filesystem evidence may not establish that generated copies are expendable. Structural analysis can nevertheless identify characteristic diffuse/high-degree generated-output patterns as evidence.

**Template / shared resource** — not currently a distinct Situation. True templates normally become modified derivatives and cease to be duplicates; identical shared-resource copies may be independently required. Structural graph evidence can identify repeated standardized Content without authorizing removal.

**Intentional deployment** — not currently a distinct Situation. Distributed identical copies may be independently required by applications or directory structures. Duplicate detection alone provides insufficient evidence that any is expendable.

**Intentional copies** — not retained as a generic Situation. Meaningful intentional duplication is better represented by specific Situations such as Backup, Snapshot, Archive, and Mirror.

**Intentional mirror** — developed into Mirror.

**Aggregation residue** — a real scenario, but not currently a distinct Situation. Pairwise exposure may reduce it naturally to Move residue or Reorganization; a sufficiently broad BranchPair may encompass several original source locations.

## Empirical refinement

The catalogue is expected to evolve from real Cases.

Objective structural characteristics can eliminate incompatible Situations or alter presentation order, but they do not establish semantic truth autonomously. When several Situations remain plausible, the user identifies the applicable one.

Likewise, a structural phenotype may turn out not to discriminate Situations at all. That is acceptable; the program should ask rather than pretend to know.
