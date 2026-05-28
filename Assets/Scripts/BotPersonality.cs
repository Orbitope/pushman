using UnityEngine;

[CreateAssetMenu(fileName = "NewBotPersonality", menuName = "Pushman/Bot Personality")]
public class BotPersonality : ScriptableObject
{
    /// <summary>
    /// Total number of distinct personalities — used to size the one-hot self-personality
    /// observation. Keep in sync with the asset count under Personalities/ (currently 5:
    /// Aggressive, Defensive, Evasive, Balanced, Counter).
    /// </summary>
    public const int Count = 5;

    [Header("Identity")]
    [Tooltip("Stable integer 0..Count-1 used to one-hot encode self-personality in the " +
             "observation. Must be unique across personality assets. Aggressive=0, " +
             "Defensive=1, Evasive=2, Balanced=3, Counter=4.")]
    [Range(0, Count - 1)]
    public int id = 0;

    [Header("Match Outcomes (Sparse)")]
    [Tooltip("Large terminal reward — must dominate the sum of dense rewards over an episode, " +
             "and survive gamma discounting back through the trajectory. Don't drop below ~8.")]
    public float winRound = 10.0f;
    public float loseRound = -10.0f;

    [Header("Combat Events (Discrete)")]
    [Tooltip("Base reward for a successful push hit. Keep small — the edgeHitBonus does the heavy lifting.")]
    public float landPushHit = 0.05f;
    public float takePushHit = -0.05f;
    public float successfulBlock = 0.15f;
    public float pushBlocked = -0.05f;

    [Tooltip("Added to a hit's reward, scaled by the opponent's distance from arena center / current ring radius. " +
             "An opponent hit at the edge gets the full bonus (≈ ring-out moment); a center hit gets ~0. " +
             "This rewards 'hits that almost ringed them out' without needing brittle lastHitBy attribution.")]
    public float edgeHitBonus = 0.5f;

    [Tooltip("Reward when agent dodges and opponent was attacking (in Pushing state). " +
             "Prevents reward from dodge spam vs standing opponent. Suggested: 0.05-0.15")]
    public float dodgeEvasionReward = 0.05f;

    [Tooltip("Base reward when agent's dodge hits a non-blocking opponent. " +
             "Keep tiny — dodge should be primarily evasive. Edge-hit bonus still applies on top.")]
    public float dodgeHitReward = 0.01f;

    [Tooltip("Penalty when dodge expires without hitting anyone. " +
             "Discourages spamming dodge as a no-risk move. Suggested: -0.05")]
    public float dodgeMissedPenalty = -0.05f;

    [Header("Positioning & Tactics (Continuous / Per Step)")]
    [Tooltip("Reward for facing the opponent. Mild nudge toward engagement. Suggested: 0.0001")]
    public float facingOpponentMultiplier = 0.0001f;

    [Tooltip("Penalty for pushing when stamina is wasted (whiff). Scales with stamina spent.")]
    public float wastedStaminaMultiplier = -0.02f;

    [Tooltip("Reward for pushing the opponent closer to the ring edge (normalized 0-1). " +
             "Encourages actually trying to ring-out, not just dealing hits.")]
    public float opponentEdgePressureMultiplier = 0.0004f;

    [Header("Time Pressure")]
    [Tooltip("Applied every FixedUpdate step. Negative value penalises long matches. " +
             "With MaxStep=5000 and winRound=1.5, a value of -0.0001 means a full-length " +
             "episode costs -0.5 — balanced against win reward to encourage aggression without panic.")]
    public float timePenaltyPerStep = -0.0001f;
}