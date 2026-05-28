using UnityEngine;

public class PlayerDodgingState : PlayerStateBase
{
    private float dodgeEndTime;
    private float savedDamping;
    private bool _hitSomething;

    public override void BeginState()
    {
        _hitSomething = false;

        if (!player.TryDodge(player.stats.dodgeStamina))
        {
            player.SetState(Player.PlayerState.Moving);
            return;
        }

        // Kill damping for the duration of the dash so the force isn't eaten by drag.
        // At damping=2 a force-10 dash only travels ~2 units; with damping=0 it travels the full arc.
        savedDamping = player.Body.linearDamping;
        player.Body.linearDamping = 0f;

        Vector2 dir = player.Brain.GetMovement();
        if (dir == Vector2.zero) dir = player.transform.up;

        player.SetVelocity(dir.normalized * player.stats.dodgeForce);
        dodgeEndTime = Time.time + player.stats.dodgeTime;
        if (player.animator != null) player.animator.SetTrigger("Dodge");
    }

    /// <summary>Called by Player when this dodge makes contact with another player.</summary>
    public void NotifyHit() => _hitSomething = true;

    public override void UpdateState()
    {
        if (Time.time >= dodgeEndTime)
            player.SetState(Player.PlayerState.Moving);
    }

    public override void EndState()
    {
        player.Body.linearDamping = savedDamping;
        player.SetVelocity(Vector2.zero);

        // If the dodge ended without touching anyone, apply the miss penalty so that
        // spamming dodge as a zero-risk move gets a cost signal.
        if (!_hitSomething)
            (player.Brain as RLAgentBrain)?.AddDodgeMissedPenalty();
    }
}
