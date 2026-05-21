using UnityEngine;

public class PlayerStunnedState : PlayerStateBase
{
    // The timer is handled by the coroutine in Player.cs, 
    // so this state mostly just handles animations and prevents input.
    public override void BeginState()
    {
        if (player.animator != null) player.animator.SetBool("IsStunned", true);
    }

    public override void UpdateState() { }

    public override void EndState()
    {
        if (player.animator != null) player.animator.SetBool("IsStunned", false);
    }
}