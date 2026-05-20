using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterStats", menuName = "Pushman/Character Stats")]
public class CharacterStats : ScriptableObject
{
    [Header("Body")]
    public float weight = 1f;
    public float movementSpeed = 6f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaRegenRate = 10f;
    public float blockStaminaUsageRate = 20f;

    [Header("Dodge")]
    public float dodgeForce = 12f;
    public float dodgeStamina = 20f;
    public float dodgeTime = 0.3f;

    [Header("Push")]
    public float pushForce = 8f;
    public float pushStamina = 15f;
    public float pushChargeMultiplier = 2f;
    public float pushChargeTime = 1f;
}
