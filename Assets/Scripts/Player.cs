using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    [Header("Configuration")]
    public CharacterStats stats;

    [Header("Combat")]
    public LayerMask opponentLayer;
    public float pushHitRadius = 0.8f;
    public float pushHitOffset = 0.7f;

    // Stamina UI is handled by StaminaHUD (screen-space canvas) — no field needed here.

    [Header("Visuals")]
    public SpriteRenderer pushHand;    // assigned by PushmanSetup or auto-discovered
    public SpriteRenderer blockHand;

    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;

    public enum PlayerState { Moving, Charging, Blocking, Pushing, Dodging, Stunned }
    public PlayerState currentState;
    private PlayerStateBase currentStateScript;
    public IPlayerBrain Brain { get; private set; }

    private Rigidbody2D rb;
    public Rigidbody2D Body => rb;
    public Animator animator;
    public float currentStamina;

    // Stamina regen boost tracking
    private float _timeInMovingState;

    // State Scripts
    public PlayerMovingState movingStateScript;
    public PlayerChargingState chargingStateScript;
    public PlayerBlockingState blockingStateScript;
    public PlayerPushingState pushingStateScript;
    public PlayerDodgingState dodgingStateScript;
    public PlayerStunnedState stunnedStateScript;

    private Coroutine stunRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();  // may be null — states check before calling
        spriteRenderer = GetComponent<SpriteRenderer>();

        Brain = GetComponent<IPlayerBrain>();
        if (Brain == null) Debug.LogError($"No IPlayerBrain found on {gameObject.name}!");

        if (stats == null)
        {
            Debug.LogError($"No CharacterStats assigned on {gameObject.name}!");
        }
        else
        {
            rb.mass = stats.weight;
            currentStamina = stats.maxStamina;
        }

        movingStateScript = GetComponent<PlayerMovingState>();
        chargingStateScript = GetComponent<PlayerChargingState>();
        blockingStateScript = GetComponent<PlayerBlockingState>();
        pushingStateScript = GetComponent<PlayerPushingState>();
        dodgingStateScript = GetComponent<PlayerDodgingState>();
        stunnedStateScript = GetComponent<PlayerStunnedState>();

        if (spriteRenderer != null) baseColor = spriteRenderer.color;

        // Auto-discover hand sprites from named children if not wired in Inspector.
        if (pushHand == null)
        {
            var t = transform.Find("PushHand");
            if (t != null) pushHand = t.GetComponent<SpriteRenderer>();
        }
        if (blockHand == null)
        {
            var t = transform.Find("BlockHand");
            if (t != null) blockHand = t.GetComponent<SpriteRenderer>();
        }
    }

    void Start() => SetState(PlayerState.Moving);

    void Update()
    {
        currentStateScript?.UpdateState();

        // Regen only while Moving — not while stunned/charging/blocking/dodging.
        // Moving-state timer accumulates to unlock a regen boost (reward for disengaging).
        if (stats != null)
        {
            if (currentState == PlayerState.Moving)
            {
                _timeInMovingState += Time.deltaTime;
                float rate = _timeInMovingState >= stats.regenBoostThreshold
                    ? stats.staminaRegenRate * stats.regenBoostMultiplier
                    : stats.staminaRegenRate;
                currentStamina = Mathf.Min(stats.maxStamina, currentStamina + rate * Time.deltaTime);
            }
            else
            {
                _timeInMovingState = 0f;
            }
        }

        UpdateStateColor();
        UpdateHands();
    }

    private void UpdateHands()
    {
        bool showPush  = currentState == PlayerState.Charging || currentState == PlayerState.Pushing;
        bool showBlock = currentState == PlayerState.Blocking;
        if (pushHand  != null) pushHand.gameObject.SetActive(showPush);
        if (blockHand != null) blockHand.gameObject.SetActive(showBlock);
    }

    private void UpdateStateColor()
    {
        if (spriteRenderer == null) return;

        Color targetColor;

        // All states blend with baseColor so the player's identity hue remains readable.
        // Mix ratio kept low (0.35–0.45) — enough to signal the state without washing out the color.
        if (currentState == PlayerState.Stunned)
            targetColor = Color.Lerp(baseColor, Color.gray, 0.45f);                 // desaturated, still tinted
        else if (currentState == PlayerState.Dodging)
            targetColor = Color.Lerp(baseColor, new Color(0.25f, 0.55f, 1f), 0.40f); // blue hint over body color
        else if (currentState == PlayerState.Blocking)
            targetColor = Color.Lerp(baseColor, new Color(0.4f, 0.85f, 1f), 0.35f); // subtle cyan shield tint
        else if (currentState == PlayerState.Charging)
            targetColor = Color.Lerp(baseColor, Color.white, 0.40f);               // brightens without going white
        else
            targetColor = baseColor;

        // 8/s lerp — fast enough that even a 0.25s dodge visibly registers.
        spriteRenderer.color = Color.Lerp(spriteRenderer.color, targetColor, Time.deltaTime * 8f);
    }

    public void SetState(PlayerState newState)
    {
        currentStateScript?.EndState();
        currentState = newState;
        currentStateScript = newState switch
        {
            PlayerState.Moving => movingStateScript,
            PlayerState.Charging => chargingStateScript,
            PlayerState.Blocking => blockingStateScript,
            PlayerState.Pushing => pushingStateScript,
            PlayerState.Dodging => dodgingStateScript,
            PlayerState.Stunned => stunnedStateScript,
            _ => currentStateScript
        };
        currentStateScript?.BeginState();
    }

    // --- Combat resolution ---

    // Explicit overlap cast: works even when both players are already touching,
    // which OnCollisionEnter2D would silently miss. Handles its own recovery stun.
    public void ExecutePush(float chargeNormalized)
    {
        float strength = stats.pushForce + stats.pushForce * stats.pushChargeMultiplier * Mathf.Clamp01(chargeNormalized);
        Vector2 origin = (Vector2)transform.position + (Vector2)transform.up * pushHitOffset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, pushHitRadius, opponentLayer);

        RLAgentBrain myRL = Brain as RLAgentBrain;

        foreach (var hitCol in hits)
        {
            Player other = hitCol.GetComponentInParent<Player>();
            if (other == null || other == this) continue;

            Vector2 pushDir = ((Vector2)(other.transform.position - transform.position)).normalized;
            RLAgentBrain otherRL = other.Brain as RLAgentBrain;
            bool blocked = other.currentState == PlayerState.Blocking && other.IsFacing(this);

            if (blocked)
            {
                // Block prevents the push — blocker stays in Blocking state
                otherRL?.AddSuccessfulBlockReward();
                myRL?.AddPushBlockedReward();
                Stun(0.2f);                          // pusher recovery frames (push stopped by block)
            }
            else
            {
                other.Stun(0.5f);
                other.TriggerHitFlash();
                other.ApplyImpulse(pushDir * strength);
                otherRL?.AddTakeHitReward();
                // Pass victim's position so the brain can compute the edge-hit bonus.
                // A hit at the ring edge ≈ ring-out moment → big bonus; center hit ≈ tiny.
                myRL?.AddLandPushReward(other.transform.position);
                Stun(0.2f);                          // recovery frames
            }
            return;
        }

        Stun(0.2f); // whiff recovery
    }

    public bool IsFacing(Player other)
    {
        Vector2 toOther = ((Vector2)(other.transform.position - transform.position)).normalized;
        return Vector2.Dot(transform.up, toOther) > 0.3f;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (currentState == PlayerState.Dodging) HandleDodgeCollision(collision);
    }

    private void HandleDodgeCollision(Collision2D collision)
    {
        Player other = collision.gameObject.GetComponentInParent<Player>();
        if (other == null || other == this)
        {
            SetVelocity(Vector2.zero);
            SetState(PlayerState.Moving);
            return;
        }

        if (other.currentState == PlayerState.Dodging)
        {
            // Mutual dodge: both fire this callback, so resolve exactly once.
            dodgingStateScript.NotifyHit();
            if (GetInstanceID() < other.GetInstanceID())
            {
                Vector2 dir = ((Vector2)(transform.position - other.transform.position)).normalized;
                Stun(0.5f);
                other.Stun(0.5f);
                ApplyImpulse(dir * stats.dodgeForce);
                other.ApplyImpulse(-dir * other.stats.dodgeForce);
            }
        }
        else if (other.currentState == PlayerState.Blocking && other.IsFacing(this))
        {
            // Dodge breaks block
            dodgingStateScript.NotifyHit();
            other.SetState(PlayerState.Moving);
            RLAgentBrain otherRL = other.Brain as RLAgentBrain;
            RLAgentBrain myRL = Brain as RLAgentBrain;
            otherRL?.AddPushBlockedReward();  // blocker penalized
            myRL?.AddDodgeEvasionReward();    // dodger rewarded for breaking block
            SetVelocity(Vector2.zero);
            SetState(PlayerState.Moving);
            other.Stun(0.3f);
        }
        else
        {
            dodgingStateScript.NotifyHit();
            Vector2 hitDir = ((Vector2)(other.transform.position - transform.position)).normalized;
            SetVelocity(Vector2.zero);
            SetState(PlayerState.Moving);
            other.Stun(0.5f);
            other.TriggerHitFlash();
            other.ApplyImpulse(hitDir * stats.dodgeForce);
            RLAgentBrain myRL = Brain as RLAgentBrain;
            myRL?.AddDodgeHitReward(other.transform.position);
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Snap the body color to bright white so a push or dodge hit registers visually.
    /// UpdateStateColor's 8/s lerp naturally fades it back — no coroutine needed.
    /// </summary>
    public void TriggerHitFlash()
    {
        if (spriteRenderer != null) spriteRenderer.color = Color.Lerp(baseColor, Color.white, 0.75f); // bright but identity still readable
    }

    public void ApplyImpulse(Vector2 force) => rb.AddForce(force, ForceMode2D.Impulse);
    public void SetVelocity(Vector2 velocity) => rb.linearVelocity = velocity;
    public bool CanUseStamina(float amount) => currentStamina >= amount;
    public void UseStamina(float amount) => currentStamina = Mathf.Max(0f, currentStamina - amount);

    public bool TryDodge(float cost)
    {
        if (!CanUseStamina(cost)) return false;
        UseStamina(cost);
        return true;
    }

    public void Stun(float duration)
    {
        if (stunRoutine != null) StopCoroutine(stunRoutine);
        stunRoutine = StartCoroutine(StunCoroutine(duration));
    }

    private IEnumerator StunCoroutine(float duration)
    {
        SetState(PlayerState.Stunned);
        yield return new WaitForSeconds(duration);
        stunRoutine = null;
        SetState(PlayerState.Moving);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector2 origin = (Vector2)transform.position + (Vector2)transform.up * pushHitOffset;
        Gizmos.DrawWireSphere(origin, pushHitRadius);
    }
}
