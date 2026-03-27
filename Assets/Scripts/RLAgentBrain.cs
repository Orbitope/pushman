using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class RLAgentBrain : Agent, IPlayerBrain
{
    [Header("Agent References")]
    private Player myPlayer;
    public Transform arenaCenter; 
    public float arenaRadius = 10f; 

    [Header("Opponents")]
    public Player[] opponents; 

    [Header("Reward Configuration")]
    public BotPersonality personality;

    [Header("Observation Configuration")]
    public bool obsSelfKinematics = true; 
    public bool obsSelfStamina = true; 
    public bool obsArenaBounds = true; 
    
    [Header("Opponent Observations")]
    public bool obsOpponentPosition = true; 
    public bool obsOpponentVelocity = true; 
    public bool obsOpponentState = true; 

    private Vector2 currentMovement;
    private bool isPushing;
    private bool isBlocking;
    private bool isDodging;

    public override void Initialize()
    {
        myPlayer = GetComponent<Player>();
    }

    private void FixedUpdate()
    {
        if (personality == null) return;

        if (personality.centerControlMultiplier != 0 && arenaCenter != null)
        {
            float distanceToCenter = Vector2.Distance(transform.position, arenaCenter.position);
            float centerScore = 1f - Mathf.Clamp01(distanceToCenter / arenaRadius);
            AddReward(centerScore * personality.centerControlMultiplier);
        }

        if (personality.facingOpponentMultiplier != 0 && opponents.Length > 0 && opponents[0] != null)
        {
            Vector2 directionToOpponent = (opponents[0].transform.position - transform.position).normalized;
            float facingScore = Vector2.Dot(transform.up, directionToOpponent);
            if (facingScore > 0.5f) 
            {
                AddReward(facingScore * personality.facingOpponentMultiplier);
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (obsSelfKinematics)
        {
            Vector2 normVelocity = myPlayer.GetComponent<Rigidbody2D>().linearVelocity / myPlayer.movementSpeed;
            sensor.AddObservation(normVelocity.x);
            sensor.AddObservation(normVelocity.y);
            float normRotation = (transform.eulerAngles.z / 180f) - 1f;
            sensor.AddObservation(normRotation);
        }

        if (obsSelfStamina) sensor.AddObservation(myPlayer.currentStamina / myPlayer.maxStamina);

        if (obsArenaBounds && arenaCenter != null)
        {
            Vector2 toCenter = (Vector2)(arenaCenter.position - transform.position);
            sensor.AddObservation(toCenter.x / arenaRadius);
            sensor.AddObservation(toCenter.y / arenaRadius);
        }

        foreach (var opp in opponents)
        {
            if (opp == null) continue;

            if (obsOpponentPosition)
            {
                Vector2 relativePos = (Vector2)(opp.transform.position - transform.position);
                sensor.AddObservation(relativePos.x / (arenaRadius * 2));
                sensor.AddObservation(relativePos.y / (arenaRadius * 2));
            }

            if (obsOpponentVelocity)
            {
                Vector2 relativeVel = opp.GetComponent<Rigidbody2D>().linearVelocity / opp.movementSpeed;
                sensor.AddObservation(relativeVel.x);
                sensor.AddObservation(relativeVel.y);
            }

            if (obsOpponentState) sensor.AddObservation((float)opp.currentState / 5f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        currentMovement.x = actions.ContinuousActions[0];
        currentMovement.y = actions.ContinuousActions[1];
        isPushing = actions.DiscreteActions[0] == 1;
        isBlocking = actions.DiscreteActions[1] == 1;
        isDodging = actions.DiscreteActions[2] == 1;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxisRaw("Horizontal");
        continuousActionsOut[1] = Input.GetAxisRaw("Vertical");

        var discreteActionsOut = actionsOut.DiscreteActions;
        discreteActionsOut[0] = Input.GetButton("Fire1") ? 1 : 0;
        discreteActionsOut[1] = Input.GetButton("Fire2") ? 1 : 0;
        discreteActionsOut[2] = Input.GetButtonDown("Jump") ? 1 : 0;
    }

    private void OnValidate()
    {
        int totalObs = 0;
        if (obsSelfKinematics) totalObs += 3;
        if (obsSelfStamina) totalObs += 1;
        if (obsArenaBounds) totalObs += 2;
        int oppObs = 0;
        if (obsOpponentPosition) oppObs += 2;
        if (obsOpponentVelocity) oppObs += 2;
        if (obsOpponentState) oppObs += 1;
        int oppCount = opponents != null ? opponents.Length : 0;
        totalObs += (oppObs * oppCount);
        Debug.Log($"[{gameObject.name}] Required ML-Agents Space Size: {totalObs}");
    }

    public void AddLandPushReward() { if (personality) AddReward(personality.landPushHit); }
    public void AddTakeHitReward() { if (personality) AddReward(personality.takePushHit); }
    public void AddSuccessfulBlockReward() { if (personality) AddReward(personality.successfulBlock); }
    public void AddPushBlockedReward() { if (personality) AddReward(personality.pushBlocked); }
    public void AddWastedStaminaPenalty(float amount) { if (personality) AddReward(amount * personality.wastedStaminaMultiplier); }
    
    public void RegisterWin() { if (personality) AddReward(personality.winRound); EndEpisode(); }
    public void RegisterLoss() { if (personality) AddReward(personality.loseRound); EndEpisode(); }

    public Vector2 GetMovement() => currentMovement.normalized;
    public bool GetPushInput() => isPushing;
    public bool GetBlockInput() => isBlocking;
    public bool GetDodgeInput() => isDodging;
    public bool GetSpecialInput() => false; 
}