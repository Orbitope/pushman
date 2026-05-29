# Pushman — Master Plan

> **Companion doc:** `DEV_NOTES.md` holds chronological decision rationales, phase
> retrospectives, and abandoned approaches. PLAN.md is the forward-looking spec —
> what the current target state is and what's left to build. When you want to know
> *why* something is the way it is, check DEV_NOTES.

## 0. Implementation Status (source of truth — update this first)
*Updated: 2026-05-28 (night)*

### Game: ✅ Playable
Human vs ChaseBotBrain test scene is fully functional. Run `Pushman/1b` then `Pushman/4` to rebuild.

### RL Training: ✅ Complete — v39 is the shipping model
`Pushman_Shared_v39` is the final model. 45M steps, pure round-robin (no self-play), drag=4, dodgeForce=14.
ONNX at `Assets/MLModels/Pushman_Shared/PushmanAgent_v39.onnx`.

**Key metrics (45M steps):**
- Episode length: 115 steps final (v38 was 57, target was 100-150 ✅)
- Entropy arc: 2.36 → 3.43 (healthy re-exploration after dropping self-play) → 2.25 (converged)
- Cumulative reward: noisy near 0 (correct for symmetric game) ✅
- Training dip at step 11M then solid recovery to 115-125 by step 33M+

**Difficulty tiers: ✅ Implemented and working**
Noise/delay humanization code is live in `RLAgentBrain`. v39 trained with random tier sampling
(`runtimeHumanization = -1` randomises from `{0, 0.33, 0.66, 1.0}` per episode). The
DifficultyShowcase (Pushman/8) pins each arena to a tier: Expert → Easy left to right.
No separate Phase 3.5 training run needed — v39 already conditions on difficulty.

### Showcase Scenes: ✅ All built, ContentKit visual overhaul complete
All scenes rebuilt with ContentKit design system (Void bg, Bloom/Vignette, Amber/Steel HUD,
Mauve/Terra player bodies). `ContentKitCamera` component guarantees Play-mode background.
- `Pushman/5` Bot vs Bot (single arena, v39)
- `Pushman/6` PersonalityShowcase (5 arenas, 5 personalities, v39)
- `Pushman/7` CharacterShowcase (3 arenas, Default/Heavyweight/Speedster, v39)
- `Pushman/8` DifficultyShowcase (4 arenas, Expert/Hard/Medium/Easy, v39)
- `Pushman/9` LegacyShowcase (3 arenas, Baby AI / Dodge Dominant / Phase 2)
- Run `Pushman/10` after any scene rebuild to apply ContentKit bg + post-processing.
- Run `Pushman/11a` to set Game view to 4K for recording.

### Diagrams: ✅ SVGs in `Diagrams/`
`combat-triangle.svg` (RPS mechanic) and `state-machine.svg` (6-state flow), ContentKit dark theme.

### Main Menu + Series Flow: ✅ Built and working
Full menu → game → series → rematch loop is live:
- `MainMenu.unity` — card-based selection (player char / personality / difficulty / opponent char) + PLAY
- `Game.unity` — Human P1 vs v39 bot with selected settings; ContentKit visuals, HUD, ring
- Best-of-3 series via `SeriesManager`; per-round banner + end-game Rematch/Menu overlay
- `GameConfig` singleton survives scene loads (DontDestroyOnLoad)

**Key gotchas resolved during build:**
- ONNX must be loaded **after** `EditorSceneManager.NewScene` (otherwise `UnloadUnusedAssets` nulls the reference)
- New scenes need explicit `InputSystemUIInputModule` (project uses new Input System; `StandaloneInputModule` crashes at runtime)
- TMP fonts must be assigned via SerializedObject in Pushman/12a (Inspector field stays null otherwise)
- All `VerticalLayoutGroup`s need `childControlHeight = true` to respect `LayoutElement.preferredHeight`
- `Pushman/12a` and `Pushman/12b` regenerate scenes from scratch — re-run after changes

### Next up
1. **WebGL build + release prep** — build target switch, test in browser, publish to itch.io / GitHub Pages
2. **Phase 4 polish** (4a–4d): audio (SFX), hit effects, charge indicator, arena feel
3. **Human playtesting** — validate push-on-release feel, stamina costs, shrink timer

> ⚠️ Build hygiene: always verify `[RLAgentBrain] obs=33` in `results/<run>/run_logs/Player-0.log`
> before walking away from a training launch. The grpc symlink also needs recreating after each
> fresh server build: `ln -sf builds/Pushman_Training/PlugIns/libgrpc_csharp_ext.x64.bundle builds/Pushman_Training/Data/Managed/libgrpc_csharp_ext.x64.bundle`

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
- ✅ ObservationProfile SOs — `Profile_A` (full, used for v2 training), `Profile_C` (noise+delay stubs, used for v2.5). Profile_B (FOV) deleted.
- ✅ `Assets/MLModels/` folder — `Pushman_Shared/PushmanAgent_v2.onnx` (30M steps, current best)

