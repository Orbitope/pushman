using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// Tracks best-of-N series wins and shows overlays between rounds and at series end.
/// Attach alongside ArenaManager in the Game scene (Pushman/12b wires everything).
///
/// Flow:
///   ArenaManager.OnRoundEnd fires → SeriesManager increments wins.
///   If someone reaches WinsRequired: set autoRestart=false, show end panel.
///   Otherwise: show brief "Player X wins the round" overlay (auto-hides after 2s).
///   Rematch button resets scores and calls arenaManager.StartNewSeries().
///   Menu button loads the MainMenu scene.
/// </summary>
public class SeriesManager : MonoBehaviour
{
    [Header("References")]
    public ArenaManager arenaManager;

    [Header("Navigation")]
    public string mainMenuSceneName = "MainMenu";

    // -----------------------------------------------------------------------
    // ContentKit palette
    // -----------------------------------------------------------------------
    static readonly Color kVoid       = new Color(0.067f, 0.063f, 0.035f);
    static readonly Color kSurface    = new Color(0.118f, 0.110f, 0.086f);
    static readonly Color kTextBright = new Color(0.910f, 0.894f, 0.847f);
    static readonly Color kTextMuted  = new Color(0.416f, 0.388f, 0.345f);
    static readonly Color kAmber      = new Color(0.910f, 0.753f, 0.408f);
    static readonly Color kSteel      = new Color(0.604f, 0.667f, 0.733f);

    // -----------------------------------------------------------------------
    // Runtime state
    // -----------------------------------------------------------------------
    int   _winsRequired;
    int[] _wins = new int[2];

    // -----------------------------------------------------------------------
    // Overlay GameObjects
    // -----------------------------------------------------------------------
    Canvas          _overlayCanvas;
    GameObject      _roundPanel;
    TextMeshProUGUI _roundResultText;
    TextMeshProUGUI _seriesScoreText;
    GameObject      _endPanel;
    TextMeshProUGUI _endTitleText;
    TextMeshProUGUI _endScoreText;

    // -----------------------------------------------------------------------

    void Awake()
    {
        // Read series length from GameConfig if available; default best-of-3
        int seriesLength = GameConfig.Current != null ? GameConfig.Current.seriesLength : 3;
        _winsRequired = Mathf.CeilToInt(seriesLength / 2f);
    }

    void Start()
    {
        if (arenaManager == null)
        {
            arenaManager = FindAnyObjectByType<ArenaManager>();
            if (arenaManager == null)
            {
                Debug.LogError("[SeriesManager] No ArenaManager found.");
                return;
            }
        }

        _wins = new int[2];
        arenaManager.OnRoundEnd += HandleRoundEnd;
        BuildOverlay();
    }

    void OnDestroy()
    {
        if (arenaManager != null)
            arenaManager.OnRoundEnd -= HandleRoundEnd;
    }

    // -----------------------------------------------------------------------
    // Round-end handler (called synchronously by ArenaManager before StartMatch)
    // -----------------------------------------------------------------------

    void HandleRoundEnd(int winnerIndex)
    {
        // winnerIndex: 0 = P1, 1 = P2, -1 = tie / no winner
        if (winnerIndex >= 0 && winnerIndex < _wins.Length)
            _wins[winnerIndex]++;

        bool seriesOver = _wins[0] >= _winsRequired || _wins[1] >= _winsRequired;

        if (seriesOver)
        {
            // Stop auto-restart before the coroutine yields to StartMatch
            arenaManager.autoRestart = false;
            ShowEndPanel(winnerIndex);
        }
        else
        {
            StartCoroutine(ShowRoundBanner(winnerIndex));
        }
    }

    // -----------------------------------------------------------------------
    // Round banner (auto-dismisses)
    // -----------------------------------------------------------------------

