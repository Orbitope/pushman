# Pushman — Master Plan

## 0. Implementation Status (source of truth — update this first)
*Updated: 2026-05-22*

### Game: ✅ Playable
Human vs ChaseBotBrain test scene is fully functional. Run `Pushman/1b` then `Pushman/4` to rebuild.

### RL Training: ✅ Phase 2 complete; Phase 3 (round-robin) ready to build
`Pushman_Master_v1` trained 20M steps of Aggressive self-play under the rock-paper-scissors mechanics (block > push, dodge > block, push > dodge). ONNX integrated; CharacterStats rebalanced post-run (Heavyweight nerf, Speedster buff). Warm-start into Phase 3 verified end-to-end (2026-05-22). Next: build the round-robin scene + multi-behavior config.

---

### What's done

**Core systems**
- ✅ `IPlayerBrain` interface — `HumanBrain`, `RLAgentBrain`, `ChaseBotBrain`, `StandingBotBrain`, `DodgingBotBrain`, `CustomLogicBrain`
- ✅ State machine — Moving / Charging / Pushing / Blocking / Dodging / Stunned, all with stamina gates
- ✅ `CharacterStats` ScriptableObject — all stats decoupled from `Player.cs`; `DefaultStats.asset` is the tuned reference
- ✅ `ArenaManager` — ring-out detection, shrinking stage, randomized respawn, round scoring, ring flash on ring-out
- ✅ `RLAgentBrain` — Discrete/Continuous action space toggle, `ObservationProfile` SO, `BotPersonality` reward hooks
- ✅ Training configs — `ppo_discrete_baseline.yaml`, `ppo_selfplay.yaml`, `sac_continuous.yaml`, `ppo_fast_experiment.yaml`
- ✅ Training scene — 8×8 = 64 arenas, 25u spacing (ring radius 10u, players ring out before reaching neighbours)
- ✅ BotPersonality SOs — `Aggressive`, `Defensive`, `Evasive`, `Balanced`, `Counter` (5 distinct RPS playstyles, retuned 2026-05-21)
- ✅ CharacterStats SOs — `DefaultStats`, `Heavyweight`, `Speedster` (rebalanced 2026-05-22 after Phase 2 match-log analysis)
- ✅ ObservationProfile SOs — `Profile_A` (full), `Profile_B` (FOV 90°), `Profile_C` (FOV + noise stubs)
- ✅ `Assets/MLModels/` folder — target for trained ONNX files; naming: `[Personality]_[Stats]/PushmanAgent.onnx`

**Editor tooling (`PushmanSetup.cs`)**
- ✅ `Pushman/1b` — Human vs ChaseBotBrain test scene from scratch
- ✅ `Pushman/4` — sprites + screen-space HUD (idempotent, safe to re-run)
- ✅ `Pushman/2` — saves Arena + Player_RL prefabs
- ✅ `Pushman/3` — builds ML_Training_Scene
- ✅ `Pushman/5` — Bot vs Bot scene (both players InferenceOnly, auto-picks newest ONNX from Assets/MLModels/)

**Visuals & HUD**
- ✅ Screen-space stamina bars — P1 green bottom-left, P2 red bottom-right; `Image.Type.Filled` with sprite so `fillAmount` actually works
- ✅ Round score display — top corners, matching player colors, reads from `ArenaManager.GetScore()`
- ✅ Hand sprites — push fist shown in Charging+Pushing, block shield in Blocking, hidden otherwise
- ✅ State color tints — Stunned=gray, Dodging=blue, Charging=white glow
- ✅ Camera — orthographic size 12, static, shows full 20u ring + 2u padding

**Stamina tuning (DefaultStats.asset)**
| Stat | Value |
|---|---|
| maxStamina | 100 |
| staminaRegenRate | 8/s (Moving only) |
| regenBoostThreshold | 0.75s in Moving state |
| regenBoostMultiplier | 2.5× (= 20/s boosted rate) |
| blockDrain | 15/s |
| dodgeStamina | 40 (tap costs 40%) |
| allowDodgeOverdraft | true |
| overdraftPauseDuration | 1.5s regen lockout |
| dodgeForce | 12 (damping zeroed during dash; reduced from 18) |
| pushStamina | 20–40 (scales with charge) |
| pushChargeTime | 1s |
| pushForce | 8 base, ×1.5 multiplier at full charge (= 12) |

