using UnityEngine;

/// <summary>
/// Faces the nearest player, then every N seconds dashes toward them.
/// </summary>
public class DodgingBotBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Behavior")]
    public float dodgeInterval    = 3f;
    public float rotationDeadzone = 5f;

    private float   nextDodgeTime;
    private float   cachedRotation;
    private bool    wantDodge;
    private Vector2 cachedMove;
    private Player  cachedSelf;
    private Player  cachedTarget;

    void Awake() => cachedSelf = GetComponent<Player>();
    void Start()  => nextDodgeTime = Time.time + dodgeInterval;

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
        cachedRotation = 0f;
        cachedMove     = Vector2.zero;
        // wantDodge is NOT reset here — it persists until GetDodgeInput() consumes it,
        // so execution order between this Update and Player.Update doesn't matter.

        if (cachedSelf == null) return;

        Player target = FindTarget();
        if (target == null) return;

        Vector2 toTarget = ((Vector2)(target.transform.position - transform.position)).normalized;

        // Rotate to face target.
        float angle = Vector2.SignedAngle(transform.up, toTarget);
        if (Mathf.Abs(angle) > rotationDeadzone)
            cachedRotation = angle > 0f ? -1f : 1f;

        // Dodge toward target on interval.
        if (Time.time >= nextDodgeTime)
        {
            cachedMove    = toTarget;   // sets direction for PlayerDodgingState
            wantDodge     = true;
            nextDodgeTime = Time.time + dodgeInterval;
        }
    }

    public Vector2 GetMovement()      => cachedMove;
    public float   GetRotationInput() => cachedRotation;
    public bool    GetPushInput()     => false;
    public bool    GetBlockInput()    => false;
    public bool    GetDodgeInput()    { bool v = wantDodge; wantDodge = false; return v; }
    public bool    GetSpecialInput()  => false;
}
