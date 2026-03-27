using UnityEngine;

public class PlayerMovingState : PlayerStateBase
{
    public override void BeginState() { }

    public override void UpdateState()
    {
        Vector2 moveDirection = player.Brain.GetMovement();
        player.ApplyForce(moveDirection * player.movementSpeed);

        if (moveDirection != Vector2.zero)
        {
            float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg - 90f;
            player.transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        if (player.Brain.GetPushInput()) player.SetState(Player.PlayerState.Charging);
        else if (player.Brain.GetBlockInput()) player.SetState(Player.PlayerState.Blocking);
        else if (player.Brain.GetDodgeInput()) player.SetState(Player.PlayerState.Dodging);
    }

    public override void EndState() { }
}