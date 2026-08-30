# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Project

## Problem statement

Ordinary duplicate-file tools expose sets of identical files and leave the user to reason about individual copies. That works for isolated duplication, but poorly for accumulated backups, reorganized trees, partial moves, migrations, archives, generated output, repositories, and other real filesystem histories.

Berries uses identical file content as evidence about both files and filesystem structure. It builds a virtual **Working Portrait** of the user's desired **Corpus** and provides an Explorer in which the user can examine **Groups**, **Directories**, **Branches**, **Directory Pairs**, and **Branch Pairs**, then Exclude, Delete, or Move selected files before any physical filesystem change occurs.

## Objective

Provide a safe, efficient way to eliminate unwanted file duplication, or deliberately remove material from Berries consideration, across large filesystem trees while preserving the content and organization the user wants.

The system should minimize required user attention without making semantic decisions on the user's behalf.

## Current vocabulary

Berries deliberately uses a small user-facing vocabulary:

- **Group** — all files in the current Working Portrait having one identical content identity, provided at least two such files remain.
- **file / copy** — an ordinary filesystem file shown in the Explorer. Core uses the narrower type name `FileInstance` when it matters that this is one filesystem instance at one path.
- **Directory** — one exact directory; Directory statistics concern directly contained files only.
- **Branch** — a Directory together with all descendants.
- **Directory Pair** — two exact Directories sharing one or more Groups directly.
- **Branch Pair** — two Branches sharing Groups somewhere beneath them.
- **Corpus Roots** — the selected root trees contributing material to the Corpus.
- **Suggestion** — a view Berries has found worth the user's attention because its structure indicates that one or a few decisions may resolve a relatively large amount of duplicated material.
- **Pivot** — change the current projection/focus without changing the Working Portrait.
- **Suggest** — navigate to the next available Suggestion.
- **Exclude / Delete / Move** — portrait-changing operations.
- **Undo** — reverse the most recent portrait-changing user command.
- **Execute** — attempt the accumulated physical filesystem work.

`ContentId` is an internal technical identity for byte-identical content. It is useful and intentionally narrower than the user-facing word Group: a Group is the current collection of files sharing one ContentId.

Two other technical words describe the current Branch Pair search rather than user workflow:

- **Seed** — a Branch ranked as a good starting point for looking for a strong relationship.
- **Counterpart** — a Branch scored relative to a Seed; the highest-scoring Counterpart forms that Seed's strongest candidate Branch Pair.

Several Seeds are evaluated before the next Suggestion is chosen. The Suggested Branch Pair is selected by pair quality, not simply by Seed rank, and often does not come from the highest-ranked Seed.

## Governing principles

1. **Corpus is logical.** Selected roots add material; configuration or interactive Exclude subtracts material from Berries consideration.
2. **Portrait-first design.** A scan produces a fixed Initial Portrait. The Working Portrait is deterministically reconstructed from that Initial Portrait plus ordered portrait operations.
3. **The Explorer is primary.** Analysis supplies evidence and Suggestions; it does not own the workflow.
4. **Selection has one meaning.** Selection always denotes files. Higher tree nodes are shorthand for the files represented beneath them.
5. **Projection is navigation.** Group, Directory, Branch, Directory Pair, Branch Pair, and Corpus Roots are organizations of the same Working Portrait.
6. **Operations are explicit.** Exclude, Delete, and Move immediately change the Working Portrait. There is no separate Keep, Accept, or Apply state.
7. **Move preserves source-relative structure.** The user establishes source and destination scopes explicitly. Existing destination organization is authoritative.
8. **Unique files remain known.** They are not duplicate-resolution targets, but can constrain operations such as Move through destination collisions.
9. **Analysis serves attention.** Exhaustive structural enumeration is not a goal. Cheap statistics and targeted search are preferred over combinatorial completeness.
10. **Execution is explicit.** No physical filesystem modification occurs until Execute.
11. **Core remains independent of UI and platform-specific filesystem behavior.**

## Current application flow

For a new session:

    Select Roots
        -> Add / Remove / Explore
        -> Corpus view appears
        -> enumerate files
        -> size-group candidate files
        -> hash candidates
        -> construct Groups
        -> construct BerriesSession
        -> Directory analysis
        -> Branch statistics
        -> targeted Seed/Counterpart search
        -> construct Suggestions
        -> Groups view becomes ready

The current initial scan path is sequential: `ScanAsync()` does not return until the downstream derived analysis is complete. The Explorer shell and status bar are already present, and portrait operations trigger derived-analysis refresh in the background. Further decoupling of the initial scan/analysis lifecycle is a current design direction, not yet implemented behavior.

