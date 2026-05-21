using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space HUD: stamina bars (bottom corners) + round score (top corners).
/// Wired by PushmanSetup — no manual Inspector work required.
/// </summary>
public class StaminaHUD : MonoBehaviour
{
    [Header("Player References")]
    public Player player1;
    public Player player2;

    [Header("Stamina Fill Images")]
    public Image p1Fill;
    public Image p2Fill;

    [Header("Score Text")]
    public Text p1ScoreText;
    public Text p2ScoreText;

    // Base colors set by PushmanSetup; cached so we can tint on low stamina.
    private Color p1BaseColor;
    private Color p2BaseColor;

    // ArenaManager for score reads.
    private ArenaManager arenaManager;

    void Start()
    {
        // Auto-discover players by scene path if not wired in Inspector or by PushmanSetup.
        if (player1 == null)
        {
            var go = GameObject.Find("Arena/Player1");
            if (go != null) player1 = go.GetComponent<Player>();
        }
        if (player2 == null)
        {
            var go = GameObject.Find("Arena/Player2");
            if (go != null) player2 = go.GetComponent<Player>();
        }

        arenaManager = FindFirstObjectByType<ArenaManager>();

        if (p1Fill != null) p1BaseColor = p1Fill.color;
        if (p2Fill != null) p2BaseColor = p2Fill.color;
    }

    void Update()
    {
        UpdateBar(p1Fill, player1, p1BaseColor);
        UpdateBar(p2Fill, player2, p2BaseColor);
        UpdateScores();
    }

    // Exposed for other scripts (e.g. CameraShake, VFX) that want to react to overdraft state.
    public bool Player1RegenPaused => player1 != null && player1.RegenPaused;
    public bool Player2RegenPaused => player2 != null && player2.RegenPaused;

    private void UpdateScores()
    {
        if (arenaManager == null || (p1ScoreText == null && p2ScoreText == null)) return;
        if (p1ScoreText != null) p1ScoreText.text = arenaManager.GetScore(0).ToString();
        if (p2ScoreText != null) p2ScoreText.text = arenaManager.GetScore(1).ToString();
    }

    private static void UpdateBar(Image fill, Player player, Color baseColor)
    {
        if (fill == null || player == null || player.stats == null) return;

        // Clamp for display — stamina can go negative during overdraft dodge.
        float ratio = Mathf.Clamp01(player.currentStamina / player.stats.maxStamina);

        // Smooth lerp toward target — snappy enough to feel responsive, slow enough to read.
        fill.fillAmount = Mathf.Lerp(fill.fillAmount, ratio, Time.deltaTime * 8f);

        if (player.RegenPaused)
        {
            // Pulsing dark red ↔ gray: overdraft lockout — regen is completely blocked.
            float pulse = Mathf.PingPong(Time.time * 3f, 1f);
            fill.color = Color.Lerp(new Color(0.55f, 0.08f, 0.08f), new Color(0.30f, 0.30f, 0.30f), pulse);
        }
        else if (ratio < 0.25f)
        {
            // Orange-red flash when below 25% stamina (but regen is still working).
            fill.color = Color.Lerp(new Color(1f, 0.25f, 0.1f), baseColor, ratio / 0.25f);
        }
        else
        {
            fill.color = baseColor;
        }
    }
}
