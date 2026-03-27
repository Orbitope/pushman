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
    public float centerControlMultiplier = 0.001f;
    public float facingOpponentMultiplier = 0.001f;
    public float wastedStaminaMultiplier = -0.05f;
}