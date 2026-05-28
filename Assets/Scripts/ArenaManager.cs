using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ArenaManager : MonoBehaviour
{
    [Header("Arena Setup")]
    public Transform arenaCenter;
    public float ringOutRadius = 10f;
    public float minRingRadius = 2f;

    [Header("Shrinking Stage")]
    [Tooltip("Seconds of inactivity before the ring starts closing.")]
    public float timeUntilShrink = 30f;
    [Tooltip("Units per second the ring shrinks.")]
    public float shrinkRate = 0.5f;

    [Header("Players")]
    public List<Player> allPlayers;

    [Header("Character Variety")]
    [Tooltip("If non-empty, each player is assigned a random CharacterStats from this pool every round. " +
             "Trains one network that generalises across stat variants. Leave empty for fixed stats.")]
    public List<CharacterStats> statsPool;

    [Header("Spawn Points")]
    public List<Transform> spawnPoints;

    [Header("Round Feedback")]
    [Tooltip("Seconds to freeze on a ring-out before resetting.")]
    public float ringOutPauseDuration = 0.6f;

    private List<Player> activePlayers = new List<Player>();
    private float currentRingRadius;
    private float shrinkTimer;
    private bool matchRunning;
    private float matchStartTime;

    // Boundary ring visual — updated each frame to match currentRingRadius.
    private SpriteRenderer boundaryRenderer;
    private float          boundaryNativeRadius; // world radius when scale = (1,1,1)

    // Score tracking — indexed by position in allPlayers list.
    private int[] scores;

    public float CurrentRingRadius => currentRingRadius;

    // Read by StaminaHUD or any score display.
    public int GetScore(int playerIndex) =>
        scores != null && playerIndex < scores.Length ? scores[playerIndex] : 0;

    void Start()
    {
        if (allPlayers == null || allPlayers.Count == 0)
        {
            Debug.LogError("[ArenaManager] No players assigned.");
            return;
        }

        scores = new int[allPlayers.Count];

        // Auto-bootstrap the match-outcome logger (one shared instance for all arenas).
        if (MatchLogger.Instance == null)
            new GameObject("MatchLogger").AddComponent<MatchLogger>();

        // Cache the ArenaBoundary child so we can resize it as the ring shrinks.
        var boundaryGO = transform.Find("ArenaBoundary");
        if (boundaryGO != null)
        {
            boundaryRenderer = boundaryGO.GetComponent<SpriteRenderer>();
            // Remember the initial scale set by PushmanSetup (which scales ring to diameter = ringOutRadius * 2).
            // Sprite is 256px at 100 PPU = 2.56 units, so scale = (ringOutRadius * 2) / 2.56.
            // Store this as the native radius for scaling calculations.
            boundaryNativeRadius = ringOutRadius;
            // Cache the initial scale so we can scale relative to it.
            float initialScale = boundaryGO.transform.localScale.x;
            boundaryNativeRadius = ringOutRadius / initialScale;
        }

        StartMatch();
    }

    private void StartMatch()
    {
        activePlayers = new List<Player>(allPlayers);
        currentRingRadius = ringOutRadius;
        shrinkTimer = 0f;
        matchRunning = true;
        matchStartTime = Time.time;
        ResetAllPlayers();
    }

    void FixedUpdate()
    {
        if (!matchRunning) return;

        // Shrink ring over time.
        shrinkTimer += Time.fixedDeltaTime;
        if (shrinkTimer > timeUntilShrink)
            currentRingRadius = Mathf.Max(minRingRadius, currentRingRadius - shrinkRate * Time.fixedDeltaTime);

        // Keep boundary sprite in sync with currentRingRadius.
        UpdateBoundaryScale();

        CheckRingOuts();
    }

    // Scale the ArenaBoundary SpriteRenderer to always match currentRingRadius.
    private void UpdateBoundaryScale()
    {
        if (boundaryRenderer == null || boundaryNativeRadius <= 0f) return;
        float s = currentRingRadius / boundaryNativeRadius;
        boundaryRenderer.transform.localScale = new Vector3(s, s, 1f);
    }

    private void CheckRingOuts()
    {
        List<Player> ringOuts = null;
        foreach (var p in activePlayers)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            if (Vector2.Distance(p.transform.position, arenaCenter.position) >= currentRingRadius)
            {
                ringOuts ??= new List<Player>();
                ringOuts.Add(p);
            }
        }

        if (ringOuts == null) return;

        // Award point to surviving player(s) before anything else.
        foreach (var p in allPlayers)
        {
            if (!ringOuts.Contains(p) && activePlayers.Contains(p))
            {
                int idx = allPlayers.IndexOf(p);
                if (idx >= 0) scores[idx]++;
            }
        }

        foreach (var p in ringOuts)
        {
            (p.Brain as RLAgentBrain)?.RegisterLoss();
            activePlayers.Remove(p);
            p.gameObject.SetActive(false);
        }

        if (activePlayers.Count <= 1)
        {
            if (activePlayers.Count == 1)
            {
                Player winner = activePlayers[0];
                (winner.Brain as RLAgentBrain)?.RegisterWin();

                // Log the decided match for permutation analysis — one row per loser.
                if (MatchLogger.Instance != null)
                {
                    float duration = Time.time - matchStartTime;
                    foreach (var loser in ringOuts)
                        MatchLogger.Instance.LogMatch(winner, loser, duration);
                }
            }

            StartCoroutine(RingOutSequence());
        }
    }

    // Brief pause + boundary flash before resetting — gives players a moment to register what happened.
    private IEnumerator RingOutSequence()
    {
        matchRunning = false;

        // Flash boundary ring white.
        if (boundaryRenderer != null)
        {
            Color originalColor = boundaryRenderer.color;
            boundaryRenderer.color = Color.white;
            yield return new WaitForSecondsRealtime(ringOutPauseDuration * 0.5f);
            boundaryRenderer.color = originalColor;
            yield return new WaitForSecondsRealtime(ringOutPauseDuration * 0.5f);
        }
        else
        {
            yield return new WaitForSecondsRealtime(ringOutPauseDuration);
        }

        EndAllEpisodes();
        StartMatch();
    }

    // Applies all rewards first, then EndEpisode on every agent — ArenaManager owns episode flow.
    private void EndAllEpisodes()
    {
        foreach (var p in allPlayers)
        {
            if (p != null && p.Brain is RLAgentBrain rl)
                rl.EndEpisode();
        }
    }

    private void ResetAllPlayers()
    {
        List<Transform> available = new List<Transform>(spawnPoints);

        foreach (var p in allPlayers)
        {
            if (p == null) continue;
            p.gameObject.SetActive(true);

            if (available.Count == 0) available = new List<Transform>(spawnPoints);
            int idx = Random.Range(0, available.Count);
            Transform spawn = available[idx];
            available.RemoveAt(idx);

            Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
            p.transform.position = spawn.position;
            p.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }

            // Randomise stats each round if a pool is provided — teaches the agent to
            // adapt its playstyle based on its self-observed CharacterStats.
            if (statsPool != null && statsPool.Count > 0)
            {
                var chosen = statsPool[Random.Range(0, statsPool.Count)];
                p.stats = chosen;
                Rigidbody2D prb = p.GetComponent<Rigidbody2D>();
                if (prb != null) prb.mass = chosen.weight;
            }

            p.currentStamina = p.stats != null ? p.stats.maxStamina : 0f;
            p.SetState(Player.PlayerState.Moving);
        }
    }

    private void OnDrawGizmos()
    {
        if (arenaCenter == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(arenaCenter.position, Application.isPlaying ? currentRingRadius : ringOutRadius);
    }
}