**Editor tooling (`PushmanSetup.cs`)**
- ✅ `Pushman/1b` — Human vs ChaseBotBrain test scene from scratch
- ✅ `Pushman/4` — sprites + screen-space HUD (idempotent, safe to re-run)
- ✅ `Pushman/2` — saves Arena + Player_RL prefabs
- ✅ `Pushman/3` — builds ML_Training_Scene
- ✅ `Pushman/5` — Bot vs Bot scene (both players InferenceOnly, defaults to Pushman_Shared/PushmanAgent_v2.onnx)
- ✅ `Pushman/6` — Personality Showcase Scene (5 arenas, shared ONNX, matchups reflect v2 win-rate matrix)

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
- ✅ `MatchLogger` path fix — `report.py` now globs both `match_logs/` and `builds/match_logs/`
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
**Next up → Section 4 "Model Iteration & Polish Roadmap"** (branch `feature/model-iteration`)
- ✅ Phase 1 — Task 1a ✅ reward retune; Task 1c ✅ trained (5M steps, 53m); Task 1d ✅ ONNX integrated
- ✅ Phase 2 — `Pushman_Master_v1` complete (20M steps, Aggressive self-play, LSTM 128/2).
  ONNX → `Assets/MLModels/Aggressive_Default/PushmanAgent.onnx`. CharacterStats balance
  fixed post-run: Heavyweight nerfed (pushForce 14→9, chargeMultiplier 2→1.5),
  Speedster buffed (pushForce 5→7). Ready to warm-start Phase 3.
- ⚠️ Phase 3 v1 — `Pushman_RoundRobin_v1` (5M steps × 5 behaviors). Outcome unusable:
  dodge-dominant collapse with no pushes, semi-random blocks. See `DEV_NOTES.md` →
  "Phase 3 v1 retrospective" for diagnosis.
- ✅ Phase 3 v2 — shared-network round-robin (one `Pushman` behavior, 5 personalities
  via reward stream + self-personality observation). Trained clean (humanization=0) from
  scratch, extended to 30M steps (initial 10M showed entropy too high at 78% of max).
  Final: entropy 3.08 (66% of max), episode length 56, reward -0.29.
  Win-rate matrix balanced: 48–51% overall range. All 5 personalities distinct.
  Value loss peaked at 10.5 and nudged back — addressed in v2.5 via num_epoch: 5.
  ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v2.onnx`.
- ✅ Phase 3.5 — humanization / difficulty tiers. Noise + delay per-obs implemented in
  `RLAgentBrain`. v39 trained with random tier sampling (`runtimeHumanization=-1`
  samples `{0, 0.33, 0.66, 1.0}` per episode). No dedicated fine-tune run needed —
  v39 already conditions the policy on difficulty level. DifficultyShowcase (Pushman/8)
  pins each arena to a tier and works correctly with v39 ONNX.
- ✅ Phase 3.9 v3 — expanded observation space. Adds 10 obs dims (23→33) to fix
  push/block aim symptom. New obs: facing dot self→opp, opp facing dot, distance to opp,
  ring-out margin, charge progress, opp state one-hot. Trained from scratch, 20M steps
  (10M validation + 10M extension). Final entropy 3.011, episode length 144 (recent avg).
  ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v3.onnx`.
  **Awaiting showcase validation — open PersonalityShowcase and check push/block aim.**
- ✅ Phase 3.7 — self-play hardening. Warm-starts from v3's 20M checkpoint.
  Ran 30M steps with ML-Agents `self_play` block active (Pushman?team=0/1 confirmed in log).
  Entropy 2.482 (converged), ELO dropped 1200→130 (likely ghost-pool artifact — ghosts trained on old
  pushForce=12 env, current env is pushForce=9; metric unreliable across physics change boundary).
  Episode length 53 steps (very short → fights ending on first push, hence v38).
  ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v37.onnx`. Showcase: fights look good, just too fast.
- ✅ Phase 3.8 — longer fights. drag 2→4, chargeMultiplier 1.5→1.0, Heavyweight rebalanced.
  ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v38.onnx`. Self-play still caused ELO decline.
