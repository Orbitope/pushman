# Pushman: Core Logic & RL Architecture Master Plan

## 1. Project Context & Overall Goals
**The Vision:** Pushman is a fast-paced, 2D physics-based top-down fighting game where 2 to 4 fighters battle in a circular arena, attempting to push each other out of bounds (similar to Sumo). 

**The Dual Purpose:**
1. **Playable Game:** A fun, lightweight multiplayer experience featuring a roster of characters with distinct physical characteristics (e.g., heavy bruisers vs. fast dodgers).
2. **Robust RL Environment:** A highly scalable testbed for Unity ML-Agents. The goal is to train diverse bot personalities (using different reward functions) that can adapt to different character stats and provide dynamic opponents for human players.

**Target Feel:** Movement must be **snappy and instantly responsive** (1:1 input), while combat impacts (pushes, blocks, dodges) must leverage the physics engine for chaotic, mass-based knockbacks.

---

## 0. Implementation Status (updated 2026-05-20)

### ⭐ FULL IMPLEMENTATION COMPLETE ⭐
All code, editor tooling, prefabs, scenes, and visual feedback are complete. Ready for testing and ML-Agents training.

### Post-Implementation Fixes (2026-05-19)
- ✅ **Input System Compatibility** — Updated HumanBrain to use new Input System package (1.19.0)
  - WASD for movement (direct key polling)
  - Left/right mouse for push/block, spacebar for dodge
  - Resolved: "InvalidOperationException: You are trying to read Input using UnityEngine.Input class"
- ✅ **Observation Space Mismatch** — Fixed BehaviorParameters configuration
  - Observation size corrected to 13 (was 1, causing truncation warning)
  - Re-ran PushmanSetup to rebuild scene and prefabs
  - Resolved: "More observations (13) made than vector observation size (1)"
- ✅ **Test Bot Brains** — Added 3 simple IPlayerBrain implementations for manual testing
  - `StandingBotBrain` — stationary, pushes every 2s
  - `ChaseBotBrain` — walks slowly, random push/block
  - `DodgingBotBrain` — dodges toward player every 5s

### Post-Implementation Fixes (2026-05-20) — Session 1
- ✅ **RLAgentBrain Input System** — Fixed `Heuristic()` using legacy `Input.GetButton/GetAxisRaw`
  - Replaced with `Keyboard.current` / `Mouse.current` (new Input System)
  - Resolved: "InvalidOperationException: You are trying to read Input using UnityEngine.Input class" in RLAgentBrain.Heuristic
- ✅ **Observation Space Mismatch (again)** — Root cause was fragile SerializedObject path lookup in ConfigureBehaviorParameters
  - Switched to direct `bp.BrainParameters.VectorObservationSize = 13` API — now reliable
  - Re-ran `Pushman/1. Setup Test Scene` to apply fix
- ✅ **Player Sprites** — Added full sprite system
  - `Assets/Sprites/PlayerCircle.png` — 64px filled circle sprite for player body
  - `Assets/Sprites/PushHand.png` — 24×16 rect for push fist (shown in Charging/Pushing states)
  - `Assets/Sprites/BlockShield.png` — 48×10 rect for block shield (shown in Blocking state)
  - `Assets/Sprites/ArenaBoundary.png` — 256px ring outline for stage boundary visual
  - Player1=Green, Player2=Red; arena ring at 45% opacity behind players
- ✅ **Hand Toggling** — `Player.cs` now has `pushHand`/`blockHand` SpriteRenderer fields
  - Auto-discovered from children named "PushHand"/"BlockHand" in Awake
  - `UpdateHands()` called in Update() — shows push hand in Charging+Pushing, block shield in Blocking
  - Dodge state adds blue body tint via `UpdateStateColor()`
- ✅ **PlayerVisuals.cs** — Optional companion component; auto-discovers hands by child name as backup
- ✅ **Prefabs updated** — Re-ran `Pushman/2. Save Prefabs` after sprite setup

---

### Post-Implementation Fixes (2026-05-20) — Session 2
- ✅ **Screen-space HUD** — replaced world-space stamina bars with overlay canvas (see Section 8D)
- ✅ **Stamina rebalance** — dodge 40, push 20→40 charge-scaled, regen Moving-only (see Section 8D)
- ✅ **HumanBrain Input System fix** — added `Update()` to re-acquire `Keyboard.current`/`Mouse.current` if null at OnEnable time
- ✅ **PushmanSetup editor robustness** — reflection-based StaminaHUD instantiation, file-exists check before asset cache lookup, stale bar child cleanup

