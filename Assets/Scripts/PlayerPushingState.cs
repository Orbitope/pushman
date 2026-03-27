using UnityEngine;

public class PlayerPushingState : PlayerStateBase
{
    private float pushStartTime;
    public float chargeTime { get; private set; } 

    public override void BeginState()
    {
        pushStartTime = Time.time;
        chargeTime = 0f;
        player.animator?.SetTrigger("Push"); 
    }

    public override void UpdateState()
    {
        chargeTime = Mathf.Min(player.pushChargeTime, Time.time - pushStartTime);

        if(chargeTime >= player.pushChargeTime || !player.Brain.GetPushInput())
        {
            Push();
        }
    }

    private void Push()
    {
        float pushStrength = player.pushForce + (player.pushForce * player.pushChargeMultiplier * (chargeTime / player.pushChargeTime));
        Vector2 pushDirection = player.transform.up; 

        if (player.CanUseStamina(player.pushStamina))
        {
            player.UseStamina(player.pushStamina);
            player.ApplyForce(pushDirection * pushStrength);
            player.Stun(0.2f); 
        }
        else if (player.Brain is RLAgentBrain rlBrain)
        {
            rlBrain.AddWastedStaminaPenalty(player.pushStamina);
        }

        player.SetState(Player.PlayerState.Moving);
    }

    public override void EndState() { }
}