- ✅ Phase 3.9 — drop self-play, fix dodge. 45M steps, episode length 115 steps final.
  ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v39.onnx`.
- ✅ Visual overhaul — ContentKit design system. Phases:
  - ✅ Phase 1 — Sprites: 128px player circle + directional chevron (80% body / 100% chevron);
    dark arena floor (Surface #1E1C16) + Amber Bright ring border (#E8C068). ContentKit
    personality colors: Amber Bright/Steel Bright/Mauve Bright/Sage Bright/Terra Bright.
    New sprites: `PlayerCircle.png`, `ArenaFloor.png`, `ArenaBorder.png`.
    Camera background exact Void #111009. Hand sprite positions corrected (visible above body).
  - ✅ Phase 2 — `Pushman/10. Apply ContentKit Scene Setup` — Global Volume with Bloom
    (threshold=0.85, intensity=0.35, warm Amber tint) + Vignette (intensity=0.25, rounded).
    Run in each showcase scene after rebuilding. Camera Void bg handled in Phase 1.
    VolumeProfile saved to `Assets/Settings/ShowcaseVolumeProfile.asset`.
  - ✅ Phase 3 — TMP label migration: all world-space labels → TextMeshPro (Rajdhani titles,
    Inter body); `MakeTMPWorldLabel` helper with `TMP_WORLD_CORRECTION = 7.84f` scale factor
    (TMP 3D renders at fontSize × 0.127 world units — inverse applied via localScale so
    fontSize maps directly to world-unit height). Label vertical spacing recalibrated.
  - ✅ Phase 4 — HUD redesign: Surface `#1E1C16` bar bg; Amber Bright `#E8C068` P1 bar,
    Steel Bright `#9AAABB` P2 bar; score text migrated from legacy `Text` to
    `TextMeshProUGUI` with Rajdhani-Medium SDF at fontSize=56. `StaminaHUD.cs`
    `p1ScoreText`/`p2ScoreText` fields updated to `TextMeshProUGUI`.
- ✅ Showcase scenes — all 4 world-space showcase scenes complete and playable:
  - `Pushman/6` PersonalityShowcase — 5 arenas, v39 ONNX, personality matchups
  - `Pushman/7` CharacterShowcase — 3 arenas, Default/Heavyweight/Speedster
  - `Pushman/8` DifficultyShowcase — 4 arenas, humanization tiers (Expert/Hard/Medium/Easy)
  - `Pushman/9` LegacyShowcase — 3 arenas showing Baby AI (13 obs) / Dodge Dominant (18 obs) /
    Phase 2 Trained (18 obs). Per-arena legacy observation profiles saved as `.asset` files
    (`_Legacy/Legacy_Profile13.asset`, `_Legacy/Legacy_Profile18.asset`). ONNX models copied
    into `Assets/MLModels/_Legacy/`. `VectorObservationSize` overridden per-arena after
    `ConfigureBehaviorParameters` to avoid Profile_A (33-dim) clobbering legacy obs sizes.
- 🔄 Phase 4 — visual & gameplay polish. Task 4e ✅ done (state readability: enlarged
  hand sprites + state tints). 4a-4d (audio, effects, arena/HUD, feel) pending.

---

### Section 6 — WebGL Build: Main Menu & Character Select

**Goal:** A playable WebGL build with a polished entry point. The player configures their
match before loading the arena — choosing their own character, the opponent's personality,
the opponent's difficulty, and the opponent's character. Then plays a best-of series before
returning to the menu.

---

#### Scene structure

```
MainMenu (new scene)
    └─ on Play → loads Game scene
Game scene  (existing BotVsBot.unity, modified to read from GameConfig)
    └─ on series end → returns to MainMenu
```

#### New systems

**`GameConfig.cs`** — persistent singleton (`DontDestroyOnLoad`)
Holds the player's selections across the scene load.
```
PlayerCharacter    : CharacterStats SO reference
OpponentCharacter  : CharacterStats SO reference
OpponentPersonality: BotPersonality SO reference
OpponentDifficulty : float (0.0 = Expert … 1.0 = Easy)
SeriesLength       : int (default 3 — first to 2 wins)
```
Reset to defaults on return to menu. Exposed as static `GameConfig.Current`.

**`MainMenuController.cs`** — drives the menu scene
- Populates card panels from arrays of SO references (set in Inspector)
- Tracks current selection per category, updates card highlight states
- Wires the Play button: validates all 4 selections made → `GameConfig.Current` → `SceneManager.LoadScene("Game")`

**`SelectionCard.cs`** — reusable card component (one per option per category)
- Shows: accent-colored border, name (Rajdhani Bold), short description (Inter)
- States: unselected (dim border), selected (full accent color + scale pop)
- `OnClick` notifies `MainMenuController`

**`SeriesManager.cs`** — wraps `ArenaManager` for best-of-N flow
- Tracks wins per side across rounds
- Shows "Round X — First to Y wins" text during play
- On series complete: shows result overlay (WIN / LOSE), Rematch button, Menu button
- Rematch reloads Game scene with same `GameConfig`; Menu loads MainMenu

---

#### Menu layout (ContentKit dark theme)

```
┌─────────────────────────────────────────────────────────┐
│  PUSHMAN                            [title, Rajdhani]   │
├────────────────────┬────────────────────────────────────┤
│   YOUR CHARACTER   │         OPPONENT                    │
│                    │  Personality  Difficulty  Character │
│  [Default ]        │  [Aggressive] [Expert   ] [Default] │
│  [Heavyweight]     │  [Defensive ] [Hard     ] [Heavy  ] │
│  [Speedster]       │  [Evasive   ] [Medium   ] [Speed  ] │
│                    │  [Balanced  ] [Easy     ]            │
│                    │  [Counter   ]                        │
├────────────────────┴────────────────────────────────────┤
│                  [ PLAY — BEST OF 3 ]                   │
└─────────────────────────────────────────────────────────┘
```

