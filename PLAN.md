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
| blockDrain | 15/s |
| dodgeStamina | 40 (tap costs 40%) |
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
- ❌ Phase 1 — retune rewards + LSTM baseline run (`Pushman_Bot_v2`)
- ❌ Phase 2 — self-play + multi-opponent curriculum (one master network)
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

### Phase 1 — Optimize the master policy (current profiles/styles)

Goal: a genuinely competent baseline bot on Profile A + Aggressive style.

**1a. Reward retune** — edit `Aggressive.asset` and `BotPersonality.cs` defaults:

| Field | Old | New | Reason |
|---|---|---|---|
| `facingOpponentMultiplier` | 0.001 | 0.0001 | kill the face-and-survive local optimum |
| `landPushHit` | 0.1 | 0.2 | reward pushing more (user request) |
| `takePushHit` | -0.1 | -0.2 | keep symmetric |
| `opponentEdgePressureMultiplier` | 0.0002 | 0.0004 | reward ring-out pressure (user request) |
| `timePenaltyPerStep` | -0.0002 | -0.0001 | -1.0/episode is too harsh; -0.5 still punishes stalling |
| `winRound` | 1.0 | 1.5 | make the ring-out outcome dominate dense shaping |
| `loseRound` | -1.0 | -1.5 | symmetric |

**1b. Use the LSTM baseline config** — `ppo_discrete_baseline.yaml` already has
`use_recurrent: true`, 128 hidden units, memory 128, 5M steps. ("Reimplement LSTM" =
the config exists; the fast experiment just didn't use it.)

**1c. Run** — reward assets changed ⇒ standalone rebuild required, then:
```
./train.sh --standalone --config=ppo_discrete_baseline --id=Pushman_Bot_v2
```
Watch TensorBoard: **episode length should fall** over training (faster ring-outs) and
reward should rise on top of that — that combination means real learning, not stalling.

**1d. Validate** — `Pushman/5` Bot vs Bot scene + human-vs-bot test scene.

---

### Phase 2 — Self-play + multi-opponent curriculum (one master network)

Goal: one `PushmanAgent` network that beats varied opponents. Only the master trains;
opponents are frozen or scripted.

- **2a. Self-play** — `ppo_selfplay.yaml` already has the `self_play:` block (ELO,
  snapshots). Run with `--initialize-from=Pushman_Bot_v2` so it starts from the Phase 1
  policy, not random:
  `./train.sh --standalone --config=ppo_selfplay --id=Pushman_SelfPlay_v1`
- **2b. Scripted-opponent curriculum** — expose the master to the hand-coded bots
  (`ChaseBotBrain`, `StandingBotBrain`, `DodgingBotBrain`): free, varied, deterministic
  opponents. New `Pushman/3c` menu item — mixed-opponent scene that randomizes the
  master's opponent per episode.
- **2c. Frozen-specialist opponents** — once Phase 3 specialists exist, drop their ONNX
  into opponent slots as `InferenceOnly`. The master keeps training; specialists stay
  static. This is how "train against different playstyles, only train the main one" works.
- **2d. One-network-plays-any-character — CODE CHANGE.** The Section 1 vision needs the
  agent to observe its own `CharacterStats`; it currently does not. Add a stat block to
  `CollectObservations` + `ObservationProfile`:
  - self stats: weight, movementSpeed, pushForce, dodgeForce, maxStamina (5 floats, normalized)
  - opponent stats: same 5 (optional, behind a profile flag)
  Then randomize `CharacterStats` per-episode in training so the network learns to
  condition on them. Without this, a DefaultStats model won't transfer to Heavyweight/Speedster.

---

### Phase 3 — Reward shaping for distinct, effective playstyles

Each `BotPersonality` asset is a different reward function over the SAME master
architecture. New reward channels to add to `BotPersonality.cs` + `RLAgentBrain.cs`:

- `ringOutWinBonus` — extra reward when the win came from a ring-out (vs a timeout)
- `dodgeEvasionReward` — opponent's push whiffed because we dodged
- `comboReward` — landed a hit within N steps of a dodge
- `centerControlMultiplier` — per-step reward for being near center (Defensive)
- `staminaSavingReward` — terminal reward proportional to leftover stamina (Defensive)
- `edgeBaitReward` — reward for surviving near the edge (Matador/Trickster)

**Personality designs:**
- **Aggressive** — high `landPushHit`, high `edgePressure`, strong `timePenalty`. Fast, relentless.
- **Defensive** — high `successfulBlock`, low `takePushHit` penalty, `centerControl` +
  `staminaSaving` rewards, ~zero `timePenalty`. Patient wall.
- **Rusher** — huge `landPushHit`, ~zero `takePushHit` penalty (reckless), strong
  `timePenalty`, penalty for blocking. All-in.
- **Balanced** — even values, mild everything.
- **Trickster** (new) — high `dodgeEvasion` + `combo` rewards, `edgeBait` reward. Baits and punishes.

Process: train each personality as a short specialist run (2-3M steps,
`--initialize-from` the Phase 1 master). Evaluate round-robin in Bot vs Bot. A playstyle
"works" if it (a) wins games and (b) is visibly distinct on screen.

---

### Phase 4 — Visual & gameplay polish

**Sound** — new `Assets/Audio/`, simple `AudioSource` triggers: charge-up whine, push
fire, hit impact, block clang, dodge whoosh, ring-out, round win/lose.

**Effects:**
- hit impact flash + particle burst
- charge-up glow ramp scaled to charge level
- dodge motion trail
- ring-out splash (on top of the existing boundary flash)
- screen shake on full-charge hits + brief hitstop (freeze-frame) on big impacts

**Visuals:**
- arena floor texture / grid so motion reads better
- charge indicator ring around the player
- shrink-ring warning pulse when the ring starts closing
- clearer directional facing on player sprites

**Gameplay feel:**
- lower `timeUntilShrink` 30s → ~12s (flagged as too long in Section 0)
- knockback feedback scaling; validate push-on-release feel
- ring-shrink audio + visual telegraph
