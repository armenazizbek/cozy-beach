using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A text box, native (unity/NATIVE_PLAN.md 2.7, S29): what the page's &lt;input type="text"&gt; built (HTMLInputElement):
/// a borderless TextField on a clear background filling the element, with the placeholder in a faint label over it,
/// so Style.uss draws the box as before. While it has the keyboard the game takes no keys (Page.IsTyping).
/// </summary>
public sealed class UiText : UiNode
{
    private readonly TextField field;
    private readonly Label placeholder;
    private string value = "";

    /// <summary>Runs when the player changes the words (not when code sets them).</summary>
    public Action OnInput;

    public UiText(string classes) : base("input", classes)
    {
        ve.focusable = true;
        field = new TextField { label = "", maxLength = -1, value = "" };
        field.style.flexGrow = 1;
        field.style.marginTop = field.style.marginBottom = field.style.marginLeft = field.style.marginRight = 0;
        field.style.backgroundColor = Color.clear;
        field.style.borderTopWidth = field.style.borderRightWidth = field.style.borderBottomWidth = field.style.borderLeftWidth = 0;
        var inner = field.Q("unity-text-input");
        if (inner != null)
        {
            inner.style.backgroundColor = Color.clear;
            inner.style.borderTopWidth = inner.style.borderRightWidth = inner.style.borderBottomWidth = inner.style.borderLeftWidth = 0;
            inner.style.paddingTop = inner.style.paddingBottom = inner.style.paddingLeft = inner.style.paddingRight = 0;
            inner.style.color = new Color(0.23f, 0.16f, 0.12f, 1f);
        }
        placeholder = new Label("") { pickingMode = PickingMode.Ignore };
        placeholder.style.position = Position.Absolute;
        placeholder.style.left = 12;
        placeholder.style.top = 10;
        placeholder.style.opacity = 0.45f;
        placeholder.style.color = new Color(0.23f, 0.16f, 0.12f, 1f);
        field.RegisterValueChangedCallback(e =>
        {
            value = e.newValue ?? "";
            placeholder.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.Flex : DisplayStyle.None;
            OnInput?.Invoke();
        });
        field.RegisterCallback<FocusInEvent>(_ =>
        {
            MenuNav.FocusNative(this);
            Page.typing = true;
        });
        field.RegisterCallback<FocusOutEvent>(_ => Page.typing = false);
        ve.Add(placeholder);
        ve.Add(field);
        Sync();
    }

    /// <summary>Takes the highlight and the keyboard (as the page's text input did).</summary>
    public override void Focus()
    {
        base.Focus();
        field.Focus();
    }

    /// <summary>The words in the box (setting them does not count as the player typing).</summary>
    public string Value
    {
        get => value;
        set
        {
            this.value = value ?? "";
            Sync();
        }
    }

    public string Placeholder
    {
        get => placeholder.text;
        set { placeholder.text = value ?? ""; Sync(); }
    }

    public int MaxLength
    {
        set => field.maxLength = value > 0 ? value : -1;
    }

    private void Sync()
    {
        if (field.value != value) field.SetValueWithoutNotify(value);
        placeholder.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