Cards use ContentKit accent colors:
- Characters: Default=Steel, Heavyweight=Terra, Speedster=Sage
- Personalities: Aggressive=Terra, Defensive=Steel, Evasive=Mauve, Balanced=Amber, Counter=Sage
- Difficulty: Expert=red `#E84040`, Hard=orange `#E87830`, Medium=yellow `#E8C040`, Easy=Sage

Short descriptions on each card (1 line):
- **Default** — "Balanced stats"
- **Heavyweight** — "Hard to push, hits hard"
- **Speedster** — "Fast and light"
- **Aggressive** — "Pushes relentlessly"
- **Defensive** — "Blocks and waits"
- **Evasive** — "Dodges everything"
- **Balanced** — "No clear weakness"
- **Counter** — "Punishes mistakes"
- **Expert** → **Easy** — difficulty descriptions already in DifficultyShowcase

---

#### Game scene changes

Modify `BotVsBot` scene setup (or add a `Pushman/12` builder) to:
1. Read `GameConfig.Current` on scene load
2. Set `player1` `CharacterStats` → `GameConfig.PlayerCharacter`
3. Set `player2` `CharacterStats` → `GameConfig.OpponentCharacter`
4. Set `player2` `RLAgentBrain.personality` → `GameConfig.OpponentPersonality`
5. Set `player2` `RLAgentBrain.runtimeHumanization` → `GameConfig.OpponentDifficulty`
6. Attach `SeriesManager` with `seriesLength = GameConfig.SeriesLength`

Player 1 always uses `HumanBrain`. Player 2 always uses `RLAgentBrain` in `InferenceOnly` mode.

---

#### Implementation checklist

**New files:**
- [ ] `Assets/Scripts/GameConfig.cs` — persistent singleton
- [ ] `Assets/Scripts/MainMenuController.cs` — menu logic
- [ ] `Assets/Scripts/SelectionCard.cs` — card UI component
- [ ] `Assets/Scripts/SeriesManager.cs` — best-of-N wrapper
- [ ] `Assets/Scenes/MainMenu.unity` — menu scene (or `Pushman/12` builder for it)

**Modified files:**
- [ ] `Assets/Scripts/ArenaManager.cs` — expose a hook for `SeriesManager` to intercept round-end
- [ ] `Assets/Editor/PushmanSetup.cs` — add `Pushman/12. Build Game Scene (WebGL)` that wires
      `GameConfig` reading, `SeriesManager`, and sets Build Settings for WebGL export
- [ ] `ProjectSettings/EditorBuildSettings.asset` — add MainMenu + Game scenes for WebGL build

**WebGL build:**
- [ ] File → Build Settings → WebGL platform
- [ ] Player Settings: resolution 1280×720 (scales for browser), WebGL memory size
- [ ] Build → `builds/WebGL/`
- [ ] Test locally via `python -m http.server` in `builds/WebGL/`
- [ ] Deploy to itch.io or GitHub Pages

---

### Section 5 — Phase 3 v2 / v2.5 spec & checklist

Forward-looking spec only. For background on why these values were chosen, why we're
not warm-starting from v1, why opponent personality is excluded, why two-phase, etc.,
see `DEV_NOTES.md`.

#### Mechanics (locked in 2026-05-23)

`CharacterStats` SOs and `Player.cs` defaults:

| Stat | Default | Heavyweight | Speedster |
|---|---|---|---|
| `pushChargeTime` | 0.25s | 0.3s | 0.2s |
| `pushHitRadius` | 0.8u | — | — |
| `dodgeForce` | 9 | 11 | 12 |
| `pushForce` | 12 | 14 | 10 |

Charging permits movement at 50% speed.

#### Reward magnitudes (locked in 2026-05-23)

`BotPersonality` SOs:

| Signal | v2 value | Notes |
|---|---|---|
| `winRound` | 10.0 | Sparse, dominant |
| `loseRound` | -10.0 | Symmetric |
| `landPushHit` (base) | 0.03–0.05 | Tiny shaping signal |
| `dodgeHitReward` (base) | 0.01–0.02 | Even tinier — dodge is evasion |
| `edgeHitBonus` | 0.4–0.6 | Added on hits, scaled by opp `dist_from_center / current_ring_radius` |
| `dodgeMissedPenalty` | -0.04 to -0.06 | Fires when dodge expires without contact |
| `successfulBlock` | 0.04–0.2 | Personality-dependent |

Per-episode budget check (busy 750-step match): dense total ≈ 2.5, win = 10.0 →
terminal dominates by ~4×.

#### Difficulty tiers (locked, used by Phase 3.5)

