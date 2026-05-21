using UnityEngine;

// Transient state: resolves the push on Enter, then ExecutePush stuns the player
// (recovery or stagger), which transitions out of this state.
public class PlayerPushingState : PlayerStateBase
{
    public override void BeginState()
    {
        float chargeNorm = player.stats.pushChargeTime > 0f
            ? player.chargingStateScript.currentChargeTime / player.stats.pushChargeTime
            : 0f;

        if (!player.CanUseStamina(player.stats.pushStamina))
        {
            (player.Brain as RLAgentBrain)?.AddWastedStaminaPenalty(player.stats.pushStamina);
            player.SetState(Player.PlayerState.Moving);
            return;
        }

        player.UseStamina(player.stats.pushStamina);
        if (player.animator != null) player.animator.SetTrigger("Push");
        player.ExecutePush(chargeNorm);
    }

    public override void UpdateState() { }

    public override void EndState() { }
}
