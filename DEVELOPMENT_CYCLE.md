# Development Cycle - HFT Ecosystem

**Version:** 3.0 (Pragmatic)  
**Updated:** 2025-11-20  
**Philosophy:** Ship fast, iterate on reality

---

## TL;DR

**Процесс:**

1. Pick task из `docs/gemini3/roadmap/README.md`
2. TDD для bugs, pragmatic для features
3. Ship ASAP (даже at 80-90%)
4. Learn from production
5. Iterate

**Структура:**

- **5 фаз** (0→4, убрали backtesting и лишнее)
- **Sprint-based** (1-2 недели)
- **Solo developer** + AI assistant (Gemini)

**Главное:** Reality > Plan

---

## Roadmap Structure

### Phases (Revised)

| Phase | Priority | Goal |
|-------|----------|------|
| **0: Foundation** | 🔴 CRITICAL | Stability (no crashes) |
| **1: Brain** | 🔴 CRITICAL | Intelligent trading (signal detection) |
| **2: Monitoring** | 🟡 HIGH | Production observability |
| **3: Latency** | 🟡 HIGH | Speed = competitive edge |
| **4: Automation** | 🟢 MEDIUM | 24/7 operation |
| **5: Web UI** | ⚪ LOW | Deferred (CLI sufficient) |

**REMOVED:**

- ~~Phase 0.5: Backtesting~~ → Test live с $100 instead
- ~~8 phases~~ → 5 phases (merged duplicates)

См. `docs/gemini3/roadmap/README.md` для деталей.

---

## Current Status

**Phase:** 0 (Foundation) - ✅ 100% COMPLETE  
**Sprint:** 2 - ✅ COMPLETE (36/36 tests, 5.5h vs 8h estimated)  
**Next:** Pre-Phase Consilium → Phase 1 (Brain)

---

## Task Workflow

### Lifecycle

```
⏸️ PENDING → 🟡 IN PROGRESS → ✅ COMPLETE
```

**Keep it simple:** No review step, no blocked state tracking (solo developer)

### Task Format (in phase files)

```markdown
## Task X.Y: [Title]

**Problem:** [What's broken]
**Solution:** [How to fix]
**Target:** [File to change]
**Priority:** HIGH/MEDIUM/LOW
**Estimate:** [Hours]
```

---

## Roles

### Developer (You)

- Pick tasks
- Write code
- Ship to production
- Final authority on all decisions

### Gemini (AI)

- Update roadmap/phase files
- Validate code vs docs
- Suggest solutions
- No authority (advisor only)

См. `GEMINI.md` для Gemini role details.

---

## Tools

### Essential

```bash
# Code
dotnet test              # C# tests
pytest                   # Python tests

# Docs (Gemini only)
python get_structure.py  # Update project structure
```

### Documentation

**Location:** `docs/gemini3/`

```
└── roadmap/
    ├── README.md              # Main backlog ← START HERE
    ├── phase-0-foundation.md  # Phase details
    └── phase-X-*.md
```

**Rule:** Update phase file when task done (mark ✅ COMPLETE)

---

## Testing

### TDD (Test-Driven Development)

**Use for:**

- ✅ Bug fixes (concurrency, data corruption)
- ❌ NOT for new features (too slow)

**Cycle:**

```
1. RED: Write failing test
2. GREEN: Minimal fix
3. REFACTOR: Clean up
```

### Integration Tests

**When:** Before production deploy  
**What:** Run live 2-3 days, monitor for crashes  
**Pass:** 0 crashes = good to go

---

## Best Practices

### Code

1. **Atomic commits** - one logical change per commit

   ```bash
   git commit -m "Fix LruCache mutation (Task 0.3)"
   ```

2. **Immutability** - use `record` for DTOs (prevents bugs)

3. **Ship at 80%** - don't wait for perfection

### Roadmap

1. **Estimates ≠ reality** - Sprint 1: 3h actual vs 1 week estimated (OK!)
2. **Bugs → backlog** - discovered during sprint → add to next sprint
3. **Phase validation** - basic check, not comprehensive report

### Documentation

**Minimal approach:**

