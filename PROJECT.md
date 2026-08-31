# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Project

## Problem statement

Ordinary duplicate-file tools expose groups of identical files and leave the user to reason about individual copies. That works for isolated duplication, but poorly for accumulated backups, reorganized trees, partial moves, migrations, archives, generated output, repositories, and other real filesystem histories.

Berries uses identical file content as evidence about both files and filesystem structure. It builds a virtual **Working Portrait** of the user's desired **Corpus** and provides an Explorer in which the user can examine **Groups**, **Directories**, **Branches**, **Directory Pairs**, and **Branch Pairs**, then Exclude, Delete, or Move selected files before any physical filesystem change occurs.

## Objective

Give the user the ability to resolve unwanted duplication with minimum practical effort while preserving the content and organization the user wants.

Berries should reduce user attention and repeated decisions without pretending to infer semantic truth or making autonomous destructive decisions.

## Why the Explorer is primary

Early work pursued the idea that Berries could present an ordered sequence of Cases, wizard-style. Real-corpus work showed that this was too rigid. A statistically strong scope can be close to the useful question without being the easiest scope for a human to recognize. A child Branch, parent Branch, nearby Directory, or related pair may make the intended disposition much clearer.

The resulting product model is deliberately free-form:

- analysis offers **Suggestions**;
- the Explorer lets the user broaden, narrow, Pivot, and follow recognizable structure;
- the user decides when a scope is coherent enough to act on;
- resolution changes the Working Portrait immediately and analysis adapts to the smaller remaining problem.

A Suggestion therefore means "this looks worth your attention," not "this is the next required Case."

## Current vocabulary

- **Group** — a content-identity set established during primary discovery. Its identity persists for the session while current membership may fall to one or zero files.
- **file / copy** — one filesystem instance; Core uses `FileInstance` where that precision matters.
- **Directory** — one exact directory.
- **Branch** — a Directory together with all descendants.
- **Directory Pair** — two exact Directories sharing Groups directly.
- **Branch Pair** — two Branches analyzed together for relationship.
- **Case** — an objective bounded set of files containing duplication, considered together for one coherent disposition. Its boundary limits disposition authority.
- **Projection** — an Explorer organization/presentation of Working-Portrait material. Projection state is not a Case.
- **Suggestion** — a promising place Berries offers for attention; currently a Branch Pair view.
- **Seed** — a Branch worth investigating as a starting point for targeted relationship search.
- **Counterpart** — a Branch scored relative to a particular Seed.
- **Pivot** — change projection/focus without changing the Working Portrait.
- **Exclude / Delete / Move** — portrait-changing operations.
- **Undo** — reverse the most recent portrait-changing user command.
- **Execute** — attempt the accumulated physical filesystem work.

## Governing principles

1. **Corpus is logical.** Selected roots add material; configuration or interactive Exclude subtracts material from Berries consideration.
2. **Portrait-first design.** A scan establishes a fixed Initial Portrait. The Working Portrait is reconstructed from it plus ordered portrait operations.
3. **The Explorer is primary.** Suggestions reduce search effort but do not own the workflow.
4. **Cases bound authority.** A Case groups files for one coherent disposition; duplicates outside its boundary can be evidence without being modified.
5. **Projection is navigation.** Group, Directory, Branch, Directory Pair, Branch Pair, and Corpus Roots are organizations of the same Working Portrait.
6. **Selection always means files.** Structural nodes are convenient scopes over represented files.
7. **Operations are explicit.** Exclude, Delete, and Move immediately change the Working Portrait. There is no separate Keep, Accept, or Apply state.
8. **Move preserves source-relative structure.** The user establishes source and destination scopes explicitly; existing destination organization is authoritative.
9. **Analysis serves attention.** The goal is useful prioritization, not exhaustive enumeration or mathematical completeness.
10. **Execution is explicit.** No physical filesystem modification occurs until Execute.
11. **Computation belongs in Core whenever possible.** Projection owns only computation that is inherently presentation-shaped; the GUI owns interaction and bounded control updates, not Corpus-/Portrait-scale work.
12. **Scaling work must preserve responsiveness.** Potentially appreciable Core/Projection operations are asynchronous, cancellable within their scaling loops, and report meaningful progress; determinate completed/total progress is preferred whenever the total is practical to know.

## Analysis strategy and empirical result