| Player-facing | `humanization` | Noise σ (u) | Delay (frames @ 50Hz / ms) |
|---|---|---|---|
| Expert | 0.0 | 0 | 0 / 0 |
| Hard | 0.33 | 0.05 | 4 / 80 |
| Medium | 0.66 | 0.10 | 8 / 160 |
| Easy | 1.0 | 0.15 | 12 / 240 |

Convention: `humanization=0` = no degradation (Expert), `humanization=1` = max
degradation (Easy). Player-facing labels are inverted from the scalar — use
"humanization" in code, "difficulty" in UI.

Phase 3.5 samples `humanization` uniformly from the discrete set
`{0.0, 0.33, 0.66, 1.0}` per agent per episode. `humanization` is fed back to the
network as a 1-dim observation so it can condition policy on its current tier.

#### Architecture spec

- One ML-Agents behavior: `BehaviorName = "Pushman"` on every agent in the round-robin scene
- One shared network. 5 personalities differentiated by reward stream (per-agent
  `BotPersonality` SO) + self-personality observation
- No opponent-personality observation — agents read opponent state through behavior obs
  (position, velocity, state, stamina, distance), which is universal across bot/human
- Profile_A (perfect info) is the only profile used for v2 training
- Profile_C (humanized) is added in v2.5; Profile_B is deleted entirely

#### Config sketch (`ppo_shared_v2.yaml`)

```yaml
behaviors:
  Pushman:
    trainer_type: ppo
    hyperparameters:
      batch_size: 2048      # bigger — more agents feeding one network
      buffer_size: 32768
      learning_rate: 3.0e-4
      learning_rate_schedule: linear
      beta: 0.005
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      normalize: true        # turn on — reward scale now ±10
      hidden_units: 256      # slightly bigger — more personalities to encode
      num_layers: 2
      memory:
        memory_size: 128
        sequence_length: 64
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    # NO init_path — train from scratch
    max_steps: 10_000_000
    time_horizon: 128
    summary_freq: 20_000
    keep_checkpoints: 5
```

For `ppo_shared_v25.yaml`: same as above but `init_path` points at the v2 checkpoint,
`max_steps: 2_000_000`, `learning_rate: 1.0e-4`.

#### Implementation checklist

*Phase 3 v2 prep (build first):*

Profile cleanup:
- [ ] Delete `Assets/ScriptableObjects/Observation/Profile_B.asset`
- [ ] Remove `useFOV`, `fieldOfViewAngle` fields from `ObservationProfile.cs`
- [ ] Remove FOV-gating branch from `RLAgentBrain.CollectObservations`

Shared-network architecture:
- [ ] Add `selfPersonalityId` flag to `ObservationProfile.cs` (default true; one-hot 5 dims; NO opponent personality)
- [ ] Give `BotPersonality.cs` a stable integer `id` (0–4) set on each asset, plus a
      const `Count = 5`. Cleaner than a runtime name→id lookup.
- [ ] Wire self-personality one-hot in `RLAgentBrain.CollectObservations`
- [ ] Update `ObservationProfile.ComputeSpaceSize` to add 5 when `selfPersonalityId` is on
- [ ] Set all round-robin arena `BehaviorName = "Pushman"` (currently five different names) in `PushmanSetup.WireRLPlayerForRR`

Phase 3 v2 training:
- [ ] Write `TrainingConfigs/ppo_shared_v2.yaml` — single `Pushman` behavior, train
      from scratch, `network_settings.normalize: true`, `hidden_units: 256`,
      `max_steps: 10_000_000`, no `init_path`
