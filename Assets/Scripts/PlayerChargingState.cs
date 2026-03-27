using UnityEngine;

public class PlayerChargingState : PlayerStateBase
{
    public override void BeginState()
    {
        player.animator?.SetBool("IsCharging", true); 
    }

    public override void UpdateState()
    {
        Vector2 moveDirection = player.Brain.GetMovement();
        player.ApplyForce(moveDirection * player.movementSpeed * 0.5f); 

        if (moveDirection != Vector2.zero)
        {
            float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg - 90f;
            player.transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        if (!player.Brain.GetPushInput())
        {
            player.SetState(Player.PlayerState.Pushing);
        }
    }

    public override void EndState()
    {
        player.animator?.SetBool("IsCharging", false); 
    }
}