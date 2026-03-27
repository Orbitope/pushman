# Pushman: RL Environment Roadmap

## Phase 1: Environment & Project Setup (Ubuntu & Unity)
- [ ] **Configure Unity 2D Settings:** Ensure the project is set to 2D.
- [ ] **Install Unity ML-Agents:** Use the Unity Package Manager to install `com.unity.ml-agents`.
- [ ] **Setup Ubuntu Python Environment:** Create a Python virtual environment (`python3 -m venv venv-mlagents`) in the project folder.
- [ ] **Install ML-Agents Python Package:** Install PyTorch and `mlagents` via pip in the Ubuntu terminal.

## Phase 2: Assets & Physics Prototyping (The Arena)
- [ ] **Create the Arena:** Build a simple circular sprite for the mat and define the center point. 
- [ ] **Create Player Prefabs:** Build the basic 2D shapes for the players.
- [ ] **Configure Player Physics:** Add `Rigidbody2D` (Dynamic, freeze Z rotation) and `CircleCollider2D` (body).
- [ ] **Configure Hand/Block Colliders:** Add child objects with colliders tagged as "Block" for pushing/blocking.
- [ ] **Set Collision Matrix:** Ensure player colliders interact with each other correctly in Project Settings.

## Phase 3: Core Logic & Human Validation (The Baseline)
- [ ] **Import State Scripts:** Bring in `Player.cs`, `PlayerStateBase.cs`, and all specific state scripts.
- [ ] **Import Brain Architecture:** Bring in the `IPlayerBrain` interface and the `HumanBrain` script.
- [ ] **Hook up Human Controls:** Attach `HumanBrain` to Player 1 and map the input to the keyboard.
- [ ] **Playtest Mechanics:** Boot the game and manually verify movement, push force, stamina drain, and knockback.

## Phase 4: Match Management (The Referee)
- [ ] **Setup Spawn Points:** Create empty GameObjects for P1 and P2 starting positions.
- [ ] **Implement ArenaManager:** Bring in the `ArenaManager.cs` script to calculate distance from the center.
- [ ] **Validate Ring-Outs:** Manually walk off the edge and verify the manager detects the loss and resets the round.

## Phase 5: Reinforcement Learning Wiring (The Senses)
- [ ] **Import RL Scripts:** Bring in `RLAgentBrain.cs` and the `BotPersonality` ScriptableObject.
- [ ] **Configure Agent Component:** Add the `Behavior Parameters` component to the player prefab and set the correct Observation Space size.
- [ ] **Design Reward Profiles:** Create a few `BotPersonality` assets in the editor and plug them in.
- [ ] **Test Heuristic Mode:** Set behavior to "Heuristic Only" to control the RL Agent manually and verify it reads observations/rewards correctly.

## Phase 6: Training (The Hyperbolic Time Chamber)
- [ ] **Create the YAML Config:** Save `PushmanConfig.yaml` in your Ubuntu project directory.
- [ ] **Initialize Training:** Run `mlagents-learn PushmanConfig.yaml --run-id=Pushman_v1` in the terminal.
- [ ] **Start the Simulation:** Hit Play in the Unity Editor to begin the training loop.
- [ ] **Monitor:** Open a second terminal to run TensorBoard and track the AI's learning curve.

## Phase 7: Evaluation & Exhibition (The Showdown)
- [ ] **Import Neural Networks:** Move the trained `.onnx` files into your Unity assets.
- [ ] **Assign Models:** Slot the trained models into the `Behavior Parameters` of your prefabs.
- [ ] **Set to Inference:** Change the behavior mode to "Inference Only".
- [ ] **The Ultimate Test:** Assign P1 a `HumanBrain` and P2 an `RLAgentBrain`, hit play, and fight your bot.