    IEnumerator ShowRoundBanner(int winnerIndex)
    {
        string winnerName = winnerIndex == 0 ? "PLAYER 1" : "PLAYER 2";
        Color  winnerColor = winnerIndex == 0 ? kAmber : kSteel;

        _roundResultText.text  = winnerIndex >= 0 ? $"{winnerName} WINS THE ROUND" : "ROUND DRAW";
        _roundResultText.color = winnerIndex >= 0 ? winnerColor : kTextMuted;
        _seriesScoreText.text  = $"{_wins[0]}  ·  {_wins[1]}";
        _roundPanel.SetActive(true);

        yield return new WaitForSecondsRealtime(2.2f);

        _roundPanel.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // Series-end panel
    // -----------------------------------------------------------------------

    void ShowEndPanel(int winnerIndex)
    {
        int seriesWinner = _wins[0] >= _winsRequired ? 0
                         : _wins[1] >= _winsRequired ? 1
                         : -1;
        string title = seriesWinner == 0 ? "PLAYER 1 WINS!"
                     : seriesWinner == 1 ? "PLAYER 2 WINS!"
                     :                     "SERIES DRAW";
        Color titleColor = seriesWinner == 0 ? kAmber
                         : seriesWinner == 1 ? kSteel
                         : kTextMuted;

        _endTitleText.text  = title;
        _endTitleText.color = titleColor;
        _endScoreText.text  = $"SERIES  {_wins[0]}  ·  {_wins[1]}";
        _endPanel.SetActive(true);
    }

    // -----------------------------------------------------------------------
    // Button callbacks
    // -----------------------------------------------------------------------

    public void OnRematch()
    {
        _wins[0]  = 0;
        _wins[1]  = 0;
        _endPanel.SetActive(false);
        _roundPanel.SetActive(false);
        if (arenaManager == null)
        {
            Debug.LogError("[SeriesManager] arenaManager is null — cannot restart.");
            return;
        }
        arenaManager.StartNewSeries();
    }

    public void OnMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // -----------------------------------------------------------------------
    // Overlay builder
    // -----------------------------------------------------------------------

    void BuildOverlay()
    {
        // Separate canvas so it draws above the game canvas
        var overlayGO = new GameObject("SeriesOverlay");
        overlayGO.transform.SetParent(transform);
        _overlayCanvas = overlayGO.AddComponent<Canvas>();
        _overlayCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 20;
        var scaler = overlayGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        overlayGO.AddComponent<GraphicRaycaster>();

        // ── Round banner ──────────────────────────────────────────────────
        _roundPanel = new GameObject("RoundBanner");
        _roundPanel.transform.SetParent(overlayGO.transform, false);
        var roundPanelRT = _roundPanel.AddComponent<RectTransform>();
        roundPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
        roundPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
        roundPanelRT.pivot     = new Vector2(0.5f, 0.5f);
        roundPanelRT.sizeDelta = new Vector2(560f, 130f);

        // Background
        var roundBg = _roundPanel.AddComponent<Image>();
        roundBg.color = new Color(kSurface.r, kSurface.g, kSurface.b, 0.92f);

        // Thin top accent line
        var accentLine = MakeOverlayRect("AccentLine", _roundPanel.transform);
        accentLine.anchorMin = new Vector2(0f, 1f);
        accentLine.anchorMax = new Vector2(1f, 1f);
        accentLine.pivot     = new Vector2(0.5f, 1f);
        accentLine.sizeDelta = new Vector2(0f, 3f);
        accentLine.gameObject.AddComponent<Image>().color = kAmber;

        _roundResultText = MakeOverlayText(_roundPanel.transform, "RoundResult",
            "PLAYER 1 WINS THE ROUND", 20f, kAmber, FontStyles.Bold);
        _roundResultText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 20f);
        _roundResultText.GetComponent<RectTransform>().sizeDelta        = new Vector2(540f, 36f);
        _roundResultText.characterSpacing = 4f;

        _seriesScoreText = MakeOverlayText(_roundPanel.transform, "SeriesScore",
            "0  ·  0", 32f, kTextBright, FontStyles.Bold);
        _seriesScoreText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);
        _seriesScoreText.GetComponent<RectTransform>().sizeDelta        = new Vector2(540f, 46f);

