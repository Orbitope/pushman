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

        // Stamina cost scales with charge: base cost at tap, up to 2× at full charge.
        float staminaCost = player.stats.pushStamina * (1f + chargeNorm);

        if (!player.CanUseStamina(staminaCost))
        {
            (player.Brain as RLAgentBrain)?.AddWastedStaminaPenalty(staminaCost);
            player.SetState(Player.PlayerState.Moving);
            return;
        }

        player.UseStamina(staminaCost);
        if (player.animator != null) player.animator.SetTrigger("Push");
        player.ExecutePush(chargeNorm);
    }

    public override void UpdateState() { }

    public override void EndState() { }
}
