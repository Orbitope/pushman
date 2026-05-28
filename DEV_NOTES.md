# Pushman — Development Notes & Decision Log

A chronological record of design decisions, retrospectives on failed approaches,
and the reasoning behind specs that live in `PLAN.md`. PLAN tells you *what* the
current state is; this file tells you *why* and *how we got here*.

Intended to seed an eventual writeup of how the project went, and to give a
loaded-from-cold reader (or future-me after a `/compact`) enough context to
reason about the project without re-deriving from the spec.

---

## 2026-03-26 — Project started

Initial Unity 2D project committed. Basic scene, Input System action asset, blank
scripts folder. No gameplay code yet. ~7-week gap before active development resumed
in May.

---

## 2026-05-19 — First working gameplay session

Session goal: get from "empty Unity project" to a playable human-vs-bot test scene.

### What was built
- Full player state machine: `PlayerMovingState`, `PlayerChargingState`, `PlayerPushingState`,
  `PlayerBlockingState`, `PlayerDodgingState`, `PlayerStunnedState`
- `ArenaManager` with ring-out detection, round scoring, respawn logic
- `IPlayerBrain` interface; `HumanBrain`, `ChaseBotBrain`, `StandingBotBrain`, `DodgingBotBrain`
- `RLAgentBrain` with `ObservationProfile` SO and `BotPersonality` reward hooks
- `PushmanSetup` editor tooling: scene builder, prefab saver, sprite generator

### First bugs hit immediately

**Input System error** — `HumanBrain` used `UnityEngine.Input` (legacy) while the project
had the new Input System package (1.19.0). Threw `InvalidOperationException` at runtime.
Fixed by rewriting `HumanBrain` to use `Keyboard.current` and `Mouse.current`.

**Observation space mismatch** — `BehaviorParameters.VectorObservationSize` was left at 1
(Unity's blank default). `CollectObservations` emitted 13 values. This produced a silent
training shape mismatch that would have caused garbage training without an obvious error.
Fixed by wiring `profile.ComputeSpaceSize()` into `PushmanSetup.ConfigureBehaviorParameters`.

**Lesson:** these are the two most common "project just added ML-Agents" failure modes.
Both are silent if you don't watch the console carefully. The obs-size one is especially
dangerous because training appears to run; it just trains garbage.

### Visual setup completed
- Circle sprite + hand sprites (push fist, block shield) generated procedurally in
  `PushmanSetup`
- Arena boundary ring drawn as an annulus texture
- Stamina bars initially as world-space Canvas/Slider — later replaced

---

## 2026-05-20 — Gameplay bugs, stamina overhaul, pre-training hardening

### Three bot brain bugs (all subtle)

All three scripted bots (`ChaseBotBrain`, `StandingBotBrain`, `DodgingBotBrain`) had
a shared anti-pattern: using `cachedMove.y` (movement direction) as a proxy for "push
now." This caused movement and action signals to collide unpredictably.

`ChaseBotBrain` specifically had a `dist` calculation bug — it computed distance from
the *normalized* direction vector (always 1.0), so the bot thought the opponent was
always 1 unit away and ran at full speed even when already on top of them.
Push decision was triggered every `Update` frame at 30% probability ≈ fires every
3 frames ≈ effectively always pushing, never walking.

Fix: dedicated `wantPush` / `wantDodge` flags; proper `dist` before normalizing;
action-interval timers.

### Stamina system evolution (three versions in one day)

**Version 1 (initial):** World-space Canvas/Slider bars wired to a `value` property.
Problem: the Slider's fill rect wasn't correctly configured, so the bar appeared to
full at all times regardless of stamina.

**Version 2:** Replaced with `SpriteRenderer` quads scaled by `fillAmount`. Worked
but was visually noisy and world-space (rotated with the player on camera zoom).

**Version 3 (final):** Screen-space Overlay canvas with `Image.Type.Filled`. P1
green bottom-left, P2 red bottom-right. This is the standard Unity HUD pattern.

`Image.Type.Filled` has a gotcha: it requires a `sprite` assigned or `fillAmount`
is silently ignored and renders a full solid block. Hit this bug, fixed by
procedurally generating `StaminaBar.png` and assigning it.

Stamina costs rebalanced this session:
- Dodge cost raised 20 → 40 (richer decision point — tapping vs hoarding)
- Push scales with charge: 20 base → 40 at full charge
- Regen restricted to `Moving` state only (you have to disengage to recover)

### Overdraft dodge (added and later removed — see 2026-05-23)

Added `allowDodgeOverdraft` to `CharacterStats`: players can dodge even when stamina
goes negative, paying a `overdraftPauseDuration` (3s → tuned to 1.5s) regen lockout.
Intended as a tactical "desperation escape." Later removed (see Phase 3 v1
retrospective) after agents learned to spam it and stay permanently negative.

### Seven pre-training risk items

Before declaring the scene "training-ready," did a code review that identified 7
specific risk areas likely to cause silent failure or bad training:

1. Obs space mismatch (already fixed above)
2. Dodge bypasses block — confirmed intentional (dodge counters block; completes
   the RPS triangle)
3. `rb.linearDamping` active during dodge — the impulse was being damped immediately,
   so dodges barely moved the player. Fixed: zero damping in `PlayerDodgingState.EnterState`,
   restore in `EndState`.
4. Dodge `wantDodge` consumption timing — flag could be consumed by `GetDodgeInput()`
   before the state machine picked it up. Fixed with a sticky flag pattern.
5. Bot brain `GetComponent` calls every `Update` — fixed with `Awake` caching
6. Ring shrink visual — `ArenaBoundary` needed to scale its `SpriteRenderer` each
   `FixedUpdate` to match `currentRingRadius`
7. `CharacterStats.cs` defaults out of sync with `DefaultStats.asset` — fixed by
   syncing them in `InitCharacterStats`

---

## 2026-05-21 — Phase 1 reward discovery, LSTM discovery, RPS rework

### Phase 1 weakness root cause

Trained `Pushman_Fast_v1` (1M steps). It reached mean reward ~1.3 but played poorly —
facing the opponent and not engaging. Root cause: `facingOpponentMultiplier = 0.001`.
Over a 5000-step episode: `0.001 × 0.7 × 5000 ≈ 3.5`, which was **3.5× the win reward
of 1.0**. The agent optimized the dominant gradient, which was "face the opponent."
Since facing rewards are available immediately (every step) and win rewards are sparse
and discounted, the policy settled in a face-and-survive local optimum.

Fix: `facingOpponentMultiplier` 0.001 → 0.0001 (one order of magnitude), `winRound`
1.0 → 1.5. This is the lesson from Section 4's opening note: per-step shaping over a
full episode must stay well under terminal magnitude.

### LSTM was never running — silent config bug

Discovered while preparing `ppo_selfplay.yaml` for Phase 2: all configs had been using
```yaml
use_recurrent: true
```
This is mlagents 0.x syntax. mlagents 1.1.0 silently ignores it and trains without
LSTM. The actual 1.1.0 syntax is:
```yaml
network_settings:
  memory:
    memory_size: 128
    sequence_length: 64
```
Every training run before Phase 2 (`Pushman_Fast_v1`, `Pushman_Bot_v2`) was feedforward
only. Neither run was harmed by this — for simple reactive play feedforward is sufficient —
but the capacity wasn't there for temporal reasoning (charge timing tells, block-break
windows). Fixed in both configs and standardized architecture: 128 hidden / 2 layers /
memory 128 across all runs so future warm-starts don't hit shape mismatches.

