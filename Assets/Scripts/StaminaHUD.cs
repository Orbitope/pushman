using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space HUD: reads Player.currentStamina each frame and updates two
/// fill Images positioned in the bottom-left (P1) and bottom-right (P2) corners.
/// Wired by PushmanSetup — no manual Inspector work required.
/// </summary>
public class StaminaHUD : MonoBehaviour
{
    [Header("Player References")]
    public Player player1;
    public Player player2;

    [Header("Fill Images")]
    public Image p1Fill;
    public Image p2Fill;

    // Base colors set by PushmanSetup; cached so we can tint on low stamina.
    private Color p1BaseColor;
    private Color p2BaseColor;

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

        if (p1Fill != null) p1BaseColor = p1Fill.color;
        if (p2Fill != null) p2BaseColor = p2Fill.color;
    }

    void Update()
    {
        UpdateBar(p1Fill, player1, p1BaseColor);
        UpdateBar(p2Fill, player2, p2BaseColor);
    }

    private static void UpdateBar(Image fill, Player player, Color baseColor)
    {
        if (fill == null || player == null || player.stats == null) return;

        float ratio = Mathf.Clamp01(player.currentStamina / player.stats.maxStamina);
        fill.fillAmount = ratio;

        // Orange-red flash when below 25% stamina.
        fill.color = ratio < 0.25f
            ? Color.Lerp(new Color(1f, 0.25f, 0.1f), baseColor, ratio / 0.25f)
            : baseColor;
    }
}
