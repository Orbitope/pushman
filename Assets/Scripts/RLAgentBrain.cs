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

    private Vector2 currentMovement;
    private float currentRotation;
    private int actionButton; // 0 none, 1 push, 2 block, 3 dodge

    // Cached from ObservationProfile; falls back to all-true if profile is null.
    private bool SelfKinematics => observationProfile == null || observationProfile.selfKinematics;
    private bool SelfStamina    => observationProfile == null || observationProfile.selfStamina;
    private bool SelfState      => observationProfile == null || observationProfile.selfState;
    private bool SelfStats      => observationProfile == null || observationProfile.selfStats;
    private bool ArenaBounds    => observationProfile == null || observationProfile.arenaBounds;
    private bool OppPosition    => observationProfile == null || observationProfile.opponentPosition;
    private bool OppVelocity    => observationProfile == null || observationProfile.opponentVelocity;
    private bool OppState       => observationProfile == null || observationProfile.opponentState;
    private bool OppStats       => observationProfile != null && observationProfile.opponentStats;
    private bool UseFOV         => observationProfile != null && observationProfile.useFOV;
    private float FOVAngle      => observationProfile != null ? observationProfile.fieldOfViewAngle : 120f;

    public override void Initialize()
    {
        myPlayer = GetComponent<Player>();
    }

    private void FixedUpdate()
    {
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

        if (ArenaBounds && center != null)
        {
            Vector2 toCenter = (Vector2)(center.position - transform.position);
            sensor.AddObservation(toCenter.x / radius);
            sensor.AddObservation(toCenter.y / radius);
            sensor.AddObservation(radius / arenaManager.ringOutRadius); // normalized shrink progress
        }

        foreach (var opp in opponents)
        {
            if (opp == null)
            {
                // Pad missing opponents with zeros so space size is fixed.
                int pad = (OppPosition ? 2 : 0) + (OppVelocity ? 2 : 0) + (OppState ? 1 : 0) + (OppStats ? 5 : 0);
                for (int i = 0; i < pad; i++) sensor.AddObservation(0f);
                continue;
            }

            bool visible = true;
            if (UseFOV)
            {
                Vector2 toOpp = (opp.transform.position - transform.position).normalized;
                float angle = Vector2.Angle(transform.up, toOpp);
                visible = angle <= FOVAngle * 0.5f;
            }

            if (OppPosition)
            {
                if (visible)
                {
                    Vector2 rel = (Vector2)(opp.transform.position - transform.position);
                    sensor.AddObservation(rel.x / (radius * 2));
                    sensor.AddObservation(rel.y / (radius * 2));
                }
                else { sensor.AddObservation(0f); sensor.AddObservation(0f); }
            }

            if (OppVelocity)
            {
                if (visible)
                {
                    Vector2 relVel = opp.Body.linearVelocity / opp.stats.movementSpeed;
                    sensor.AddObservation(relVel.x);
                    sensor.AddObservation(relVel.y);
                }
                else { sensor.AddObservation(0f); sensor.AddObservation(0f); }
            }

            if (OppState) sensor.AddObservation(visible ? (float)opp.currentState / 5f : 0f);

            if (OppStats)
            {
                if (visible && opp.stats != null)
                {
                    sensor.AddObservation(opp.stats.weight        / 3f);
                    sensor.AddObservation(opp.stats.movementSpeed / 12f);
                    sensor.AddObservation(opp.stats.pushForce     / 25f);
                    sensor.AddObservation(opp.stats.dodgeForce    / 30f);
                    sensor.AddObservation(opp.stats.maxStamina    / 150f);
                }
                else { for (int i = 0; i < 5; i++) sensor.AddObservation(0f); }
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

    private void OnValidate()
    {
        int oppCount = opponents != null ? opponents.Length : 0;
        int spaceSize = observationProfile != null
            ? observationProfile.ComputeSpaceSize(oppCount)
            : ComputeDefaultSpaceSize(oppCount);
        Debug.Log($"[{gameObject.name}] Observation Space Size: {spaceSize} | Opponents: {oppCount} | Mode: {actionSpaceMode}");
    }

    // Profile A defaults: selfKin(3)+selfStam(1)+selfState(1)+selfStats(5)+arena(3) + per-opp(5)
    private static int ComputeDefaultSpaceSize(int oppCount) => 13 + oppCount * 5;

    // ArenaManager calls these; EndEpisode is called separately by ArenaManager to keep ordering explicit.
    public void RegisterWin() { if (personality) AddReward(personality.winRound); }
    public void RegisterLoss() { if (personality) AddReward(personality.loseRound); }

    public void AddLandPushReward() { if (personality) AddReward(personality.landPushHit); }
    public void AddTakeHitReward() { if (personality) AddReward(personality.takePushHit); }
    public void AddSuccessfulBlockReward() { if (personality) AddReward(personality.successfulBlock); }
    public void AddPushBlockedReward() { if (personality) AddReward(personality.pushBlocked); }
    public void AddDodgeEvasionReward() { if (personality) AddReward(personality.dodgeEvasionReward); }
    public void AddDodgeHitReward() { if (personality) AddReward(personality.dodgeHitReward); }
    public void AddWastedStaminaPenalty(float amount) { if (personality) AddReward(amount * personality.wastedStaminaMultiplier); }

    public Vector2 GetMovement() => Vector2.ClampMagnitude(currentMovement, 1f);
    public float GetRotationInput() => currentRotation;
    public bool GetPushInput() => actionButton == 1;
    public bool GetBlockInput() => actionButton == 2;
    public bool GetDodgeInput() => actionButton == 3;
    public bool GetSpecialInput() => false;
}
