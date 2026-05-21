using UnityEngine;

public class PlayerChargingState : PlayerStateBase
{
    // Read by PlayerPushingState on Enter to scale push strength.
    public float currentChargeTime { get; private set; }

    public override void BeginState()
    {
        currentChargeTime = 0f;
        if (player.animator != null) player.animator.SetBool("IsCharging", true);
    }

    public override void UpdateState()
    {
        currentChargeTime = Mathf.Min(player.stats.pushChargeTime, currentChargeTime + Time.deltaTime);

        Vector2 move = player.Brain.GetMovement();
        player.SetVelocity(move * player.stats.movementSpeed * 0.5f);

        float rot = player.Brain.GetRotationInput();
        if (rot != 0f)
            player.transform.Rotate(0f, 0f, -rot * 360f * Time.deltaTime);

        if (!player.Brain.GetPushInput())
            player.SetState(Player.PlayerState.Pushing);
    }

    public override void EndState()
    {
        if (player.animator != null) player.animator.SetBool("IsCharging", false);
    }
}
