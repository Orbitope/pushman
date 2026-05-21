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
    [Header("Arena — 2 floats (toCenter x/y, normalized) + 1 float (ring radius)")]
    public bool arenaBounds = true;

    [Header("Opponent Observations (per opponent)")]
    public bool opponentPosition = true;  // 2 floats
    public bool opponentVelocity = true;  // 2 floats
    public bool opponentState = true;     // 1 float

    [Header("Profile B — Field of View Restriction")]
    [Tooltip("If true, opponents outside the FOV cone are padded with zeros instead of real data.")]
    public bool useFOV = false;
    [Range(10f, 360f)] public float fieldOfViewAngle = 120f;

    [Header("Self Stats — 5 floats (weight, speed, pushForce, dodgeForce, maxStamina — normalized)")]
    [Tooltip("Lets the agent observe its own CharacterStats so one network generalises across " +
             "DefaultStats / Heavyweight / Speedster without retraining. Adds 5 obs.")]
    public bool selfStats = true;

    [Header("Opponent Stats — 5 floats per opponent")]
    [Tooltip("Observe the opponent's CharacterStats. Adds 5 obs per opponent. Off by default — " +
             "useful for Phase 3 specialist training.")]
    public bool opponentStats = false;

    [Header("Profile C — Humanized (stub, not yet implemented)")]
    [Tooltip("Applies Gaussian noise to opponent position/velocity observations.")]
    public bool applyNoise = false;
    public float positionNoiseMagnitude = 0.05f;
    [Tooltip("Feeds observations from N frames ago to simulate human reaction time.")]
    public int observationDelayFrames = 0;

    // Compute total observation space size for this profile given an opponent count.
    public int ComputeSpaceSize(int opponentCount)
    {
        int total = 0;
        if (selfKinematics) total += 3;
        if (selfStamina)    total += 1;
        if (selfState)      total += 1;
        if (arenaBounds)    total += 3; // toCenter(2) + ringRadius(1)
        if (selfStats)      total += 5; // weight, speed, pushForce, dodgeForce, maxStamina
        int perOpp = 0;
        if (opponentPosition) perOpp += 2;
        if (opponentVelocity) perOpp += 2;
        if (opponentState)    perOpp += 1;
        if (opponentStats)    perOpp += 5;
        total += perOpp * opponentCount;
        return total;
    }
}
