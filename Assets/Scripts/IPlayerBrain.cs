using UnityEngine;

public interface IPlayerBrain
{
    Vector2 GetMovement();
    float GetRotationInput();
    bool GetPushInput();
    bool GetBlockInput();
    bool GetDodgeInput();
    bool GetSpecialInput();
}
