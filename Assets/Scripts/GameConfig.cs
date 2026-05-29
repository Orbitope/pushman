using UnityEngine;

/// <summary>
/// Persistent singleton that carries menu selections across the MainMenu → Game scene load.
/// Created once on first use; survives scene transitions via DontDestroyOnLoad.
/// Read by GameSceneController on Game scene start to configure players.
/// </summary>
public class GameConfig : MonoBehaviour
{
    public static GameConfig Current { get; private set; }

    [Header("Player")]
    public CharacterStats playerCharacter;

    [Header("Opponent")]
    public CharacterStats  opponentCharacter;
    public BotPersonality  opponentPersonality;
    [Range(0f, 1f)]
    public float           opponentDifficulty;   // 0 = Expert, 1 = Easy (= runtimeHumanization)

    [Header("Series")]
    public int seriesLength = 3;                 // first to Ceil(seriesLength / 2) wins

    public int WinsRequired => Mathf.CeilToInt(seriesLength / 2f);

    void Awake()
    {
        if (Current != null && Current != this) { Destroy(gameObject); return; }
        Current = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Called by MainMenuController just before loading the Game scene.</summary>
    public void Apply(CharacterStats playerChar, CharacterStats oppChar,
                      BotPersonality oppPersonality, float oppDifficulty)
    {
        playerCharacter    = playerChar;
        opponentCharacter  = oppChar;
        opponentPersonality = oppPersonality;
        opponentDifficulty = oppDifficulty;
    }

    /// <summary>Returns a GameConfig singleton, creating it if needed.</summary>
    public static GameConfig GetOrCreate()
    {
        if (Current != null) return Current;
        var go = new GameObject("GameConfig");
        return go.AddComponent<GameConfig>();
    }
}
