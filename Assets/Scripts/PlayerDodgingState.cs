using UnityEngine;

public class PlayerDodgingState : PlayerStateBase
{
    private float dodgeEndTime;
    private Vector2 dodgeDirection;

    public override void BeginState()
    {
        if (player.CanUseStamina(player.dodgeStamina))
        {
            player.UseStamina(player.dodgeStamina);
            dodgeDirection = player.Brain.GetMovement();
            
            if (dodgeDirection == Vector2.zero) dodgeDirection = player.transform.up; 
            
            player.SetVelocity(dodgeDirection * player.dodgeForce); 
            dodgeEndTime = Time.time + player.dodgeTime;
            player.animator?.SetTrigger("Dodge"); 
        }
        else
        {
             player.SetState(Player.PlayerState.Moving); 
        }
    }

    public override void UpdateState()
    {
        if (Time.time > dodgeEndTime)
        {
            player.SetState(Player.PlayerState.Moving);
        }
    }

    public override void EndState()
    {
       player.SetVelocity(Vector2.zero); 
    }
}