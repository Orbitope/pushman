using UnityEngine;

// Every 5 seconds, dodge toward the player
public class DodgingBotBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Behavior")]
    public float dodgeInterval = 5f;
    public float aimDotThreshold = 0.8f;
    public float rotationDeadzone = 5f;

    private float nextDodgeTime;
    private Vector2 cachedMove;
    private float cachedRotation;

    void Start() => nextDodgeTime = Time.time + dodgeInterval;

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

        // Face target
        float signedAngle = Vector2.SignedAngle(transform.up, toTarget);
        if (Mathf.Abs(signedAngle) > rotationDeadzone)
            cachedRotation = signedAngle > 0f ? -1f : 1f;

        // Dodge on interval toward target
        if (Time.time >= nextDodgeTime)
        {
            cachedMove = toTarget;  // move in direction of target to dodge
            nextDodgeTime = Time.time + dodgeInterval;
        }
    }

    public Vector2 GetMovement() => cachedMove;
    public float GetRotationInput() => cachedRotation;
    public bool GetPushInput() => false;
    public bool GetBlockInput() => false;
    public bool GetDodgeInput() => cachedMove.sqrMagnitude > 0.5f;
    public bool GetSpecialInput() => false;
}
