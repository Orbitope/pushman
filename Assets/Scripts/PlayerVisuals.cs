using UnityEngine;

/// <summary>
/// Manages hand sprite visibility based on the player's combat state.
/// Attach to any Player game object. Wire up pushHand and blockHand
/// via Inspector or the PushmanSetup editor menu item.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerVisuals : MonoBehaviour
{
    [Header("Hand Sprites")]
    [Tooltip("Shown while Charging or Pushing.")]
    public SpriteRenderer pushHand;

    [Tooltip("Shown while Blocking.")]
    public SpriteRenderer blockHand;

    private Player player;

    void Awake()
    {
        player = GetComponent<Player>();

        // Auto-discover hand sprites from named children if not wired via Inspector.
        if (pushHand == null)
        {
            var t = transform.Find("PushHand");
            if (t != null) pushHand = t.GetComponent<SpriteRenderer>();
        }
        if (blockHand == null)
        {
            var t = transform.Find("BlockHand");
            if (t != null) blockHand = t.GetComponent<SpriteRenderer>();
        }
    }

    void Update()
    {
        if (player == null) return;

        bool showPush  = player.currentState == Player.PlayerState.Charging
                      || player.currentState == Player.PlayerState.Pushing;
        bool showBlock = player.currentState == Player.PlayerState.Blocking;

        if (pushHand  != null) pushHand.gameObject.SetActive(showPush);
        if (blockHand != null) blockHand.gameObject.SetActive(showBlock);
    }
}
