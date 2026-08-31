# Berries Problem Boundary — Empirical Findings

This document records empirical findings about Berries' practical problem boundary. It is research context, not a competing workflow specification. Governing behavior is defined in `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`.

## What the experiments established

Berries reliably discovers real identical-content Groups. The difficult question is not whether files are byte-identical but which duplication is useful for the user to act on.

Real corpora contain both user-managed organization and application/generated structure. Content identity alone does not explain why copies exist.

Developer/source corpora were useful stress tests because they contain repositories, generated output, package material, copied source trees, saved web assets, and many repeated support files. They exposed analysis failure modes early.

## Group-level duplication can manufacture structural noise

Widely repeated Groups can induce many Directory Pairs and apparent Branch relationships even when the containing Directories do not represent one coherent user problem.

Early experiments showed that excluding a relatively small number of diffusely repeated files could collapse a large amount of downstream structural evidence.

The lasting lessons are:

    Group-centric and container-centric duplication are complementary viewpoints

and:

    low-level repeated Groups can manufacture weak higher-level structure

## Application-managed/generated material

Generated and application-managed material can be genuinely duplicated and can reveal real ancestry among Branches without being useful deletion targets.

Rather than encode producer-specific knowledge, Berries gives the user direct control over the logical Corpus through selected roots and Exclude.

## User-managed material remains the clearest target

Berries is particularly well matched to accumulated user-managed trees such as documents, photographs, downloads, old-machine copies, backups, archives, migrations, partial moves, reorganized trees, and staging/import areas.

Here the user is the authority on organization, and Group relationships can support coherent structural dispositions.

## Structural discovery findings

Exhaustive Branch Pair enumeration is unnecessary. Large-corpus experiments showed combinatorial growth without proportional user benefit.

Cheap Branch statistics plus targeted Seed/Counterpart search repeatedly surfaced recognizable high-value structures while remaining tractable.

The earliest "leverage" idea—maximize duplicate instances addressed by one Case—was useful but incomplete. Very broad scopes can have high resolving power while presenting an unclear question. A nearby narrower Branch Pair can be easier for the user to recognize and dispose of coherently.

This led to the current two-stage discovery idea:

    promising local Branches -> Seeds
    strong relationships to those Branches -> Counterparts / candidate Branch Pairs

Several Seeds must be evaluated before choosing a Suggestion because the best Branch Pair often does not come from the highest-ranked Seed.

## Rapid problem reduction

A central empirical result is that useful Case-level decisions can reduce the remaining problem extremely quickly. Large corpora with tens of thousands of duplicate instances could often be potentially resolved through only a handful of structural Cases.

This supports repeated local discovery and re-analysis rather than exhaustive global planning.

## Explorer rather than wizard

A wizard-like sequence proved impractical. Suggested scopes can be close to the useful human question without being exactly right.

The Explorer therefore treats Suggestions as attention aids. The user can broaden or narrow scopes, Pivot among projections, inspect nearby evidence, and follow recognizable structure before acting.

## Current practical boundary

1. Berries scans the roots the user chooses.
2. Configuration or interactive Exclude removes material from consideration.
3. Group-oriented resolution operations target current duplicate structure.
4. Unique files remain modeled because they affect structural statistics and can constrain valid operations.
5. General rename and arbitrary filesystem reorganization are out of scope.
6. Berries does not require semantic knowledge of Git, build systems, package managers, browsers, or other producers.
7. Physical filesystem modification is deferred until Execute.

## Cases, Situations, and dispositions

Case remains a valid domain concept: an objective bounded set of current-Portrait files containing duplication and considered together for one coherent disposition. The boundary limits disposition authority.

Situation remains useful optional semantic vocabulary for recognizable human histories such as backup, migration, reorganization, archive, and staging. Berries does not require Situation classification before direct Explorer operations.

The old mandatory Situation -> Resolution -> Disposition workflow machinery is not part of the current UI, but the semantic research remains relevant to judging whether a Case is comprehensible and what natural actions it evokes.

## Unique-file question

Earlier structural Cases could include unique files within their bounds. The current Portrait and statistical measures also retain unique files. Whether unique files should remain members of Cases is unresolved and must be reviewed separately; it should not be decided indirectly through terminology cleanup.

## Questions intentionally left empirical

- whether recurring Exclude patterns motivate richer configuration/rules;
- whether same-Directory copies need specialized presentation;
- whether semantic Situation hints add practical value;
- whether current Suggest heuristics consistently find useful work across varied corpora;
- whether unique files should remain members of structural Cases and how their counts should influence ranking;
- whether saved sessions are valuable enough to justify persistence;
- whether analysis recomputation should become independently validity/demand driven.