- [x] Rebuild round-robin scene (`Pushman/3e`) — same 15 pairings × 3, just unified BehaviorName
- [x] Train `Pushman_Shared_v2` — extended to 30M steps (10M entropy too high at 78% of max)
- [x] Final metrics: entropy 3.08, episode length 56, reward -0.29, win-rate matrix 48–51%
- [x] Export ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v2.onnx`
- [x] Showcase scene (`Pushman/6`) updated to use shared ONNX; matchups reflect v2 win-rates
- [x] Run `Pushman/6` — ring-out attempts confirmed, play quality acceptable. Personality
      differences subtle visually (expected — divergence is statistical, not behavioral);
      win-rate matrix asymmetries (up to 62%) are the real verification.
- [ ] Verify human-vs-bot scene works at humanization=0 (clean obs only at this stage)

*Phase 3.5 — COMPLETE (superseded by v39)*

All humanization code is implemented and live. v39 trained with random tier sampling,
conditioning the policy on difficulty. DifficultyShowcase (Pushman/8) demonstrates all
4 tiers working with v39 ONNX. No further training work needed for difficulty tiers.

- [x] Noise/delay implemented in `RLAgentBrain` (Profile_C, ring buffer, Box-Muller)
- [x] `runtimeHumanization` inspector field (≥0 pins tier, <0 randomises — default -1)
- [x] v39 trained with random tier sampling across {0, 0.33, 0.66, 1.0}
- [x] DifficultyShowcase scene (Pushman/8) built with 4 arenas, tiers pinned per arena

---

#### Phase 3.9 v3 — Expanded observation space (NEW)

Symptom observed during v2.5 evaluation: dodges hit reliably, but pushes and blocks
fired in apparently random directions. Root cause confirmed in the game code:

- **Dodge direction** = `player.Brain.GetMovement()` — comes from the agent's movement
  input, which it sets to chase the opponent (since it has opponent's relative position).
  Naturally aims correctly. ✓
- **Push** requires `Vector2.Dot(transform.up, toOther) > 0.3f` (~70° facing cone) to
  land. Agent has its own absolute rotation AND opponent's relative position, but no
  pre-computed "am I aimed at the opponent" signal. It must derive the trig implicitly,
  and after 30M+8M steps still hadn't. ✗
- **Block** same issue — block direction = facing direction.

The fix is a small set of pre-computed observations that reduce derivation load on
the network and surface strategically important signals directly.

**Observation additions (10 dims total, 23 → 33):**

| Obs | Dims | Rationale |
|---|---|---|
| `selfChargeProgress` | +1 | `currentChargeTime / pushChargeTime`; 0 when not charging. Enables push-release timing. |
| `selfRingOutMargin` | +1 | `1 - distFromCenter / currentRingRadius`. Pre-computed safety scalar. |
| `selfFacingOpponentDot` | +1/opp | `Dot(self.up, dirToOpp)`. Directly answers "am I aimed at opp." Fixes the symptom above. |
| `distanceToOpponent` | +1/opp | `|relPos| / ringOutRadius`. Saves the net from learning sqrt. |
| `opponentFacingSelfDot` | +1/opp | `Dot(opp.up, -dirToOpp)`. Predicts incoming pushes/blocks. |
| `opponentStateOneHot` | +5/opp | Replaces 1-dim float with 6-dim one-hot. Cleaner categorical encoding. |

**Total v3 obs space (Profile_A, 1v1):**
```
self:    kin(3) + stam(1) + state(1) + chargeProg(1) + stats(5) + personality(5) = 16
arena:   bounds(3) + ringOutMargin(1) = 4
per-opp: pos(2) + vel(2) + state-onehot(6) + facingSelfToOpp(1) + dist(1) + facingOppToSelf(1) = 13
TOTAL:   33
```

**Trade-offs:**
- Cannot warm-start from v2/v2.5 (obs space changed — input layer mismatch)
- +10 obs dims → +2560 first-layer weights (network has ~65k in that layer; trivial cost)
- Should converge in ~v2's 30M budget; might be faster because of the pre-computed features

**Strategy: validation-first.** v3 runs for **10M steps only** to confirm the obs
expansion fixes the aim symptom. If validation passes, Phase 3.7 warm-starts from
the v3 checkpoint and trains for another 20M steps with self-play enabled (combined
30M total budget). This keeps the v3 hypothesis cleanly testable while not wasting
the from-scratch training on a pure obs experiment.

**Config: `ppo_shared_v3.yaml`:**
- `max_steps: 10_000_000` (validation run)
- `num_epoch: 5` (carried over from v2.5 — value-function helper)
- `learning_rate: 3.0e-4` (v2 initial; we're starting fresh)
- No `init_path`

**Validation criteria (at 10M steps before launching Phase 3.7):**
1. Push and block aim look intentional (not random) in `Pushman/6` showcase
2. Entropy ≤ 3.5 (v2 was at 3.68 at 10M)
3. Episode length stable, not creeping above 200 (would indicate facing-reward dominance)

**Implementation checklist:**
- [x] Add 6 new obs flags to `ObservationProfile.cs`
- [x] Update `ObservationProfile.ComputeSpaceSize`
- [x] Add property accessors + emission logic to `RLAgentBrain.cs`
- [x] Enable new flags on Profile_A.asset and Profile_C.asset
- [x] Update `PushmanSetup.OBS_SIZE` to 33
- [x] Write `TrainingConfigs/ppo_shared_v3.yaml` (10M validation)
- [x] Write `TrainingConfigs/ppo_shared_v37.yaml` (20M self-play warm-start)
- [ ] Rebuild round-robin scene (`Pushman/3e`) — picks up new obs size automatically via Profile.ComputeSpaceSize
- [ ] Rebuild standalone, apply post-build patches (xattr + gRPC symlink)
- [ ] Train `Pushman_Shared_v3` (~3-4 h target at 10M steps)
- [ ] Validate aim in showcase before continuing
- [ ] Wire Team IDs in `PushmanSetup.WireRLPlayerForRR` (`bp.TeamId = playerIndex % 2`)
- [ ] Rebuild round-robin scene + standalone again for Phase 3.7
- [ ] Train `Pushman_Shared_v37` warm-starting from v3 (~6-8 h at 20M steps)
- [ ] Export ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v37.onnx`
- [ ] Update showcase scenes to use v3.7 ONNX (the final shipping model)

---

#### Phase 3.7 — Self-play hardening (after v3 validates)

