# Berries User and Execution Workflow

This document defines the current interaction and execution contract. Terminology and invariants are in `MODEL.md`; discovery and ranking are in `ANALYSIS.md`.

## Interaction model

Berries uses a long-lived **Explorer**, not a prescribed sequence of duplicate questions. Suggestions identify promising places to look; the user may inspect them directly, broaden or narrow either side, Pivot to another view, or follow nearby structure before acting.

The objective is minimum practical user effort while keeping destructive decisions explicit.

## New session

    choose Corpus roots
        -> Explore
        -> selected roots appear immediately as a lightweight placeholder
        -> acquire files and discover Groups
        -> establish BerriesSession / Working Portrait
        -> Groups view becomes usable
        -> derived Directory / Branch / Suggestion analysis continues in background

The placeholder is not the **Corpus Roots projection**. Corpus Roots is a full Branch-style projection of current grouped files. It is prewarmed after the initial Groups view is published so a later Pivot normally uses the cached projection without delaying first useful display.

## Explorer projections

Current projections are:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

A Projection is presentation/navigation state, not a Case.

- **Groups** — one root per Group, with current files beneath it.
- **Directory** — grouped files directly in one Directory.
- **Branch** — grouped files beneath one Directory hierarchy.
- **Corpus Roots** — one Branch-style tree for each Corpus root.
- **Directory Pair / Branch Pair** — two scopes shown side by side.

Structural nodes are selection shortcuts over represented files; selection itself always denotes files.

## Suggestions and navigation

A **Suggestion** is a promising place for attention, currently a Branch Pair found by targeted Seed/Counterpart analysis. It is not a command or necessarily the final useful Case boundary.

**Pivot** changes projection or scope without changing the Working Portrait. Pair breadcrumbs let either side be broadened or narrowed independently. `Suggest` cycles through available Suggestions and likewise changes only navigation.

Selection persists across projections. Back/Forward controls exist, but navigation history is not yet implemented.

## Portrait operations

### Exclude

Removes selected files from the Working Portrait without creating physical filesystem Actions.

### Delete

Removes selected files from the Working Portrait and adds deletion Actions.

### Move

Move is duplicate-motivated relocation between explicit source and destination scopes in a pair projection. It is not general-purpose file management.

For source scope `S`, destination scope `D`, and selected source file `f`:

1. preserve `f`'s parent path relative to `S` beneath `D`;
2. if identical content already exists directly in that exact destination Directory, retain the destination and Delete the source;
3. otherwise use the source filename;
4. if that path is free, Move;
5. same filename and same content means retain destination and Delete source;
6. same filename and different content is a collision; leave both unchanged and continue.

Berries does not search descendant Directories for a substitute destination, invent filenames, or overwrite different content.

## Immediate modeled result

Exclude, Delete, and Move change the Working Portrait immediately. There is no Apply state.

A successful portrait operation rebuilds the Working Portrait, advances its generation, rebinds selection, makes older derived analysis stale, requests cancellation of obsolete work, and schedules analysis for the new generation. The Explorer remains usable while that work runs.

One top-level user command is one Undo step.

## Unique files

Initially unique files are concrete during primary discovery, then their individual `FileInstance`s are pruned from the session Portrait. Fixed per-Directory unique counts remain because total Directory/Branch population affects structural analysis.

Unique files are therefore not ordinary selection or duplicate-resolution targets in the current session model.

## Execute

Execute is the physical commitment boundary. Before it, Berries reports the planned Action count and potential physical content loss and asks for explicit approval.

Execution attempts accumulated Actions without a global pre-execution rescan. Independent safe work continues after local failures. Move first attempts a filesystem Move; on `IOException` it falls back to Copy then Delete, and the source is not deleted if Copy fails.

After execution Berries reports completed Actions, dependent skips, and failures.

## Semantic context

The user may recognize a relationship as a backup, migration, reorganization, archive, staging area, accidental copy, or another familiar history. Such recognition can make the natural action obvious, but Berries does not require semantic classification. Content and structure provide evidence; the user remains the authority on intent.

## Save / Load

Save/Load is not implemented. A fresh session rescans the selected Corpus.