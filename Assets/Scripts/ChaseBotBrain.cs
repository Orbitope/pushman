using UnityEngine;

/// <summary>
/// Slowly walks toward the nearest player, faces them, and randomly pushes or blocks.
/// </summary>
public class ChaseBotBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Movement")]
    public float walkSpeed = 0.6f;       // fraction of full movementSpeed
    public float pushRange = 1.5f;       // world units — start pushing inside this
    public float chaseRange = 30f;       // only chase within this range

    [Header("Rotation")]
    public float rotationDeadzone = 5f;

    [Header("Timing")]
    public float minActionInterval = 0.8f;   // seconds between push/block decisions
    public float maxActionInterval = 1.8f;

    [Header("Randomness")]
    [Range(0f, 1f)] public float pushChance  = 0.55f;
    [Range(0f, 1f)] public float blockChance = 0.25f;

    // Cached state — written by Update, read by IPlayerBrain methods.
    private Vector2  cachedMove;
    private float    cachedRotation;
    private bool     wantPush;
    private float    blockUntil;
    private float    nextActionTime;

    private Player cachedSelf;
    private Player cachedTarget;   // refreshed only when null or inactive

    void Awake()
    {
        cachedSelf = GetComponent<Player>();
    }

    void Start()
    {
        nextActionTime = Time.time + Random.Range(minActionInterval, maxActionInterval);
    }

    // Returns the cached target; re-searches only when the cached one is gone.
    private Player FindTarget()
    {
        if (cachedTarget != null && cachedTarget.isActiveAndEnabled) return cachedTarget;
        cachedTarget = null;
        float bestDist = float.MaxValue;
        foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == cachedSelf || !p.isActiveAndEnabled) continue;
            float d = Vector2.Distance(p.transform.position, transform.position);
            if (d < bestDist) { bestDist = d; cachedTarget = p; }
        }
        return cachedTarget;
    }

    void Update()
    {
        // Reset per-frame signals.
        cachedMove     = Vector2.zero;
        cachedRotation = 0f;
        wantPush       = false;

        if (cachedSelf == null) return;

        Player target = FindTarget();
        if (target == null) return;

        // Calculate direction AND distance from the raw (un-normalised) vector.
        Vector2 rawDiff = (Vector2)(target.transform.position - transform.position);
        float   dist    = rawDiff.magnitude;
        Vector2 toTarget = dist > 0.001f ? rawDiff / dist : Vector2.up;

        // Rotate to face target.
        float angle = Vector2.SignedAngle(transform.up, toTarget);
        if (Mathf.Abs(angle) > rotationDeadzone)
            cachedRotation = angle > 0f ? -1f : 1f;

        // Walk toward target when outside push range but inside chase range.
        if (dist > pushRange && dist < chaseRange)
            cachedMove = toTarget * walkSpeed;

        // Push or block on a timer, only when close and roughly aimed.
        bool aimed = Vector2.Dot(transform.up, toTarget) > 0.75f;
        if (aimed && dist <= pushRange && Time.time >= nextActionTime)
        {
            float roll = Random.value;
            if (roll < pushChance)
            {
                wantPush = true;
            }
            else if (roll < pushChance + blockChance)
            {
                blockUntil = Time.time + Random.Range(0.3f, 0.8f);
            }
            nextActionTime = Time.time + Random.Range(minActionInterval, maxActionInterval);
        }
    }

    public Vector2 GetMovement()      => cachedMove;
    public float   GetRotationInput() => cachedRotation;
    public bool    GetPushInput()     => wantPush;
    public bool    GetBlockInput()    => Time.time < blockUntil;
    public bool    GetDodgeInput()    => false;
    public bool    GetSpecialInput()  => false;
}