### Phase 1 — Logic & State Machine Fixes: ✅ COMPLETE
- ✅ `CharacterStats.cs` ScriptableObject created; `Player.cs` fully migrated (`stats` reference, `rb.mass = stats.weight`)
- ✅ `IPlayerBrain` has `GetRotationInput()`; all implementors updated in one pass
- ✅ `HumanBrain` — mouse-tracking rotation with deadzone
- ✅ `CustomLogicBrain` — heuristic chase/charge bot, implements full interface
- ✅ `PlayerMovingState` / `PlayerChargingState` — `rb.linearVelocity` (snappy), rotation via `GetRotationInput()`
- ✅ `PlayerChargingState` — tracks `currentChargeTime` timer (charge-zero bug fixed)
- ✅ `PlayerPushingState` — reads `chargingStateScript.currentChargeTime`; stamina gate; whiff/recovery stun
- ✅ `PlayerBlockingState` — stamina drain; exits to Moving when stamina depleted
- ✅ `Player.ExecutePush` — `Physics2D.OverlapCircleAll` (fixes `OnCollisionEnter2D` trap); block-break + pusher stun; dodge-vs-dodge mutual bounce

### Phase 2 — Scalability & Environment Resets: ✅ COMPLETE
- ✅ `ArenaManager` — `List<Player>` architecture; dynamic ring-out; shrinking stage; randomized spawns & rotations; explicit `EndAllEpisodes()` ordering fix

### Phase 3 — RL Architecture: ✅ COMPLETE (code)
- ✅ `RLAgentBrain` — `ActionSpaceMode` toggle (Discrete/Continuous); `BotPersonality` reward hooks; `OnValidate` space-size logger
- ✅ `ObservationProfile` ScriptableObject — Profile A/B flags, FOV gate, Profile C stubs (noise/delay not yet active)
- ✅ ScriptableObject assets created: `DefaultStats.asset`, `Profile_A.asset`, `Aggressive.asset`
- ✅ `TrainingConfigs/ppo_discrete_baseline.yaml` — PPO + LSTM (Option A)
- ✅ `TrainingConfigs/ppo_selfplay.yaml` — PPO + Self-Play (Option B)
- ✅ `TrainingConfigs/sac_continuous.yaml` — SAC / Continuous mode (Option C)

### Phase 4 (Section 5) — Unity Editor Setup: ✅ COMPLETE
- ✅ Arena Prefab — `Assets/Prefabs/Arena.prefab` (ArenaManager, ArenaCenter, 4 SpawnPoints at cardinal positions, Player1/Player2)
- ✅ Player Prefab — `Assets/Prefabs/Player_RL.prefab` (RLAgentBrain, BehaviorParameters, DecisionRequester period=5)
- ✅ `ML_Training_Scene.unity` — 8×8 = 64 arenas in grid (25u spacing; isolation by distance, ringOutRadius=10)
- ✅ `PushmanSetup.cs` editor script — 3-step menu: "1. Setup Test Scene" / "2. Save Prefabs" / "3. Build Training Scene"
- Note: Physics 2D layer-per-arena not feasible (only 32 layers). Arenas isolated by spacing — players ring out before reaching neighbours.

### Section 8D — Visual Feedback: ✅ COMPLETE
- ✅ **Screen-space stamina HUD** — `StaminaHUD.cs` Screen Space Overlay canvas
  - P1 green bar bottom-left, P2 red bar bottom-right (300×28px, 20px margin)
  - `Image.Type.Filled` horizontal fill — no scale tricks, no world-space issues
  - Orange-red tint flash when stamina < 25%
  - Auto-discovers `Arena/Player1` and `Arena/Player2` by name if not inspector-wired
  - `PushmanSetup` creates + wires HUD via reflection (avoids editor compile dependency)
  - Old world-space SpriteRenderer bars removed; `PushmanSetup/4` cleans up stale children
- ✅ `SpriteRenderer.color` state indicators:
  - **Stunned** → Gray flash (visual stagger effect)
  - **Charging** → White glow (full brightness, shows readiness)
  - **Dodging** → Blue tint
  - **Low Stamina** (< 20%) → Red tint (warning indicator)
  - **Normal** → Base color with smooth transition
- ✅ **Stamina rebalance** (2026-05-20)
  - Dodge cost: 20 → **40** (~40% of pool per dash)
  - Push cost: flat 20 → **20–40 scaled by charge** (`pushStamina * (1 + chargeNorm)`)
  - Block drain: 15/s (unchanged)
  - Regen: 8/s, **Moving state only** — must disengage to recover
  - All values synced between `DefaultStats.asset` and `InitCharacterStats()`
