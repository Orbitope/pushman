using UnityEngine;
using UnityEngine.InputSystem;

public class HumanBrain : MonoBehaviour, IPlayerBrain
{
    [Tooltip("Degrees of slack before the player rotates toward the pointer (prevents jitter).")]
    public float rotationDeadzone = 8f;

    private Keyboard keyboard;
    private Mouse mouse;

    void OnEnable()
    {
        keyboard = Keyboard.current;
        mouse = Mouse.current;
    }

    public Vector2 GetMovement()
    {
        if (keyboard == null) return Vector2.zero;

        float h = 0f;
        float v = 0f;

        if (keyboard.aKey.isPressed) h -= 1f;
        if (keyboard.dKey.isPressed) h += 1f;
        if (keyboard.sKey.isPressed) v -= 1f;
        if (keyboard.wKey.isPressed) v += 1f;

        return new Vector2(h, v).normalized;
    }

    public float GetRotationInput()
    {
        Camera cam = Camera.main;
        if (cam == null || mouse == null) return 0f;

        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        Vector2 toMouse = (Vector2)mouseWorld - (Vector2)transform.position;
        if (toMouse.sqrMagnitude < 0.0001f) return 0f;

        float signedAngle = Vector2.SignedAngle(transform.up, toMouse);
        if (Mathf.Abs(signedAngle) < rotationDeadzone) return 0f;
        return signedAngle > 0f ? -1f : 1f;
    }

    public bool GetPushInput() => mouse != null && mouse.leftButton.isPressed;
    public bool GetBlockInput() => mouse != null && mouse.rightButton.isPressed;
    public bool GetDodgeInput() => keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
    public bool GetSpecialInput() => false;
}
