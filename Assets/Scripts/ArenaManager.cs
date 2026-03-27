using UnityEngine;

public class ArenaManager : MonoBehaviour
{
    [Header("Arena Setup")]
    public Transform arenaCenter;
    public float ringOutRadius = 10f; 

    [Header("Players")]
    public Player player1;
    public Player player2;

    [Header("Spawn Points")]
    public Transform p1Spawn;
    public Transform p2Spawn;

    void FixedUpdate()
    {
        CheckRingOuts();
    }

    private void CheckRingOuts()
    {
        float p1Distance = Vector2.Distance(player1.transform.position, arenaCenter.position);
        float p2Distance = Vector2.Distance(player2.transform.position, arenaCenter.position);

        bool p1Out = p1Distance >= ringOutRadius;
        bool p2Out = p2Distance >= ringOutRadius;

        if (p1Out && !p2Out) EndMatch(winner: player2, loser: player1);
        else if (p2Out && !p1Out) EndMatch(winner: player1, loser: player2);
        else if (p1Out && p2Out) EndMatch(winner: null, loser: null);
    }

    private void EndMatch(Player winner, Player loser)
    {
        if (winner != null && winner.Brain is RLAgentBrain winnerBrain) winnerBrain.RegisterWin();
        if (loser != null && loser.Brain is RLAgentBrain loserBrain) loserBrain.RegisterLoss();

        if (winner == null && loser == null)
        {
            if (player1.Brain is RLAgentBrain p1Brain) p1Brain.EndEpisode();
            if (player2.Brain is RLAgentBrain p2Brain) p2Brain.EndEpisode();
        }

        ResetPlayer(player1, p1Spawn);
        ResetPlayer(player2, p2Spawn);
    }

    private void ResetPlayer(Player p, Transform spawnPoint)
    {
        p.transform.position = spawnPoint.position;
        p.transform.rotation = spawnPoint.rotation;

        Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero; // <-- Updated line
            rb.angularVelocity = 0f;
        }

        p.currentStamina = p.maxStamina;
        p.SetState(Player.PlayerState.Moving);
    }

    private void OnDrawGizmos()
    {
        if (arenaCenter != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(arenaCenter.position, ringOutRadius);
        }
    }
}