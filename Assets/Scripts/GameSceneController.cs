using UnityEngine;
using Unity.MLAgents.Policies;

/// <summary>
/// Reads GameConfig.Current and applies menu selections to the Game scene players
/// before any other script runs.
///
/// Execution order -100 guarantees Awake() fires before Player.Awake() (order 0),
/// so stats are in place when Player.Awake() reads them to set rb.mass etc.
///
/// Attach to a dedicated GameObject in the Game scene (created by Pushman/12b).
/// Wire player1 and player2 in the Inspector, or leave null to auto-find.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameSceneController : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("The human-controlled player (Player1). Auto-found if null.")]
    public Player player1;

    [Tooltip("The bot player with RLAgentBrain (Player2). Auto-found if null.")]
    public Player player2;

    // -----------------------------------------------------------------------

    void Awake()
    {
        // Auto-discover players from the Arena hierarchy if not wired in Inspector
        if (player1 == null || player2 == null)
        {
            var allPlayers = FindObjectsByType<Player>(FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p.GetComponent<HumanBrain>() != null)  player1 = p;
                else if (p.GetComponent<RLAgentBrain>() != null) player2 = p;
            }
        }

        var cfg = GameConfig.Current;
        if (cfg == null)
        {
            Debug.Log("[GameSceneController] No GameConfig found — using scene defaults.");
            return;
        }

        // Apply character stats (before Player.Awake reads them)
        if (player1 != null && cfg.playerCharacter != null)
        {
            player1.stats = cfg.playerCharacter;
            Debug.Log($"[GameSceneController] P1 stats → {cfg.playerCharacter.name}");
        }

        if (player2 != null && cfg.opponentCharacter != null)
        {
            player2.stats = cfg.opponentCharacter;
            Debug.Log($"[GameSceneController] P2 stats → {cfg.opponentCharacter.name}");
        }

        // Apply bot brain settings (personality + difficulty tier)
        // These are read in RLAgentBrain.OnEpisodeBegin() which fires in Start()
        if (player2 != null)
        {
            var rl = player2.GetComponent<RLAgentBrain>();
            if (rl != null)
            {
                if (cfg.opponentPersonality != null)
                {
                    rl.personality = cfg.opponentPersonality;
                    Debug.Log($"[GameSceneController] Bot personality → {cfg.opponentPersonality.name}");
                }
                // runtimeHumanization: 0 = Expert, 1 = Easy
                rl.runtimeHumanization = cfg.opponentDifficulty;
                Debug.Log($"[GameSceneController] Bot difficulty → {cfg.opponentDifficulty:F2} " +
                          $"(0=Expert, 1=Easy)");
            }
        }

        // Safety net: if BehaviorParameters has no model, switch to HeuristicOnly so the
        // agent doesn't throw at OnEnable. The bot will be passive but the scene won't crash.
        if (player2 != null)
        {
            var bp = player2.GetComponent<BehaviorParameters>();
            if (bp != null && bp.BehaviorType == BehaviorType.InferenceOnly && bp.Model == null)
            {
                Debug.LogError("[GameSceneController] Player2 has BehaviorType=InferenceOnly but " +
                               "no Model assigned. Falling back to HeuristicOnly to avoid crash. " +
                               "Re-run Pushman/12b to wire the ONNX model.");
                bp.BehaviorType = BehaviorType.HeuristicOnly;
            }
        }
    }
}