- ✅ **Dodge fix** (2026-05-20) — damping was eating the dash velocity
  - `PlayerDodgingState.BeginState()` now zeros `rb.linearDamping` for the dash duration, restores in `EndState()`
  - `dodgeForce` 10 → **18** (travels ~4.5 units in 0.25s — readable as a distinct action)
- ✅ **Camera** (2026-05-20) — orthographic size set to 12 (shows full 20u ring + 2u padding each side, static)

### Pending — Bugs (pre-identified, not yet hit in testing)

**Definite / likely to hit:**
- ❌ **Ring shrink is invisible** — `ArenaManager` correctly shrinks `currentRingRadius` after 30s of inactivity, but `ArenaBoundary` SpriteRenderer scale is baked at setup time. Players ring out at an invisible boundary inside the visible ring. Fix: `ArenaManager` must update the `ArenaBoundary` child's scale each frame to match `currentRingRadius`.
- ❌ **No round-reset feedback** — when a ring-out occurs, players just teleport with no pause, flash, or score display. Will be confusing during testing. Fix: brief freeze-frame or color flash on ring-out + simple score counter UI.
- ❌ **Input priority: accidental mouse hold blocks dodge** — `PlayerMovingState` checks push → block → dodge in order. Holding left mouse + pressing Space enters Charging, not Dodging. Likely to feel like "dodge is broken" to new players. Fix: either consume `wasPressedThisFrame` for dodge regardless of mouse state, or document as intentional trade-off.
- ❌ **`CharacterStats.cs` C# field defaults are stale** — class defaults (`dodgeForce=12`, `dodgeStamina=20`, `blockUsageRate=20`) don't match `DefaultStats.asset` or `InitCharacterStats()`. New assets created from the menu will have wrong values. Fix: sync C# defaults to match the tuned values.

**Medium risk — training-relevant:**
- ❌ **`FindObjectsByType<Player>()` called every `Update()` in all 3 bot brains** — fine in the test scene (1 arena), but in the 64-arena training scene this is hundreds of expensive searches per frame. Fix: cache the target in `Start()` or have the bot brain accept a direct reference via an inspector field or `ArenaManager`.
- ❌ **`DodgingBotBrain.wantDodge` is single-frame, execution-order dependent** — the flag is true for exactly one frame. If `Player.Update()` runs before `DodgingBotBrain.Update()` that frame, the dodge input is silently missed. Script execution order is not configured. Fix: use a persistent flag that clears only after `GetDodgeInput()` is read, or configure Script Execution Order.
- ❌ **Observation space size hard-coded at 13; will break if `ObservationProfile` changes** — if any flag in `Profile_A.asset` is toggled, actual collected observations diverge from `VectorObservationSize = 13`, causing a hard ML-Agents runtime error. Fix: call `observationProfile.ComputeSpaceSize()` when setting up `BehaviorParameters` rather than using `OBS_SIZE` constant.

**Design ambiguities to confirm during play:**
- ❓ **Dodge bypasses block** — `HandleDodgeCollision` doesn't check if the target is blocking. A dodge-tackle always stuns regardless of block stance. Possibly intentional (dodge = block-breaker) but undocumented. Decide and document.
- ❓ **Push fires on mouse RELEASE, not press** — holding left mouse charges, releasing fires the push. Correct fighting-game design but unintuitive for new players expecting click = push.

### Pending — Visual Polish
- ❌ **Stamina bars: real-time drain animation** — the `StaminaHUD` fill bars update each frame but the instant snap may feel abrupt; add a smooth lerp to `fillAmount` so the drain is visually animated rather than a hard cut. Consider a "ghost" secondary bar that lags behind to show recent stamina loss (common in fighting games — the gap between the ghost and the live bar shows the cost of the action just taken).
- ❌ **Remove body color-change as stamina indicator** — the red tint on <20% stamina in `Player.UpdateStateColor()` is redundant now that the HUD bars exist. Remove or repurpose it so color change only reflects state (stunned/dodging/charging), not stamina.

---

## 2. Current State Assessment
Based on an audit of the current workspace (`Assets/Scripts` and project structure):