### RPS mechanics rework (why Phase 2 superseded Phase 1)

`Pushman_SelfPlay_v1` (20M steps under old mechanics, archived) had converged on
dodge-spam. Investigation revealed the mechanics didn't actually form a rock-paper-scissors
cycle: blocks didn't stop pushes (push was always beneficial), so the only counter to
push was to dodge it, and dodge was both offense and defense. No reason to ever block.

Rework:
- Block now **stops** a push (pusher enters `Stunned` state briefly; blocker stays
  `Blocking`)
- Dodge breaking a block is rewarded (`dodgeEvasionReward`)
- Dodge connecting with a non-blocker opponent is rewarded (`dodgeHitReward`)
- Dodge forces reduced (Default 18 → 12, Speedster 22 → 16) — not a ring-out tool

The RPS triangle is now: **Push** wins vs Dodge (forces dodge receivers back); **Block**
wins vs Push (push is stopped, pusher is stunned); **Dodge** wins vs Block (breaks the
block, rewards the dodger). Each state has a counter, so the agent has to read opponent
state rather than picking one dominant action.

### Match logging system

At this point all health signals during training were subjective ("does it look
good?"). Before launching Phase 2's 20M-step run, added:

- `MatchLogger.cs` — logs every completed match (winner/loser personality + stats +
  duration) to a timestamped CSV. Buffered writes + 15s timed flush; zero hot-path I/O.
- `tools/report.py` — reads TensorBoard tfevents + newest match CSV, prints a win-rate
  matrix and training health summary. Run on demand: `python tools/report.py [run_id]`.

The match log proved essential in Phase 2e (CharacterStats balance) where it revealed
Heavyweight at 75% win rate — a signal that would have been invisible from reward curves
alone.

---

## 2026-05-22 — Phase 2 complete; CharacterStats rebalanced

`Pushman_Master_v1` ran 20M steps of Aggressive self-play under LSTM 128/2 with the
corrected RPS mechanics. Results:

- Reward -0.75 → +0.19 (converging positive — agents win more than they lose vs random play)
- Episode length fell 216 → 134 (real ring-outs happening, matches ending)
- ELO oscillated by self-play snapshot cycle (expected — the opponent pool rotates)

**CharacterStats balance issue** (found via match log, 56,845 matches):
- Heavyweight: 75% win rate — dominant
- Speedster: ~31% win rate — nearly unplayable
- Default: reasonable but overshadowed

Root cause: Heavyweight had `pushForce=14` AND `pushChargeMultiplier=2.0`, which at
full charge produced `14 × 2.0 = 28` force. Combined with high mass (harder to ring
out), it one-shotted Speedsters at near-ring velocity. Fix: Heavyweight `pushForce`
14→9, `pushChargeMultiplier` 2.0→1.5; Speedster `pushForce` 5→7.

Warm-start into Phase 3 verified via a 50k-step test run (`ppo_warmstart_test.yaml`)
— checkpoint loads clean, reward starts at +0.1 not -0.7, confirming the v1 weights
are a good initialization.

---

## 2026-05-22 — Phase 2 → Phase 3 v1 transition

Phase 2 (`Pushman_Master_v1`) trained 20M steps of Aggressive self-play under
LSTM 128/2. ONNX integrated; CharacterStats rebalanced post-run (Heavyweight
nerf, Speedster buff). Warm-start path into Phase 3 verified.

Decision: Phase 3 v1 trains 5 separate behaviors (Aggressive / Defensive /
Evasive / Balanced / Counter) on a round-robin scene (15 pairings × 3 arenas).
Each behavior warm-starts from the Master_v1 checkpoint. Diversity comes from
per-personality reward streams (BotPersonality SOs).

---

## 2026-05-23 — Phase 3 v1 retrospective (what went wrong)

Trained `Pushman_RoundRobin_v1` for 5M steps per behavior. Outcome: **unusable**.
All 5 personalities collapsed to a dodge-dominant, no-push policy with
semi-random blocking. The user noticed it watching the showcase scene — visually
obvious that pushes never happened.

### Root causes (six compounding)

1. **Charging was a tactical loss.** `pushChargeTime = 1s` at 50% movement speed
   gave the opponent a full second to dodge into the charging agent for a
   `dodgeHitReward`. Every push attempt was punished early in training. Push
   was abandoned before it could be properly explored.
2. **Dodge-hit was as rewarding as push-hit, 5× easier to execute.**
   `dodgeHitReward` (0.05–0.2) was comparable to `landPushHit` (0.1–0.35), but
   dodge collapses to "press 3 toward opponent" while push requires a
   multi-step temporal sequence (hold / release / stay-in-position /
   opponent-still-there). Identical expected reward, dramatically lower expected
   effort. Dodge dominated on EV alone.
3. **No miss penalty on dodge whiffs.** Push out-of-stamina was penalized; dodge
   that hit nothing was free. Asymmetric punishment made dodge spam strictly
   safe.
4. **Block was untrainable.** Block only stops a push, and pushes never
   happened in the converged policy. The network never saw enough push-vs-block
   events to learn timing. Semi-random blocking was the rational response to
   "no signal."
5. **Force values inverted the intent.** `dodgeForce = 12` vs `pushForce = 8`
   meant a tap-push sent opponents 4u while a dodge sent them 6u. Even an
   agent that did push discovered it was less lethal than dodging. Only a
   full-charge push (force=20) was stronger — and full charge required the 1s
   vulnerability window. Compound trap.
6. **Win signal was buried in dense rewards.** `winRound = 1.5` at `gamma = 0.99`
   over a ~750-step episode meant the win signal at step 0 contributed
   `0.99^750 ≈ 0.0005` of its face value to the gradient. Meanwhile dense
   per-step rewards (facing, edge pressure, time penalty) plus per-hit rewards
   accumulated to 1–3 per episode regardless of win/loss. The terminal
   objective barely registered.

The lesson is roughly: when the *easy* action gives the *same* reward as the
*hard* one, the network never learns the hard one. And when the terminal
objective is in the same order of magnitude as the dense shaping, dense
shaping becomes the objective.

---

## 2026-05-23 — Reward design iteration (what we tried)

The fix went through three drafts. Recording each because the abandoned ones
are instructive.

### Draft 1: Just rebalance the rewards

Initial impulse: make `landPushHit` bigger (0.35 → 0.5), `dodgeHitReward`
smaller (0.10 → 0.03), add a `dodgeMissedPenalty` (-0.05), boost
`successfulBlock` (0.05 → 0.15). Keep win at 1.5.

User pushback: "are pushes too weak mechanically?" and "what are our ring-out
rewards/penalties? Maybe we need to increase those significantly too. The goal
isn't to get hits and fight but to ring out."

This was correct. Just rebalancing reward magnitudes didn't address the
underlying mechanical imbalance (dodge force ≥ push force at low charge) or
the fact that win at 1.5 was numerically dominated by accumulated dense
rewards.

### Draft 2: `ringOutContribution` reward (rejected)

Idea: track `lastHitBy` on each Player. When a player rings out, award the
agent that dealt the last hit a `ringOutContribution` reward (1.0–1.5). This
would close the credit-assignment gap between "I hit them" and "they ringed
out."

Implemented it. Then rejected it the same session for three reasons:

1. **The agent has no observation of `lastHitBy`** — it can't learn what signal
   it's optimizing. The reward fires "magically" from the agent's POV.
2. **Attribution is ambiguous.** Hit at t=1s, ring-out at t=6s might be due to
   the hit's momentum, or might be due to the victim's later mistake. The
   reward fires either way.
3. **Mutual dodge collisions confuse attribution.** Both players become each
   other's `lastHitBy` simultaneously.

It was a hack to bridge credit assignment, and bridges that the agent can't
observe are usually worse than they look.

### Draft 3: `edgeHitBonus` (kept)

Replacement design: on any successful push/dodge hit, the brain adds
`edgeHitBonus × (opponent_distance_from_center / current_ring_radius)` on top
of the base hit reward. A hit at the center is ~0.05; a hit at the ring edge
is ~0.55.

This is the right idea because:
- The reward is attached to a *specific action the agent took* (hit landed),
  not a state the agent doesn't observe (`lastHitBy`).
- It naturally rewards "hits that almost ringed them out" without needing
  attribution.
- No ambiguity: the hit either lands at the edge (big bonus) or it doesn't.
- The agent can directly learn the gradient: "pushing someone toward the edge
  → next hit pays more."

### Win/loss scale: 1.5 → 4.0 → 10.0

User feedback after the `edgeHitBonus` swap: "I'm still concerned our win
reward is too small and will continually get lost between battles. It's the
most important outcome and it's what they should be optimizing for."

This is the right concern. With ~10–15 hits per battle and `landPushHit ≈ 0.2`,
dense reward accumulation per battle was 2–4. Win at 4.0 barely dominated.

Going to **win = 10.0** with **base `landPushHit = 0.05`** and most of the hit
reward coming from `edgeHitBonus` cleanly separates the signals:

- Center hits: 0.05 (exploration nudge, basically noise)
- Edge hits: ~0.55 (meaningful but still <10% of win)
- Dense reward budget per busy 750-step match: ~2.5 total
- Win: 10.0 → dominates dense rewards by ~4×

Survives gamma discounting better too — even at step 0 of a 750-step
episode, `10 × 0.99^750 ≈ 0.005`, still bigger than the per-step shaping
contribution at that point.

---

## 2026-05-23 — Mechanics rebalance

Changes:

| Stat | Default before → after | Heavyweight | Speedster | Reason |
|---|---|---|---|---|
| `pushChargeTime` | 1.0 → 0.25s | 1.2 → 0.3 | 0.7 → 0.2 | Shorter vulnerability window; tighter credit assignment |
| `pushHitRadius` | 0.6 → 0.8u | — | — | More forgiving hit detection (effective reach 1.8 → 2.1u center-to-center) |
| `dodgeForce` | 12 → 9 | 14 → 11 | 16 → 12 | Dodge sends opponent 4.5u (was 6u) — rarely rings out alone |
| `pushForce` | 8 → 12 | 9 → 14 | 7 → 10 | Tap push now stronger than dodge; full charge = 30 force (15u travel = guaranteed ring-out) |

The force flip is the most important change. Previously dodge was strictly
stronger than tap-push, so even an agent that explored push found it
underwhelming and reverted. Now the hierarchy is clearly:

- Dodge (force 9) — 4.5u travel — primarily evasive
- Tap push (force 12) — 6u travel — comparable to old dodge
- Full-charge push (force 30) — 15u travel — guaranteed ring-out from most positions

Worth noting: charging continues to allow movement at 50% speed (not
stationary). My earlier description of charging as "holding still" was wrong
and made me overestimate the vulnerability cost. The user caught this. With
0.25s charge time + 50% movement, push is genuinely viable mid-engagement.

---

## 2026-05-23 — No warm-start from v1

Decision: Phase 3 v2 trains from scratch, not from v1 weights.

Rationale: the v1 weights encode a value function estimated under reward
magnitudes (~±2) that are now 6.7× smaller than the new scale (±10).
Action-value preferences are now sign-flipped on push vs dodge (force values
swapped). The new `edgeHitBonus` is a reward dimension that didn't exist in
v1's training signal.

Warm-starting would spend ~500k steps just re-normalizing the value function
and un-learning dodge dominance before any new strategy could form. Starting
fresh from random init is faster and avoids the risk that the dodge-spam
attractor never fully washes out.

Contrast with v2 → v2.5 warm-start (planned, see below): that one only shifts
the observation distribution. Same actions, same reward magnitudes, same
mechanics. That's the tractable kind of transfer learning. v1 → v2 isn't.

---

## 2026-05-23 — Shared network architecture (decided)

v1 trained 5 separate behaviors (one per personality). v2 trains one shared
`Pushman` behavior with self-personality fed in as observation.

### Why shared

- 5× more experience per gradient update (all 90 agents feed one network
  instead of one-fifth of them feeding each of five networks).
- Network can share learned features across personalities — e.g., "how to
  predict where a charging opponent will be" is the same skill for all 5
  personalities, no reason to learn it 5× independently.
- Aligns with the original PLAN vision of "a single master network that adapts
  to any CharacterStats" — now extended to BotPersonality too.

### Why only SELF personality in the observation, not opponent

Original draft: include both `selfPersonalityId` (5 dims) and
`oppPersonalityId` (5 dims). User pushback: "I want a human to be able to play
against it" — and humans don't have a valid personality label.

The right framing: personality is a *reward-shaping label*, not a behavior
descriptor. The agent should learn to react to opponent *behavior* (state,
velocity, position, stamina, distance) — those are universal and visible
regardless of opponent type (bot, different personality, human).

Self-personality stays in the observation because each bot has its own reward
stream and needs to know "which value function am I optimizing." Without it,
the network can't predict its own return correctly.

Additional benefit: the network can't shortcut by reading a label and assuming
a playstyle. It has to actually attend to opponent behavior. More robust under
distribution shift.

### Training mode: round-robin only, no self-play

ML-Agents has a `self_play:` block (snapshot windows, swap rates, ELO). Skipped
for v2. The 5 personality reward streams + self-personality observation already
give within-network diversity. Self-play adds complexity (opponent-pool
variance, ELO tracking) without clear benefit at this stage. Defer until/unless
a personality collapses or refuses to differentiate.

---

## 2026-05-23 — Phasing decision: two-phase (v2 + v2.5)

### The question

Should v2 train clean (humanization = 0) and v2.5 fine-tune humanization on
top, or should we do domain randomization across difficulty levels in v2 in
one shot?

### Analysis (pros/cons)

**One-phase (everything in v2):**
- ✓ Single training run, single ONNX
- ✓ Robust strategies emerge organically — network can't develop a
  perfect-obs-only policy
- ✗ Stacks experiments — if v2 fails you can't tell whether it was rewards,
  shared network, or humanization that broke it
- ✗ Multi-modal observations slow early learning (gradient signal noisier)
- ✗ No reusable artifact for future experiments
- ✗ Reward tuning iteration cost is full 10M-step rerun every time

**Two-phase (v2 clean, v2.5 fine-tunes humanization):**
- ✓ Clean debugging — v2 either validates the core changes or doesn't
- ✓ v2 is a reusable checkpoint for *every* future fine-tune (more tiers,
  reward variants, new personalities, stat variants)
- ✓ Curriculum effect: agent learns core game first, then learns robustness
- ✓ Faster reward iteration (if v2 reveals reward problems, rerun v2 clean
  without paying humanization cost)
- ✗ Two runs (v2 ~6h, v2.5 ~2h)
- ✗ v2 alone isn't a shippable model (no difficulty options)
- ✗ Small risk: v2's clean-trained strategies don't transfer well — but
  obs-distribution shift is the *easy* kind of transfer; well-established as
  tractable

### Decision: two-phase

The strongest argument is iteration speed over the next month. v2 is making
four big changes simultaneously (forces, rewards, shared network, training
from scratch). The largest risk is one of those changes being wrong. With
two-phase, a v2 failure costs ~6 hours and the diagnosis is obvious. With
one-phase, a failure costs ~12 hours and you also have to disentangle "is it
the rewards or the humanization?"

The case for one-phase was real but weaker — it's not actually a "retrain
avoidance," it's a fixed 2h-shorter total wall-clock at the cost of
debuggability and reusability.

---

## 2026-05-23 — Humanization tier values (iterated)

### Initial guess (rejected as too aggressive)

My first proposed tier table:

| Player label | humanization | Noise σ (u) | Delay (frames) |
|---|---|---|---|
| Easy | 0.0 | 0.8 | 12 |
| Medium | 0.33 | 0.5 | 8 |
| Hard | 0.66 | 0.25 | 4 |
| Expert | 1.0 | 0 | 0 |

Noise σ = 0.8u was about a full body width of positional uncertainty.

User pushback: "Reduce the noise levels on all of them a bit. It should know
where you are, it's mostly just about having a reasonable reaction time."

### Final values

| Player label | humanization | Noise σ (u) | Delay (frames @ 50Hz / ms) |
|---|---|---|---|
| Expert | 0.0 | 0 | 0 / 0 |
| Hard | 0.33 | 0.05 | 4 / 80 |
| Medium | 0.66 | 0.10 | 8 / 160 |
| Easy | 1.0 | 0.15 | 12 / 240 |

The reasoning the user articulated: Pushman is a clean-info game (the player
can see exactly where the opponent is on screen). Realistic "human" behavior
isn't perception-limited — it's reaction-time-limited. A casual player isn't
*bad at seeing* where the opponent is; they're *slow to react* to changes.

So humanization is delay-dominant. Noise stays as a minor tremor representing
"slight aim wobble under pressure" rather than "can't see the opponent
clearly."

### Note: humanization mapping convention

`humanization = 0` is Expert (no degradation), `humanization = 1` is Easy
(maximum degradation). Player-facing labels are inverted from the scalar:
high humanization → easy bot → easy for player. Use "humanization" in code,
"difficulty" in UI, and don't confuse them.

---

## 2026-05-23 — Humanization observation requirement (caught late)

When first drafting the humanization design, I forgot to include `humanization`
itself as an observation to the agent. This would have been a silent failure
mode: the network would have to marginalize across all four tiers (it sees
both clean obs in some episodes and noisy-delayed obs in others, with no
signal differentiating them) and end up with a mediocre averaged policy that
plays poorly at every tier.

Caught it during the critical-pros/cons writeup the user requested. Adding
1-dim `humanization` to the observation lets the network condition policy on
its current degradation level: "right now my obs are clean, play sharply"
vs "right now they're noisy and delayed, play conservatively."

The lesson: anytime you randomize a parameter during training, the agent
needs to *observe* that parameter unless you specifically want the agent to
generalize across it without knowing. Domain randomization without the
randomized parameter as input = averaging, not robustness.

---

## 2026-05-23 — Sampling: discrete over continuous (decided)

For Phase 3.5, sample `humanization` from the discrete set
`{0.0, 0.33, 0.66, 1.0}` per agent per episode, not continuous `Uniform(0,1)`.

### Why discrete

- The shipped game exposes 4 difficulty tiers (Easy / Medium / Hard / Expert),
  not a continuous slider. Optimizing performance at those exact values
  matters more than interpolation between them.
- Cleaner per-tier verification: sweep four points, characterize behavior.
- Less variance in training observations than continuous, faster convergence.
- Cheaply reversible: if a 5th tier or continuous slider is needed later,
  fine-tune v2.5 again with the new sample set.

### Why not continuous

- Performance at any specific tier slightly worse than the discrete-trained
  equivalent (specialization vs generalization tradeoff).
- Continuous's main advantage (exposing a 0–100% slider) isn't a current
  product requirement. Don't optimize for hypothetical UX.

The decision is much less consequential than the phasing decision and can be
revisited later.

---

## 2026-05-23 — Profile_B (FOV-restricted) dropped

Original plan had three observation profiles:
- A: perfect info (all observations on)
- B: FOV-restricted (opponents outside 90° forward cone padded with zeros)
- C: humanized (noise + delay, stub only — never implemented)

Decision: drop B entirely. The FOV mechanic adds complexity (`useFOV` flag,
angle param, the visibility-gating branch in `CollectObservations` that
zero-pads invisible opponents) that we wouldn't use. The FOV branch is also
a sparsity pattern the network would have to learn to handle but doesn't
need to.

Keep A (for v2 training) and C (for v2.5 training). C must be properly
implemented before v2.5 — the existing flags are a stub with no code applying
noise or delay.

---

## 2026-05-23 — Phase 3 v2 results

`Pushman_Shared_v2` ran 10M steps. Key metrics:

| Metric | Start | End | Notes |
|---|---|---|---|
| Cumulative reward | -3.64 | -0.02 | Near zero = balanced zero-sum equilibrium |
| Episode length | 90 | 116 | Peaked at 381 mid-run, then fell — agents learned to end matches |
| Entropy | 4.68 | 3.68 | Max is 4.68 for this action space; not collapsed |
| Value loss | 7.27 | 6.70 | Dipped to 2.0 at ~2M steps, crept back up |

**Win-rate matrix (28,761 matches):**
```
             Aggressive  Balanced  Counter  Defensive  Evasive  Overall
Aggressive       --         51%      57%      59%        41%      51.2%
Balanced        49%          --      53%      38%        48%      48.2%
Counter         43%         47%       --      50%        52%      48.9%
Defensive       41%         62%      50%       --        56%      51.4%
Evasive         59%         52%      48%      44%         --      50.5%
```

All 5 personalities are distinct with meaningful asymmetries. The range 48–51%
overall means no personality is dominant or dominated — good balance for a shared
network.

Notable structure in the matrix:
- Evasive beats Aggressive (59%): dodge evades push spam
- Aggressive beats Defensive (59%): aggression overwhelms passive blocking
- Defensive beats Balanced (62%): strongest single asymmetry; balanced style gives
  Defensive too many chances to block
- Counter vs Evasive (52%): Counter punishes dodge whiffs

**Match duration concern:**
Average 16.3s but median 11.5s and 45.7% of matches under 10 seconds. Distribution
is bimodal — lots of quick ring-outs AND a cluster at 30-45s (ring-forced conclusions).
A match under 10s can be skill (clean ring-out from a good push) or luck (early stumble).
This is worth watching in the showcase scene. Could indicate `pushForce=12` is strong
enough to ring-out on a single tap from a poor starting position.

**Value loss creep:**
Value loss bottomed at 2.0 around 2M steps then climbed steadily to 6.7 by 10M steps
— it wasn't leveling off at the end. In a shared network trained on 5 different reward
streams simultaneously, the value function must average across very different return
distributions. This is structurally harder than a single-personality network. Not
catastrophic (the policy was still improving, entropy still decreasing), but suggests
the network could benefit from another 3-5M steps for the value function to stabilize.

Phase 3.5 warm-starts from this checkpoint, which adds more training time implicitly.

**Verdict:** adequate for Phase 3 v2's goal (validate mechanics + shared architecture).
The personality asymmetries are real, the network is not collapsed, and the balance is
competitive. Phase 3.5 adds humanization and more steps, which will address the value
loss drift.

---

## 2026-05-24 — v2.5 evaluation + v3 obs-space expansion

### Symptom

After v2.5 (8M warm-start steps from v2), watching the showcase scene revealed that
agents' **dodges hit reliably but pushes and blocks fired in apparently random
directions**. This was consistent across all 5 personalities and both humanization
tiers tested.

### Diagnosis

Checked the actual mechanics in the player state code:

- `PlayerDodgingState`: `Vector2 dir = player.Brain.GetMovement()` — dodge direction
  comes from the agent's movement input, which it sets to chase the opponent. Since
  the agent has opponent relative position as an observation, "move toward opponent"
  is one inference step. Dodge naturally aims correctly.
- `Player.ExecutePush`: pushes only LAND if `Vector2.Dot(transform.up, toOther) > 0.3f`
  — a ~70° facing cone in world coordinates. The push hitbox is offset by `transform.up`.
- `PlayerBlockingState`: similar — block faces the agent's facing direction.

The agent had all the raw data to compute facing alignment: `transform.eulerAngles.z`
(absolute self rotation, normalized) and `opp_rel_position`. But the trig to derive
"is my forward direction aligned with the direction to the opponent" was being
learned implicitly, and after 38M+ steps the network still hadn't gotten it right.

The fix is a single 1-dim pre-computed obs: `Dot(self.up, dirToOpp)`.

### Decision: v3 obs-space expansion (33 dims, was 23)

Rather than add just the one facing obs, we audited the full obs space and added
multiple pre-computed features that the network was deriving (or failing to derive):

| Obs | Why |
|---|---|
| `selfFacingOpponentDot` | The root cause — fixes push/block aim |
| `opponentFacingSelfDot` | Lets the agent predict incoming pushes/blocks |
| `distanceToOpponent` | Pre-computed `sqrt(rel.x² + rel.y²)` — small nets struggle with sqrt |
| `selfRingOutMargin` | Pre-computed safety scalar; clean "how close to losing" signal |
| `selfChargeProgress` | The agent's own charge time was only knowable via LSTM state-change memory |
| `opponentStateOneHot` | Replaced 1-dim float (0.0, 0.2, …, 1.0 for 6 states) with 6-dim one-hot. Categorical-as-float is wasted capacity. |

### Why all at once vs incrementally

Each obs space change breaks warm-start compatibility (input layer shape mismatch).
Doing them piecemeal would mean N from-scratch retrains. If we're paying the
from-scratch cost once, get the value from all the additions at once.

### Notable design choice

`opponent stamina` was considered and rejected. Argued for: would let the agent
exploit opponent gas-outs. Argued against: adds attack-prediction asymmetry the
network already mostly handles through state observations; user dropped it. We're
shipping without it for v3 and can revisit if shipped play feels under-leveraged.

### Expected outcome

v3 should match v2.5 quality with cleaner push/block aim. If the symptom resolves,
this validates the diagnosis; if push/block aim is still random, the issue is
elsewhere (likely the reward shaping for facing isn't strong enough to drive policy
toward sharp aiming even when the obs makes it trivially learnable).

---

## 2026-05-24 — v3 + 3.7 phasing decision (validation-first)

### Original plan

Phase 3.9 v3 would be a 30M-step from-scratch run with the expanded obs space. Phase
3.7 (self-play hardening) was deferred indefinitely, scheduled "after v3 validates."
Combined target: ~35-40M steps total across two runs.

### The user's question

"Why not combine self-play with v3?"

A reasonable question. v3 is from-scratch anyway; the marginal cost of self-play is
just the YAML block + Team ID wiring. Why pay for two from-scratch runs?

### Counter-arguments (case for sequential)

1. **Experimental hygiene.** v3 tests one hypothesis: "the obs expansion fixes random
   push/block aim." Combined with self-play, a good result can't be attributed to
   either change; a bad result can't be isolated. This is exactly the trap that
   made Phase 3 v1 hard to debug (5 simultaneous changes).
2. **Throughput cost.** ML-Agents self-play uses team IDs. Team 0 learns; team 1 is
   a frozen ghost snapshot. In v2/v3's round-robin setup, BOTH players per arena
   currently train — 2× experience per arena. Self-play halves that throughput.
3. **Snapshot quality early on.** The self-play ghost pool is populated from past
   checkpoints. In the first few million steps, the policy is bad/random, so the
   ghost pool is garbage. The pool only becomes useful once the live policy is
   competent — i.e., several million steps in — which is exactly when v3 alone
   would have validated its hypothesis.
4. **Round-robin already provides diversity.** 5 personalities × 15 pairings × 3
   arenas already give varied opponents. Self-play's main value is "play past selves
   to prevent forgetting and cyclic behavior," which is less urgent when personality
   diversity is built in.

### Decision: validation-first phasing

Compromise approach:

- **v3 alone for 10M steps** — validates the obs hypothesis cleanly. ~3-4 hours.
- **Validation gate:** showcase aim looks intentional + entropy ≤ 3.5 + stable episode
  length.
- **Phase 3.7 then warm-starts from v3 for 20M steps with self-play** — robustness
  layer on a validated foundation.
- **Combined budget: 30M steps**, same as v2's from-scratch run, but split for
  diagnosability.

This gets most of the speed benefit of combining (no separate 30M v3 + separate
20M Phase 3.7) while keeping the diagnosis clean. If v3 alone fails to fix aim,
we know the hypothesis is wrong before spending 20M more steps on self-play.

### Self-play vs obs expansion — what each actually does

Worth recording the framing, since these are easy to conflate:

| | v3 obs expansion | Self-play (Phase 3.7) |
|---|---|---|
| **Solves** | Information gap (net can't derive certain features) | Strategic robustness (forgetting, exploitation) |
| **Symptom** | Random push/block aim | Brittle policy, cyclic behaviors |
| **Risk if skipped** | Aim stays broken | Policy is fine in training, exploitable in shipped game |
| **Type** | Architectural fix (better inputs) | Training method enhancement (harder opponents) |

Critical point: **self-play won't fix a missing observation.** If the network can't
compute facing alignment from existing inputs, no amount of self-play will teach it.
v3 is foundational; self-play is polish on top.

The right ordering is v3 → 3.7, never 3.7 alone.

### Hyperparameter notes for Phase 3.7

- `learning_rate: 1.0e-4` — preserve v3 strategies while adding robustness (between
  v3's 3e-4 fresh-start LR and v2.5's tighter fine-tune LR)
- `save_steps: 100_000` — snapshot for ghost pool every 100k steps (slower than typical
  50k to keep the pool more stable given longer training)
- `swap_steps: 20_000` — rotate ghost every 20k steps (~10 swaps per snapshot generation)
- `window: 15` — keep 15 past snapshots in the pool (more than typical 10 because
  20M steps is a long run; want diverse pool)
- `play_against_latest_model_ratio: 0.5` — 50/50 split between live policy and ghosts

### Notable risk

If v3 alone fixes aim *and* produces a competent policy, the question becomes whether
Phase 3.7 is worth doing at all. For a hobby project where players play once or twice,
v3 might be sufficient. Phase 3.7 matters most for:

- Long-tail player engagement where exploit patterns get found
- A measurable ELO/skill progression for marketing
- Bots intended as "final boss" content

Worth re-evaluating after v3 validates rather than treating Phase 3.7 as automatic.

---

## 2026-05-25 — Phase 3.7 results & v3 retrospective

### v3 (20M steps, 33-dim obs) — aim fix confirmed

v3 trained twice: 10M steps for initial validation, extended to 20M via a second run from the
same init. Final metrics: entropy 3.011 (well-converged, ~64% of max), episode length 144
(recent avg), reward stable near zero-sum.

Showcase validation confirmed the obs-space hypothesis: **push/block aim is now visibly
intentional.** Agents face opponents before pushing and orient correctly for blocks. The
`selfFacingOpponentDot` obs was the key addition — without the pre-computed dot product the
network had been expected to implicitly learn the trig from absolute rotation + relative
position over ~38M+ steps and never got it right.

Key build-hygiene lesson from this run: the server build (`builds/Pushman_Training/Pushman`)
and the `.app` are separate targets. Training ran on the old server build (timestamped pre-code-change)
even though the user had rebuilt the `.app`. `train.sh` prefers the server build when present —
if you rebuild the `.app` but not the server build, training silently uses stale code.
Mitigation: `[RLAgentBrain] obs=33` log in `results/<run>/run_logs/Player-0.log` during
`Initialize()` lets you verify obs size before walking away from a long training run.

### Phase 3.7 (30M steps, self-play) — robustness layer

Warm-started from v3's 20M checkpoint. Key pre-run changes: `pushForce` reduced across all
characters (Default 12→9, Heavyweight 14→11, Speedster 10→8) because full-charge from v3 was
already reliably ending fights on a single hit. Team IDs wired in `WireRLPlayerForRR`
(`PlayerIndex % 2`) — without these, `self_play` has no team distinction and silently no-ops.
Confirmed active via `Pushman?team=0` / `Pushman?team=1` in the startup log.

**ELO anomaly:** ELO dropped 1200 → 130 while reward stayed positive (+0.144). Most likely
explanation: the ghost pool was populated from the early training checkpoints trained under
`pushForce=12` dynamics; the live environment had `pushForce=9`. ELO comparisons across a
physics-change boundary are meaningless. The ghost opponents' push distances didn't match
the live env, so the live policy was "worse" at exploiting the ghosts' old distance
expectations. This is a limitation of warm-starting self-play through an env change.

Showcase confirmed fights look **competent** — agents targeting opponents, using pushes
directionally — despite the ELO signal. The metric was broken; the policy was not.

**Episode length: 53 steps (very short).** Fights were concluding on a single full-charge
push in many cases. The pushForce reduction from 12→9 wasn't enough — `9 × 2.5 = 22.5`
impulse on a mass-1 player with drag=2 gives max travel 11.25 units on an 8-unit arena.
Still a guaranteed ring-out from anywhere.

---

## 2026-05-26 — Phase 3.8: knockback physics fix

### Problem

Single-push ring-outs were causing ~53-step average episodes. Even after reducing pushForce
from 12→9, the full-charge formula gave `pushForce × (1 + pushChargeMultiplier) = 9 × 2.5 = 22.5`
impulse. With `linearDamping = 2`, maximum travel distance = 22.5 / 2 = 11.25 units — well
beyond the ±4u ring boundary from any starting position.

### Why increasing drag is the right lever

Player locomotion uses `rb.linearVelocity = value` (direct assignment, drag-immune).
Knockback from pushes uses `rb.AddForce(force, ForceMode2D.Impulse)` (drag acts on this).

Increasing drag therefore:
- **Does not affect** walking speed, dodge speed, or charge-while-moving
- **Does slow down** post-push travel, making ring-outs from center survivable

This is the ideal decoupled fix: a pure knockback limiter with no locomotion side-effects.

### Changes made

| File | Change | Effect |
|---|---|---|
| `Player_RL.prefab` | `m_LinearDamping` 2 → 4 | Knockback decelerates 2× faster |
| `DefaultStats.asset` | `pushChargeMultiplier` 1.5 → 1.0 | Full charge = 2× base, not 2.5× |
| `Heavyweight.asset` | `pushChargeMultiplier` 1.5 → 1.0 | Same |
| `Heavyweight.asset` | `pushForce` 11 → 7 | Offset mass advantage (see below) |
| Speedster | No change — already had `pushChargeMultiplier = 1` | |

### Heavyweight balance correction

Initial plan reduced `pushChargeMultiplier` to 1.0 across the board but kept Heavyweight
`pushForce = 11`. This was wrong — it recreated the Phase 2 75%-win-rate imbalance. The
full asymmetry at those settings:

- Heavyweight hits Default: `11×2 / (4×1.0) = 5.5u` — still a center ring-out
- Default hits Heavyweight: `9×2 / (4×2.5) = 1.8u` — takes 3+ pushes to move them to edge

High mass = survive; high pushForce = one-shot others. Not "hard but beatable," just dominant.

Decision: Heavyweight identity is **true tank** — weak puncher, nearly unkickable. Reduces
pushForce to 7 (below Default's 9) so their hit strength matches their defensive advantage:

- Heavyweight hits Default: `7×2 / (4×1.0) = 3.5u` — near-ring-out but not instant; needs edge positioning
- Default hits Heavyweight: `9×2 / (4×2.5) = 1.8u` — still takes 3+ pushes, but Default fights longer

To ring out Heavyweight, opponent must maneuver Heavyweight near the edge through attrition —
3-4 hits while surviving Heavyweight's ~3.5u return shots. Requires positioning skill, not just
raw push power. This is the intended "hard but beatable" character archetype.

### Expected max travel at full charge (post-change)

```
Max travel = impulse / drag = (pushForce × (1 + chargeMultiplier)) / (drag × mass)
```

Heavyweight weight also nudged 2.5 → 2.0. At 2.5, Default needed 3+ pushes to ring out
Heavyweight from center — felt like an attrition war with no good counter. At 2.0, Default
needs ~2 clean full-charge hits. Still clearly the hardest matchup but reachable with good
positioning, not just luck.

| Character | vs Default (mass 1.0) | vs Heavyweight (mass 2.0) | vs Speedster (mass 0.6) |
|---|---|---|---|
| Default (pushForce 9) | 4.5u | 2.25u | 7.5u |
| Heavyweight (pushForce 7) | 3.5u | 1.75u | 5.8u |
| Speedster (pushForce 8) | 4.0u | 2.0u | 6.7u |

Ring boundary is ±4u from center. No character one-shots another from true center.
Speedster is glass cannon — any hit sends them 6-7.5u, instant ring-out from most positions.
Heavyweight takes 2 solid hits to reach the edge; their return shots are near-ring-outs
(3.5u) but not instant kills. Positional play restored.

### Phase 3.8 training config

- Warm-starts from v37's 30M checkpoint (`Pushman-30000085.pt`)
- LR raised 1e-4 → 2e-4 to help re-adapt to new dynamics faster
- 20M steps (enough to adapt + stabilize, less than the full 30M since strategies transfer)
- Self-play carried forward
- Target episode length: 100–150 steps (back to v3 range)

### Retraining rationale

Any physics change to knockback dynamics requires retraining because:
1. The value function learned return estimates under `drag=2` and `chargeMultiplier=1.5`
2. Push reach changes affect opponent-prediction: "will this push ring them out from here?"
3. Velocity observations are normalized by `movementSpeed`; post-push velocities were
   routinely 3–4× `movementSpeed` under old settings and will now decay faster

Warm-starting from v37 is appropriate: the 33-dim obs space is unchanged, high-level
strategies (approach opponent, aim push, edge pressure) transfer directly. Only the
fine-grained push-range judgments need re-learning.

---

## 2026-05-27 — Phase 3.9 results: round-robin without self-play succeeds

### Run summary

45M total steps (warm-start from v38's 20M checkpoint; trained 25M new steps).
Config: `ppo_shared_v39.yaml` — no `self_play` block, both players team 0, LR 3e-4 linear decay.

### Key metrics

| Metric | v37 | v38 | **v39** | Target |
|---|---|---|---|---|
| Episode length (final) | 53 steps | 57 steps | **115 steps** | 100-150 |
| Self-play ELO | 1200→-38 | declining | N/A (off) | N/A |
| Entropy (final) | — | — | **2.25** | — |
| Training complete | ✅ | ✅ | ✅ | — |

### Entropy arc — the healthy sign

Entropy went 2.36 → 3.43 (step 5.6M) → 2.25 (final). The jump from 2.36 to 3.43 early in training
is significant: it shows the model *re-explored* after losing self-play pressure. Under self-play,
the ghost pool drove convergence toward a single counter-strategy (ring-out on first push, short
episodes). Without that pressure, the policy relaxed and explored the full action space again before
reconverging around 22-28M steps. This is the signature of self-play's over-constraining effect
being released — the training loop working as intended.

### Episode length mid-training dip

Episode length dipped to 73 steps around step 11M before recovering. Interpretation: the
warm-start carried v38's "fast ring-out" habits (57-step episodes). The policy spent ~10M steps
unlearning those habits (short fights → low reward under the current reward function) before
developing the longer engagement style. Recovery to 115-125 steps by step 33M+ confirms the
new dodge-competent physics (dodgeForce 9→14) is working.

### Why round-robin without self-play wins here

The core problem with self-play in Pushman:
1. Ghost pool snapshot selection introduces implicit curriculum — earlier ghosts trained under
   different physics (v37 pushForce=12, v38 drag changes), making ELO comparisons meaningless.
2. Self-play drives toward counter-exploitation: agents converge to the single best response to
   the pool distribution, not a robust diverse policy. In a symmetric game with 5 personalities,
   that means finding the one aggressive ring-out move that beats the median ghost.
3. Round-robin provides inherently diverse pressure: each episode, the agent faces a *different
   personality*, each with different reward-shaping goals (Aggressive=chase, Defensive=block,
   Evasive=dodge, Balanced=mixed, Counter=punish). No single strategy dominates all five.

Result: v39 develops genuinely multi-modal behavior tuned to the opponent's style rather than
a single aggressive ring-out policy.

### ONNX

`Assets/MLModels/Pushman_Shared/PushmanAgent_v39.onnx` ← from `results/Pushman_Shared_v39/Pushman/Pushman-45000114.onnx`

Note: step 44,499,938 had the highest reward checkpoint (0.32) vs final at 0.04. The fluctuation
is noise in cumulative reward, not a real quality difference — the final checkpoint is preferred
(linear LR decay ensures it has the most refined policy).

---

---

## 2026-05-28 — Visual overhaul: ContentKit colors, TMP scale fix, Legacy Showcase crash chain

### ContentKit color system applied

Replaced all ad-hoc colors in `PushmanSetup.cs` with the ContentKit design system palette:

- **Void** `#111009` → camera background (all 4 showcase scene cameras)
- **Surface** `#1E1C16` → arena floor sprite
- **Amber Bright** `#E8C068` → arena ring border, title text
- **Steel Bright** `#9AAABB` → Defensive personality
- **Sage Bright** `#9ABB86` → Balanced personality (computed at +20% L from Sage)
- **Mauve Bright** `#B898CC` → Evasive personality
- **Terra Bright** `#DC9878` → Counter personality
- **Text Primary** `#C8C0AE` → subtitles
- **Text Secondary** `#9A9484` → stats/body text
- **Text Muted** `#6A6358` → VS separator

The "Bright" variants are specifically needed for filled shapes on dark backgrounds — the base
palette (Amber `#C49A3C`, Steel `#6A7F8C`, etc.) reads as muddy at normal opacity on Void.
For 1px chart lines the base variants are fine, but for player body tints and arena elements
the +20-30% L Bright variants are required.

### TMP 3D world-space scale: the 7.84× correction

**Problem:** world-space `TextMeshPro` components were rendering text invisibly small. A label
with `fontSize = 2.8` produced text approximately 0.18 world units tall — not 2.8 units.

**Discovery:** Used Unity MCP `get_gameobject` to inspect the actual mesh bounds of a label GO
in-scene. Mesh bounds height = 0.18u at fontSize=2.8. This gives a ratio of 0.18/2.8 ≈ 0.064.
After accounting for `localScale` already being 1.0 at the time of inspection, the true ratio
of `(rendered height) / fontSize` = approximately **0.127**.

**Fix:** A `TMP_WORLD_CORRECTION = 7.84f` constant (= 1/0.127) applied as `localScale` on the
label GameObject. With this, `fontSize = 2.0` → 2.0u tall text, 1:1 with world units.
`rectWidth` is correspondingly divided by the correction to maintain proportional layout.

**Why the label vertical positions were recalibrated afterward:** with the old (broken) scale,
a label with `fontSize = 2.8` was only 0.36u tall, so 1.8u spacing worked fine. After the fix,
the same label is 2.8u tall and the 1.8u spacing caused overlap. All 4 label helper methods
(`AddArenaLabel`, `AddCharacterLabel`, `AddTitleSubtitleLabel`, `AddDifficultyLabel`) had their
y-positions spread wider to match the corrected text heights.

### Legacy Showcase crash chain: three crashes, three root causes

`Pushman/9` (LegacyShowcase) hit three separate crashes in sequence. Each fix was correct and
each crash had a distinct root cause.

**Crash 1 — "Could not load model" warnings, arenas ran without ONNX**
- Root cause: model paths were relative to `results/`, which is outside `Assets/`. `AssetDatabase`
  cannot import files from outside the project's `Assets/` directory.
- Fix: copy ONNX files to `Assets/MLModels/_Legacy/` first, then import and load from that path.
  Pattern: `File.Copy(srcPath, assetPath, overwrite: true)` → `AssetDatabase.ImportAsset(assetPath)`
  → `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)`.

**Crash 2 — Sentis tensor shape mismatch: "obs size 23 vs model's expected 13/18"**
- Root cause: per-arena `ObservationProfile` objects were created via
  `ScriptableObject.CreateInstance<>()` (in-memory only). After Play-mode entry triggers a
  domain reload, Unity re-serializes the scene — in-memory ScriptableObject references serialize
  as null. At runtime, `RLAgentBrain` fell back to the default 23-dim profile, causing a shape
  mismatch with legacy models (actual shapes: 13 dims for Baby AI, 18 dims for the other two).
  The 13/18 dim counts were verified via Python `onnx` library: `onnx.load(path).graph.input`.
- Fix: save profiles as `.asset` files to `Assets/MLModels/_Legacy/Legacy_Profile13.asset` and
  `Legacy_Profile18.asset`. Load them via `AssetDatabase.LoadAssetAtPath` — references survive
  domain reload because they point to disk assets, not in-memory objects.

**Crash 3 — "Fewer observations (13) made than vector observation size (33). Padded."**
- Root cause: `ConfigureBehaviorParameters()` always loads `Profile_A.asset` (33 dims) and
  sets `bp.BrainParameters.VectorObservationSize = 33`, regardless of the per-agent profile
  passed to `BuildBotPlayer`. It was called before the obs-size override was applied, and no
  override was applied afterward. At collection time, `RLAgentBrain.CollectObservations`
  correctly provided 13 obs (from `Legacy_Profile13`), but `VectorObservationSize = 33` caused
  ML-Agents to pad the tensor, then Sentis rejected the mismatched input shape.
- Fix: in `BuildBotPlayer`, immediately after the `ConfigureBehaviorParameters(go)` call, add:
  ```csharp
  bp.BrainParameters.VectorObservationSize = profile != null ? profile.ComputeSpaceSize(1) : OBS_SIZE;
  ```
  This overrides the Profile_A default with the correct per-arena dim count.

**Lesson:** `ConfigureBehaviorParameters` is a shared utility that hard-codes the "standard" obs
size. Any builder that assigns a non-standard profile must override `VectorObservationSize`
afterward. This is now documented inline in the method.

---

## Future entries

Add new entries here as decisions get made. Format: date header, problem
statement, options considered, decision, rationale. Keep PLAN.md as the
forward-looking "current spec" doc — this file is the chronological narrative.
