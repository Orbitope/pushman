using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterStats", menuName = "Pushman/Character Stats")]
public class CharacterStats : ScriptableObject
{
    [Header("Body")]
    public float weight = 1f;
    public float movementSpeed = 6f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaRegenRate = 8f;
    public float blockStaminaUsageRate = 15f;

    [Header("Stamina Regen Boost")]
    [Tooltip("Seconds spent in Moving state before the regen boost activates. Rewards disengaging.")]
    public float regenBoostThreshold = 0.75f;
    [Tooltip("Multiplier applied to staminaRegenRate after regenBoostThreshold is reached.")]
    public float regenBoostMultiplier = 2.5f;

    [Header("Stamina Overdraft (Dodge Only)")]
    [Tooltip("Allow a dodge even when stamina would go negative. Must not already be in overdraft debt.")]
    public bool allowDodgeOverdraft = true;
    [Tooltip("Seconds regen is paused after an overdraft dodge resolves back to Moving.")]
    public float overdraftPauseDuration = 1.5f;

    [Header("Dodge")]
    public float dodgeForce = 18f;
    public float dodgeStamina = 40f;
    public float dodgeTime = 0.25f;

    [Header("Push")]
    public float pushForce = 8f;
    public float pushStamina = 20f;
    public float pushChargeMultiplier = 1.5f;
    public float pushChargeTime = 1f;
}