Goal: make the agent robust against strategies it hasn't seen during the current training
run. ML-Agents' `self_play` block maintains a pool of past policy snapshots and
periodically swaps the round-robin opponents to older versions, so the policy must beat
not just its current self but prior iterations too. This prevents forgetting and improves
resistance to novel human play.

**Prerequisite code change — Team IDs:**
ML-Agents `self_play` requires each agent to have a `Team ID` set on its
`BehaviorParameters` (0 or 1). Currently all agents use the default (team 0). The
round-robin scene needs one player per arena set to team 0 and the other to team 1.
- [ ] In `PushmanSetup.WireRLPlayerForRR`, set `bp.TeamId = playerIndex % 2` (0 or 1)
- [ ] Rebuild round-robin scene (`Pushman/3e`) after the change

**Config — `ppo_shared_v37.yaml` (written, see file):**
- `init_path: results/Pushman_Shared_v3/Pushman/Pushman-10000000.pt` (warm-start from v3)
- `max_steps: 20_000_000` (combined with v3's 10M = 30M total budget, matches v2)
- `learning_rate: 1.0e-4` (preserve v3 strategies while adding robustness)
- `num_epoch: 5` (carried from v3)
- `self_play`: save_steps=100k, swap_steps=20k, window=15, play_against_latest_ratio=0.5

**Throughput note:** with self-play active, only the team-0 side learns each step
(team-1 is the frozen ghost). Effectively halves gradient updates per arena compared
to the v3 round-robin where both players trained. The 20M steps here ≈ 10M
v3-equivalent gradient updates. Accept this cost for the robustness benefit.

**Checklist:**
- [ ] Validate v3 aim improvements pass (see Phase 3.9 validation criteria above)
- [ ] Set `TeamId` on player agents in `PushmanSetup.WireRLPlayerForRR` (`playerIndex % 2`)
- [ ] Rebuild round-robin scene
- [x] Write `TrainingConfigs/ppo_shared_v37.yaml` with `self_play` block and `init_path`
      pointing at the v3 10M checkpoint
- [ ] Rebuild standalone, apply post-build patches
- [ ] Train `Pushman_Shared_v37` (~6-8h target at 20M steps with self-play active)
- [ ] Export ONNX → `Assets/MLModels/Pushman_Shared/PushmanAgent_v37.onnx`
- [ ] Verify ELO curve in TensorBoard (`Self-Play/ELO`) rises and stabilizes — a flat or
      falling ELO means the ghost pool is too strong or the LR is too low
- [ ] Human-vs-bot feel check vs v3 baseline — should be more strategically robust

---

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
  distinct on the 6 core action rewards. Revisit only if the 3f eval shows two
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

**Task 3c — Pre-rebuild fixes (code/assets — must precede the Phase 3 rebuild).**
Two unrelated changes that both must land before the standalone is rebuilt for Phase 3.

**3c.1 — `match_logs` path fix.** `MatchLogger` writes to
`Directory.GetCurrentDirectory()/match_logs/`, which for a standalone build resolves to
`builds/match_logs/`, not the project-root `match_logs/` that `report.py` searches. The
win-rate matrix is the ONLY mid-run health signal once self-play ELO is gone, so this
must work before Phase 3 launches.
- [x] In `tools/report.py`, function `newest_match_log()` — glob both locations:
  ```python
  files = sorted(
      glob.glob("match_logs/*.csv") + glob.glob("builds/match_logs/*.csv"),
      key=os.path.getmtime)
  ```
  (`glob` and `os` are already imported.)
- [x] Acceptance: `python tools/report.py --matches-only` finds the newest CSV with no
  hand-passed path. Phase 2's logs are in `builds/match_logs/` — test against those.

**3c.2 — Remove the dodge overdraft mechanic.** WHY: `Player.TryDodgeWithOverdraft`
lets a player dodge with insufficient stamina (the only gate is `currentStamina >= 0`),
going negative and pausing regen for `overdraftPauseDuration` (1.5s). The Phase 2 agents
abuse it — the dodge's reward is immediate, the ~6s "broke and can't act" dead zone is
delayed and diffuse, so the policy converged on "dodge freely, eat the debt." Playtest
on 2026-05-22 confirmed both agents stuck at negative stamina, drifting uselessly.
Overdraft turns a hard stamina limit into a soft one and kills stamina management as a
skill. DECISION: remove it (Option A) — a clutch-escape mechanic can be redesigned
properly later if wanted.
TIMING: must land before Phase 3 training. Phase 3 warm-starts from `Master_v1`, a
policy adapted to overdraft; it does NOT reset — under the new mechanic PPO adjusts it
away from the overdraft-reliant local optimum during the run. Doing it now folds that
adaptation into a run that happens anyway; doing it after Phase 3 needs a separate
adaptation run. Expect Phase 3 to converge a little slower since the warm-start begins
maladapted (un-learning overdraft on top of learning the 5-way differentiation).
- [x] Removed `allowDodgeOverdraft` and `overdraftPauseDuration` fields from
  `CharacterStats.cs` entirely. Removed from `DefaultStats.asset`. Heavyweight/Speedster
  never had the fields serialized — no changes needed.
- [x] Replaced `TryDodgeWithOverdraft` with `TryDodge` (simple `CanUseStamina` check).
  Removed `_regenPausedUntil`/`RegenPaused` from `Player.cs`. Cleaned regen logic
  (removed `!RegenPaused` gate). Updated `PlayerDodgingState.cs` and `StaminaHUD.cs`.
- [x] Dodge hard-requires `dodgeStamina`; stamina floors at 0.

**Task 3d — Round-robin training scene (`Pushman/3e`).** ✅ DONE 2026-05-22
- [x] `Pushman/3e. Build Round-Robin Training Scene` added to `PushmanSetup.cs`.
- [x] 45 arenas (9×5), 15 pairings × 3. 5 mirrors + 10 cross. `pairing = arenaIndex % 15`.
- [x] Each personality appears exactly 18 times (9 arenas × 2 players). Verified via
  scene YAML parse: 90 total behavior entries, 18 per personality.
- [x] `BehaviorParameters.BehaviorName` = personality name ("Aggressive", etc.).
  Both players `BehaviorType.Default` — both train.
- [x] `ObservationProfile` = Profile_A, obs space 18. All `statsPool` = 3 variants.
- [x] Saved → `Assets/Scenes/ML_Training_RoundRobin.unity`.
  Build Settings: round-robin is enabled first; all other training scenes disabled.

**Task 3e — Multi-behavior config + train.**
- [x] `TrainingConfigs/ppo_roundrobin.yaml` written — 5 behaviors, all matching Master
  checkpoint architecture (128/2/mem128). All `init_path` verified to exist.
  YAML parses cleanly; all 5 checkpoint paths confirmed present.
- [ ] FAST-FAIL CHECK: within the first ~2 min of the run, confirm the log prints
  `Initializing from results/Pushman_Master_v1/...` for every behavior and shows NO
  `Failed to load` warnings. Abort immediately on any failure.
- [ ] Rebuild standalone (ML_Training_RoundRobin is now the only enabled scene;
  overdraft code removed — both require a rebuild). Apply post-build patches.
- [ ] Run: `./train.sh --standalone --config=ppo_roundrobin --id=Pushman_RoundRobin_v1 --force`
  All 5 networks train at once. ~4-6 h; ~5M steps per behavior.
- [ ] Export each behavior's ONNX →
  `Assets/MLModels/[Personality]_Default/PushmanAgent.onnx`.

**Task 3f — Evaluate.**
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

**Task 4e — State readability (sprite visibility).** ✅ DONE 2026-05-22
The hand and state sprites were too small to read at the camera's zoom (orthographic
size 12, full 20u ring) — a viewer could not tell which state a player was in.
- ✅ Hand sprites enlarged (`PushmanSetup.ApplyPlayerSprites`): push fist scale
  0.45/0.30 → 1.6/1.6 (≈0.38×0.26u); block shield 1.1/0.18 → 1.5/1.8 — was a 0.018u
  hairline, now a 0.72×0.18u body-width plate.