        _roundPanel.SetActive(false);

        // ── Series-end panel ──────────────────────────────────────────────
        _endPanel = new GameObject("EndPanel");
        _endPanel.transform.SetParent(overlayGO.transform, false);
        var endRT = _endPanel.AddComponent<RectTransform>();
        endRT.anchorMin = new Vector2(0.5f, 0.5f);
        endRT.anchorMax = new Vector2(0.5f, 0.5f);
        endRT.pivot     = new Vector2(0.5f, 0.5f);
        endRT.sizeDelta = new Vector2(680f, 320f);
        var endBg = _endPanel.AddComponent<Image>();
        endBg.color = new Color(kSurface.r, kSurface.g, kSurface.b, 0.97f);

        // Thick top accent bar
        var topBar = MakeOverlayRect("TopBar", _endPanel.transform);
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.pivot     = new Vector2(0.5f, 1f);
        topBar.sizeDelta = new Vector2(0f, 5f);
        topBar.gameObject.AddComponent<Image>().color = kAmber;

        _endTitleText = MakeOverlayText(_endPanel.transform, "EndTitle",
            "PLAYER 1 WINS!", 38f, kAmber, FontStyles.Bold);
        _endTitleText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 80f);
        _endTitleText.GetComponent<RectTransform>().sizeDelta        = new Vector2(640f, 56f);
        _endTitleText.characterSpacing = 5f;

        _endScoreText = MakeOverlayText(_endPanel.transform, "EndScore",
            "SERIES  0  ·  0", 22f, kTextMuted, FontStyles.Normal);
        _endScoreText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 22f);
        _endScoreText.GetComponent<RectTransform>().sizeDelta        = new Vector2(640f, 36f);

        // Rematch button (left, well-separated from menu)
        CreateEndButton(_endPanel.transform, "REMATCH", kAmber,
            new Vector2(-120f, -80f), OnRematch);

        // Menu button (right)
        CreateEndButton(_endPanel.transform, "MAIN MENU", kSteel,
            new Vector2(120f, -80f), OnMainMenu);

        _endPanel.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // End-panel button helper
    // -----------------------------------------------------------------------

    Button CreateEndButton(Transform parent, string label, Color accent,
                           Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        var btnGO = new GameObject($"Btn_{label}");
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = new Vector2(200f, 52f);

        var img = btnGO.AddComponent<Image>();
        img.color = new Color(accent.r, accent.g, accent.b, 0.15f);

        // Border
        var borderGO = new GameObject("Border");
        borderGO.transform.SetParent(btnGO.transform, false);
        var brt = borderGO.AddComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = new Color(accent.r, accent.g, accent.b, 0.5f);
        borderImg.raycastTarget = false;

        var btn = btnGO.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f);
        btn.colors      = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var lblTMP = MakeOverlayText(btnGO.transform, "BtnLabel", label,
            14f, accent, FontStyles.Bold);
        lblTMP.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        lblTMP.GetComponent<RectTransform>().anchorMax = Vector2.one;
        lblTMP.characterSpacing = 4f;

        return btn;
    }

    // -----------------------------------------------------------------------
    // Overlay UI helpers
    // -----------------------------------------------------------------------

    static RectTransform MakeOverlayRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static TextMeshProUGUI MakeOverlayText(Transform parent, string name,
        string content, float size, Color color, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text       = content;
        tmp.fontSize   = size;
        tmp.color      = color;
        tmp.fontStyle  = style;
        tmp.alignment  = TextAlignmentOptions.Center;
        tmp.textWrappingMode   = TextWrappingModes.NoWrap;
        return tmp;
    }
}
