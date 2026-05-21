using UnityEngine;

// Heuristic baseline opponent: chases the nearest player and pulse-charges pushes
// when in range and aimed. Intended as a fixed sparring partner for RL training.
public class CustomLogicBrain : MonoBehaviour, IPlayerBrain
{
    [Header("Targeting")]
    public Player target;

    [Header("Tuning")]
    public float pushRange = 1.4f;
    public float chaseRange = 25f;
    public float aimDotThreshold = 0.9f;
    public float rotationDeadzone = 8f;
    public float pushChargeDuration = 0.35f;

    private Vector2 cachedMove;
    private float cachedRotation;
    private bool wantPush;
    private bool committing;
    private float commitUntil;

    void Update()
    {
        if (target == null || !target.isActiveAndEnabled) AcquireTarget();
        Think();
    }

    private void AcquireTarget()
    {
        Player nearest = null;
        float best = float.MaxValue;
        foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p.gameObject == gameObject || !p.isActiveAndEnabled) continue;
            float d = Vector2.Distance(p.transform.position, transform.position);
            if (d < best) { best = d; nearest = p; }
        }
        target = nearest;
    }

    private void Think()
    {
        cachedMove = Vector2.zero;
        cachedRotation = 0f;
        wantPush = false;
        if (target == null) return;

        Vector2 toTarget = (Vector2)(target.transform.position - transform.position);
        float dist = toTarget.magnitude;
        if (dist > chaseRange || dist < 0.001f) return;

        Vector2 dir = toTarget / dist;

        float signedAngle = Vector2.SignedAngle(transform.up, dir);
        if (Mathf.Abs(signedAngle) > rotationDeadzone)
            cachedRotation = signedAngle > 0f ? -1f : 1f;

        bool aimed = Vector2.Dot(transform.up, dir) > aimDotThreshold;

        if (dist > pushRange)
        {
            cachedMove = dir;
        }
        else if (aimed && !committing)
        {
            committing = true;
            commitUntil = Time.time + pushChargeDuration;
        }

        if (committing)
        {
            if (Time.time < commitUntil)
            {
                wantPush = true;
            }
            else
            {
                wantPush = false;
                committing = false;
            }
        }
    }

    public Vector2 GetMovement() => cachedMove;
    public float GetRotationInput() => cachedRotation;
    public bool GetPushInput() => wantPush;
    public bool GetBlockInput() => false;
    public bool GetDodgeInput() => false;
    public bool GetSpecialInput() => false;
}