- **What we have:** A functional but flawed prototype. There is an `IPlayerBrain` interface, a basic state machine (`PlayerMovingState`, `PlayerPushingState`, etc.), an `ArenaManager`, and a placeholder `RLAgentBrain`.
- **What is missing/broken:**
  - **Physics Feel:** The current movement relies on `AddForce()`, which feels sluggish ("floaty") rather than snappy.
  - **Logic Gaps:** Charging does not properly transfer duration to the push strength. Blocking collisions don't properly break the block or stun the attacker. Dodging collision rules are incomplete.
  - **Archetypes:** Player stats (`weight`, `speed`, `pushForce`) are hardcoded into the `Player.cs` class, making it impossible to hot-swap characters or train agents on different body types seamlessly.
  - **Scalability:** `ArenaManager` is strictly hardcoded for a 1v1 (`player1` vs `player2`).
  - **RL Architecture:** The agent's action space and environment reset loops are not configured for massive parallel training.

### Code Audit Findings (2026-05-19)
A line-by-line audit of `Assets/Scripts` confirmed the following concrete defects. Each is addressed by a specific task in Section 4:
1. **Charge time is always zero.** `PlayerChargingState` never tracks how long the push button is held; `PlayerPushingState.BeginState()` resets `chargeTime = 0` and measures elapsed time from when the *push* state begins, not from when charging began. Charged pushes therefore gain no extra strength. *(Fix: Phase 1 — Charging/Pushing.)*
2. **Blocked pushes do not stun or break the block.** `Player.HandlePushCollision` applies backwards force to the pusher on a block hit but never calls `Stun()` on the pusher and never forces the blocker out of `Blocking`. *(Fix: Phase 1 — Player collisions.)*
3. **Dodge-vs-dodge is unhandled.** `Player.HandleDodgeCollision` always knocks the other player; it never checks whether the other player is also `Dodging` to apply a mutual bounce + stun. *(Fix: Phase 1 — Player collisions.)*
4. **`OnCollisionEnter2D` misses already-touching pushes.** If two players are already in contact when `Push()` fires, no collision event fires and the push silently does nothing. *(Fix: Phase 3 — explicit overlap cast.)*
5. **`EndMatch` reward/episode ordering is fragile.** `RegisterWin()`/`RegisterLoss()` each call `EndEpisode()` internally, then `EndMatch` continues to mutate player state afterward. *(Fix: Phase 2 — ArenaManager.)*
6. **`IPlayerBrain` has no `GetRotationInput()`.** Rotation is still derived from movement direction via `Atan2` in the state scripts, so strafing is impossible. *(Fix: Phase 1 — interface change cascades to all brains.)*

---

## 3. Core Architecture & Gameplay Mechanics

### A. The "Brain" Abstraction (Input Agnostic)
The `Player.cs` script will remain entirely ignorant of *how* it is being controlled. It simply queries the `IPlayerBrain` interface. This allows us to instantly hot-swap controllers in the Unity Editor:
1. **HumanBrain:** Reads user input. *(Note: The project contains `InputSystem_Actions.inputactions`. We will ensure `HumanBrain` maps to `Input.GetAxisRaw` for now, but is ready for the new Input System).*
2. **RLAgentBrain:** Driven by the ML-Agents neural network policy.
3. **CustomLogicBrain (New):** A heuristic C# template (e.g., `if (distance < 2) return Push`) to create baseline offline bots for RL agents to train against. *This is a Phase 1 deliverable, not an afterthought — RL agents need a non-trivial scripted opponent before self-play is viable.*

### B. The Hybrid Physics System
To achieve the "snappy" feel, we will separate movement from impacts:
- **Movement (Kinematic Feel):** In `Moving` and `Charging` states, we directly set `rb.linearVelocity = moveDirection * movementSpeed`. The character stops instantly when input ceases.
- **Combat (Dynamic Feel):** During a `Dodge`, or when receiving a `Push` (Hit/Stunned), we relinquish velocity control and apply `rb.AddForce(..., ForceMode2D.Impulse)`. The physics engine resolves the knockback distance naturally based on the character's `rb.mass`.

### C. Player Characteristics System
To support varied playstyles, we will decouple stats from `Player.cs`.
- **[NEW] `CharacterStats.cs`:** A ScriptableObject defining `weight`, `maxStamina`, `staminaRegenRate`, `blockStaminaUsageRate`, `dodgeForce`, `dodgeStamina`, `dodgeTime`, `pushForce`, `pushStamina`, `pushChargeMultiplier`, `pushChargeTime`, and `movementSpeed`.
  - **Prerequisite ordering:** `CharacterStats.cs` must be created and `Player.cs` migrated to it *before* the Phase 1 movement refactor, because the movement code references `movementSpeed`. `Player.cs` currently hardcodes all of these as public fields (plus an unused `specialStamina`) — those fields must be deleted and replaced with a single `public CharacterStats stats` reference, and every state script repointed to `player.stats.*`. Decide whether `weight` is pushed onto `Rigidbody2D.mass` in `Awake()`.
