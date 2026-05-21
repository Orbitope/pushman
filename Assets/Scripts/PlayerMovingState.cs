using UnityEngine;

public class PlayerMovingState : PlayerStateBase
{
    public override void BeginState() { }

    public override void UpdateState()
    {
        Vector2 move = player.Brain.GetMovement();
        player.SetVelocity(move * player.stats.movementSpeed);

        float rot = player.Brain.GetRotationInput();
        if (rot != 0f)
            player.transform.Rotate(0f, 0f, -rot * 360f * Time.deltaTime);

        // Dodge checked first — it's a one-shot (wasPressedThisFrame) and should never
        // be swallowed by a held push button.
        if (player.Brain.GetDodgeInput()) player.SetState(Player.PlayerState.Dodging);
        else if (player.Brain.GetPushInput()) player.SetState(Player.PlayerState.Charging);
        else if (player.Brain.GetBlockInput()) player.SetState(Player.PlayerState.Blocking);
    }

    public override void EndState() { }
}
