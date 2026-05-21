using UnityEngine;

public class PlayerDodgingState : PlayerStateBase
{
    private float dodgeEndTime;

    public override void BeginState()
    {
        if (!player.CanUseStamina(player.stats.dodgeStamina))
        {
            player.SetState(Player.PlayerState.Moving);
            return;
        }

        player.UseStamina(player.stats.dodgeStamina);

        Vector2 dir = player.Brain.GetMovement();
        if (dir == Vector2.zero) dir = player.transform.up;

        player.SetVelocity(dir.normalized * player.stats.dodgeForce);
        dodgeEndTime = Time.time + player.stats.dodgeTime;
        if (player.animator != null) player.animator.SetTrigger("Dodge");
    }

    public override void UpdateState()
    {
        if (Time.time >= dodgeEndTime)
            player.SetState(Player.PlayerState.Moving);
    }

    public override void EndState()
    {
        player.SetVelocity(Vector2.zero);
    }
}