- **RL Observation Injection:** The `RLAgentBrain` will observe its own `CharacterStats`. This means a single, master neural network can learn to play *any* character. If it spawns with high weight/low speed, the network will dynamically adapt to play defensively.

---

## 4. Technical Implementation Roadmap (Code Updates)

### Phase 1: Logic & State Machine Fixes
*Ordering matters: do the `CharacterStats` migration first, then the `IPlayerBrain` change, then the state scripts, then `Player.cs`.*

- **[PREREQUISITE] `CharacterStats.cs` migration (see Section 3C):** Create the ScriptableObject, delete the hardcoded stat fields from `Player.cs`, add a `public CharacterStats stats` reference, and repoint every `player.movementSpeed` / `player.pushForce` / etc. usage in the state scripts to `player.stats.*`. Nothing else in Phase 1 compiles cleanly until this is done.
- **[NEW] `CustomLogicBrain.cs`:** Implement a heuristic `IPlayerBrain` (distance/angle checks → push/block/dodge) as a baseline training opponent. Must implement the full interface, including the new `GetRotationInput()`.
- **`IPlayerBrain.cs` & `HumanBrain.cs`:** 
  - Add `float GetRotationInput()` to decouple rotation from movement, allowing strafing.
  - **Cascade warning:** Adding a method to `IPlayerBrain` forces *every* implementer to be updated in the same change — `HumanBrain`, `RLAgentBrain`, and the new `CustomLogicBrain`. Do all of them in one pass or the project will not compile.
  - `HumanBrain` will calculate rotation dynamically by tracking the mouse pointer in world space. It will compare the player's `transform.up` vector to the vector pointing at the mouse. If the angle between them is outside a small "dead zone" (e.g., 5-10 degrees to prevent jitter/whiplash), it will return `-1` or `1` to rotate the player smoothly toward the pointer.
- **`PlayerMovingState.cs` & `PlayerChargingState.cs`:** 
  - Switch to `rb.linearVelocity = moveDirection * movementSpeed`. 
  - Remove auto-rotation (`Mathf.Atan2(velocity)`). Apply rotation continuously via `player.transform.Rotate(0, 0, -rotInput * 360f * Time.deltaTime)`.
  - **`PlayerChargingState`:** Track `public float currentChargeTime`. Increment via `Time.deltaTime`, clamped to `stats.pushChargeTime`. **This timer does not exist today** — the charging state currently has none. `PlayerPushingState` must *read* this value on Enter; it must not recompute its own elapsed time. The present `chargeTime = Time.time - pushStartTime` logic inside `PlayerPushingState` is the source of the zero-charge bug and must be deleted.
- **`PlayerPushingState.cs`:** 
  - **Enter:** Read `player.chargingStateScript.currentChargeTime`. Trigger the "Push" animation.
  - **Push():** Apply forward force `pushForce + (pushForce * multiplier * (chargeTime / maxChargeTime))`. 
  - **Stamina/Penalty:** If not enough stamina, do not lunge, apply a wasted stamina RL penalty, and immediately return to `Moving`.
  - **Exit:** The `Push()` action will conclude by calling `player.Stun(0.2f)`, simulating the attack recovery frames before returning to `Moving`.
- **`PlayerBlockingState.cs`:** 
  - Enforce stamina drain: `if (player.CanUseStamina(rate * Time.deltaTime)) { player.UseStamina(...); } else { player.SetState(Moving); }`.
