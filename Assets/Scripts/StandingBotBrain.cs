using UnityEngine;

/// <summary>
/// Stands still, faces the nearest player, and pushes on a timer.
/// </summary>
public class StandingBotBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Behavior")]
    public float pushInterval    = 2f;
    public float aimDotThreshold = 0.85f;
    public float rotationDeadzone = 5f;

    private float   nextPushTime;
    private float   cachedRotation;
    private bool    wantPush;
    private Player  cachedSelf;

    void Awake() => cachedSelf = GetComponent<Player>();
    void Start()  => nextPushTime = Time.time + pushInterval;

    void Update()
    {
        cachedRotation = 0f;
        wantPush       = false;

        if (cachedSelf == null) return;

        // Find nearest other player.
        Player target   = null;
        float  bestDist = float.MaxValue;
        foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == cachedSelf || !p.isActiveAndEnabled) continue;
            float d = Vector2.Distance(p.transform.position, transform.position);
            if (d < bestDist) { bestDist = d; target = p; }
        }
        if (target == null) return;

        Vector2 toTarget = ((Vector2)(target.transform.position - transform.position)).normalized;

        // Rotate to face target.
        float angle = Vector2.SignedAngle(transform.up, toTarget);
        if (Mathf.Abs(angle) > rotationDeadzone)
            cachedRotation = angle > 0f ? -1f : 1f;

        // Push on interval when aimed.
        bool aimed = Vector2.Dot(transform.up, toTarget) > aimDotThreshold;
        if (aimed && Time.time >= nextPushTime)
        {
            wantPush      = true;
            nextPushTime  = Time.time + pushInterval;
        }
    }

    public Vector2 GetMovement()      => Vector2.zero;
    public float   GetRotationInput() => cachedRotation;
    public bool    GetPushInput()     => wantPush;
    public bool    GetBlockInput()    => false;
    public bool    GetDodgeInput()    => false;
    public bool    GetSpecialInput()  => false;
}