**Bug fixes applied**
- ✅ Dodge damping fix — `rb.linearDamping` zeroed during dash, restored in EndState
- ✅ Dodge input priority — checked before push in PlayerMovingState (space can't be eaten by held mouse)
- ✅ `DodgingBotBrain.wantDodge` — flag persists until consumed by `GetDodgeInput()`, execution-order safe
- ✅ Bot brain caching — all 3 bots cache their target; `FindObjectsByType` only called on null/inactive
- ✅ Ring shrink visual — `ArenaBoundary` SpriteRenderer scaled each FixedUpdate to match `currentRingRadius`
- ✅ Observation space — `PushmanSetup` uses `profile.ComputeSpaceSize()` instead of hardcoded constant
- ✅ `CharacterStats.cs` field defaults synced to match `DefaultStats.asset`
- ✅ `Image.Type.Filled` sprite fix — fill images now have `StaminaBar.png` assigned; `fillAmount` was silently ignored without a sprite
- ✅ LSTM config fix — `use_recurrent: true` is mlagents 0.x syntax, silently ignored by
  1.1.0 (so NO prior run used LSTM). Both `ppo_discrete_baseline.yaml` and
  `ppo_selfplay.yaml` now use the nested `memory:` block. Architectures standardized to
  128 hidden / 2 layers / memory 128 so runs can warm-start from each other.

**Measurement & reporting**
- ✅ `MatchLogger.cs` — logs every decided match (winner/loser personality + stats) to a
  timestamped CSV. Buffered writes + 15s timed flush = no hot-path I/O.
  Auto-bootstrapped by `ArenaManager` — no scene-builder wiring needed.
- ⚠️ KNOWN ISSUE — `MatchLogger` writes to `Directory.GetCurrentDirectory()/match_logs/`.
  For a standalone build that resolves to `builds/match_logs/`, NOT the project-root
  `match_logs/` that `report.py` searches. Until fixed, pass the path explicitly. Fix
  options: (a) `report.py` also globs `builds/match_logs/`, or (b) `MatchLogger` writes
  to an absolute project path. Do (a) before Phase 3 — the win-rate matrix is the only
  mid-run health signal once self-play ELO is gone.
- ✅ `tools/report.py` — single on-demand report tool. Reads TensorBoard tfevents
  (training progress + health heuristic) and the newest match CSV (personality &
  CharacterStats win-rate matrices, balance/coverage check). Run from project root:
  `python tools/report.py [run_id]`. Zero added training overhead.

**Design decisions locked in**
- Dodge bypasses block (intentional — dodge is the counter to block)
- Push fires on mouse RELEASE (hold = charge, release = fire)
- Camera is static (edge awareness is the core mechanic)
- No sound yet
- Overdraft dodge only (not push/block) — push spam would worsen with overdraft; block auto-breaks on empty
- Regen boost is Moving-state only; timer resets on any non-Moving transition (charges must be earned)

---

### Pending

**Visual polish**
- ✅ Stamina bar lerp — `fillAmount` lerps at 8×/s toward target (smooth drain visible)
- ✅ Remove body red-tint stamina indicator — removed from `UpdateStateColor()`; state tints only (stunned=gray, dodging=blue, charging=white glow)

**Gameplay / feel (validate during testing)**
- ❓ Push-on-release feel — correct fighting-game design but unintuitive; watch for confusion during testing.
- ❓ Stamina costs — dodge 40%, push 20–40% — validate these feel fair in play.
- ❓ Shrink timer (30s) — may be too long for short test sessions; lower to ~10s to test the mechanic.

**ML-Agents training**
- ✅ Arena prefab fixed — `BehaviorType=Default`, `DecisionRequester` period=5
- ✅ Training scenes — `ML_Training_Scene_Small` (4×4=16) + `ML_Training_Scene` (8×8=64)
- ✅ `venv-mlagents/` ready — Python 3.10.12, mlagents 1.1.0, torch 2.3.1, onnx 1.15.0, setuptools<81
- ✅ `train.sh`, `.gitignore`, standalone build — all working (see Standalone Build Notes below)
- ✅ First standalone run — `Pushman_Fast_v1` trained to 1M steps, ONNX exported & integrated
- ✅ ONNX integration — `Pushman/5` Bot vs Bot watch scene auto-loads the newest model
- ⚠️ **v1 bots play weakly** — root-caused to a reward-scaling bug, NOT under-training. Full diagnosis + 4-phase fix roadmap in **Section 4**.

**Next up → Section 4 "Model Iteration & Polish Roadmap"** (branch `feature/model-iteration`)
- ✅ Phase 1 — Task 1a ✅ reward retune; Task 1c ✅ trained (5M steps, 53m); Task 1d ✅ ONNX integrated
- ✅ Phase 2 — `Pushman_Master_v1` complete (20M steps, Aggressive self-play, LSTM 128/2).
  ONNX → `Assets/MLModels/Aggressive_Default/PushmanAgent.onnx`. CharacterStats balance
  fixed post-run: Heavyweight nerfed (pushForce 14→9, chargeMultiplier 2→1.5),
  Speedster buffed (pushForce 5→7). Ready to warm-start Phase 3.
- 🔄 Phase 3 — REVISED to round-robin multi-personality self-play (see Section 4).
  Task 3b ✅ 5 personality assets authored + retuned (Aggressive, Defensive, Evasive,
  Balanced, Counter — RPS-differentiated). Task 3a 🔄 partial: `dodgeHitReward` added +
  wired; the 6 optional "playstyle channels" deferred (5 personalities are already
  distinct on the core action rewards). 3c/3d/3e ❌ pending.
- ❌ Phase 4 — visual & gameplay polish (sound, effects, feel)

**Standalone Build Notes (apply after every rebuild)**
Two patches are required after building — they survive until the next rebuild:

1. **mlagents-envs macOS path patch** — already applied to `venv-mlagents/lib/.../mlagents_envs/env_utils.py`. Adds a raw-executable fallback for macOS so mlagents can find the Server Build folder layout (which produces `Pushman_Training/Pushman`, not a `.app` bundle). Re-apply if you recreate the venv.

2. **gRPC native library symlink** — run once after each build:
   ```bash
   mkdir -p builds/Pushman_Training/Data/Plugins
   ln -sf "$(pwd)/builds/Pushman_Training/PlugIns/libgrpc_csharp_ext.x64.bundle" \
          builds/Pushman_Training/Data/Plugins/libgrpc_csharp_ext.x64.bundle
   ```
   Root cause: Unity Server Build places `libgrpc_csharp_ext.x64.bundle` in `PlugIns/` but the gRPC C# runtime searches `Data/Plugins/`. Without this symlink Unity falls back to inference mode silently and mlagents times out after 600 s.

**Training notes**
- Stamina costs must be locked before training — changing cost ratios (not maxStamina) invalidates learned policy
- Model naming: `Assets/MLModels/[Personality]_[Stats]/PushmanAgent.onnx`
- Test in test scene: swap Player2 brain to RLAgentBrain, set Inference Only, assign ONNX + BotPersonality + ObservationProfile

---

## 1. Project Vision

**Game:** Fast-paced 2D top-down sumo fighter. 2–4 players, circular arena, push opponents out of bounds. Physics-based knockback — mass, charge, and positioning matter.

**RL goal:** Train diverse bot personalities using different reward functions and observation profiles. A single master network learns to adapt to any `CharacterStats` asset it's given. Tiered difficulty via `ObservationProfile` hot-swap (Profile A = perfect info, B = FOV-limited, C = noisy/delayed).

---

## 2. Architecture Reference

**Brain abstraction** — `Player.cs` only calls `IPlayerBrain`. Swap components to change controller with no other code changes.

**Hybrid physics** — Movement uses `rb.linearVelocity` (snappy, instant stop). Combat uses `rb.AddForce(Impulse)` (physics knockback). Dodge temporarily zeroes `linearDamping` for full impulse distance.

**CharacterStats SO** — All tunable values live in `Assets/ScriptableObjects/Characters/`. One asset per character archetype. The RL agent observes its own stats so one network can play any character.

**ObservationProfile SO** — Hot-swap what the agent can see. Profile A = all observations on. Profile B = FOV cone restricts opponent data. Profile C = noise + observation delay (stubs only). Declared `VectorObservationSize` is computed from the active profile via `ComputeSpaceSize()`.

**BotPersonality SO** — Reward shaping lives in `Assets/ScriptableObjects/Personalities/`. Swap to produce bots with different aggression, risk tolerance, and stamina management.

**Action space** — Discrete (default): move-H(3), move-V(3), rotate(3), action(4). Continuous mode also supported via `ActionSpaceMode` toggle on `RLAgentBrain`. Switching modes requires matching `BehaviorParameters` and YAML config.

---

## 3. Training Pipeline

### Setup — ✅ venv installed (Python 3.10.12)
`venv-mlagents/` is ready at the project root. Activate and go:
```bash
source venv-mlagents/bin/activate
```
Installed: mlagents 1.1.0 · torch 2.3.1 · onnx 1.15.0

**To recreate from scratch** (grpcio needs binary pre-install on macOS ARM):
```bash
pyenv install 3.10.12   # already installed
PYENV_VERSION=3.10.12 python3 -m venv venv-mlagents
source venv-mlagents/bin/activate
pip install --upgrade pip setuptools wheel
pip install --only-binary=grpcio grpcio
pip install "h5py" "Pillow" "pyyaml" "torch>=2.1.1" "tensorboard>=2.14" \
            "six" "attrs" "huggingface-hub" "onnx==1.15.0" "cattrs>=1.1.0,<1.7" \
            "numpy>=1.23.5,<1.24.0" "protobuf>=3.6,<3.21"
pip install "mlagents==1.1.0" --no-deps
pip install "mlagents-envs==1.1.0"
pip install "setuptools<81"   # setuptools 81+ breaks StrictVersion used by mlagents_envs
# IMPORTANT: do NOT install onnxscript — it forces protobuf 4+ which breaks mlagents pb2 files
# torch 2.3.1 is the pinned version; torch 2.5+ requires onnxscript for ONNX export
```

### Training scenes
| Scene | Arenas | Agents | Use for |
|---|---|---|---|
| `ML_Training_Scene_Small.unity` | 4×4 = 16 | 32 | Fast experiments, editor mode |
| `ML_Training_Scene.unity` | 8×8 = 64 | 128 | Full production runs (standalone build only) |

Rebuild scenes after code changes: `Pushman/3b` (small) or `Pushman/3` (full).

### Mode A — Editor (current, fast experiments only)
```bash
source venv-mlagents/bin/activate
# then press Play in Unity on ML_Training_Scene_Small AFTER "Listening on port 5004"
mlagents-learn TrainingConfigs/ppo_fast_experiment.yaml --run-id=Pushman_Fast_v1 --force --timeout-wait=300

# Resume after a timeout:
mlagents-learn TrainingConfigs/ppo_fast_experiment.yaml --run-id=Pushman_Fast_v1 --resume --timeout-wait=300
```
Or use the script: `./train.sh` / `./train.sh --resume`

### Mode B — Standalone build (full training runs, no Editor needed)
**Build once in Unity:**
1. `File → Build Settings`
2. Add `ML_Training_Scene_Small` or `ML_Training_Scene` to scenes list, check it
3. Check **Server Build** (headless — no rendering, 3–5× faster)
4. Platform: Mac → **Build** → save as `builds/Pushman_Training`

**Train (no Unity open):**
```bash
./train.sh --standalone --config=ppo_discrete_baseline --id=Pushman_v1
```
Python launches the binary, trains to completion, saves ONNX — fully hands-off.

**Rebuild binary when:** any `.cs` gameplay file changes, or ScriptableObject asset values change.
**Don't need to rebuild when:** only YAML configs or `Assets/MLModels/` change.

### Monitor
```bash
source venv-mlagents/bin/activate && tensorboard --logdir results/
# open http://localhost:6006
```

### Checklist (already done — for reference)
- ✅ Arena prefab: `BehaviorType = Default`, `DecisionRequester` period=5
- ✅ `VectorObservationSize = 13` (Profile_A, 1 opponent)
- ✅ Incremental GC enabled in Project Settings
- ✅ `results/` and `builds/` in .gitignore

---

## 4. Model Iteration & Polish Roadmap
*Branch: `feature/model-iteration` — added 2026-05-21*

### Diagnosis — why the v1 bots are weak

`Pushman_Fast_v1` (1M steps) reached mean reward ~1.3 but plays poorly. The problem is
**reward mis-scaling, not insufficient training:**

- `facingOpponentMultiplier` on `Aggressive.asset` is `0.001` — 10× the value its own
  tooltip recommends (`0.0001`).
- Per-step facing reward over a full 5000-step episode: `0.001 × ~0.7 × 5000 ≈ 3.5` —
  **3.5× the +1.0 win reward.**
- Early in training (long episodes, nobody can ring-out yet) the dominant gradient is
  "face the opponent and don't fall out." The policy settles into a face-and-survive
  local optimum and never learns to push aggressively.
- The fast config also had **no LSTM** (`use_recurrent: false`) and only 64 hidden units
  — too little capacity to learn charge-up / block-break timing tells.

**Rule going forward:** total per-step dense shaping over a full episode must stay well
under the terminal win/loss magnitude (target ≈ ±0.5 dense vs ±1.5 terminal).

---

**How to use this section:** each Phase is a list of Tasks; each Task is a `- [ ]`
checklist with file paths, exact values, and an acceptance check. A Sonnet session
should be able to pick up any unchecked Task and execute it without further design
decisions. Decisions already made are marked `DECISION:`. Tick boxes and flip the
Section 0 phase status as work completes.

### Phase 1 — Optimize the master policy (current profiles/styles)

Goal: a genuinely competent baseline bot on Profile A + Aggressive style.
Effort: ~30 min setup + ~2-3 h unattended training.

**Task 1a — Reward retune.**
- [ ] Edit `Assets/ScriptableObjects/Personalities/Aggressive.asset` (Inspector or YAML):

  | Field | Old | New |
  |---|---|---|
  | `facingOpponentMultiplier` | 0.001 | 0.0001 |
  | `landPushHit` | 0.1 | 0.2 |
  | `takePushHit` | -0.1 | -0.2 |
  | `opponentEdgePressureMultiplier` | 0.0002 | 0.0004 |
  | `timePenaltyPerStep` | -0.0002 | -0.0001 |
  | `winRound` | 1.0 | 1.5 |
  | `loseRound` | -1.0 | -1.5 |

- [ ] Update the matching default initializers in `Assets/Scripts/BotPersonality.cs`
  so future personality assets start from sane numbers.
- [ ] Reward-budget check: dense per-step shaping over a full 5000-step episode must
  stay ≈ ±0.5; terminal win/loss is ±1.5.

**Task 1b — LSTM baseline config.** ✅ Fixed (2026-05-21) — `ppo_discrete_baseline.yaml`
originally used the mlagents 0.x `use_recurrent: true` syntax, which 1.1.0 silently
ignores, so NO run before Phase 2 actually used LSTM. Now uses the nested `memory:`
block (128 hidden, 2 layers, memory 128). Same fix applied to `ppo_selfplay.yaml`.

**Task 1c — Rebuild + train.**
- [ ] Reward asset values changed ⇒ rebuild the standalone in Unity
  (Build Profiles → macOS → Build → `builds/Pushman_Training`).
- [ ] Re-apply post-build fixes (see Standalone Build Notes, Section 0):
  `xattr -dr com.apple.quarantine builds/Pushman_Training/` + the gRPC symlink.
- [ ] Run: `./train.sh --standalone --config=ppo_discrete_baseline --id=Pushman_Bot_v2`
- [ ] Monitor TensorBoard: **episode length must fall** over training AND reward rise.
  Both together = real learning. Reward rising while episode length stays pinned at
  5000 = still stalling → stop and re-check reward scaling.

**Task 1d — Integrate + validate.**
- [ ] Copy `results/Pushman_Bot_v2/PushmanAgent.onnx` →
  `Assets/MLModels/Aggressive_Default/PushmanAgent.onnx` (overwrite v1).
- [ ] `Pushman/5` Bot vs Bot — expect active pushing, real ring-outs, short rounds.
- [ ] Human-vs-bot test scene — the bot should give a real fight.
- [ ] Flip Phase 1 → ✅ in Section 0; commit.

---

### Phase 2 — Self-play master (REVISED 2026-05-21)

Goal: one competent `PushmanAgent` network, trained by self-play under the corrected
game mechanics, that generalises across all `CharacterStats`. This supersedes the
original "self-play + scripted curriculum" plan — scripted bots proved too weak to be
useful opponents, and self-play already produces varied, co-evolving opposition.

**Context — why the original Phase 2 was scrapped.**
- The first self-play run (`Pushman_SelfPlay_v1`, 20M steps) is dead weight: the
  `use_recurrent: true` syntax was mlagents 0.x and silently ignored, so it never used
  LSTM; its config also used a 256/3 architecture. Nothing can warm-start from it.
- It also trained under broken mechanics (blocks didn't stop pushes) and converged on
  dodge-spam. Both the mechanics and the reward scaling have since been fixed.
- Net: Phase 2 restarts fresh. The only salvage is the playtest insight that drove the
  rock-paper-scissors mechanics rework.

**Task 2a — `--init-from` in `train.sh`.** ✅ Done.

**Task 2b — Config + mechanics fixes.** ✅ Done.
- LSTM enabled for real — nested `memory:` block in both `ppo_discrete_baseline.yaml`
  and `ppo_selfplay.yaml`; architectures standardised to 128 hidden / 2 layers /
  memory 128 so every run can warm-start from every other.
- Rock-paper-scissors mechanics: block prevents push, dodge breaks block, push beats
  dodge. Dodge forces reduced (Default 12, Speedster 16). Aggressive rewards retuned.
- CharacterStats self-observation already wired (obs space 18); `statsPool` randomises
  stats per episode so one network generalises across Default/Heavyweight/Speedster.

**Task 2c — Measurement system.** ✅ Done.
- `MatchLogger.cs` + `tools/report.py` — see Section 0 "Measurement & reporting".

**Task 2d — Rebuild + launch self-play.** ✅ DONE 2026-05-22
- ✅ Standalone rebuilt; post-build fixes applied.
- ✅ Ran `./train.sh --standalone --config=ppo_selfplay --id=Pushman_Master_v1 --force`.
  Fresh run to the full 20M step cap (~4 h).
- ✅ Health check passed: reward rose -0.75 → +0.19, recent-average episode length
  fell 216 → 134. ELO oscillated by self-play snapshot cycle (expected artefact).

**Task 2e — Integrate + validate.** ✅ DONE 2026-05-22
- ✅ `results/Pushman_Master_v1/PushmanAgent.onnx` →
  `Assets/MLModels/Aggressive_Default/PushmanAgent.onnx`.
- ✅ Match-log analysis (56,845 matches) showed a CharacterStats imbalance — Heavyweight
  at 75% win rate. Rebalanced: Heavyweight `pushForce` 14→9, `pushChargeMultiplier` 2→1.5;
  Speedster `pushForce` 5→7. (New numbers computed, not yet observed in a run — the
  Phase 3 match log re-checks them.)
- ✅ Warm-start into a non-self-play PPO trainer verified end-to-end via a 50k-step test
  (`ppo_warmstart_test.yaml`) — checkpoint loads clean, reward starts at +0.1 not -0.7.
- ✅ Phase 2 flipped to ✅ in Section 0; committed.

Note: the old `ML_Training_Mixed` scripted-bot scene exists but is unused — left in
place, harmless. Frozen-specialist opponents (old Task 2f) are folded into Phase 3:
the round-robin already makes every personality face every other.

---

### Phase 3 — Round-robin multi-personality training (REVISED 2026-05-21)

Goal: 5 visibly-distinct, competitive personalities — same architecture, only the
`BotPersonality` reward asset differs. REVISED from "master then isolated specialists":
instead, all 5 personalities train **simultaneously against each other** in one
multi-behavior run. Each learns to beat every other style, not just a mirror of itself.
Stats randomise per episode (`statsPool`), so the full (personality × stats) permutation
gets covered over training time.

**Task 3a — Dodge reward channel (CODE CHANGE).** ✅ DONE 2026-05-21
- ✅ `BotPersonality.cs` — `dodgeEvasionReward` (Phase 2) + `dodgeHitReward` (new) fields.
- ✅ `RLAgentBrain.cs` — `AddDodgeEvasionReward()` + `AddDodgeHitReward()` wired.
- ✅ `Player.HandleDodgeCollision` — calls `AddDodgeEvasionReward()` when a dodge breaks
  a block, `AddDodgeHitReward()` when a dodge connects with a non-blocking opponent.
  This completes the RPS triangle: Block beats Push, Dodge beats Block, Push beats Dodge.
- DEFERRED: the 6 "playstyle channels" from the earlier plan draft (`ringOutWinBonus`,
  `comboReward`/`comboWindowSteps`, `centerControlMultiplier`, `staminaSavingReward`,
  `edgeBaitMultiplier`) are NOT needed — the 5 personalities below are already visibly
  distinct on the 6 core action rewards. Revisit only if the 3e eval shows two
  personalities converging to the same strategy.

**Task 3b — Author the 5 personality assets.** ✅ DONE 2026-05-21
(`Assets/ScriptableObjects/Personalities/` — `Rusher` renamed to `Evasive`; `Counter`
added new.) Differentiation is purely the reward asset; architecture is identical.

| Field | Aggressive | Defensive | Evasive | Balanced | Counter |
|---|---|---|---|---|---|
| winRound | 1.5 | 1.5 | 1.5 | 1.5 | 1.5 |
| loseRound | -1.5 | -1.5 | -1.5 | -1.5 | -1.5 |
| landPushHit | 0.35 | 0.15 | 0.10 | 0.20 | 0.25 |
| takePushHit | -0.20 | -0.10 | -0.30 | -0.15 | -0.25 |
| successfulBlock | 0.05 | 0.25 | 0.02 | 0.10 | 0.15 |
| pushBlocked | -0.02 | -0.05 | 0.00 | -0.02 | -0.10 |
| dodgeEvasionReward | 0.15 | 0.05 | 0.25 | 0.10 | 0.12 |
| dodgeHitReward | 0.10 | 0.05 | 0.20 | 0.10 | 0.12 |
| facingOpponentMultiplier | 0.0003 | 0.0002 | 0.0001 | 0.0003 | 0.0003 |
| wastedStaminaMultiplier | -0.02 | -0.05 | -0.03 | -0.03 | -0.04 |
| opponentEdgePressureMultiplier | 0.0006 | 0.0002 | 0.0003 | 0.0004 | 0.0003 |
| timePenaltyPerStep | -0.0002 | -0.00005 | -0.0001 | -0.0001 | -0.00008 |

Intent & expected meta (action-level RPS lifted to personality level):
- **Aggressive** — push spam, low block reward. Beats Defensive (pushes pressure it),
  loses to Evasive (dodges escape the spam).
- **Defensive** — high block reward, soft hit penalty, patient (low time penalty).
  Beats Aggressive (blocks the pushes), loses to Evasive (dodges break the blocks).
- **Evasive** — dodge-focused, harsh hit penalty (-0.30 — must not get caught), never
  blocks. Beats Defensive (breaks blocks), loses to Aggressive (caught by push spam).
- **Balanced** — even rewards across all actions. Neutral matchup vs everyone; the
  control baseline.
- **Counter** — harsh penalties for both taking hits (-0.25) and whiffing pushes
  (-0.10). Punishes opponent mistakes; rewards disciplined play.

**Task 3c — Round-robin training scene (`Pushman/3e`).**
- [ ] Add `Pushman/3e. Build Round-Robin Training Scene` to `PushmanSetup.cs`; use
  `BuildTrainingScene()` as the template.
- [ ] DECISION (2026-05-22): include mirrors. 15 pairings total — the 10 cross
  pairings C(5,2) PLUS the 5 mirrors (Aggressive vs Aggressive, etc.). Mirrors close
  the gap where two same-personality bots meet at deployment and let each personality
  learn its own counter.
- [ ] Grid: 45 arenas (9×5) — 15 pairings × 3 arenas each, exact and even.
  Distribute via `pairing = arenaIndex % 15`.
- [ ] Per arena, Player1 and Player2 take the pairing's two personalities. For each:
  set `RLAgentBrain.personality` to the matching `BotPersonality` SO, and set
  `BehaviorParameters.BehaviorName` to that personality's name (e.g. `Aggressive`).
  Both stay `BehaviorType.Default` — both train.
- [ ] REQUIRED — keep `ObservationProfile` = `Profile_A` with 1 opponent so obs space
  stays 18. The Master checkpoint's input layer is 128×18; any other size breaks
  warm-start.
- [ ] Populate every `ArenaManager.statsPool` with `DefaultStats`, `Heavyweight`,
  `Speedster` so stats randomise per episode.
- [ ] Save to `Assets/Scenes/ML_Training_RoundRobin.unity`. In Build Settings make it
  the ONLY enabled scene — if the old training scene stays enabled and ordered first,
  the build silently trains the wrong scene for hours.
- [ ] Acceptance (verify ALL before the multi-hour run): 45 arenas built, all 15
  pairings represented 3× each; every `RLAgentBrain.personality` is non-null; every
  `BehaviorParameters.BehaviorName` exactly matches a `behaviors:` key in
  `ppo_roundrobin.yaml`; all 5 personalities present; obs space logged as 18.

**Task 3d — Multi-behavior config + train.**
- [ ] New `TrainingConfigs/ppo_roundrobin.yaml` — 5 `behaviors:` blocks, one per
  personality name. Each block MUST match the Master checkpoint exactly: `normalize:
  false`, `hidden_units: 128`, `num_layers: 2`, `memory: {sequence_length: 64,
  memory_size: 128}`, `reward_signals.extrinsic: {gamma: 0.99, strength: 1.0}`.
  Give each a per-behavior:
  ```
  init_path: results/Pushman_Master_v1/PushmanAgent/PushmanAgent-20000528.pt
  ```
  to warm-start every personality from the Phase 2 master. (Path + cross-trainer
  compatibility VERIFIED 2026-05-22 — a 50k-step `ppo_warmstart_test.yaml` run loaded
  this exact checkpoint into a non-self-play PPO trainer clean; reward started at +0.1,
  not the -0.7 of a from-scratch run.) No `self_play:` block — opponents are the other
  live behaviors, not frozen snapshots.
- [ ] FAST-FAIL CHECK: within the first ~2 min of the run, confirm the log prints
  `Initializing from results/Pushman_Master_v1/...` for every behavior and shows NO
  `Failed to load` warnings. Abort immediately on any failure — do not discover it
  hours in (the Phase 2b lesson).
- [ ] Rebuild standalone. Run:
  `./train.sh --standalone --config=ppo_roundrobin --id=Pushman_RoundRobin_v1 --force`
  All 5 networks train at once. ~4-6 h; ~3-5M steps per behavior.
- [ ] Export each behavior's ONNX →
  `Assets/MLModels/[Personality]_Default/PushmanAgent.onnx`.

**Task 3e — Evaluate.**
- [ ] `python tools/report.py Pushman_RoundRobin_v1` — the personality win-rate matrix
  is the headline metric. A personality passes if it (a) wins a fair share across the
  matrix AND (b) is visibly distinct in `Pushman/5`.
- [ ] Re-tune the 3b reward table and retrain any personality that is dominated or
  visually indistinct.

---

### Phase 4 — Visual & gameplay polish

Goal: the game feels responsive and readable. Independent of the ML work — can run in
parallel, e.g. between long training runs.

**Task 4a — Audio.**
- [ ] Create `Assets/Audio/`. Source 7 short SFX (free packs or synthesized):
  `charge`, `push`, `hit`, `block`, `dodge`, `ringout`, `roundwin`.
- [ ] Add `Assets/Scripts/PlayerAudio.cs` — `AudioSource` + one `AudioClip` field per event.
- [ ] Trigger points:
  - `PlayerChargingState.EnterState` → charge
  - `PlayerPushingState` on fire → push; on landed hit → hit
  - `PlayerBlockingState` on a blocked push → block
  - `PlayerDodgingState.EnterState` → dodge
  - `ArenaManager.RingOutSequence` → ringout + roundwin
- [ ] Wire `PlayerAudio` into the player prefab via `PushmanSetup` step 4.

**Task 4b — Hit & action effects.**
- [ ] Hit flash: on take-hit, flash the body `SpriteRenderer` white ~0.1 s (coroutine in
  `PlayerVisuals.cs`).
- [ ] Hit particle burst: a small `ParticleSystem` prefab spawned at the contact point
  on a landed push.
- [ ] Charge glow ramp: `PlayerVisuals` already tints white while charging — scale glow
  intensity with charge fraction (0→1 over `pushChargeTime`).
- [ ] Dodge trail: add a `TrailRenderer` to the player, enabled only during `Dodging`.
- [ ] Screen shake: `Assets/Scripts/CameraShake.cs` on the main camera with
  `Shake(duration, magnitude)`; call from a landed push, magnitude scaled by charge.
- [ ] Hitstop: on a full-charge hit, drop `Time.timeScale` to ~0.05 for ~0.05 s realtime,
  then restore.

**Task 4c — Arena & HUD visuals.**
- [ ] Arena floor: a subtle textured/gridded sprite under the ring so motion reads.
- [ ] Charge indicator: a child ring `SpriteRenderer` that fills as the push charges.
- [ ] Shrink warning: pulse the `ArenaBoundary` color when the ring starts closing.
- [ ] Clearer directional facing on the player sprite.

**Task 4d — Gameplay feel.**
- [ ] Lower `ArenaManager.timeUntilShrink` 30 s → 12 s (update the Arena prefab default).
- [ ] Validate push-on-release feel and stamina costs in playtests (Section 0 ❓ items).
- [ ] Ring-shrink telegraph: combine the 4c boundary pulse with an audio cue.

---

### Suggested execution order

1. ✅ **Phase 1** — reward retune + LSTM baseline. Done.
2. ✅ **Phase 2** — `Pushman_Master_v1`, 20M-step Aggressive self-play. Done.
3. **Phase 3** — round-robin (NEXT). Remaining tasks in dependency order:
   a. Fix the `match_logs` path so `report.py` finds standalone-build logs (code change,
      must land before the rebuild).
   b. Task 3c — build `ML_Training_RoundRobin` scene in Unity.
   c. Task 3d — write `ppo_roundrobin.yaml`, ONE rebuild, launch + fast-fail check.
   d. Task 3e — evaluate the personality win-rate matrix; retune as needed.
4. **Phase 4** — visual & gameplay polish. Parallelisable; pick up between training runs.
