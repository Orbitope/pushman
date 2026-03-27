using UnityEngine;

public interface IPlayerBrain
{
    Vector2 GetMovement();
    bool GetPushInput();    
    bool GetBlockInput();   
    bool GetDodgeInput();   
    bool GetSpecialInput(); 
}