- ✅ Update `roadmap/README.md` (progress)
- ✅ Update phase files (task status)
- ❌ No validation reports
- ❌ No architecture docs (unless team scales)

---

## Phase Transition

**From Phase 0 → 1:**

1. All Phase 0 tasks ✅ COMPLETE
2. Tests passing (basic check)
3. Ship to production
4. Monitor 2-3 days
5. IF stable → **Pre-Phase Consilium** → Phase 1
6. IF issues → hotfix → re-deploy

**Don't wait for 100% perfect** - 80-90% is enough.

---

## Pre-Phase Consilium 🎯

**Обязательный процесс перед началом КАЖДОЙ фазы.**

**Цель:** Убедиться что техническая реализация соответствует бизнес-ожиданиям.

### Когда проводим

- ✅ После завершения предыдущей фазы
- ✅ Перед первым коммитом новой фазы
- ⏱️ Duration: ~30 минут

### Структура консилиума

#### 1. Business Expectations Review (5-10 мин)

**Вопросы:**

- Что должно измениться в **бизнес-метриках**? (fill rate? latency? P&L?)
- Какая **конкретная проблема** решается?
- Как измерим **success**? (acceptance criteria в числах)

**Output:** Clear business goal в метриках

---

#### 2. Current State Validation (10-15 мин)

**Действия:**

- Провалидировать **текущий код** (что уже есть?)
- Найти **gaps** между "как есть" и "как должно быть"
- Выявить **tech debt / blockers**

**Output:** Gap analysis (что нужно добавить/изменить)

---

#### 3. Phase Definition (5-10 мин)

**Уточнить:**

- Tasks: не абстрактно ("port algo"), а **конкретно** ("port zero_crossing.py lines 45-120")
- Acceptance criteria: **measurable** (latency <10ms, not "fast")
- Estimate: **realistic** (based on previous sprints)

**Output:** Updated phase file с конкретными tasks

---

#### 4. Risk Assessment (5 мин)

**Вопросы:**

- Что может **пойти не так**?
- Какие **assumptions** делаем? (e.g. "Python algo profitable")
- **Plan B** если не получится?

**Output:** Risk mitigation plan

---

### Результат консилиума

**Deliverables:**

1. ✅ Updated `phase-X-*.md` с конкретными tasks
2. ✅ Clear acceptance criteria (метрики)
3. ✅ Risk mitigation план
4. ✅ Go/No-Go решение

**Критерий успеха:**

- Developer понимает **зачем** (business value)
- Gemini понимает **что** делать (concrete tasks)
- Оба понимают **как** измерить success (metrics)

---

## Next Steps

**This week (Sprint 2):**

- [ ] Task 0.3: LruCache immutable (1h)
- [ ] Task 0.4: Fix tests (2h) - skip if too hard
- [ ] Task 0.5: Health Monitor (2h)
- [ ] **SHIP collections**

**Next 2 weeks (Phase 1):**

- [ ] Port zero-crossing detector
- [ ] Create signals API
- [ ] Live test: $100 capital, 1 week
- [ ] Decision: GO (scale) or NO-GO (pivot)

---

## Key Decisions

**What we removed (Skeptic feedback):**

- ❌ Phase 0.5 (Backtesting) - live test > backtest
- ❌ Extensive docs - solo dev doesn't need 30 files
- ❌ Validation reports - ship faster instead
- ❌ 100% completion gates - 80% good enough

**What we kept:**

- ✅ Phase-based planning (clear milestones)
- ✅ TDD for bugs (quality where it matters)
- ✅ Task tracking (phase files)
- ✅ Ship-first mindset

---

## Links

**Roadmap:**

- [`docs/gemini3/roadmap/README.md`](docs/gemini3/roadmap/README.md) ← YOUR MAIN FILE

**Roles:**

- [`GEMINI.md`](GEMINI.md) - AI assistant role

**Proposals (architecture decisions):**

- [`docs/gemini3/proposals/`](docs/gemini3/proposals/)

---

**Version:** 3.0 (Pragmatic)  
**Author:** Solo Developer + Gemini
**Updated:** 2025-11-20
