using UnityEngine;
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

    [Header("Spawn Points")]
    public List<Transform> spawnPoints;

    private List<Player> activePlayers = new List<Player>();
    private float currentRingRadius;
    private float shrinkTimer;
    private bool matchRunning;

    public float CurrentRingRadius => currentRingRadius;

    void Start()
    {
        if (allPlayers == null || allPlayers.Count == 0)
        {
            Debug.LogError("[ArenaManager] No players assigned.");
            return;
        }
        StartMatch();
    }

    private void StartMatch()
    {
        activePlayers = new List<Player>(allPlayers);
        currentRingRadius = ringOutRadius;
        shrinkTimer = 0f;
        matchRunning = true;
        ResetAllPlayers();
    }

    void FixedUpdate()
    {
        if (!matchRunning) return;

        shrinkTimer += Time.fixedDeltaTime;
        if (shrinkTimer > timeUntilShrink)
            currentRingRadius = Mathf.Max(minRingRadius, currentRingRadius - shrinkRate * Time.fixedDeltaTime);

        CheckRingOuts();
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

        foreach (var p in ringOuts)
        {
            (p.Brain as RLAgentBrain)?.RegisterLoss();
            activePlayers.Remove(p);
            p.gameObject.SetActive(false);
        }

        if (activePlayers.Count <= 1)
        {
            if (activePlayers.Count == 1)
                (activePlayers[0].Brain as RLAgentBrain)?.RegisterWin();

            EndAllEpisodes();
            StartMatch();
        }
    }

    // Applies all rewards first, then EndEpisode on every agent — ArenaManager owns episode flow.
    private void EndAllEpisodes()
    {
        matchRunning = false;
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

            // Pick a unique random spawn; recycle if there are more players than spawn points.
            if (available.Count == 0) available = new List<Transform>(spawnPoints);
            int idx = Random.Range(0, available.Count);
            Transform spawn = available[idx];
            available.RemoveAt(idx);

            Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
            p.transform.position = spawn.position;
            p.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }

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
