using UnityEngine;

// Stands at spawn, faces the player, pushes every N seconds
public class StandingBotBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Behavior")]
    public float pushInterval = 2f;
    public float aimDotThreshold = 0.85f;
    public float rotationDeadzone = 5f;

    private float nextPushTime;
    private Vector2 cachedMove;
    private float cachedRotation;

    void Start() => nextPushTime = Time.time + pushInterval;

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

        // Face target
        Vector2 toTarget = ((Vector2)(target.transform.position - transform.position)).normalized;
        float signedAngle = Vector2.SignedAngle(transform.up, toTarget);
        if (Mathf.Abs(signedAngle) > rotationDeadzone)
            cachedRotation = signedAngle > 0f ? -1f : 1f;

        // Push on interval if aimed
        bool aimed = Vector2.Dot(transform.up, toTarget) > aimDotThreshold;
        if (aimed && Time.time >= nextPushTime)
        {
            cachedMove.y = 1f;  // brief push input
            nextPushTime = Time.time + pushInterval;
        }
    }

    public Vector2 GetMovement() => cachedMove;
    public float GetRotationInput() => cachedRotation;
    public bool GetPushInput() => cachedMove.y > 0.5f;
    public bool GetBlockInput() => false;
    public bool GetDodgeInput() => false;
    public bool GetSpecialInput() => false;
}
