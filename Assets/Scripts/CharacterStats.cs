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
