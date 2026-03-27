using UnityEngine;
using System.Collections;

public class Player : MonoBehaviour
{
    public float weight = 1f;
    public float maxStamina = 100f;
    public float staminaRegenRate = 10f;
    public float blockStaminaUsageRate = 20f;
    public float dodgeForce = 500f;
    public float dodgeStamina = 20f;
    public float dodgeTime = 0.5f;
    public float pushForce = 300f;
    public float pushStamina = 15f;
    public float pushChargeMultiplier = 2f;
    public float pushChargeTime = 1f;
    public float movementSpeed = 200f;
    public float specialStamina = 50f;

    public enum PlayerState { Moving, Charging, Blocking, Pushing, Dodging, Stunned }
    public PlayerState currentState;
    private PlayerStateBase currentStateScript; 
    public IPlayerBrain Brain { get; private set; }

    private Rigidbody2D rb;
    public Animator animator; 
    public float currentStamina;

    // State Scripts
    public PlayerMovingState movingStateScript;
    public PlayerChargingState chargingStateScript;
    public PlayerBlockingState blockingStateScript;
    public PlayerPushingState pushingStateScript;
    public PlayerDodgingState dodgingStateScript;
    public PlayerStunnedState stunnedStateScript;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        currentStamina = maxStamina;

        Brain = GetComponent<IPlayerBrain>();
        if (Brain == null) Debug.LogError($"No IPlayerBrain found on {gameObject.name}!");

        movingStateScript = GetComponent<PlayerMovingState>();
        chargingStateScript = GetComponent<PlayerChargingState>();
        blockingStateScript = GetComponent<PlayerBlockingState>();
        pushingStateScript = GetComponent<PlayerPushingState>();
        dodgingStateScript = GetComponent<PlayerDodgingState>();
        stunnedStateScript = GetComponent<PlayerStunnedState>();
    }

    void Start()
    {
        SetState(PlayerState.Moving); 
    }

    void Update()
    {
        currentStateScript?.UpdateState();

        if (currentState == PlayerState.Moving || currentState == PlayerState.Pushing || currentState == PlayerState.Stunned)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenRate * Time.deltaTime);
        }
    }

    public void SetState(PlayerState newState)
    {
        currentStateScript?.EndState(); 
        currentState = newState;

        switch (newState)
        {
            case PlayerState.Moving: currentStateScript = movingStateScript; break;
            case PlayerState.Charging: currentStateScript = chargingStateScript; break;
            case PlayerState.Blocking: currentStateScript = blockingStateScript; break;
            case PlayerState.Pushing: currentStateScript = pushingStateScript; break;
            case PlayerState.Dodging: currentStateScript = dodgingStateScript; break;
            case PlayerState.Stunned: currentStateScript = stunnedStateScript; break;
        }

        currentStateScript?.BeginState(); 
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (currentState == PlayerState.Dodging) HandleDodgeCollision(collision);
        else if (currentState == PlayerState.Pushing) HandlePushCollision(collision);
    }

    private void HandleDodgeCollision(Collision2D collision)
    {
        Player otherPlayer = collision.gameObject.GetComponent<Player>();
        if (otherPlayer != null && otherPlayer != this) 
        {
            SetVelocity(Vector2.zero); 
            SetState(PlayerState.Moving); 

            Vector2 hitDirection = (collision.transform.position - transform.position).normalized;
            otherPlayer.ApplyForce(hitDirection * dodgeForce);
            otherPlayer.Stun(0.5f); 
        }
        else
        {
            SetVelocity(Vector2.zero); 
            SetState(PlayerState.Moving); 
        }
    }

    private void HandlePushCollision(Collision2D collision)
    {
        Player otherPlayer = collision.gameObject.GetComponent<Player>();
        if (otherPlayer != null && otherPlayer != this)
        {
            Vector2 pushDirection = (collision.transform.position - transform.position).normalized;
            float pushStrength = pushForce + (pushForce * pushChargeMultiplier * (pushingStateScript.chargeTime / pushChargeTime));

            RLAgentBrain myRLBrain = Brain as RLAgentBrain;
            RLAgentBrain opponentRLBrain = otherPlayer.Brain as RLAgentBrain;

            if (collision.otherCollider.CompareTag("Block"))
            {
                ApplyForce(-pushDirection * pushStrength); 
                if (myRLBrain != null) myRLBrain.AddPushBlockedReward();           
                if (opponentRLBrain != null) opponentRLBrain.AddSuccessfulBlockReward(); 
            }
            else
            {
                otherPlayer.ApplyForce(pushDirection * pushStrength); 
                otherPlayer.Stun(0.5f); 
                if (myRLBrain != null) myRLBrain.AddLandPushReward();      
                if (opponentRLBrain != null) opponentRLBrain.AddTakeHitReward(); 
            }
        }
    }

    public void ApplyForce(Vector2 force) => rb.AddForce(force);
    public void SetVelocity(Vector2 velocity) => rb.linearVelocity = velocity;
    public bool CanUseStamina(float amount) => currentStamina >= amount;
    public void UseStamina(float amount) => currentStamina -= amount;
    public void Stun(float duration) => StartCoroutine(StunCoroutine(duration));

    private IEnumerator StunCoroutine(float duration)
    {
        SetState(PlayerState.Stunned);
        yield return new WaitForSeconds(duration);
        SetState(PlayerState.Moving); 
    }
}