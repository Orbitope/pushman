using UnityEngine;

// Slowly walks toward player, randomly pushes or blocks
public class ChaseBotBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Behavior")]
    public float walkSpeed = 0.5f;            // fraction of full speed
    public float pushRange = 1.5f;
    public float chaseRange = 25f;
    public float aimDotThreshold = 0.8f;
    public float rotationDeadzone = 5f;

    [Header("Randomness")]
    public float pushChance = 0.3f;           // 30% chance per frame to push
    public float blockChance = 0.2f;          // 20% chance to block

    private Vector2 cachedMove;
    private float cachedRotation;
    private float blockUntil;

    void Update()
    {
        cachedMove = Vector2.zero;
        cachedRotation = 0f;

        Player self = GetComponent<Player>();
        if (self == null) return;

        // Find nearest visible player
        Player target = null;
        float bestDist = float.MaxValue;
        foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p.gameObject == gameObject || !p.isActiveAndEnabled) continue;
            float d = Vector2.Distance(p.transform.position, transform.position);
            if (d < bestDist) { bestDist = d; target = p; }
        }

        if (target == null) return;

        Vector2 toTarget = ((Vector2)(target.transform.position - transform.position)).normalized;
        float dist = toTarget.magnitude;

        // Face target
        float signedAngle = Vector2.SignedAngle(transform.up, toTarget);
        if (Mathf.Abs(signedAngle) > rotationDeadzone)
            cachedRotation = signedAngle > 0f ? -1f : 1f;

        // Walk toward target at reduced speed
        if (dist > pushRange && dist < chaseRange)
        {
            cachedMove = toTarget * walkSpeed;
        }

        // Push or block at random when in range and aimed
        bool aimed = Vector2.Dot(transform.up, toTarget) > aimDotThreshold;
        if (aimed && dist < pushRange)
        {
            float rand = Random.value;
            if (rand < pushChance)
            {
                cachedMove.y = 1f;  // push
            }
            else if (rand < pushChance + blockChance)
            {
                blockUntil = Time.time + Random.Range(0.3f, 0.8f);
            }
        }
    }

    public Vector2 GetMovement() => cachedMove;
    public float GetRotationInput() => cachedRotation;
    public bool GetPushInput() => cachedMove.y > 0.5f;
    public bool GetBlockInput() => Time.time < blockUntil;
    public bool GetDodgeInput() => false;
    public bool GetSpecialInput() => false;
}