The earliest ranking idea was leverage: prefer a Case in which one user decision could dispose of many duplicate instances. That captures an important goal—work per question—but it is not sufficient by itself. Very broad Branch Pairs can have high theoretical resolving power while presenting an unclear human question.

Experiments found a more effective targeted strategy:

1. compute inexpensive Branch statistics;
2. rank promising **Seeds** by parent-relative Group concentration;
3. for a small window of good Seeds, find strong non-nested **Counterparts**;
4. score the actual Seed/Counterpart relationships by shared Groups and overlap;
5. compare the best relationships across the Seed window;
6. surface the strongest result as a **Suggestion**;
7. after a resolution changes the Working Portrait, repeat on the reduced problem.

Seed priority and Branch Pair quality are intentionally different quantities. The best Branch Pair often does not originate from the highest-ranked Seed.

The practical result observed during R&D is central to the design: resolving a small number of well-chosen, comprehensible Cases can collapse a very large duplicate problem extremely quickly. Corpora with tens of thousands of duplicate instances could often be reduced by only a handful of structural decisions. This is why targeted discovery plus repeated re-analysis is more valuable than exhaustive generation of every possible Branch Pair or a globally optimized wizard sequence.

## Current application flow

    Select Roots
        -> Explore
        -> Corpus view appears
        -> enumerate files
        -> size-group candidate files
        -> hash candidates
        -> construct Groups
        -> count uniques by Directory
        -> prune unique FileInstances
        -> construct BerriesSession
        -> Groups view becomes ready
        -> background Directory analysis / Directory Pairs
        -> background Branch statistics
        -> background targeted Seed/Counterpart search
        -> Suggestions become available

Derived analysis is generation-aware and dependency-driven. Portrait changes make older products stale immediately, request cancellation of obsolete work, and schedule analysis for the new Working Portrait while the Explorer remains usable.

## Explorer projections

Current projections are:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

Pair-view breadcrumbs allow the user to broaden or narrow either side independently. This is an important part of the "follow your nose" interaction: a Suggested Branch Pair is a starting point, not a frozen Case boundary.

## Resolution and execution

Current portrait operations are Exclude, Delete, and Move. One user command is one Undo step.

Move maps source-relative paths beneath an explicit destination scope. Existing identical content in the exact destination Directory is authoritative; same-name/different-content collisions are reported and left unchanged. Berries does not invent filenames or overwrite different content.

Before Execute, Berries summarizes planned physical Actions and potential physical content loss. Execute handles filesystem failures locally and continues independent safe work.

## Unique files after discovery

Unique files are needed during primary discovery and for the structural population statistics that influence Seed concentration. After Groups are established, however, their individual `FileInstance`s no longer participate in duplicate resolution.

Berries therefore retains fixed unique-file counts by physical Directory and removes the unique `FileInstance`s from the session Portrait. Directory and Branch totals reconstruct their current file population from those retained unique counts plus current files belonging to session-stable Groups. This preserves ranking behavior while reducing long-lived memory use and repeated Portrait traversal cost.

## Design documents

- [MODEL.md](MODEL.md) — authoritative vocabulary and invariants.
- [ARCHITECTURE.md](ARCHITECTURE.md) — Core/Projection/GUI boundaries, responsiveness, cancellation, and progress rules.
- [ANALYSIS.md](ANALYSIS.md) — Group discovery, Seed/Counterpart search, ranking, and empirical findings.
- [WORKFLOW.md](WORKFLOW.md) — Explorer interaction, portrait operations, Move, Undo, and Execute.
- [SEMANTIC-RESEARCH.md](SEMANTIC-RESEARCH.md) — retained Situation/disposition research and recognizable filesystem histories.
- [BOUNDARY.md](BOUNDARY.md) — empirical problem-boundary findings.
- [DEVELOPMENT.md](DEVELOPMENT.md) — current implementation map and near-term architectural work.

## Platform and architecture

    C#
    .NET 10
    Avalonia

Solution decomposition:

    Berries.Core
        domain/session model, Group discovery, structural analysis,
        analysis lifecycle, queries, portrait operations, planning/execution contracts

    Berries.Projection
        UI-independent Explorer projection construction and ProjectionState

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem adapter

    Berries.Gui
        Avalonia desktop UI and interaction orchestration

The placement rule is **Core if possible, Projection if warranted, GUI only for presentation and interaction**. See `ARCHITECTURE.md` for the operational contract, including cancellation and progress requirements.

    Berries.Core.Tests
        platform-independent tests using synthetic filesystem/model data
