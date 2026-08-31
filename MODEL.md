# Berries Domain Model

This document defines the architectural vocabulary and semantic invariants used throughout Berries. `PROJECT.md` is the governing overview; `ANALYSIS.md` and `WORKFLOW.md` contain the corresponding detailed designs.

## Vocabulary layers

Berries keeps user-facing language simple while retaining narrower technical names where they carry real information.

- **Group** — all files in the current Working Portrait having one `ContentId`, when at least two remain.
- **file / copy** — ordinary UI language for one `FileInstance`.
- **Directory** — one exact directory.
- **Branch** — a Directory together with all descendants.
- **Directory Pair / Branch Pair** — two structural scopes considered together.
- **Case** — an objective bounded set of files considered together for one coherent disposition.
- **Projection** — an Explorer presentation of Working-Portrait material.
- **Suggestion** — a promising place Berries offers for the user's attention.

Technical terms:

- `FileInstance` — one filesystem instance at one exact path.
- `ContentId` — established byte-content identity.
- **Seed** — a Branch selected as a promising starting point for targeted relationship search.
- **Counterpart** — a Branch scored relative to a particular Seed.

Seed and Counterpart are search roles, not alternate names for Branches, Branch Pairs, Cases, or Suggestions.

## Core terminology

### Filesystem

The physical universe Berries observes and eventually modifies. It is larger than the logical Corpus.

### Corpus

The logical filesystem material the user intends Berries to consider. Selected roots add material; configuration and interactive Exclude subtract material from consideration.

### Corpus root

A selected top-level Directory contributing its tree to the Corpus. Roots are normalized so no retained root is a descendant of another retained root.

### FileInstance

One filesystem instance of content at one path. It carries path, parent Directory, size, observed timestamp, and, when established, `ContentId`.

During primary acquisition Berries temporarily models all accessible Corpus files as `FileInstance`s. After Group discovery, files that belonged to no Group are represented only by fixed per-Directory unique-file counts and their individual `FileInstance`s are discarded.

### ContentId

Internal identity for byte-identical file content. Current discovery establishes it with SHA-256 after size grouping.

### Group

All files in the current Working Portrait having one ContentId, when at least two such files remain. A Group therefore changes with the Working Portrait and disappears when fewer than two instances remain.

A file that began in a Group may remain in the Working Portrait after its Group collapses to one instance. It remains a concrete Portrait file even though it no longer belongs to an active Group.

### Initial Portrait

The fixed session Portrait established after primary acquisition and Group discovery. It contains Group-originating files with established `ContentId`s; initially unique files have already been counted and pruned.

The per-Directory counts of initially unique files are retained separately as fixed session statistics. Berries does not continuously reconcile either the Initial Portrait or those counts with external filesystem changes.

### Working Portrait

The modeled state of the user's desired duplicate-relevant Corpus material during the session. It is deterministically rebuilt from the Initial Portrait plus ordered portrait operations.

Current portrait-changing operations are:

    Exclude
    Delete
    Move

### Case

A bounded set of files in the current portrait, containing at least one duplicate, considered together for one coherent disposition.

A Case is objective and program-discovered. A Situation is not required to discover a Case, but semantic context can alter how a Case is explained or presented.

The Case boundary limits disposition authority. Duplicate instances outside the Case may provide context or evidence, but remain unchanged unless independently brought under disposition authority.

Initially unique filesystem files are not Case members because they are no longer concrete members of the session Portrait. A Group-originating file that later becomes the sole surviving instance remains a Portrait file and may still lie within a structural Case boundary.

A Case is **not** projection state. The Explorer may display material that is not a Case, and the user may navigate away from or adjust the scope of a suggested Case.

### Situation

Optional human semantic context explaining why a Case exists: backup, migration, reorganization, staging residue, and so forth. Filesystem evidence can make a Situation plausible but generally cannot prove intent; the user is the semantic authority.

Situation classification is not required before the user can navigate or act.

### Disposition

The operational outcome applied under a Case's authority. Historical Situation/Resolution/Disposition research remains useful vocabulary for reasoning about coherent outcomes, but the current UI does not impose a classification wizard before Exclude/Delete/Move.

### Directory

One exact filesystem Directory. Direct Directory statistics concern directly contained files only.

Directory population distinguishes:

    UniqueFileCount
        fixed count of files initially found unique in the Directory

    PortraitFileCount
        current concrete files retained in the Working Portrait

    FileCount
        UniqueFileCount + PortraitFileCount

