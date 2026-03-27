using UnityEngine;

public class HumanBrain : MonoBehaviour, IPlayerBrain
{
    public Vector2 GetMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        return new Vector2(h, v).normalized;
    }

    public bool GetPushInput() => Input.GetButton("Fire1"); 
    public bool GetBlockInput() => Input.GetButton("Fire2");
    public bool GetDodgeInput() => Input.GetButtonDown("Jump");
    public bool GetSpecialInput() => Input.GetButtonDown("Fire3");
}