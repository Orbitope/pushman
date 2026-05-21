using UnityEngine;

[CreateAssetMenu(fileName = "NewBotPersonality", menuName = "Pushman/Bot Personality")]
public class BotPersonality : ScriptableObject
{
    [Header("Match Outcomes (Sparse)")]
    public float winRound = 1.0f;
    public float loseRound = -1.0f;

    [Header("Combat Events (Discrete)")]
    public float landPushHit = 0.1f;
    public float takePushHit = -0.1f;
    public float successfulBlock = 0.05f;
    public float pushBlocked = -0.05f;

    [Header("Positioning & Tactics (Continuous / Per Step)")]
    [Tooltip("Reward for facing the opponent. Mild nudge toward engagement. Suggested: 0.0001")]
    public float facingOpponentMultiplier = 0.0001f;

    [Tooltip("Penalty for pushing when stamina is wasted (whiff). Scales with stamina spent.")]
    public float wastedStaminaMultiplier = -0.02f;

    [Tooltip("Reward for pushing the opponent closer to the ring edge (normalized 0-1). " +
             "Encourages actually trying to ring-out, not just dealing hits.")]
    public float opponentEdgePressureMultiplier = 0.0002f;

    [Header("Time Pressure")]
    [Tooltip("Applied every FixedUpdate step. Negative value penalises long matches. " +
             "With MaxStep=5000 and winRound=1, a value of -0.0002 means a full-length " +
             "episode costs -1.0 — equal to the win reward, strongly encouraging aggression. " +
             "Set to 0 to disable.")]
    public float timePenaltyPerStep = -0.0002f;
}