### Directory Pair

An unordered pair of exact Directories sharing one or more Groups directly. `SharedGroupCount` is the number of distinct Groups represented directly in both Directories.

### Branch

A Directory together with all descendants. Branch statistics aggregate file and Group information through ancestry. Branch `FileCount` likewise combines the fixed initial unique population with the current concrete Portrait population.

### Seed

A Branch chosen as a promising starting point for targeted Branch relationship search because its Group concentration makes it worth investigating.

Seed priority answers only:

    where is it worth looking?

It does not measure the quality of a Counterpart, Branch Pair, Case, or Suggestion.

### Counterpart

A non-nested Branch evaluated relative to one Seed. Counterparts are ranked by the strength of their relationship with that Seed.

### Branch Pair

Two non-nested Branches analyzed together for relationship and promise. A Branch Pair need not arise from Seed/Counterpart discovery; the Explorer can also request the best Branch Pair for an explicitly selected Branch.

### Suggestion

Something Berries has found worth the user's attention. Current Suggestions are Branch Pair views produced by targeted Seed/Counterpart analysis.

A Suggestion is an attention aid, not a command, semantic diagnosis, mandatory queue item, or guarantee that its exact scope is the best Case boundary. The user may resolve it directly, broaden or narrow either side, Pivot elsewhere, or simply follow nearby structural evidence.

### Projection

A UI-independent organization of Working-Portrait files for Explorer presentation. Current projections are:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

`ProjectionState` records the current presentation/navigation state. It is not a Case and carries no disposition authority.

### Pivot

Navigate to another projection, scope, or structural interpretation without changing the Working Portrait.

### Suggest

Navigate to the next available Suggestion. Suggest is navigation, not a portrait operation and not a required workflow step.

### Selection

A persistent set of files in the current Working Portrait. Selection is independent of projection; higher Explorer nodes are shorthand for the files represented beneath them.

### Exclude

Remove selected files from the Working Portrait without changing the physical filesystem or creating filesystem Actions.

### Delete

Remove selected files from the Working Portrait and add corresponding physical deletion work.

### Move

Duplicate-motivated relocation from an explicit source scope to an explicit destination scope. Relative source structure is preserved beneath the destination scope.

For each source file:

1. compute the exact destination Directory from its relative source path;
2. if identical content already exists directly there, retain the destination and reduce the source to Delete;
3. otherwise use the source filename;
4. free path -> Move;
5. same filename and same content -> retain destination, Delete source;
6. same filename and different content -> collision; leave both unchanged and continue.

Berries does not invent filenames or overwrite different content.

### Action

A primitive physical filesystem operation used to realize the Working Portrait at Execute time. Current primitives include Delete, Move, Copy, and necessary directory creation.

### Undo

Remove the most recent top-level portrait operation and deterministically rebuild Working Portrait, Groups, Actions, and selection binding.

### Execute

The explicit physical commitment boundary. Berries does not globally rescan immediately before execution; encountered failures are handled locally and independent safe work continues.

## Interaction principle

The Explorer is primary. Berries does not attempt a wizard-like sequence of mandatory Cases.

The product objective is deliberately loose:

> Give the user the ability to resolve duplicates with minimum practical effort.

Analysis reduces search effort by surfacing promising structure. The user remains free to adjust scope and follow recognizable relationships through the Explorer.

## Governing invariants

1. The Corpus is logical material Berries is considering; it is not the filesystem.
2. The Initial Portrait is fixed for the lifetime of a session and contains Group-originating files only.
3. Initial unique-file counts are fixed session statistics retained by physical Directory after unique `FileInstance`s are pruned.
4. The Working Portrait is reconstructible from the Initial Portrait plus ordered portrait operations.
5. A Group is the current set of at least two files sharing one ContentId.
6. A Case is objective, bounded, contains duplication, and limits disposition authority.
7. A Projection is presentation/navigation state, not a Case.
8. Selection always denotes files.
9. Exclude changes the Working Portrait but creates no filesystem Action.
10. Delete and Move change the Working Portrait immediately and contribute physical Actions.
11. There is no Keep, Accept, or Apply state in the current interaction model.
12. Seed priority, Counterpart/pair score, and Case presentation priority are distinct concepts.
13. Suggestions guide attention but do not prescribe workflow or exact final scope.
14. Derived analysis is valid only for the portrait generation from which it was computed.
15. No physical filesystem change occurs before Execute.
16. Core remains independent of UI and platform-specific filesystem behavior.
