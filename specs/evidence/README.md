# /specs/evidence

One file per phase: `phase-N-evidence.md`, written by `qa-agent` at the end of that phase.

An evidence report contains **artefacts, not assertions** (`project.md` §7, agent 8):

- the full build output,
- the full test-run summary with individual test names and the pass/fail count,
- for compliance phases, the actual sandbox API request/response pairs (credentials redacted),
- screenshots where the criterion is visual (template designer, shell window, printed invoice),
- an explicit line per checkpoint criterion: criterion id, verdict, and the artefact that proves it.

"Implemented" is not evidence. "Tests pass" without the output is not evidence.

The project owner reviews this file before `CURRENT_PHASE` is advanced.
