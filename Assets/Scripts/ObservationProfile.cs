using UnityEngine;

// Replaces the ad-hoc per-observation booleans that were on RLAgentBrain.
// Hot-swap this asset alongside a BotPersonality to produce tiered bot difficulty.
[CreateAssetMenu(fileName = "NewObservationProfile", menuName = "Pushman/Observation Profile")]
public class ObservationProfile : ScriptableObject
{
    [Header("Self — always 3 floats (velocity x/y, rotation)")]
    public bool selfKinematics = true;
    [Header("Self — 1 float")]
    public bool selfStamina = true;
    [Header("Self — 1 float (enum / 5)")]
    public bool selfState = true;
    [Header("Self — 1 float (charge progress 0–1; 0 when not Charging)")]
    [Tooltip("Pre-computed PlayerChargingState.currentChargeTime / pushChargeTime. " +
             "Without this the network has to derive charge progress from LSTM state-change " +
             "timing, which it does poorly. Strong v3 candidate. Adds 1 obs.")]
    public bool selfChargeProgress = false;
    [Header("Self ring-out margin — 1 float (1 - distFromCenter / currentRingRadius)")]
    [Tooltip("Pre-computed safety margin: 1=at center, 0=at ring edge. Derivable from " +
             "arenaBounds but pre-computing gives a clean safety scalar. Adds 1 obs.")]
    public bool selfRingOutMargin = false;
    [Header("Arena — 2 floats (toCenter x/y, normalized) + 1 float (ring radius)")]
    public bool arenaBounds = true;

    [Header("Opponent Observations (per opponent)")]
    public bool opponentPosition = true;  // 2 floats
    public bool opponentVelocity = true;  // 2 floats
    public bool opponentState = true;     // 1 float (legacy single-float encoding)

    [Header("Per-opponent geometry — facing + distance")]
    [Tooltip("Dot(self.up, dirToOpp) — 1 dim per opp. Tells the agent how well it's aimed " +
             "at this opponent. Fixes random push/block directions. Adds 1 obs/opponent.")]
    public bool selfFacingOpponentDot = false;
    [Tooltip("Pre-computed distance to opponent (normalized by ringOutRadius). Reduces the " +
             "trig load on the network. Adds 1 obs/opponent.")]
    public bool distanceToOpponent = false;
    [Tooltip("Dot(opp.up, -dirToOpp) — tells the agent whether the opponent is aimed at it. " +
             "Helps predict incoming pushes/blocks. Adds 1 obs/opponent.")]
    public bool opponentFacingSelfDot = false;
    [Tooltip("One-hot encoding of opponent state (6 dims per opp). REPLACES the 1-dim " +
             "opponentState float when enabled — cleaner categorical encoding. " +
             "Net change: +5 dims per opponent.")]
    public bool opponentStateOneHot = false;

    [Header("Self Stats — 5 floats (weight, speed, pushForce, dodgeForce, maxStamina — normalized)")]
    [Tooltip("Lets the agent observe its own CharacterStats so one network generalises across " +
             "DefaultStats / Heavyweight / Speedster without retraining. Adds 5 obs.")]
    public bool selfStats = true;

    [Header("Self Personality — one-hot 5 floats")]
    [Tooltip("Lets a SHARED network observe its own personality so it can condition policy on " +
             "its current reward stream (Defensive cares about blocks more than Aggressive, etc). " +
             "Required for Phase 3 v2 shared-network training. Opponent personality is " +
             "intentionally NOT observed — agents react to opponent BEHAVIOR (state/pos/vel), " +
             "which works equally well against bots and humans.")]
    public bool selfPersonalityId = true;

    [Header("Opponent Stats — 5 floats per opponent")]
    [Tooltip("Observe the opponent's CharacterStats. Adds 5 obs per opponent. Off by default — " +
             "useful for Phase 3 specialist training.")]
    public bool opponentStats = false;

    [Header("Profile C — Humanized")]
    [Tooltip("Applies Gaussian noise to opponent position/velocity observations.")]
    public bool applyNoise = false;
    public float positionNoiseMagnitude = 0.05f;
    [Tooltip("Feeds observations from N frames ago to simulate human reaction time.")]
    public int observationDelayFrames = 0;
    [Tooltip("Emit the agent's current humanization scalar (0=Expert, 1=Easy) as a 1-dim obs. " +
             "Required for Phase 3.5 — lets the network condition policy on its current tier. " +
             "Leave OFF for Profile_A (v2 training) to keep obs space at 23.")]
    public bool humanization = false;

    // Compute total observation space size for this profile given an opponent count.
    public int ComputeSpaceSize(int opponentCount)
    {
        int total = 0;
        if (selfKinematics)       total += 3;
        if (selfStamina)          total += 1;
        if (selfState)            total += 1;
        if (selfChargeProgress)   total += 1;
        if (selfRingOutMargin)    total += 1;
        if (arenaBounds)          total += 3; // toCenter(2) + ringRadius(1)
        if (selfStats)            total += 5; // weight, speed, pushForce, dodgeForce, maxStamina
        if (selfPersonalityId)    total += BotPersonality.Count; // one-hot 5 dims
        int perOpp = 0;
        if (opponentPosition)          perOpp += 2;
        if (opponentVelocity)          perOpp += 2;
        if (opponentStateOneHot)       perOpp += 6;   // 6-dim one-hot (Moving/Charging/Pushing/Blocking/Dodging/Stunned)
        else if (opponentState)        perOpp += 1;   // legacy single-float encoding
        if (selfFacingOpponentDot)     perOpp += 1;
        if (distanceToOpponent)        perOpp += 1;
        if (opponentFacingSelfDot)     perOpp += 1;
        if (opponentStats)             perOpp += 5;
        total += perOpp * opponentCount;
        if (humanization) total += 1; // scalar 0–1 (Expert→Easy)
        return total;
    }
}
