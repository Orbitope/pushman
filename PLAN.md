# Pushman — Master Plan

## 0. Implementation Status (source of truth — update this first)
*Updated: 2026-05-20*

### Game: ✅ Playable
Human vs ChaseBotBrain test scene is fully functional. Run `Pushman/1b` then `Pushman/4` to rebuild.

### RL Training: ✅ Code complete, not yet trained
All ML-Agents infrastructure is wired. Training configs exist. No runs have been executed yet.

---

### What's done

**Core systems**
- ✅ `IPlayerBrain` interface — `HumanBrain`, `RLAgentBrain`, `ChaseBotBrain`, `StandingBotBrain`, `DodgingBotBrain`, `CustomLogicBrain`
- ✅ State machine — Moving / Charging / Pushing / Blocking / Dodging / Stunned, all with stamina gates
- ✅ `CharacterStats` ScriptableObject — all stats decoupled from `Player.cs`; `DefaultStats.asset` is the tuned reference
- ✅ `ArenaManager` — ring-out detection, shrinking stage, randomized respawn, round scoring, ring flash on ring-out
- ✅ `RLAgentBrain` — Discrete/Continuous action space toggle, `ObservationProfile` SO, `BotPersonality` reward hooks
- ✅ Training configs — `ppo_discrete_baseline.yaml`, `ppo_selfplay.yaml`, `sac_continuous.yaml`
- ✅ Training scene — 8×8 = 64 arenas, 25u spacing (ring radius 10u, players ring out before reaching neighbours)

**Editor tooling (`PushmanSetup.cs`)**
- ✅ `Pushman/1b` — Human vs ChaseBotBrain test scene from scratch
- ✅ `Pushman/4` — sprites + screen-space HUD (idempotent, safe to re-run)
- ✅ `Pushman/2` — saves Arena + Player_RL prefabs
- ✅ `Pushman/3` — builds ML_Training_Scene

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
- ❌ Stamina bar lerp — snap is abrupt; lerp `fillAmount` toward target each frame. Optional: ghost bar that lags behind to show stamina loss cost.
- ❌ Remove body red-tint stamina indicator — redundant now that HUD bars exist; `UpdateStateColor()` should only reflect state, not stamina level.

**Gameplay / feel (validate during testing)**
- ❓ Push-on-release feel — correct fighting-game design but unintuitive; watch for confusion during testing.
- ❓ Stamina costs — dodge 40%, push 20–40% — validate these feel fair in play.
- ❓ Shrink timer (30s) — may be too long for short test sessions; lower to ~10s to test the mechanic.

**ML-Agents training (not started)**
- ❌ First training run — `ppo_discrete_baseline.yaml` against `ChaseBotBrain` opponent
- ❌ Self-play run — once baseline is trained
- ❌ Profile B/C observations — FOV restriction and noise/delay (stubs exist in `ObservationProfile`)
- ❌ ONNX model integration — load trained model into `Inference Only` mode for offline play

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

### Setup
```bash
python3 -m venv venv-mlagents
source venv-mlagents/bin/activate
pip install mlagents torch
```

### Run training
```bash
# Baseline PPO (start here)
mlagents-learn TrainingConfigs/ppo_discrete_baseline.yaml --run-id=Pushman_v1

# Self-play (after baseline)
mlagents-learn TrainingConfigs/ppo_selfplay.yaml --run-id=Pushman_SelfPlay_v1
```

Then press **Play** in Unity on `ML_Training_Scene.unity`. Unity connects to Python on port 5004.

### Monitor
```bash
tensorboard --logdir results/
# open http://localhost:6006
```

### Before training — checklist
1. Run `Pushman/3` to rebuild the training scene with latest code
2. Set Player_RL prefab `Behavior Parameters > Behavior Type` to `Default`
3. Confirm `VectorObservationSize` matches `Profile_A.ComputeSpaceSize(1)` (currently 13)
4. Confirm `DecisionRequester.DecisionPeriod = 5`
