# CoreBase v2.130p1 Demo Documentation Sprint — Brainstorm + Plan

**Date**: 2026-04-23 18:53 Asia/Saigon  
**Severity**: Medium (time-sensitive: demo tomorrow 2026-04-24)  
**Component**: Demo delivery, documentation, technical enablement  
**Status**: Plan created, ready for Phase 01 implementation

---

## What Happened

Brainstorm session to design 4-artifact demo package for techlead team showcasing CoreBase v2.130p1 post-release. Created plan structure for 11h sprint (~1.5 days):
1. **Phase 01** — Pitch Deck (16 slides HTML VN, 7h)
2. **Phase 02** — Architecture Cheatsheet (A4 handout 6-8 pages + FAQ, 3h)
3. **Phase 03** — QuickStart Handout (1-2 pages prereq + 5 commands, 2h)
4. **Phase 04** — Load Test Report (k6 3 scenarios, 3-5 pages, 3h)

---

## The Brutal Truth

Time is tight. Demo is **tomorrow 2026-04-24** but team doesn't have hands-on docs yet. V2.130p1 shipped with all code + changelogs, but zero guidance for techlead audience = high risk of "nice release but I don't understand the story."

Pitch deck is load-bearing — without it, walk into that room with no narrative. Cheatsheet gets teams unstuck. QuickStart + load test feed confidence that infra actually scales + works end-to-end.

---

## Technical Details

**Plan scope** (`plans/260423-1853-demo-corebase-docs/`):
- 4 phases, 11h total
- Phase 01 MUST complete (deck is prerequisite for live demo engagement)
- Phase 02–04 SHOULD complete (cheatsheet + quickstart + perf report add confidence, but deck is MVP)

**Key insight**: k6 infrastructure **already exists** (v1.6.1 installed, 18 test scripts in `tests/performance/`, identity/cases/workflow-load-test.js ready). Phase 04 budget reduced **50%** (6h → 3h). Expected output: 3–5 page report with 5 key metrics feeding Slide 14 (performance narrative).

**Artifact breakdown**:
- Pitch Deck: 16 slides, dark + light theme toggle, text-based Aequitas logo (no file dependency), Vietnamese throughout, "slide surprise" (tbd during implementation) for live engagement
- Cheatsheet: 6–8 pages A4, visual architecture diagrams + FAQ 10 questions, printed handouts
- QuickStart: 1–2 pages, prerequisites + 5 core commands (local setup → verify multi-tenancy)
- Load Test: k6 summary + 3 scenario results, feed Slide 14 metrics

**Audience**: Mixed (new + familiar faces) → deck must explain v2.130p1 value + architecture in 30 min, cheatsheet unblock implementation questions.

---

## What We Tried

**Considered (rejected)**:
- Live-coding demo script → too risky, cut to focus on deck narrative. User will live-demo during session.
- Swagger/Scalar API docs in deck → overkill, cheatsheet handles API reference.
- Docker E2E demo → deferred, would balloon timeline. Focus on k6 perf instead.

**Decided**:
- 4 self-contained artifacts, distributable before demo
- Deck as primary engagement tool (16 slides ≈ 30 min + Q&A)
- Cheatsheet as handout (take-home reference)
- QuickStart as low-barrier entry (get devs running locally)
- k6 load test as proof-of-concept (scales work)

---

## Root Cause Analysis

**Why this plan exists**: V2.130p1 is feature-complete but storytelling is missing. Release notes cover what shipped, but techlead audience needs **why it matters** (architecture narrative, integration story, performance proof).

**Why timing is critical**: Demo is tomorrow. Creating deck overnight is feasible (phase-01 is pure content creation, uses `/design` skill for HTML generation). After deck lands, cheatsheet + quickstart are parallel work.

**Why k6 budget dropped**: Lucky timing — perf test infra was already built during v2.129 cycle (per memory: `local-sqlserver-2022.md`). Phase 04 isn't new work, it's wrapping existing test runs into a report.

---

## Lessons Learned

1. **Demo readiness = separate concern from code shipping**. V2.130p1 code shipped clean, but without narrative deck, techlead meeting becomes "show me the code" instead of "let me tell you the story."

2. **Reuse infra aggressively**. k6 scripts already exist — don't build new perf tooling. Phase 04 wins because we didn't duplicate work.

3. **Distribute artifacts early**. Tomorrow: send deck + cheatsheet + quickstart to audience today (2026-04-23 night) so people walk in primed. Live demo becomes reinforcement, not first exposure.

4. **Audience segmentation matters**. Deck for story, cheatsheet for skeptics ("where's the cross-module data pattern?"), quickstart for eager devs ("I want to try this on my laptop").

5. **Plan drift is invisible**. Memory noted `260314-2208-local-performance-test-setup` = deferred, but k6 infra actually matured to prod-ready. When v2.131 ships, revisit that old plan status — reality moved ahead.

---

## Next Steps

**Immediate (Phase 01 — Deck)**:
1. Implement Phase 01 via `/ck:design` skill (HTML slide generation, VN content, 16 slides, dark + light theme)
2. Embed 5 key metrics placeholder (to fill from Phase 04)
3. Design "slide surprise" mechanism for live engagement (tbd by implementer)

**Parallel (Phases 02–04)**:
1. Phase 02 — Architecture cheatsheet (6–8 pages, diagrams, 10-Q FAQ)
2. Phase 03 — QuickStart (prereq section + 5 local setup commands)
3. Phase 04 — k6 load test summary report (run 3 scenarios, extract metrics → feed Phase 01)

**Finalize**:
1. Cross-link Phase 04 metrics → Phase 01 Slide 14
2. Distribute all 4 artifacts to team (2026-04-23 23:00 target)
3. Ready for live demo 2026-04-24

**Owner**: Implementation subagent (delegate via `/ck:plan` phase details)  
**Blocker**: None identified. k6 infra ready, design skill available, timeline is tight but achievable.

---

## Unresolved Questions (Resolved in Brainstorm)

- **Logo**: Text-based "Aequitas" typography (no file import needed) ✅
- **Theme**: Dark + light toggle button ✅
- **Distribution**: Send files 1 day before demo ✅
- **Slide surprise**: Type TBD (implementer decides) — low risk

---

## What Didn't Change

- No code committed
- No `docs/` updates required (existing README + roadmap sufficient)
- No memory writes (plan tracking only)
- Existing plans (cases-workflow-refactor, d2-workflow-update-rename) untouched
- No architecture changes

---

**Next Session**: Implement Phase 01 (Pitch Deck) — 7h sprint to delivery.