- ✅ `Player.UpdateStateColor`: added a Blocking body tint (there was none — blocking
  showed only via the hairline shield); Dodging strengthened to a vivid blue; colour
  lerp 5→8/s so a 0.25s dodge registers.
- ✅ Applied via `Pushman/4`; regenerated hand bounds verified in-scene over the MCP.
- DEFERRED to the Task 4b effects pass: dodge `TrailRenderer` + scale-pop.

PLAYTEST FINDING (2026-05-22) — while verifying 4e in Play mode, both agents were
observed stuck in NEGATIVE stamina (P1 −34, P2 −7; max 100), unable to push/dodge/block,
drifting in Moving state. Root cause: the dodge overdraft mechanic is being abused
(see Phase 3 Task 3c.2). This is why the watch scene looks static — it is a gameplay
issue, not a visual one. The 4e visuals themselves are correct and verified.

---

### Suggested execution order

1. ✅ **Phase 1** — reward retune + LSTM baseline. Done.
2. ✅ **Phase 2** — `Pushman_Master_v1`, 20M-step Aggressive self-play. Done.
3. **Phase 3** — round-robin (NEXT). Remaining tasks in dependency order:
   a. Task 3c — pre-rebuild fixes: match_logs path + remove dodge overdraft (code/assets).
   b. Task 3d — build `ML_Training_RoundRobin` scene in Unity.
   c. Task 3e — write `ppo_roundrobin.yaml`, ONE rebuild, launch + fast-fail check.
   d. Task 3f — evaluate the personality win-rate matrix; retune as needed.
4. **Phase 4** — visual & gameplay polish. Task 4e (state readability) ✅ done; 4a-4d
   parallelisable, pick up between training runs.
