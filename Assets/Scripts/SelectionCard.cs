using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

/// <summary>
/// Reusable selection card for the Main Menu.
/// Shows a label and optional description; highlights on hover/select.
/// Created and initialised by MainMenuController at runtime via Init().
/// </summary>
public class SelectionCard : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // --- Data (set by MainMenuController before calling Init) ---
    [HideInInspector] public Color  accentColor = Color.white;
    [HideInInspector] public UnityEngine.Object data;   // CharacterStats or BotPersonality SO
    [HideInInspector] public float  floatValue;          // runtimeHumanization for difficulty cards
    [HideInInspector] public string cardLabel;

    /// <summary>Fires when the card is clicked; passes itself as argument.</summary>
    public event Action<SelectionCard> Clicked;

    // --- Private visual references (set by Init) ---
    Image           _body;
    Image           _border;
    TextMeshProUGUI _label;
    TextMeshProUGUI _desc;
    bool            _selected;
    bool            _hovered;

    // ContentKit Surface colours
    static readonly Color kSurface    = new Color(0.118f, 0.110f, 0.086f); // #1E1C16
    static readonly Color kSurfaceHov = new Color(0.160f, 0.150f, 0.118f); // slightly lighter

    // -----------------------------------------------------------------------

    /// <summary>
    /// Called by MainMenuController after creating child Images and Texts.
    /// The card is invisible / inert until Init() is called.
    /// </summary>
    public void Init(Image body, Image border, TextMeshProUGUI label, TextMeshProUGUI desc)
    {
        _body   = body;
        _border = border;
        _label  = label;
        _desc   = desc;
        Refresh();
    }

    public bool IsSelected => _selected;

    public void SetSelected(bool value)
    {
        _selected = value;
        Refresh();
    }

    // --- Pointer events -------------------------------------------------

    public void OnPointerClick(PointerEventData e) => Clicked?.Invoke(this);

    public void OnPointerEnter(PointerEventData e) { _hovered = true;  Refresh(); }
    public void OnPointerExit (PointerEventData e) { _hovered = false; Refresh(); }

    // --- Internal -------------------------------------------------------

    void Refresh()
    {
        // Body always stays dark so text remains legible.
        // A subtle tint on hover signals interactivity.
        if (_body != null)
            _body.color = _hovered ? kSurfaceHov : kSurface;

        // Border carries all selection feedback: dim → bright accent.
        if (_border != null)
            _border.color = _selected
                ? accentColor
                : new Color(accentColor.r, accentColor.g, accentColor.b, 0.25f);
    }
}
