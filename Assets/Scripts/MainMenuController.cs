using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Builds and drives the Main Menu UI at runtime.
/// Attach to the Canvas GameObject in the MainMenu scene.
/// Pushman/12a wires all SO references via the CardDef arrays.
///
/// Layout (1920×1080 reference):
///   Header  (90px)   — title + subtitle
///   Divider (1px)
///   Content (flex)   — [Player column 360px] | vsep | [Opponent area flex]
///   Divider (1px)
///   Footer  (90px)   — PLAY button
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Card definition
    // -----------------------------------------------------------------------
    [System.Serializable]
    public struct CardDef
    {
        public string label;
        [TextArea(1, 3)] public string description;
        public Color  accentColor;
        public UnityEngine.Object data;   // CharacterStats or BotPersonality SO
        public float  floatValue;          // runtimeHumanization for difficulty cards
    }

    // -----------------------------------------------------------------------
    // Inspector fields (wired by Pushman/12a)
    // -----------------------------------------------------------------------
    [Header("Card Data")]
    public CardDef[] playerCharCards;
    public CardDef[] personalityCards;
    public CardDef[] difficultyCards;
    public CardDef[] opponentCharCards;

    [Header("Fonts  (null → TMP default)")]
    public TMP_FontAsset fontTitle;
    public TMP_FontAsset fontBody;

    [Header("Navigation")]
    public string gameSceneName = "Game";

    // -----------------------------------------------------------------------
    // ContentKit palette
    // -----------------------------------------------------------------------
    static readonly Color kVoid       = new Color(0.067f, 0.063f, 0.035f); // #111009
    static readonly Color kSurface    = new Color(0.118f, 0.110f, 0.086f); // #1E1C16
    static readonly Color kSurfaceHov = new Color(0.160f, 0.150f, 0.118f);
    static readonly Color kTextBright = new Color(0.910f, 0.894f, 0.847f); // #E8E4D8
    static readonly Color kTextMuted  = new Color(0.416f, 0.388f, 0.345f); // #6A6358
    static readonly Color kMutedLine  = new Color(0.22f,  0.20f,  0.15f,  1f);

    // Category accent colours
    static readonly Color kAmber = new Color(0.910f, 0.753f, 0.408f); // #E8C068
    static readonly Color kMauve = new Color(0.722f, 0.596f, 0.800f); // #B898CC
    static readonly Color kSteel = new Color(0.604f, 0.667f, 0.733f); // #9AAABB
    static readonly Color kTerra = new Color(0.863f, 0.596f, 0.471f); // #DC9878

    // -----------------------------------------------------------------------
    // Runtime selection state
    // -----------------------------------------------------------------------
    int _playerCharIdx   = 0;
    int _personalityIdx  = 0;
    int _difficultyIdx   = 1;  // default Hard
    int _opponentCharIdx = 0;

    SelectionCard[] _playerCharGroup;
    SelectionCard[] _personalityGroup;
    SelectionCard[] _difficultyGroup;
    SelectionCard[] _opponentCharGroup;

    // -----------------------------------------------------------------------

    void Start()
    {
        if (playerCharCards   != null) _playerCharIdx   = Mathf.Clamp(_playerCharIdx,   0, playerCharCards.Length   - 1);
        if (personalityCards  != null) _personalityIdx  = Mathf.Clamp(_personalityIdx,  0, personalityCards.Length  - 1);
        if (difficultyCards   != null) _difficultyIdx   = Mathf.Clamp(_difficultyIdx,   0, difficultyCards.Length   - 1);
        if (opponentCharCards != null) _opponentCharIdx = Mathf.Clamp(_opponentCharIdx, 0, opponentCharCards.Length - 1);

        BuildUI();

        SelectInGroup(_playerCharGroup,   _playerCharIdx);
        SelectInGroup(_personalityGroup,  _personalityIdx);
        SelectInGroup(_difficultyGroup,   _difficultyIdx);
        SelectInGroup(_opponentCharGroup, _opponentCharIdx);
    }

    // -----------------------------------------------------------------------
    // UI builder
    // -----------------------------------------------------------------------

    void BuildUI()
    {
        // Ensure Canvas / Scaler / Raycaster on this GO
        var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;

        var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        var root = transform;

        // Full-screen Void background
        NewFullRect("BG", root).AddComponent<Image>().color = kVoid;

        // Root VerticalLayoutGroup — fills canvas, stacks header/content/footer
        var rootGO = NewFullRect("Root", root);
        var rootVL = rootGO.AddComponent<VerticalLayoutGroup>();
        rootVL.childControlWidth      = true;
        rootVL.childControlHeight     = true;
        rootVL.childForceExpandWidth  = true;
        rootVL.childForceExpandHeight = false;
        rootVL.spacing = 0f;

        // ── Header ────────────────────────────────────────────────────────
        var header = NewGO("Header", rootGO.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 90f;
        header.AddComponent<Image>().color = new Color(0.082f, 0.077f, 0.043f);

        // Inner VLayout so title+subtitle stack naturally
        var hdrVL = header.AddComponent<VerticalLayoutGroup>();
        hdrVL.childAlignment       = TextAnchor.MiddleCenter;
        hdrVL.childControlWidth    = true;
        hdrVL.childControlHeight   = true;     // must be true so children's preferredHeight applies
        hdrVL.childForceExpandWidth  = true;
        hdrVL.childForceExpandHeight = false;
        hdrVL.padding = new RectOffset(0, 0, 12, 10);
        hdrVL.spacing = 2f;

        var title = NewTMP(header.transform, "Title", "PUSHMAN",
            38f, kTextBright, fontTitle, FontStyles.Bold, TextAlignmentOptions.Center);
        title.characterSpacing = 12f;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

        var sub = NewTMP(header.transform, "Sub", "CHOOSE YOUR FIGHTERS",
            11f, kTextMuted, fontBody, FontStyles.Normal, TextAlignmentOptions.Center);
        sub.characterSpacing = 5f;
        sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

        HLine(rootGO.transform);

        // ── Content ───────────────────────────────────────────────────────
        var content = NewGO("Content", rootGO.transform);
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var contentHL = content.AddComponent<HorizontalLayoutGroup>();
        contentHL.padding = new RectOffset(40, 40, 24, 20);
        contentHL.spacing = 0f;
        contentHL.childControlWidth      = true;
        contentHL.childControlHeight     = true;
        contentHL.childForceExpandWidth  = false;
        contentHL.childForceExpandHeight = true;

        // Player column (fixed 360px)
        var playerCol = NewGO("PlayerCol", content.transform);
        playerCol.AddComponent<LayoutElement>().preferredWidth = 360f;
        BuildPlayerColumn(playerCol.transform);

        // Vertical separator
        var vsep = NewGO("VSep", content.transform);
        var vsepLE = vsep.AddComponent<LayoutElement>();
        vsepLE.preferredWidth  = 1f;
        vsepLE.flexibleWidth   = 0f;
        vsep.AddComponent<Image>().color = kMutedLine;

        // Spacer left of opponent column
        var oppPad = NewGO("OppPad", content.transform);
        oppPad.AddComponent<LayoutElement>().preferredWidth = 40f;

        // Opponent column (fills remaining space)
        var oppCol = NewGO("OppCol", content.transform);
        oppCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        BuildOpponentColumn(oppCol.transform);

        HLine(rootGO.transform);

        // ── Footer ────────────────────────────────────────────────────────
        var footer = NewGO("Footer", rootGO.transform);
        footer.AddComponent<LayoutElement>().preferredHeight = 90f;
        BuildFooter(footer.transform);
    }

    // -----------------------------------------------------------------------
    // Column builders
    // -----------------------------------------------------------------------

    void BuildPlayerColumn(Transform parent)
    {
        var vl = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 10f;
        vl.childControlWidth      = true;
        vl.childControlHeight     = true;   // must be true so LayoutElement.preferredHeight is obeyed
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;

        SectionLabel(parent, "YOUR CHARACTER", kAmber);

        _playerCharGroup = BuildCardStack(parent, playerCharCards, 96f,
            idx => { _playerCharIdx = idx; SelectInGroup(_playerCharGroup, idx); });

        // Push cards to top
        var flex = NewGO("Flex", parent);
        flex.AddComponent<LayoutElement>().flexibleHeight = 1f;
    }

    void BuildOpponentColumn(Transform parent)
    {
        var vl = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 14f;
        vl.childControlWidth      = true;
        vl.childControlHeight     = true;   // must be true so LayoutElement.preferredHeight is obeyed
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;

        // — Personality —
        SectionLabel(parent, "OPPONENT STYLE", kMauve);
        _personalityGroup = BuildCardRow(parent, personalityCards, 88f,
            idx => { _personalityIdx = idx; SelectInGroup(_personalityGroup, idx); });

        SubDivider(parent);

        // — Difficulty —
        SectionLabel(parent, "DIFFICULTY", kSteel);
        _difficultyGroup = BuildCardRow(parent, difficultyCards, 72f,
            idx => { _difficultyIdx = idx; SelectInGroup(_difficultyGroup, idx); });

        SubDivider(parent);

        // — Opponent Character —
        SectionLabel(parent, "OPPONENT CHARACTER", kTerra);
        _opponentCharGroup = BuildCardRow(parent, opponentCharCards, 88f,
            idx => { _opponentCharIdx = idx; SelectInGroup(_opponentCharGroup, idx); });

        var flex = NewGO("Flex", parent);
        flex.AddComponent<LayoutElement>().flexibleHeight = 1f;
    }

    void BuildFooter(Transform parent)
    {
        var hl = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment       = TextAnchor.MiddleCenter;
        hl.childControlWidth    = false;
        hl.childControlHeight   = false;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;

        // PLAY button (NewGO already added a RectTransform — just grab it)
        var btnGO = NewGO("PlayBtn", parent);
        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(260f, 52f);
        btnGO.AddComponent<LayoutElement>().preferredWidth = 260f;

        var bg = btnGO.AddComponent<Image>();
        bg.color = kAmber;

        var btn = btnGO.AddComponent<Button>();
        var cols = btn.colors;
        cols.normalColor      = Color.white;
        cols.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
        cols.pressedColor     = new Color(0.78f, 0.78f, 0.78f);
        btn.colors        = cols;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OnPlay);

        var lbl = NewTMP(btnGO.transform, "Label", "PLAY",
            22f, kVoid, fontTitle, FontStyles.Bold, TextAlignmentOptions.Center);
        lbl.characterSpacing = 10f;
        FillRect(lbl.rectTransform);
    }

    // -----------------------------------------------------------------------
    // Card group builders
    // -----------------------------------------------------------------------

    /// <summary>Vertical stack — used for the player character column.</summary>
    SelectionCard[] BuildCardStack(Transform parent, CardDef[] defs, float cardHeight,
                                   System.Action<int> onSelect)
    {
        if (defs == null || defs.Length == 0) return new SelectionCard[0];
        var list = new List<SelectionCard>();
        for (int i = 0; i < defs.Length; i++)
        {
            var card = CreateCard(parent, defs[i], cardHeight, expand: true);
            int ci = i;
            card.Clicked += _ => onSelect(ci);
            list.Add(card);
        }
        return list.ToArray();
    }

    /// <summary>Horizontal row — used for personality, difficulty, opponent char.</summary>
    SelectionCard[] BuildCardRow(Transform parent, CardDef[] defs, float cardHeight,
                                 System.Action<int> onSelect)
    {
        if (defs == null || defs.Length == 0) return new SelectionCard[0];

        var rowGO = NewGO("Row", parent);
        var rowLE = rowGO.AddComponent<LayoutElement>();
        rowLE.preferredHeight = cardHeight;
        var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10f;
        hl.childControlWidth      = true;
        hl.childControlHeight     = true;
        hl.childForceExpandWidth  = true;
        hl.childForceExpandHeight = true;

        var list = new List<SelectionCard>();
        for (int i = 0; i < defs.Length; i++)
        {
            var card = CreateCard(rowGO.transform, defs[i], cardHeight, expand: false);
            int ci = i;
            card.Clicked += _ => onSelect(ci);
            list.Add(card);
        }
        return list.ToArray();
    }

    // -----------------------------------------------------------------------
    // Card creation
    // -----------------------------------------------------------------------

    SelectionCard CreateCard(Transform parent, CardDef def, float height, bool expand)
    {
        var cardGO = NewGO($"Card_{def.label}", parent);
        var cardLE = cardGO.AddComponent<LayoutElement>();
        cardLE.preferredHeight = height;
        if (!expand) cardLE.flexibleWidth = 1f;

        var card = cardGO.AddComponent<SelectionCard>();
        card.accentColor = def.accentColor;
        card.data        = def.data;
        card.floatValue  = def.floatValue;
        card.cardLabel   = def.label;

        // Border (full card)
        var borderGO = NewGO("Border", cardGO.transform);
        var borderImg = borderGO.AddComponent<Image>();
        FillRect(borderImg.rectTransform);

        // Body (inset 2px)
        var bodyGO = NewGO("Body", cardGO.transform);
        var bodyImg = bodyGO.AddComponent<Image>();
        FillRect(bodyImg.rectTransform);
        bodyImg.rectTransform.offsetMin = new Vector2( 2f,  2f);
        bodyImg.rectTransform.offsetMax = new Vector2(-2f, -2f);

        // Inner content — VerticalLayoutGroup for clean text stacking
        // (NewGO already adds RectTransform — don't add a second one)
        var contentGO = NewGO("Content", cardGO.transform);
        var contentRT = contentGO.GetComponent<RectTransform>();
        FillRect(contentRT);
        contentRT.offsetMin = new Vector2(4f, 4f);
        contentRT.offsetMax = new Vector2(-4f, -4f);
        var contentVL = contentGO.AddComponent<VerticalLayoutGroup>();
        contentVL.childAlignment      = TextAnchor.MiddleCenter;
        contentVL.childControlWidth   = true;
        contentVL.childControlHeight  = true;
        contentVL.childForceExpandWidth  = true;
        contentVL.childForceExpandHeight = false;
        contentVL.spacing = 2f;

        // Label
        var labelTMP = NewTMP(contentGO.transform, "Label", def.label.ToUpperInvariant(),
            14f, def.accentColor, fontBody, FontStyles.Bold, TextAlignmentOptions.Center);
        labelTMP.characterSpacing = 2f;
        labelTMP.textWrappingMode = TextWrappingModes.NoWrap;
        labelTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        // Description
        TextMeshProUGUI descTMP = null;
        if (!string.IsNullOrEmpty(def.description))
        {
            descTMP = NewTMP(contentGO.transform, "Desc", def.description,
                10f, kTextMuted, fontBody, FontStyles.Normal, TextAlignmentOptions.Center);
            descTMP.textWrappingMode = TextWrappingModes.Normal;
            descTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        }

        card.Init(bodyImg, borderImg, labelTMP, descTMP);
        return card;
    }

    // -----------------------------------------------------------------------
    // Play action
    // -----------------------------------------------------------------------

    void OnPlay()
    {
        CharacterStats playerChar = playerCharCards != null && playerCharCards.Length > 0
            ? playerCharCards[_playerCharIdx].data as CharacterStats : null;
        CharacterStats oppChar = opponentCharCards != null && opponentCharCards.Length > 0
            ? opponentCharCards[_opponentCharIdx].data as CharacterStats : null;
        BotPersonality oppPersonality = personalityCards != null && personalityCards.Length > 0
            ? personalityCards[_personalityIdx].data as BotPersonality : null;
        float oppDifficulty = difficultyCards != null && difficultyCards.Length > 0
            ? difficultyCards[_difficultyIdx].floatValue : 0.33f;

        GameConfig.GetOrCreate().Apply(playerChar, oppChar, oppPersonality, oppDifficulty);
        SceneManager.LoadScene(gameSceneName);
    }

    // -----------------------------------------------------------------------
    // Selection helpers
    // -----------------------------------------------------------------------

    static void SelectInGroup(SelectionCard[] group, int idx)
    {
        if (group == null) return;
        for (int i = 0; i < group.Length; i++)
            group[i]?.SetSelected(i == idx);
    }

    // -----------------------------------------------------------------------
    // Layout / UI primitives
    // -----------------------------------------------------------------------

    static GameObject NewGO(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    /// <summary>Creates a child GO with a stretch-fill RectTransform.</summary>
    static GameObject NewFullRect(string name, Transform parent)
    {
        var go = NewGO(name, parent);
        FillRect(go.GetComponent<RectTransform>());
        return go;
    }

    static void FillRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Thin horizontal separator line managed by a LayoutGroup parent.</summary>
    void HLine(Transform parent)
    {
        var go = NewGO("HLine", parent);
        go.AddComponent<LayoutElement>().preferredHeight = 1f;
        go.AddComponent<Image>().color = kMutedLine;
    }

    /// <summary>Thin sub-divider between opponent sections.</summary>
    void SubDivider(Transform parent)
    {
        var go = NewGO("SubDiv", parent);
        go.AddComponent<LayoutElement>().preferredHeight = 1f;
        go.AddComponent<Image>().color = new Color(kMutedLine.r, kMutedLine.g, kMutedLine.b, 0.5f);
    }

    /// <summary>Section header label (e.g. "YOUR CHARACTER").</summary>
    void SectionLabel(Transform parent, string text, Color color)
    {
        var tmp = NewTMP(parent, "Lbl_" + text, text,
            10f, color, fontBody, FontStyles.Bold, TextAlignmentOptions.Left);
        tmp.characterSpacing  = 4f;
        tmp.textWrappingMode  = TextWrappingModes.NoWrap;
        var le = tmp.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 20f;
    }

    TextMeshProUGUI NewTMP(Transform parent, string name, string text,
        float size, Color color, TMP_FontAsset font,
        FontStyles style, TextAlignmentOptions align)
    {
        var go  = NewGO(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.overflowMode     = TextOverflowModes.Ellipsis;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        if (font != null)
            tmp.font = font;
        else
        {
            // Always ensure a font — TMP throws a mesh error if none is set
            var fallback = TMP_Settings.defaultFontAsset;
            if (fallback != null) tmp.font = fallback;
        }
        return tmp;
    }
}
