using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class RLAgentBrain : Agent, IPlayerBrain
{
    public enum ActionSpaceMode { Discrete, Continuous }

    [Header("Action Space")]
    public ActionSpaceMode actionSpaceMode = ActionSpaceMode.Discrete;

    [Header("Agent References")]
    private Player myPlayer;
    public ArenaManager arenaManager;

    [Header("Opponents")]
    public Player[] opponents;

    [Header("Reward Configuration")]
    public BotPersonality personality;

    [Header("Observation Profile")]
    [Tooltip("Swap this asset to change what the agent can see. Null = Profile A defaults (all on).")]
    public ObservationProfile observationProfile;

    [Header("Humanization")]
    [Tooltip("-1 = randomise per episode from {0, 0.33, 0.66, 1.0} (training mode). " +
             "0–1 = pin to a specific tier for inference scenes (0=Expert, 1=Easy).")]
    public float runtimeHumanization = -1f;

    private Vector2 currentMovement;
    private float currentRotation;
    private int actionButton; // 0 none, 1 push, 2 block, 3 dodge

    // Humanization runtime state — set each OnEpisodeBegin, read in CollectObservations
    private float _humanization;  // current episode tier scalar (0=Expert, 1=Easy)
    private float _noiseSigma;    // = _humanization * 0.15
    private int   _delayFrames;   // = floor(_humanization * 12)

    // Ring buffer for observation delay — written every FixedUpdate, read in CollectObservations
    private const int RING_SIZE = 16; // must exceed max delay frames (12)
    private struct OppSnapshot { public Vector2 position; public Vector2 velocity; public float state; }
    private OppSnapshot[,] _oppRing; // [opponentIndex, ringSlot]
    private int _ringWriteIdx;

    // Cached from ObservationProfile; falls back to all-true if profile is null.
    private bool SelfKinematics => observationProfile == null || observationProfile.selfKinematics;
    private bool SelfStamina    => observationProfile == null || observationProfile.selfStamina;
    private bool SelfState      => observationProfile == null || observationProfile.selfState;
    private bool SelfStats         => observationProfile == null || observationProfile.selfStats;
    private bool SelfPersonalityId => observationProfile == null || observationProfile.selfPersonalityId;
    private bool ArenaBounds       => observationProfile == null || observationProfile.arenaBounds;
    private bool OppPosition    => observationProfile == null || observationProfile.opponentPosition;
    private bool OppVelocity    => observationProfile == null || observationProfile.opponentVelocity;
    private bool OppState       => observationProfile == null || observationProfile.opponentState;
    private bool OppStats              => observationProfile != null && observationProfile.opponentStats;
    private bool ObserveHumanization   => observationProfile != null && observationProfile.humanization;
    private bool SelfChargeProgress    => observationProfile != null && observationProfile.selfChargeProgress;
    private bool SelfRingOutMargin     => observationProfile != null && observationProfile.selfRingOutMargin;
    private bool SelfFacingOpponentDot => observationProfile != null && observationProfile.selfFacingOpponentDot;
    private bool DistanceToOpponent    => observationProfile != null && observationProfile.distanceToOpponent;
    private bool OpponentFacingSelfDot => observationProfile != null && observationProfile.opponentFacingSelfDot;
    private bool OppStateOneHot        => observationProfile != null && observationProfile.opponentStateOneHot;

    public override void Initialize()
    {
        myPlayer = GetComponent<Player>();

        // Sanity-check obs space at startup — fires in both Editor and standalone builds.
        // The fast-fail check in the training YAML tells you what to look for in the log.
        int oppCount = opponents != null ? opponents.Length : 0;
        int computedSize = observationProfile != null
            ? observationProfile.ComputeSpaceSize(oppCount)
            : ComputeDefaultSpaceSize(oppCount);
        Debug.Log($"[RLAgentBrain] {gameObject.name} obs={computedSize} opps={oppCount} " +
                  $"profile={(observationProfile != null ? observationProfile.name : "default")}");
    }

    public override void OnEpisodeBegin()
    {
        // Determine humanization tier for this episode.
        // runtimeHumanization >= 0 pins a specific tier (inference scenes).
        // runtimeHumanization < 0 samples uniformly from the 4 training tiers.
        if (runtimeHumanization >= 0f)
        {
            _humanization = Mathf.Clamp01(runtimeHumanization);
        }
        else
        {
            float[] tiers = { 0f, 0.33f, 0.66f, 1.0f };
            _humanization = tiers[Random.Range(0, tiers.Length)];
        }

        _noiseSigma  = _humanization * 0.15f;
        _delayFrames = Mathf.FloorToInt(_humanization * 12f);

        // Pre-fill ring buffer with current opponent state so the first N frames
        // of the episode don't read uninitialised data.
        if (_delayFrames > 0 && opponents != null && opponents.Length > 0)
        {
            if (_oppRing == null || _oppRing.GetLength(0) < opponents.Length)
                _oppRing = new OppSnapshot[opponents.Length, RING_SIZE];

            for (int o = 0; o < opponents.Length; o++)
            {
                var opp = opponents[o];
                var snap = opp != null
                    ? new OppSnapshot
                      {
                          position = opp.transform.position,
                          velocity = opp.Body.linearVelocity,
                          state    = (float)opp.currentState / 5f
                      }
                    : default;
                for (int s = 0; s < RING_SIZE; s++)
                    _oppRing[o, s] = snap;
            }
            _ringWriteIdx = 0;
        }
    }

    private void FixedUpdate()
    {
        // Write current opponent state into ring buffer every physics frame.
        // CollectObservations reads back _delayFrames steps ago to simulate reaction time.
        if (_delayFrames > 0 && _oppRing != null && opponents != null)
        {
            for (int o = 0; o < opponents.Length; o++)
            {
                var opp = opponents[o];
                _oppRing[o, _ringWriteIdx] = opp != null
                    ? new OppSnapshot
                      {
                          position = opp.transform.position,
                          velocity = opp.Body.linearVelocity,
                          state    = (float)opp.currentState / 5f
                      }
                    : default;
            }
            _ringWriteIdx = (_ringWriteIdx + 1) % RING_SIZE;
        }

        if (personality == null || arenaManager == null) return;

        // Time pressure: constant per-step penalty so agents prefer shorter matches.
        if (personality.timePenaltyPerStep != 0)
            AddReward(personality.timePenaltyPerStep);

        if (opponents.Length > 0 && opponents[0] != null)
        {
            // Mild reward for facing the opponent — encourages engagement over spinning in place.
            if (personality.facingOpponentMultiplier != 0)
            {
                Vector2 dir = (opponents[0].transform.position - transform.position).normalized;
                float dot = Vector2.Dot(transform.up, dir);
                if (dot > 0.5f) AddReward(dot * personality.facingOpponentMultiplier);
            }

            // Reward for pressuring the opponent toward the ring edge.
            // This replaces center-control: instead of rewarding where WE are,
            // reward how close THEY are to the boundary.
            if (personality.opponentEdgePressureMultiplier != 0 && arenaManager != null)
            {
                float oppDist = Vector2.Distance(opponents[0].transform.position,
                                                 arenaManager.arenaCenter.position);
                float edgePressure = Mathf.Clamp01(oppDist / arenaManager.CurrentRingRadius);
                AddReward(edgePressure * personality.opponentEdgePressureMultiplier);
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (myPlayer == null) return;

        float radius = arenaManager != null ? arenaManager.CurrentRingRadius : 1f;
        Transform center = arenaManager != null ? arenaManager.arenaCenter : null;

        if (SelfKinematics)
        {
            Vector2 normVel = myPlayer.Body.linearVelocity / myPlayer.stats.movementSpeed;
            sensor.AddObservation(normVel.x);
            sensor.AddObservation(normVel.y);
            sensor.AddObservation((transform.eulerAngles.z / 180f) - 1f);
        }

        if (SelfStamina) sensor.AddObservation(myPlayer.currentStamina / myPlayer.stats.maxStamina);
        if (SelfState)   sensor.AddObservation((float)myPlayer.currentState / 5f);

        // Charge progress — 0 when not in Charging state, otherwise currentChargeTime / pushChargeTime.
        // Lets the policy time push release optimally (full charge = full damage).
        if (SelfChargeProgress)
        {
            float prog = 0f;
            if (myPlayer.currentState == Player.PlayerState.Charging &&
                myPlayer.chargingStateScript != null && myPlayer.stats != null)
            {
                prog = Mathf.Clamp01(myPlayer.chargingStateScript.currentChargeTime /
                                     Mathf.Max(0.01f, myPlayer.stats.pushChargeTime));
            }
            sensor.AddObservation(prog);
        }

        // Ring-out margin — 1 at center, 0 at the ring edge. Pre-computed safety scalar.
        if (SelfRingOutMargin && center != null)
        {
            float distFromCenter = Vector2.Distance(transform.position, center.position);
            float margin = 1f - Mathf.Clamp01(distFromCenter / Mathf.Max(0.01f, radius));
            sensor.AddObservation(margin);
        }

        // CharacterStats obs — lets one network generalise across all stat variants.
        // Norm constants span Default/Heavyweight/Speedster into roughly [0,1].
        if (SelfStats && myPlayer.stats != null)
        {
            sensor.AddObservation(myPlayer.stats.weight          / 3f);
            sensor.AddObservation(myPlayer.stats.movementSpeed   / 12f);
            sensor.AddObservation(myPlayer.stats.pushForce       / 25f);
            sensor.AddObservation(myPlayer.stats.dodgeForce      / 30f);
            sensor.AddObservation(myPlayer.stats.maxStamina      / 150f);
        }

        // Self-personality one-hot — lets the shared network condition policy on which
        // reward stream it's currently optimizing for. Opponent personality is intentionally
        // NOT observed (see DEV_NOTES.md for rationale).
        if (SelfPersonalityId)
        {
            int myId = personality != null ? personality.id : -1;
            for (int i = 0; i < BotPersonality.Count; i++)
                sensor.AddObservation(i == myId ? 1f : 0f);
        }

        if (ArenaBounds && center != null)
        {
            Vector2 toCenter = (Vector2)(center.position - transform.position);
            sensor.AddObservation(toCenter.x / radius);
            sensor.AddObservation(toCenter.y / radius);
            sensor.AddObservation(radius / arenaManager.ringOutRadius); // normalized shrink progress
        }

        // Humanization scalar — lets the network condition policy on its current
        // difficulty tier (0=Expert/clean, 1=Easy/max delay+noise). Only emitted
        // when profile.humanization is true (Profile_C). Profile_A omits it to keep
        // the v2 obs space at 23 dims.
        if (ObserveHumanization)
            sensor.AddObservation(_humanization);

        for (int oi = 0; oi < opponents.Length; oi++)
        {
            var opp = opponents[oi];
            if (opp == null)
            {
                // Pad missing opponents with zeros so space size is fixed.
                int oppStateDims = OppStateOneHot ? 6 : (OppState ? 1 : 0);
                int pad = (OppPosition ? 2 : 0)
                        + (OppVelocity ? 2 : 0)
                        + oppStateDims
                        + (SelfFacingOpponentDot ? 1 : 0)
                        + (DistanceToOpponent ? 1 : 0)
                        + (OpponentFacingSelfDot ? 1 : 0)
                        + (OppStats ? 5 : 0);
                for (int i = 0; i < pad; i++) sensor.AddObservation(0f);
                continue;
            }

            // Resolve delayed snapshot if delay is active, otherwise use live values.
            Vector2 oppPos;
            Vector2 oppVel;
            float   oppState;
            if (_delayFrames > 0 && _oppRing != null)
            {
                // Read the snapshot from _delayFrames physics frames ago.
                int readIdx = ((_ringWriteIdx - 1 - _delayFrames) % RING_SIZE + RING_SIZE) % RING_SIZE;
                var snap = _oppRing[oi, readIdx];
                oppPos   = snap.position;
                oppVel   = snap.velocity;
                oppState = snap.state;
            }
            else
            {
                oppPos   = opp.transform.position;
                oppVel   = opp.Body.linearVelocity;
                oppState = (float)opp.currentState / 5f;
            }

            // Apply Gaussian noise to position and velocity when profile.applyNoise is on.
            bool applyNoise = observationProfile != null && observationProfile.applyNoise;
            if (applyNoise && _noiseSigma > 0f)
            {
                oppPos += new Vector2(GaussianNoise(_noiseSigma), GaussianNoise(_noiseSigma));
                // Velocity noise scaled to half sigma — velocity changes less abruptly than position
                oppVel += new Vector2(GaussianNoise(_noiseSigma * 0.5f), GaussianNoise(_noiseSigma * 0.5f));
            }

            // Direction & distance from self to (possibly noised/delayed) opponent position.
            Vector2 selfToOpp = oppPos - (Vector2)transform.position;
            float oppDistance = selfToOpp.magnitude;
            Vector2 dirToOpp  = oppDistance > 1e-4f ? selfToOpp / oppDistance : Vector2.zero;

            if (OppPosition)
            {
                sensor.AddObservation(selfToOpp.x / (radius * 2));
                sensor.AddObservation(selfToOpp.y / (radius * 2));
            }

            if (OppVelocity)
            {
                Vector2 relVel = oppVel / (opp.stats != null ? opp.stats.movementSpeed : 6f);
                sensor.AddObservation(relVel.x);
                sensor.AddObservation(relVel.y);
            }

            // Opponent state: prefer one-hot (cleaner categorical encoding) when enabled.
            if (OppStateOneHot)
            {
                // PlayerState enum: Moving=0, Charging=1, Blocking=2, Pushing=3, Dodging=4, Stunned=5
                // Note: oppState here is the normalized float for the DELAYED snapshot; we want
                // the categorical int. If delay is active and the live state differs from snapshot,
                // we use the snapshot's encoded value (consistent with what the network "sees").
                int stateIdx = _delayFrames > 0
                    ? Mathf.RoundToInt(oppState * 5f)            // snapshot float → int (0..5)
                    : (int)opp.currentState;
                stateIdx = Mathf.Clamp(stateIdx, 0, 5);
                for (int s = 0; s < 6; s++)
                    sensor.AddObservation(s == stateIdx ? 1f : 0f);
            }
            else if (OppState)
            {
                sensor.AddObservation(oppState);
            }

            // Facing alignment self→opp. Fixes random push/block aim (push only lands within
            // ~70° of transform.up; without this obs the agent had to derive the angle).
            if (SelfFacingOpponentDot)
                sensor.AddObservation(Vector2.Dot(transform.up, dirToOpp));

            // Pre-computed distance — saves the network from having to learn sqrt.
            if (DistanceToOpponent)
                sensor.AddObservation(Mathf.Clamp01(oppDistance / arenaManager.ringOutRadius));

            // Opponent facing toward self. Predicts incoming pushes/blocks. Uses LIVE opponent
            // facing (not delayed) since rotation isn't snapshotted in the ring buffer.
            if (OpponentFacingSelfDot)
                sensor.AddObservation(Vector2.Dot(opp.transform.up, -dirToOpp));

            if (OppStats && opp.stats != null)
            {
                sensor.AddObservation(opp.stats.weight        / 3f);
                sensor.AddObservation(opp.stats.movementSpeed / 12f);
                sensor.AddObservation(opp.stats.pushForce     / 25f);
                sensor.AddObservation(opp.stats.dodgeForce    / 30f);
                sensor.AddObservation(opp.stats.maxStamina    / 150f);
            }
            else if (OppStats)
            {
                for (int i = 0; i < 5; i++) sensor.AddObservation(0f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (actionSpaceMode == ActionSpaceMode.Discrete)
        {
            currentMovement.x = DiscreteToAxis(actions.DiscreteActions[0]);
            currentMovement.y = DiscreteToAxis(actions.DiscreteActions[1]);
            currentRotation = DiscreteToAxis(actions.DiscreteActions[2]);
            actionButton = actions.DiscreteActions[3];
        }
        else
        {
            currentMovement.x = actions.ContinuousActions[0];
            currentMovement.y = actions.ContinuousActions[1];
            currentRotation = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
            actionButton = actions.DiscreteActions[0];
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var keyboard = Keyboard.current;
        var mouse    = Mouse.current;

        float h = 0f, v = 0f;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) h -= 1f;
            if (keyboard.dKey.isPressed) h += 1f;
            if (keyboard.sKey.isPressed) v -= 1f;
            if (keyboard.wKey.isPressed) v += 1f;
        }

        bool fire1 = mouse != null && mouse.leftButton.isPressed;
        bool fire2 = mouse != null && mouse.rightButton.isPressed;
        bool jump  = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;

        int button = fire1 ? 1 : fire2 ? 2 : jump ? 3 : 0;

        if (actionSpaceMode == ActionSpaceMode.Discrete)
        {
            var d = actionsOut.DiscreteActions;
            d[0] = AxisToDiscrete(h);
            d[1] = AxisToDiscrete(v);
            d[2] = 0;
            d[3] = button;
        }
        else
        {
            var c = actionsOut.ContinuousActions;
            c[0] = h;
            c[1] = v;
            c[2] = 0f;
            actionsOut.DiscreteActions.Array[actionsOut.DiscreteActions.Offset] = button;
        }
    }

    private static float DiscreteToAxis(int v) => v == 1 ? -1f : v == 2 ? 1f : 0f;
    private static int AxisToDiscrete(float v) => v < -0.5f ? 1 : v > 0.5f ? 2 : 0;

    // Box-Muller transform — produces a Gaussian sample with mean 0 and given sigma.
    private static float GaussianNoise(float sigma)
    {
        float u1 = Mathf.Max(1e-6f, Random.value); // avoid log(0)
        float u2 = Random.value;
        float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return z * sigma;
    }

    private void OnValidate()
    {
        int oppCount = opponents != null ? opponents.Length : 0;
        int spaceSize = observationProfile != null
            ? observationProfile.ComputeSpaceSize(oppCount)
            : ComputeDefaultSpaceSize(oppCount);
        Debug.Log($"[{gameObject.name}] Observation Space Size: {spaceSize} | Opponents: {oppCount} | Mode: {actionSpaceMode}");
    }

    // Fallback when no ObservationProfile is assigned — assumes pre-v3 23-dim layout.
    // For accurate v3 sizing always assign a profile and use ComputeSpaceSize().
    private static int ComputeDefaultSpaceSize(int oppCount) => 13 + BotPersonality.Count + oppCount * 5;

    // ArenaManager calls these; EndEpisode is called separately by ArenaManager to keep ordering explicit.
    public void RegisterWin() { if (personality) AddReward(personality.winRound); }
    public void RegisterLoss() { if (personality) AddReward(personality.loseRound); }

    // Edge-hit bonus: scales with the victim's distance from arena center over current ring radius.
    // A hit at the edge ≈ full bonus (close to a ring-out moment); a center hit ≈ 0 bonus.
    // This replaces lastHitBy attribution — the hit's own reward already reflects how "ring-out-ish" it was.
    private float EdgeBonus(Vector3 victimPos)
    {
        if (arenaManager == null || arenaManager.arenaCenter == null) return 0f;
        float dist = Vector2.Distance(victimPos, arenaManager.arenaCenter.position);
        float prox = Mathf.Clamp01(dist / Mathf.Max(0.01f, arenaManager.CurrentRingRadius));
        return prox * personality.edgeHitBonus;
    }

    public void AddLandPushReward(Vector3 victimPos)
    {
        if (personality) AddReward(personality.landPushHit + EdgeBonus(victimPos));
    }
    public void AddTakeHitReward() { if (personality) AddReward(personality.takePushHit); }
    public void AddSuccessfulBlockReward() { if (personality) AddReward(personality.successfulBlock); }
    public void AddPushBlockedReward() { if (personality) AddReward(personality.pushBlocked); }
    public void AddDodgeEvasionReward() { if (personality) AddReward(personality.dodgeEvasionReward); }
    public void AddDodgeHitReward(Vector3 victimPos)
    {
        if (personality) AddReward(personality.dodgeHitReward + EdgeBonus(victimPos));
    }
    public void AddDodgeMissedPenalty() { if (personality) AddReward(personality.dodgeMissedPenalty); }
    public void AddWastedStaminaPenalty(float amount) { if (personality) AddReward(amount * personality.wastedStaminaMultiplier); }

    public Vector2 GetMovement() => Vector2.ClampMagnitude(currentMovement, 1f);
    public float GetRotationInput() => currentRotation;
    public bool GetPushInput() => actionButton == 1;
    public bool GetBlockInput() => actionButton == 2;
    public bool GetDodgeInput() => actionButton == 3;
    public bool GetSpecialInput() => false;
}
