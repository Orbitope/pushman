using UnityEngine;

public class PlayerBlockingState : PlayerStateBase
{
    public override void BeginState()
    {
        if (player.animator != null) player.animator.SetBool("IsBlocking", true);
    }

    public override void UpdateState()
    {
        if (!player.Brain.GetBlockInput())
        {
            player.SetState(Player.PlayerState.Moving);
            return;
        }

        float cost = player.stats.blockStaminaUsageRate * Time.deltaTime;
        if (!player.CanUseStamina(cost))
        {
            player.SetState(Player.PlayerState.Moving);
            return;
        }
        player.UseStamina(cost);

        Vector2 move = player.Brain.GetMovement();
        player.SetVelocity(move * player.stats.movementSpeed * 0.25f);

        float rot = player.Brain.GetRotationInput();
        if (rot != 0f)
            player.transform.Rotate(0f, 0f, -rot * 360f * Time.deltaTime);
    }

    public override void EndState()
    {
        if (player.animator != null) player.animator.SetBool("IsBlocking", false);
    }
}