## Explorer projections

### Groups

One pane:

    Group
        full-path file
        full-path file
        ...

### Directory

One exact Directory, showing grouped files directly within it.

### Branch

One Branch, showing grouped files organized beneath its directory tree.

### Corpus Roots

The selected Corpus roots, each displayed as a Branch projection.

### Directory Pair / Branch Pair

Two equivalent panes. Higher directory nodes are selection shortcuts for represented files; Directories themselves are not filesystem-operation targets.

## Navigation and resolution

Current controls include:

    Pivot
    Suggest
    Invert
    Exclude
    Delete
    Move ->
    <- Move
    Undo

Back and Forward controls are present but navigation history is not yet implemented.

`Suggest` cycles through the current Suggestions. The implemented Suggestion source is the targeted Branch search, so each current Suggestion opens a Branch Pair. A Suggestion is not a queue and does not force a resolution.

## Move semantics

For each selected source file, Berries preserves the file's relative directory path beneath the selected source scope when mapping it beneath the destination scope.

Within the exact computed destination directory:

1. If the same content already exists there, regardless of filename, retain the destination file and reduce the source work to Delete.
2. Otherwise use the source filename.
3. If that path is free, Move the source there.
4. If that path contains the same content, retain the destination and Delete the source.
5. If that path contains different content, report a collision immediately and leave both files unchanged while other requested moves continue.

Berries does not invent filenames or overwrite different content.

## Analysis strategy

Group discovery is size grouping followed by SHA-256 hashing of files in non-singleton size groups.

Directory analysis derives direct Directory records, Directory Pairs, and inexpensive graph diagnostics. Branch statistics aggregate physical and Group information through ancestry without constructing every possible Branch Pair.

Branch Seeds are ranked by parent-relative concentration. The current useful Seed measure is based on:

    D = Branch GroupCount
    C = Group retention / ordinary file retention relative to parent

    seed score = D * (1 - 1/C), for C > 1; otherwise 0

For each Seed, targeted Counterpart search measures actual Branch relationships using shared Groups and Jaccard overlap. The current pair score is:

    shared Group count * Jaccard overlap

The search evaluates the top 10 currently eligible Seeds, finds the best Counterpart relationship for each, and chooses the strongest resulting Branch Pair as the next Suggestion. Consequently, Seed rank and Suggestion quality are deliberately not the same thing.

Comprehensive ancestor-Cartesian Branch Pair enumeration was abandoned after large-corpus experiments demonstrated severe combinatorial growth without proportional user value.

## Working Portrait and execution

`BerriesSession` owns:

- fixed Initial Portrait;
- current Working Portrait;
- persistent selection;
- ordered Exclude/Delete/Move operations;
- current Groups;
- physical Actions implied by Delete and Move.

Exclude produces no physical Action. Delete and Move do.

Before Execute, the GUI reports the planned action count and the number of Groups that would have no surviving physical file after the plan. Execute attempts the Actions, continues independent safe work after failures, and reports completed, skipped-dependent, and failed work.

## Persistence

Save/Load commands are present but disabled. A future saved session should restore modeled session state directly rather than silently rescanning or reconciling the filesystem.

## Design documents

- [MODEL.md](MODEL.md) — authoritative model vocabulary and invariants.
- [ANALYSIS.md](ANALYSIS.md) — Group discovery and structural analysis.
- [WORKFLOW.md](WORKFLOW.md) — Explorer interaction, portrait operations, Move, Undo, and Execute.
- [SEMANTIC-RESEARCH.md](SEMANTIC-RESEARCH.md) — retained empirical scenarios and natural user actions without imposing a classification workflow.
- [BOUNDARY.md](BOUNDARY.md) — empirical problem-boundary findings.
- [DEVELOPMENT.md](DEVELOPMENT.md) — current implementation map and near-term architectural work.

## Platform and architecture

Implementation platform:

    C#
    .NET 10
    Avalonia

Solution decomposition:

    Berries.Core
        domain/session model, Portrait, Group discovery,
        structural analysis, queries, planning/execution contracts

    Berries.Projection
        UI-independent Explorer projection construction

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem adapter

    Berries.Gui
        Avalonia desktop UI and orchestration

    Berries.Core.Tests
        platform-independent tests using synthetic filesystem/model data

Architectural test:

    If Core cannot be exercised by a simple test harness against synthetic
    data, a platform or UI concern has leaked across a boundary.
