using UnityEngine;

public class HumanBrain : MonoBehaviour, IPlayerBrain
{
    [Tooltip("Degrees of slack before the player rotates toward the pointer (prevents jitter).")]
    public float rotationDeadzone = 8f;

    public Vector2 GetMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        return new Vector2(h, v).normalized;
    }

    public float GetRotationInput()
    {
        Camera cam = Camera.main;
        if (cam == null) return 0f;

        Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 toMouse = (Vector2)mouseWorld - (Vector2)transform.position;
        if (toMouse.sqrMagnitude < 0.0001f) return 0f;

        float signedAngle = Vector2.SignedAngle(transform.up, toMouse);
        if (Mathf.Abs(signedAngle) < rotationDeadzone) return 0f;
        return signedAngle > 0f ? -1f : 1f;
    }

    public bool GetPushInput() => Input.GetButton("Fire1");
    public bool GetBlockInput() => Input.GetButton("Fire2");
    public bool GetDodgeInput() => Input.GetButtonDown("Jump");
    public bool GetSpecialInput() => Input.GetButtonDown("Fire3");
}