- **`Player.cs` (Collisions & Stamina):** 
  - Centralize stamina regeneration rules in `Update` (only recharge during Moving, Pushing, Stunned).
  - In `HandlePushCollision`: If hit on the "Block" tag, `ApplyForce` to the pusher backwards, `Stun(0.5f)` the pusher, and force the blocker into the `Moving` state to break the block. Add `opponentRLBrain.AddSuccessfulBlockReward()`. *(Current code applies the backwards force only — the stun and the block-break are both missing.)*
  - In `HandleDodgeCollision`: If both players are dodging (`otherPlayer.currentState == PlayerState.Dodging`), apply impulse bounce to *both* and `Stun(0.5f)` *both*. *(Current code never checks the other player's state.)*

### Phase 2: Scalability & Environment Resets
- **`ArenaManager.cs`:** 
  - Refactor to use `public List<Player> activePlayers`.
  - Implement dynamic ring-out checks: If `Vector2.Distance(player.pos, center) > ringOutRadius`, apply `loseRound` reward, remove from `activePlayers`, and disable GameObject.
  - **Win Condition:** When `activePlayers.Count <= 1`, apply `winRound` reward to the survivor, and call `EndEpisode()` on all agents.
  - **Reward/episode ordering:** Apply *all* rewards (`winRound`, `loseRound`) **before** any call to `EndEpisode()`, and call `EndEpisode()` exactly once per agent. The current `RegisterWin()`/`RegisterLoss()` helpers call `EndEpisode()` themselves, which makes ordering fragile — refactor so reward application and episode termination are separate, explicit steps owned by `ArenaManager`.
  - **ResetEnvironment():** Randomize spawn locations and starting rotations (0 to 360 degrees) to prevent RL positional memorization. Reset stamina and states.

### Phase 3: RL Architecture (`RLAgentBrain.cs`)
- **Action Space Toggle (DECISION: support both modes):** Expose a `public enum ActionSpaceMode { Discrete, Continuous }` on `RLAgentBrain`. `OnActionReceived()` and `Heuristic()` branch on this flag so the *same* script can train either way. Each mode needs its own YAML config and matching `Behavior Parameters` settings.
  - **Discrete mode** (faster convergence — default for baseline PPO):
    - Branch 0 (Move X): `0=None, 1=Left, 2=Right` -> Mapped to `[-1, 0, 1]`
    - Branch 1 (Move Y): `0=None, 1=Down, 2=Up` -> Mapped to `[-1, 0, 1]`
    - Branch 2 (Rotate): `0=None, 1=CCW, 2=CW` -> Mapped to `[-1, 0, 1]`
    - Branch 3 (Action): `0=None, 1=Push, 2=Block, 3=Dodge`
  - **Continuous mode** (analog movement — pairs with SAC / Option C): 2 continuous axes for movement + 1 continuous axis for rotation, plus a discrete branch for the action button. This mirrors the current implementation and must be kept working.
  - **Caveat:** `Behavior Parameters` action sizes are set in the Editor and cannot change at runtime. The toggle only selects which code path runs — switching modes still requires reconfiguring the component and using the correct YAML. Document both presets.
- **Push Hit Detection (fixes the `OnCollisionEnter2D` trap, Section 8C):** Do not rely on `OnCollisionEnter2D` to detect a landed push — it never fires when two players are already touching. When `Push()` executes, do an explicit `Physics2D.OverlapCircle` (or `OverlapBox`) in front of the player against the opponent layer, and resolve the push/block/reward logic from that result. `OnCollisionEnter2D` may remain as a fallback but must not be the primary path.
- **Modular Observation Space & Imperfect Information (DECISION: build the SO now):** 
  To support training bots of varying difficulty levels, we will create an `ObservationProfile` ScriptableObject. **This replaces the ad-hoc per-observation booleans currently on `RLAgentBrain`** (`obsSelfKinematics`, `obsOpponentPosition`, `obsOpponentVelocity`, etc.) — migrate those flags into the SO so observation behavior travels with the asset, not the prefab. The `RLAgentBrain` will read this profile dynamically to determine what it is "allowed" to see:
  - **Profile A (Perfect Information / Hard Difficulty):** 
    - *Self (16 floats):* Velocity(2), Rotation(1), Stamina(1), Vector to Center(2), Character Stats(10).
    - *Opponents (Fixed-Padding, 12 floats):* Always observes the exact Relative Position(2) and Relative Velocity(2) of up to 3 active opponents, regardless of where they are. (Missing opponents padded with zeros).
  - **Profile B (Imperfect Information / Normal Difficulty):**
    - *Field of View Restrictions:* Uses **Raycast Sensors** (or manual angle checks) instead of global coordinate vectors. The agent only gets data on opponents that fall within a realistic view cone (e.g., 120 degrees forward). If an opponent is behind them, the agent is functionally blind to them.
  - **Profile C (Humanized / Easy Difficulty):**
    - *Noise & Delay:* Takes the observations from Profile B, but applies Gaussian Noise to opponent positions/velocities. Furthermore, it utilizes a buffer array to feed the neural network observations from 5-10 frames ago, explicitly simulating human reaction time.

  *Implementation Note:* By hot-swapping the `ObservationProfile` alongside the `BotPersonality` (Rewards), you can explicitly train a tiered roster of AI opponents (e.g., Easy bots with delayed reactions vs. Hard bots with perfect global spatial awareness).

  *Observation count caveat:* The "Self (16 floats)" figure in Profile A omits two observations the plan requires elsewhere — the agent's own `currentState` (Section 3) and the dynamic `ringOutRadius` (Section 8A). Whatever the final per-profile float count is, it must be recomputed and the `Behavior Parameters` Space Size updated to match, or ML-Agents will throw a shape-mismatch error at connection time. Keep the `OnValidate()` space-size logger already in `RLAgentBrain` and extend it to read the active `ObservationProfile`.

---

## 5. Unity Editor Configuration (MCP Actions)
Assuming direct manipulation of the Unity Editor and assets, the following structure will be enforced:
1. **ScriptableObjects:** Create `Assets/ScriptableObjects/Characters/` to store `CharacterStats` assets (e.g., `SumoBot.asset`, `SpeedBot.asset`), and `Assets/ScriptableObjects/Observation/` for `ObservationProfile` assets (Profiles A/B/C).
2. **Arena Prefab:** Encapsulate the floor sprite, `ArenaManager`, and 4 empty GameObjects tagged as `SpawnPoint`.
3. **Player Prefab Components:** Add the `DecisionRequester` component (`Decision Period = 5`, see Section 8B) and the `Behavior Parameters` component to the Player prefab. Set action sizes to match the chosen action-space mode (Section 4 Phase 3).
4. **Training Scene (`ML_Training_Scene.unity`):** Instantiate the `Arena` prefab 64 times in a grid layout. Configure Physics 2D Layer Collision Matrix so players in Arena A cannot collide with players in Arena B.

---

## 6. Training Pipeline Options & Recommendations

While the implementation above assumes a standard PPO configuration, ML-Agents offers multiple approaches depending on how the AI should learn. The implementing agent should consider the following options and configure the workspace accordingly.

### Option A: Standard PPO with Heuristic Shaping (Recommended Baseline)
This is the fastest way to get competent bots that mimic specific playstyles.
- **Algorithm:** Proximal Policy Optimization (PPO).
- **Configuration File:** `TrainingConfigs/ppo_discrete_baseline.yaml`
- **Approach:** Use the dense reward shaping defined in `BotPersonality.cs` (e.g., +0.1 for landing a push, -0.05 for wasting stamina). This guides the bot explicitly on how to play.
- **Key Feature:** Enable **LSTM (Memory)** (`memory_size: 128`, `sequence_length: 64`). Because physics-based tells (like someone charging a push) unfold over time, memory is critical for the agent to recognize patterns rather than just reacting to a single frame of velocity.

### Option B: Self-Play (For Competitive Mastery)
If the goal is to create highly advanced, unpredictable opponents that discover their own meta-strategies, Self-Play is ideal for fighting games.
- **Algorithm:** PPO + Self-Play Module.
- **Configuration File:** `TrainingConfigs/ppo_selfplay.yaml`
- **Approach:** Remove most dense "shaping" rewards. Provide only sparse rewards (+1.0 for winning, -1.0 for losing). The agents fight copies of themselves (or past versions of themselves) via the ELO system and figure out the optimal way to win entirely on their own.
- **Tradeoff:** Takes significantly longer to train, but often produces superhuman tactics that human designers wouldn't think to reward.

### Option C: Soft Actor-Critic (SAC) (For Continuous Action Spaces)
If you decide to abandon the Pure Discrete action space and stick with Continuous actions (joystick-like precise movement):
- **Algorithm:** Soft Actor-Critic (SAC).
- **Configuration File:** `TrainingConfigs/sac_continuous.yaml`
- **Approach:** SAC is often more sample-efficient and robust for complex, continuous environments because it encourages exploration by maximizing the policy's entropy.
- **Tradeoff:** SAC requires more memory and computational overhead per step compared to PPO.

### Recommended File Structure for RL
The implementing AI agent should organize the training configurations cleanly in the root directory:
- `/TrainingConfigs/`
  - `ppo_discrete_baseline.yaml` (Fastest, uses Option A)
  - `ppo_selfplay.yaml` (Uses Option B)
  - `sac_continuous.yaml` (Uses Option C)

**Execution Command Examples:**
- `mlagents-learn TrainingConfigs/ppo_discrete_baseline.yaml --run-id=Pushman_Bot_Tank_v1`
- `mlagents-learn TrainingConfigs/ppo_selfplay.yaml --run-id=Pushman_SelfPlay_v1`

---

## 7. Python Server & Training Execution Protocol

To actually train the neural networks, the implementing agent must bridge the Python ML-Agents server with the Unity Editor client. Here is the strict protocol for spinning up the environment:

### Step 1: Python Environment Setup (Ubuntu/Mac)
The training server requires a dedicated Python environment.
1. Open a terminal in the root project directory.
2. Create a virtual environment: `python3 -m venv venv-mlagents`
3. Activate it: `source venv-mlagents/bin/activate`
4. Install dependencies: `pip install mlagents torch`

### Step 2: Spinning up the RL Training Server
1. Ensure the Unity Editor is open and currently on the `ML_Training_Scene.unity`.
2. In the activated Python terminal, execute the training command (e.g., `mlagents-learn TrainingConfigs/ppo_discrete_baseline.yaml --run-id=Pushman_v1`).
3. The Python server will initialize PyTorch and output a message similar to: `Listening on port 5004. Start training by pressing the Play button in the Unity Editor.`

### Step 3: Connecting the Unity Environment
1. Switch to the Unity Editor.
2. Press the **Play** button.
3. Unity will connect to the local Python server via socket on port 5004. The game will immediately fast-forward (TimeScale > 1), and the terminal will begin logging Mean Reward stats.

### Step 4: Real-Time Monitoring (TensorBoard)
1. Open a *second* terminal window and activate the virtual environment (`source venv-mlagents/bin/activate`).
2. Run TensorBoard pointing to the results folder: `tensorboard --logdir results/`
3. Open `http://localhost:6006` in a web browser to live-track the Cumulative Reward, Entropy, and Policy Loss graphs to ensure the bots are actually learning.

---

## 8. Crucial Missing Systems & Edge Cases (To Be Implemented)

While reviewing the architecture, several critical ML-Agents and gameplay edge cases were identified that must be accounted for in the implementation:

### A. The Shrinking Stage (Preventing Stalemates)
If two defensive bots spawn, they might never push each other, leading to an infinite episode where no learning occurs. Instead of an abrupt "Draw" timeout, we will force engagement naturally.
- **Action:** Update `ArenaManager.cs` to include a `timeUntilShrink` and `shrinkRate`. After a certain amount of time, the `ringOutRadius` will gradually decrease over time until it reaches a tiny minimum size (like a Battle Royale circle). 
- **Critical RL Update:** The `RLAgentBrain` *must* observe the current `ringOutRadius` dynamically in `CollectObservations()` so the bots realize the bounds are closing in on them and panic accordingly!

### B. Decision Requester (Frame Skipping)
Agents do not need to make decisions every single Unity frame (60fps). 
- **Action:** Add a `DecisionRequester` component to the Player prefab. Set the `Decision Period` to `5`. This means the agent only acts every 5 physics frames. This drastically speeds up training and prevents jittery, indecisive micro-movements.

### C. Combat Resolution (The `OnCollisionEnter2D` Trap)
Currently, `Push()` applies forward force and waits for `OnCollisionEnter2D`. 
- **The Bug:** If two players are *already touching* when one initiates a push, `OnCollisionEnter2D` will not fire (because they are already colliding). 
- **The Fix:** The implementation must either use `OnCollisionStay2D` and check if the state is `Pushing`, or better yet, use an explicit `Physics2D.OverlapCircle` cast in front of the player precisely when `Push()` is called.

### D. Visual Feedback & UI (The Human Element)
The AI knows exactly how much stamina it has and exactly what state it is in because it reads the floats perfectly. A human player will be completely blind.
- **Action:** Implement a World Space `Canvas` attached to the Player prefab. Place a simple UI `Slider` positioned just beneath the player sprite to act as the Stamina Bar. 
- **State Indicators:** Use `SpriteRenderer.color` changes to clearly broadcast states (e.g., Flash Grey when Stunned, tint Red when attempting an action without stamina, or glow White when fully charged). Without this, humans cannot fairly fight the trained bots.

### E. Component Behavior Toggling
The implementing agent must expose a clean way to swap who is controlling the `Player`.
- **Action:** Rely on the `Behavior Parameters` component's `Behavior Type` dropdown:
  - `Heuristic Only` -> Uses `HumanBrain` or keyboard input.
  - `Default` -> Connects to the Python server for training.
  - `Inference Only` -> Uses the baked `.onnx` neural network file to play locally without Python.
