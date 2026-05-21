# Pushman — Master Plan

## 0. Implementation Status (source of truth — update this first)
*Updated: 2026-05-21*

### Game: ✅ Playable
Human vs ChaseBotBrain test scene is fully functional. Run `Pushman/1b` then `Pushman/4` to rebuild.

### RL Training: ✅ Code complete, assets ready, not yet trained
All ML-Agents infrastructure is wired. Training configs exist. ScriptableObject assets created. No runs have been executed yet.

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
- ✅ BotPersonality SOs — `Aggressive`, `Defensive`, `Balanced`, `Rusher`
- ✅ CharacterStats SOs — `DefaultStats`, `Heavyweight`, `Speedster`
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
| dodgeForce | 18 (damping zeroed during dash) |
| pushStamina | 20–40 (scales with charge) |
| pushChargeTime | 1s |
| pushForce | 8 base → 20 at full charge |

**Bug fixes applied**
- ✅ Dodge damping fix — `rb.linearDamping` zeroed during dash, restored in EndState
- ✅ Dodge input priority — checked before push in PlayerMovingState (space can't be eaten by held mouse)
- ✅ `DodgingBotBrain.wantDodge` — flag persists until consumed by `GetDodgeInput()`, execution-order safe
- ✅ Bot brain caching — all 3 bots cache their target; `FindObjectsByType` only called on null/inactive
- ✅ Ring shrink visual — `ArenaBoundary` SpriteRenderer scaled each FixedUpdate to match `currentRingRadius`
- ✅ Observation space — `PushmanSetup` uses `profile.ComputeSpaceSize()` instead of hardcoded constant
- ✅ `CharacterStats.cs` field defaults synced to match `DefaultStats.asset`
- ✅ `Image.Type.Filled` sprite fix — fill images now have `StaminaBar.png` assigned; `fillAmount` was silently ignored without a sprite

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
- 🔄 Phase 2 — Task 2a ✅ --init-from; Task 2b 🔄 self-play training live (20M steps, ~6-10h); Task 2c ✅ mixed scene; Task 2d ✅ CharacterStats obs
- ❌ Phase 3 — reward shaping for distinct playstyles
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

**Task 1b — LSTM baseline config.** No work — `ppo_discrete_baseline.yaml` already has
`use_recurrent: true`, 128 hidden, memory 128, 5M steps. Just confirm it is unchanged.
("Reimplement LSTM" = the config exists; the fast experiment never used it.)

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

### Phase 2 — Self-play + multi-opponent curriculum (one master network)

Goal: one `PushmanAgent` network that beats varied opponents and adapts to any
`CharacterStats`. Only the master trains; opponents are frozen or scripted.

**Task 2a — Add `--init-from` to `train.sh`.**
- [ ] `train.sh` has no warm-start flag. Add `--init-from=RUN_ID` that appends
  `--initialize-from=RUN_ID` to the `mlagents-learn` command. Needed by 2e and Phase 3.

**Task 2b — Self-play run.**
- [ ] `ppo_selfplay.yaml` already has the `self_play:` block (ELO, snapshots, window 10).
- [ ] Run warm-started from Phase 1:
  `./train.sh --standalone --config=ppo_selfplay --id=Pushman_SelfPlay_v1 --init-from=Pushman_Bot_v2`
- [ ] Monitor TensorBoard `Self-play/ELO` — must climb steadily. Flat ELO = policy not
  improving vs itself → revisit reward shaping. ~6-10 h (20M cap); stop early on plateau.

**Task 2c — Scripted-opponent curriculum scene (`Pushman/3c`).**
Purpose: expose the master to the hand-coded bots — free, varied, deterministic
opponents that stop self-play overfitting to a single style.
- [ ] Add `Pushman/3c. Build Mixed-Opponent Training Scene` to `PushmanSetup.cs`;
  use `BuildTrainingScene()` as the template.
- [ ] DECISION: 6×6 = 36 arenas (divisible by 3). For each arena instance:
  - Player1 = master `RLAgentBrain`, `BehaviorType.Default` — the only thing that trains.
  - Player2 = scripted bot by `arenaIndex % 3`: 0 → `ChaseBotBrain`, 1 →
    `StandingBotBrain`, 2 → `DodgingBotBrain`. Remove Player2's `RLAgentBrain` +
    `BehaviorParameters` + `DecisionRequester` first (copy the removal order from
    `SetupBotTestScene`), then add the scripted brain.
  - `ArenaManager.allPlayers` still lists both; `RegisterWin/Loss` only fire on
    `RLAgentBrain`, so only the master gets rewards — no `ArenaManager` change needed.
- [ ] Save to `Assets/Scenes/ML_Training_Mixed.unity`; add to Build Settings.
- [ ] Acceptance: open the scene, confirm 36 arenas, each Player2 a scripted brain,
  each Player1 an `RLAgentBrain` with `BehaviorType.Default`.

**Task 2d — Self-observed `CharacterStats` (CODE CHANGE — enables "one network plays
any character").** The agent does not currently observe its own stats, so a DefaultStats
model will not transfer to Heavyweight/Speedster.
- [ ] `Assets/Scripts/ObservationProfile.cs`: add `public bool selfStats = true;` and
  `public bool opponentStats = false;`. Update `ComputeSpaceSize(oppCount)`: `+5` when
  `selfStats`, `+5 * oppCount` when `opponentStats`.
- [ ] `Assets/Scripts/RLAgentBrain.cs` `CollectObservations`: after the `SelfState`
  block add a `SelfStats` block — 5 normalized floats:
  `weight/3f, movementSpeed/12f, pushForce/25f, dodgeForce/30f, maxStamina/150f`
  (norm constants chosen so Default/Heavyweight/Speedster all land in ~0-1). Add an
  `OpponentStats` block (same 5 fields) inside the opponent loop behind the flag, with
  zero-padding for null/invisible opponents (match the existing pad logic).
- [ ] `Assets/Scripts/ArenaManager.cs`: add `public List<CharacterStats> statsPool;`.
  In `ResetAllPlayers()`, if `statsPool` is non-empty assign each player a random entry
  (`p.stats = statsPool[Random.Range(0, statsPool.Count)]`). Empty pool = fixed stats.
- [ ] `Assets/Editor/PushmanSetup.cs`: when building training scenes, populate each
  `ArenaManager.statsPool` with `DefaultStats`, `Heavyweight`, `Speedster`.
- [ ] `ConfigureBehaviorParameters` already derives the obs size from
  `profile.ComputeSpaceSize` — verify it picks up the new size (Profile A: 13 → 18).
- [ ] This INVALIDATES the old obs space ⇒ a fresh run, not a resume. Rebuild scenes
  (`Pushman/3`, `3c`) and the standalone.

**Task 2e — Master retrain (stats + curriculum).**
- [ ] Fresh run (obs space changed): `ppo_discrete_baseline.yaml` on `ML_Training_Mixed`
  with `statsPool` populated. Run id `Pushman_Master_v1`.
- [ ] Self-play polish: `ppo_selfplay.yaml --init-from=Pushman_Master_v1`,
  id `Pushman_Master_SelfPlay_v1`.
- [ ] Validate in `Pushman/5`: master plays competently as Default, Heavyweight, AND
  Speedster (swap each player's `CharacterStats`).

**Task 2f — Frozen-specialist opponents (after Phase 3).** Once Phase 3 specialists
exist, build a scene where Player2 = `RLAgentBrain`, `BehaviorType.InferenceOnly`, with a
specialist ONNX; Player1 = master, `Default`, trains. This is the literal form of
"train against different playstyles, only train the main one."

---

### Phase 3 — Reward shaping for distinct, effective playstyles

Goal: 5 visibly-distinct, competitive personalities — all the SAME master architecture,
only the `BotPersonality` reward asset differs.

**Task 3a — New reward channels (CODE CHANGE).**
- [ ] `Assets/Scripts/BotPersonality.cs` — add fields:
  ```
  [Header("Playstyle Channels")]
  public float ringOutWinBonus = 0f;        // extra reward when the win is a ring-out
  public float dodgeEvasionReward = 0f;     // opponent push whiffed while we dodged
  public float comboReward = 0f;            // hit landed within comboWindowSteps of a dodge
  public int   comboWindowSteps = 30;
  public float centerControlMultiplier = 0f;// per-step, reward for being near center
  public float staminaSavingReward = 0f;    // terminal, scaled by leftover stamina on a win
  public float edgeBaitMultiplier = 0f;     // per-step, reward for surviving near the edge
  ```
- [ ] `Assets/Scripts/RLAgentBrain.cs` — wire each channel:
  - `ringOutWinBonus`: in `RegisterWin()` add `personality.ringOutWinBonus` (every
    `RegisterWin` IS a ring-out — timeouts never call it).
  - `staminaSavingReward`: in `RegisterWin()` add
    `staminaSavingReward * (myPlayer.currentStamina / myPlayer.stats.maxStamina)`.
  - `comboReward`: track `int stepsSinceDodge` — reset to 0 when a dodge is issued,
    increment each `FixedUpdate`. In `AddLandPushReward()`, if
    `stepsSinceDodge < personality.comboWindowSteps` also add `personality.comboReward`.
  - `centerControlMultiplier`: in `FixedUpdate`, add `(1 - selfDist/radius) * centerControlMultiplier`.
  - `edgeBaitMultiplier`: in `FixedUpdate`, add `(selfDist/radius) * edgeBaitMultiplier`.
  - `dodgeEvasionReward`: add an `AddDodgeEvasionReward()` method. In `PlayerPushingState`
    (where a push resolves its hit), when the target is in `Dodging` state and so takes
    no hit, call `(target.Brain as RLAgentBrain)?.AddDodgeEvasionReward()`.

**Task 3b — Author the 5 personality assets** (`Assets/ScriptableObjects/Personalities/`).
Starting values — tune after the 3d eval:

| Field | Aggressive | Defensive | Rusher | Balanced | Trickster |
|---|---|---|---|---|---|
| winRound | 1.5 | 1.5 | 1.5 | 1.5 | 1.5 |
| loseRound | -1.5 | -1.0 | -2.0 | -1.5 | -1.5 |
| landPushHit | 0.25 | 0.10 | 0.40 | 0.20 | 0.15 |
| takePushHit | -0.20 | -0.05 | -0.02 | -0.15 | -0.20 |
| successfulBlock | 0.05 | 0.20 | 0.00 | 0.08 | 0.05 |
| pushBlocked | -0.05 | -0.05 | -0.10 | -0.05 | -0.05 |
| facingOpponentMultiplier | 0.0001 | 0.0001 | 0.0001 | 0.0001 | 0.0001 |
| opponentEdgePressureMultiplier | 0.0004 | 0.0002 | 0.0005 | 0.0003 | 0.0003 |
| timePenaltyPerStep | -0.0001 | 0.0 | -0.0002 | -0.0001 | -0.00005 |
| wastedStaminaMultiplier | -0.05 | -0.02 | -0.01 | -0.04 | -0.05 |
| ringOutWinBonus | 0.30 | 0.20 | 0.50 | 0.30 | 0.30 |
| dodgeEvasionReward | 0.05 | 0.05 | 0.00 | 0.05 | 0.20 |
| comboReward | 0.10 | 0.00 | 0.05 | 0.10 | 0.30 |
| centerControlMultiplier | 0.0 | 0.0003 | 0.0 | 0.0001 | 0.0 |
| staminaSavingReward | 0.0 | 0.30 | 0.0 | 0.10 | 0.10 |
| edgeBaitMultiplier | 0.0 | 0.0 | 0.0 | 0.0 | 0.0002 |

Intent: Aggressive = relentless pressure · Defensive = patient blocking wall · Rusher =
reckless all-in · Balanced = well-rounded · Trickster = dodge-bait-punish.

**Task 3c — Train specialists.**
- [ ] For each non-Aggressive personality, train ~2-3M steps warm-started from the
  Phase 2 master: `--init-from=Pushman_Master_v1` (they specialize, not relearn basics).
  Swap the wired `BotPersonality` asset before building the training scene, or add a
  personality parameter to the `Pushman/3` builder.
- [ ] Export each ONNX → `Assets/MLModels/[Personality]_Default/PushmanAgent.onnx`.

**Task 3d — Evaluate.**
- [ ] Round-robin in `Pushman/5` (swap each player's ONNX + `BotPersonality`).
- [ ] A personality passes if it (a) wins a fair share AND (b) is visibly distinct on
  screen. Re-tune the 3b table and retrain any that fail.

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

1. **Phase 1** — highest ROI, smallest change, fixes the root cause. Do first.
2. **Phase 2d** (stat observations) — do before 2c/2e: it changes the obs space and
   forces a fresh run anyway, so batch all obs-space work together.
3. **Phase 2a/2b/2c/2e** — `train.sh` flag, self-play, curriculum scene, master retrain.
4. **Phase 3** — specialists warm-started from the Phase 2 master; then Phase 2f.
5. **Phase 4** — parallelisable; pick up between long training runs.
