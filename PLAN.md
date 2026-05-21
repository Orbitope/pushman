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

**ML-Agents training (pre-training done, ready to run)**
- ✅ Arena prefab fixed — `BehaviorType=Default`, `DecisionRequester` period=5
- ✅ Training scenes — `ML_Training_Scene_Small` (4×4=16 arenas) + `ML_Training_Scene` (8×8=64)
- ✅ `venv-mlagents/` ready — Python 3.10.12, mlagents 1.1.0, torch 2.3.1, onnx 1.15.0, setuptools<81
- ✅ `train.sh` — convenience script for editor and standalone modes
- ✅ `.gitignore` — `results/` and `builds/` excluded
- ✅ Standalone build — `builds/Pushman_Training/` (Server Build via Unity 6 Build Profiles → macOS Server); two fixes required (see Standalone Build Notes below)
- 🔄 Fast sanity run — `Pushman_Fast_v1` running standalone; previously reached 80k steps editor-mode. Resume/restart with `./train.sh --standalone --force`
- ❌ First full training run — `ppo_discrete_baseline.yaml` (~2-3h, run via `./train.sh --standalone --config=ppo_discrete_baseline --id=Pushman_v1`)
- ❌ Self-play run — after baseline trained; `./train.sh --standalone --config=ppo_selfplay --id=Pushman_SelfPlay_v1`
- ✅ ONNX integration — `results/Pushman_Fast_v1/PushmanAgent.onnx` copied to `Assets/MLModels/Aggressive_Default/PushmanAgent.onnx`; `Pushman/5` builds a watch scene automatically

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
Installed: mlagents 1.1.0 · torch 2.12.0 · onnx 1.15.0

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
