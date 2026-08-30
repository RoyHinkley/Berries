# Berries Problem Boundary — Empirical Findings

This document records empirical findings about Berries' practical problem boundary. It is research context, not a competing workflow specification. Governing behavior is defined in `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`.

## What the experiments established

Berries reliably discovers real identical-content Groups. The difficult question is not whether files are byte-identical but which duplication is useful for the user to act on.

Real corpora contain both user-managed organization and application/generated structure. Content identity alone does not explain why copies exist.

Developer/source corpora were intentionally useful stress tests because they contain repositories, generated output, package material, copied source trees, saved web assets, and many repeated support files. They exposed failure modes early.

## Group-level duplication can manufacture structural noise

Widely repeated Groups can induce many Directory Pairs and apparent Branch relationships even when the containing Directories do not represent one coherent user problem.

Early experimental screens made this especially visible: excluding a relatively small number of diffusely repeated files removed a large amount of downstream structural evidence.

The lasting lesson is:

    Group-centric and container-centric duplication are different useful viewpoints

and:

    low-level repeated Groups can manufacture weak higher-level structure

The ordinary Groups projection and Exclude operation now provide the general mechanism for removing such material from the Working Portrait when the user does not want Berries to consider it.

## Application-managed/generated material

Observed examples include Git hook samples and object storage, build outputs, DLL/PDB groups, UUID-like generated artifacts, saved-web-page support files, package/runtime trees, and other repeated infrastructure.

These can be genuine Groups and can reveal meaningful ancestry among Branches. That does not make them automatically useful deletion targets.

Rather than encode application-specific knowledge, Berries gives the user direct control over the logical Corpus:

    selected roots add material
    Exclude subtracts material

`Berries.config [exclude]` provides persistent automatic subtraction for known unwanted paths/patterns. Interactive Exclude provides the same logical result during a session.

## User-managed material remains the clearest target

Berries is particularly well matched to accumulated user-managed trees such as:

    documents and photographs
    downloads
    old machine copies
    backups and archives
    migrations
    partial moves
    reorganized directory trees
    staging/import areas

Here the user is the authority on organization, and Group relationships can support meaningful structural operations such as the explicit source/destination Move behavior defined in `WORKFLOW.md`.

## Structural discovery findings

Exhaustive Branch Pair enumeration is not necessary to exploit structural evidence. Large-corpus experiments showed combinatorial growth without proportional user benefit.

Cheap first-class Branch statistics plus targeted counterpart search repeatedly surfaced recognizable high-value structures, including known copied/moved trees. This materially expands the practical corpus size Berries can address without comprehensive pair generation.

Boundary precision also proved less important than initially feared. A mathematically broader counterpart can differ from the intuitively neat child boundary by only one Group and still support the same useful operation. Ranking should not be complicated merely to perfect such boundaries unless Explorer use demonstrates a real operational difference.

## Current practical boundary

1. Berries scans the roots the user chooses.
2. Configuration or interactive Exclude removes material from consideration.
3. Group-oriented resolution operations target files belonging to current Groups.
4. Unique files remain modeled because they can constrain valid operations, but general unique-file maintenance is out of scope.
5. General rename and arbitrary filesystem reorganization are out of scope.
6. Berries does not require semantic knowledge of Git, build systems, package managers, browsers, or other producers.
7. Physical filesystem modification is deferred until Execute.

This boundary can widen later if real use demonstrates a coherent need. It should not widen merely because another special filesystem phenomenon can be detected.

## Semantic research

The retired classification model is no longer part of code or workflow. Valuable observations about recognizable human scenarios—backup, migration, reorganization, archive, staging, and similar histories—are retained in `SEMANTIC-RESEARCH.md` as research.

Their value is to remind us what kinds of natural user actions real duplication histories evoke. They do not imply that Berries must classify a filesystem relationship before the user can operate on it.

## Questions intentionally left empirical

Worth observing during real Explorer use:

- whether recurring Exclude patterns motivate richer configuration/rules;
- whether same-Directory copies need specialized presentation beyond ordinary projections;
- whether any application-managed material needs a future visible-but-protected state rather than simple Exclude;
- whether current Suggest heuristics consistently find useful work across non-source corpora;
- whether saved sessions are valuable enough to justify persistence;
- whether semantic scenario hints add practical value once direct Pivot/selection/operations are familiar.

These are empirical product questions, not prerequisites for the current engine architecture.
