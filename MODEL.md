# Berries Domain Model

This document is the authoritative vocabulary and semantic-invariant reference. `PROJECT.md` gives the overview; `ARCHITECTURE.md`, `ANALYSIS.md`, and `WORKFLOW.md` define implementation, analysis, and interaction contracts.

## Core vocabulary

### Filesystem

The physical universe Berries observes and, only at Execute, may modify.

### Corpus

The logical filesystem material the user asks Berries to consider. Selected roots add material; configuration and interactive Exclude subtract material.

Roots are normalized so no retained Corpus root is a descendant of another retained root.

### FileInstance

One filesystem instance at one exact path. During primary acquisition all accessible Corpus files are concrete `FileInstance`s. After Group discovery, initially unique files are represented only by fixed per-Directory counts and their individual instances are discarded from the session Portrait.

UI language normally says **file** or **copy**.

### ContentId

Established byte-content identity. Current discovery uses SHA-256 after size grouping.

### Group

A content-identity set established during primary discovery. Group identity persists for the session; portrait operations change only current membership.

A Group may therefore have two or more, one, or zero current files. Berries does not rediscover or redefine Groups during the session. Empty Groups remain valid session objects even when projections have nothing to display for them.

### Initial Portrait

The fixed session Portrait established after primary discovery. It contains Group-originating files with `ContentId`s. Initially unique files have already been counted and pruned.

### Working Portrait

The modeled current state of the user's duplicate-relevant Corpus. It is deterministically rebuilt from the Initial Portrait plus ordered portrait operations.

Current portrait-changing operations are Exclude, Delete, and Move.

### Directory

One exact filesystem Directory. Direct Directory statistics distinguish:

    UniqueFileCount
        fixed count initially unique in this Directory

    GroupedFileCount
        current files belonging to session-stable Groups

    FileCount
        UniqueFileCount + GroupedFileCount

A Group's sole remaining member still contributes to `GroupedFileCount`; a zero-member Group contributes no files.

### Branch

A Directory together with all descendants. Branch statistics aggregate Directory/file/Group information through ancestry and use the same fixed-unique plus current-grouped population model.

### Directory Pair

Two exact Directories currently sharing one or more Groups directly. `SharedGroupCount` is the number of distinct Groups represented directly in both.

### Branch Pair

Two non-nested Branches analyzed together for relationship. A Branch Pair can come from Suggestion analysis or an explicit Explorer request.

### Seed

A Branch ranked as a promising starting point for targeted relationship search. Seed priority answers **where is it worth looking?** It does not measure final pair quality.

### Counterpart

A non-nested Branch evaluated relative to one Seed. Seed and Counterpart are search roles, not alternate names for Branches, Branch Pairs, Cases, or Suggestions.

### Suggestion

A promising place Berries offers for attention. Current Suggestions are Branch Pair views produced by targeted Seed/Counterpart analysis.

A Suggestion is not a command, semantic diagnosis, required queue item, or guarantee that its exact scopes are the best final Case boundary.

### Case

A bounded set of current-Portrait files containing duplication and considered together for one coherent disposition.

The Case boundary limits disposition authority: duplicate instances outside it may provide evidence but are not modified under that Case.

A Case is not projection state. The Explorer may display material that is not itself a Case, and the user may adjust a Suggested scope before acting.

### Situation / disposition

A **Situation** is optional human context such as backup, migration, reorganization, archive, or staging residue. Filesystem evidence may suggest context but generally cannot prove intent; the user is the semantic authority.

A **disposition** is the coherent operational outcome chosen for a Case. Berries does not require Situation classification before direct Explorer operations.

### Projection

A UI-independent organization of Working-Portrait material for Explorer presentation. Current projections are Groups, Directory, Branch, Corpus Roots, Directory Pair, and Branch Pair.

`ProjectionState` is navigation/presentation state only; it is not a Case and carries no disposition authority.

### Pivot / Suggest

**Pivot** navigates to another projection or scope without changing the Working Portrait. **Suggest** navigates among available Suggestions. Neither is a portrait operation.

### Selection

A persistent set of files in the current Working Portrait. Selection is independent of projection. Structural Explorer nodes are shorthand for their represented files.

### Exclude

Remove selected files from the Working Portrait without creating physical filesystem Actions.

### Delete

Remove selected files from the Working Portrait and add corresponding deletion Actions.

### Move

Duplicate-motivated relocation between explicit source and destination scopes. Relative source structure is preserved beneath the destination.

For each source file:

1. compute the exact destination Directory from its source-relative parent path;
2. if identical content already exists directly there, retain the destination and Delete the source;
3. otherwise use the source filename;
4. free path -> Move;
5. same filename and same content -> retain destination, Delete source;
6. same filename and different content -> collision; leave both unchanged.

Berries does not invent filenames or overwrite different content.

### Action

A primitive physical filesystem operation used to realize the modeled result at Execute time. Current primitives include Delete, Move, Copy, and required directory creation.

### Undo

Remove the most recent top-level portrait operation and deterministically rebuild Working Portrait, Group membership, Actions, and selection binding. One user command is one Undo step.

### Execute

The explicit physical commitment boundary. No physical filesystem modification occurs before Execute.

## Governing invariants

1. Corpus is logical material under consideration, not the filesystem itself.
2. Initial Portrait is fixed for a session and contains Group-originating files only.
3. Initially unique files are retained only as fixed per-Directory counts after discovery.
4. Working Portrait is reconstructible from Initial Portrait plus ordered portrait operations.
5. Group identity is established once per session; membership may be any nonnegative count.
6. Selection always denotes files and persists across projections.
7. Case is bounded disposition authority; Projection is presentation/navigation. They are not interchangeable.
8. Exclude changes the Working Portrait without creating a filesystem Action.
9. Delete and Move change the Working Portrait immediately and contribute physical Actions.
10. There is no Keep, Accept, or Apply state.
11. Seed priority and Branch Pair relationship quality are distinct quantities.
12. Suggestions guide attention but do not prescribe workflow or exact final scope.
13. Derived analysis is valid only for the portrait generation from which it was computed.
14. Only the current navigation generation may publish visible Explorer state.
15. No physical filesystem change occurs before Execute.
16. Work belongs at the lowest reusable architectural layer that naturally owns its meaning; cost does not determine ownership.
17. Potentially appreciable work is non-blocking to the GUI, cooperatively cancellable, and reports meaningful progress; determinate progress is preferred when practical.
18. Large Explorer item populations are virtualized; logical tree size must not imply realization of every visual control.
19. Cached results must be keyed by every state dimension on which they depend.