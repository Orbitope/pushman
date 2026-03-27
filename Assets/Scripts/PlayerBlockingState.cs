using UnityEngine;

public class PlayerBlockingState : PlayerStateBase
{
    public override void BeginState()
    {
        player.animator?.SetBool("IsBlocking", true); 
    }

    public override void UpdateState()
    {
        if (!player.Brain.GetBlockInput())
        {
            player.SetState(Player.PlayerState.Moving); 
        }

        if (player.CanUseStamina(player.blockStaminaUsageRate * Time.deltaTime))
        {
            player.UseStamina(player.blockStaminaUsageRate * Time.deltaTime);
        }
        else
        {
            player.SetState(Player.PlayerState.Moving); 
        }

        Vector2 moveDirection = player.Brain.GetMovement();
        player.ApplyForce(moveDirection * player.movementSpeed * 0.25f); 
    }

    public override void EndState()
    {
        player.animator?.SetBool("IsBlocking", false); 
    }
}