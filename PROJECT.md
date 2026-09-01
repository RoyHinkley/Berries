# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Project

## Problem

Ordinary duplicate-file tools expose sets of identical files and leave the user to reason about individual copies. Real duplication is often structural: backups, reorganized trees, migrations, partial moves, archives, staging areas, downloads, and accumulated copies can create thousands of duplicate instances whose useful resolution is much simpler than the raw count suggests.

Berries uses identical content as evidence about both files and filesystem structure. It builds a virtual **Working Portrait** of the user's desired **Corpus** and provides an Explorer for examining Groups, Directories, Branches, and structural pairs before any physical filesystem change occurs.

## Objective

> Give the user the ability to resolve unwanted duplication with minimum practical effort while preserving the content and organization the user wants.

Berries reduces search and repeated decisions without pretending to infer user intent or making autonomous destructive choices.

## Product model

The Explorer is primary. Analysis produces **Suggestions** that identify promising structure; the user can inspect, broaden, narrow, Pivot, select, and act wherever the relationship becomes comprehensible.

A Suggestion means **worth your attention**, not **the next required action**.

Current projections are:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

Current portrait operations are:

    Exclude
    Delete
    Move
    Undo

No physical filesystem modification occurs until **Execute**.

## Governing rules

1. **Corpus is logical.** Roots add material; configuration and Exclude subtract material from Berries consideration.
2. **Portrait-first.** The Initial Portrait is fixed for a session. The Working Portrait is deterministically reconstructed from it plus ordered portrait operations.
3. **Groups are session-stable identities.** Group membership may fall to one or zero; Groups are not rediscovered after primary discovery.
4. **Unique files are summarized after discovery.** Their fixed per-Directory counts remain for structural statistics; their individual `FileInstance`s are pruned from the session Portrait.
5. **Selection always means files** and persists across projections.
6. **Projection is navigation, not authority.** A Case is a bounded duplicate problem considered for one coherent disposition; a displayed projection need not itself be a Case.
7. **Suggestions guide attention, not workflow.** The user remains free to follow recognizable structure.
8. **Operations are explicit.** Exclude/Delete/Move change the Working Portrait immediately; there is no Keep, Accept, or Apply state.
9. **Move preserves source-relative structure.** Existing destination organization is authoritative; Berries does not invent names or overwrite different content.
10. **Analysis is generation-bound.** Stale derived results cannot publish after the Working Portrait changes.
11. **Navigation is generation-bound.** Only the most recently requested navigation may publish visible state.
12. **Responsiveness is architectural.** Scaling work is asynchronous where appreciable, cancellable at useful granularity, and reports meaningful progress.
13. **Large Explorer populations are virtualized.** Logical tree size must not imply realization of every Avalonia control.
14. **Execution is explicit.** Physical changes occur only after user approval of Execute.

## Analysis strategy

Berries first discovers exact content Groups, then derives Directory and Branch structure. Branch statistics identify promising **Seeds**; for several good Seeds, Berries searches non-nested **Counterparts** and scores the actual relationships. Suggestions are chosen by pair quality, not simply by Seed rank or maximum raw duplicate count.

The important distinction is:

    Seed priority
        where is it worth looking?

    Branch Pair score
        how strong is this particular relationship?

The current relationship score combines shared Group count with Jaccard overlap. Several Seeds are compared before selecting a Suggestion because the strongest relationship often does not originate from the highest-ranked Seed.

This targeted search avoids exhaustive Branch-Pair enumeration and favors useful, recognizable local structure.

## Runtime flow

    select roots
        -> lightweight root placeholder
        -> acquire files
        -> size-group and hash candidates
        -> establish Groups and ContentIds
        -> retain unique counts / prune unique FileInstances
        -> establish BerriesSession and portrait generation
        -> Groups view becomes usable
        -> derived Directory / Branch / Suggestion analysis runs in background
        -> Corpus Roots projection is prewarmed for later Pivot

Portrait operations repeat the same pattern on the reduced Working Portrait: rebuild immediately, invalidate older derived analysis by generation, and recompute in the background while the Explorer remains usable.

## Architecture

    Berries.Core
        domain/session model, discovery, analysis, queries,
        portrait operations, analysis lifecycle, planning contracts

    Berries.Projection
        UI-independent presentation-shaped projections

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem adapter

    Berries.Gui
        Avalonia interaction and GUI-specific presentation

Placement rule: **put work at the lowest reusable layer that naturally owns it**. Cost does not determine ownership.

## Documentation

Keep these documents current-state oriented; remove obsolete development history rather than accumulating a diary.

- [MODEL.md](MODEL.md) — authoritative vocabulary and semantic invariants.
- [ARCHITECTURE.md](ARCHITECTURE.md) — ownership, responsiveness, caching, virtualization, cancellation, and publication rules.
- [ANALYSIS.md](ANALYSIS.md) — Group discovery and structural/Suggestion analysis.
- [WORKFLOW.md](WORKFLOW.md) — Explorer interaction, portrait operations, Undo, and Execute.
- [DEVELOPMENT.md](DEVELOPMENT.md) — compact implementation map and near-term work.

The eventual README is intended for users: illustrate the duplicate-file problem, explain what is different about Berries, and provide Getting Started guidance.