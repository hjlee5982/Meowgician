#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 개발 전용 UI(밸런싱 툴)를 코드로 만드는 작은 도우미. 릴리스 빌드에는 포함되지 않는다.
/// 옵션/아이템 이름이 한글이라 TextMeshPro를 쓰고, 게임에서 이미 쓰는 한글 폰트 에셋을 재사용한다.
/// </summary>
internal static class DevUi
{
    public static readonly Color Accent = new Color(0.20f, 0.35f, 0.60f, 1f);
    public static readonly Color Minus = new Color(0.62f, 0.30f, 0.30f, 1f);
    public static readonly Color Plus = new Color(0.28f, 0.55f, 0.35f, 1f);
    public static readonly Color Neutral = new Color(0.28f, 0.32f, 0.40f, 1f);
    public static readonly Color Warn = new Color(1f, 0.82f, 0.45f, 1f);
    public static readonly Color Good = new Color(0.7f, 1f, 0.7f, 1f);
    public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.07f);

    private static TMP_FontAsset font;
    private static Font legacyFont;

    // ---------------------------------------------------------------- 폰트
    /// <summary>씬에 이미 있는 TMP 텍스트 중 한글이 들어 있는 것의 폰트를 찾아 둔다.</summary>
    public static void ResolveFont()
    {
        if (font != null) return;

        TMP_FontAsset fallback = null;
        foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.font == null) continue;
            if (fallback == null) fallback = t.font;
            if (ContainsHangul(t.text))
            {
                font = t.font;
                return;
            }
        }

        font = fallback;
    }

    private static bool ContainsHangul(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
            if (text[i] >= 0xAC00 && text[i] <= 0xD7A3) return true;

        return false;
    }

    public static Font LegacyFont
    {
        get
        {
            if (legacyFont == null)
                legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return legacyFont;
        }
    }

    // ---------------------------------------------------------------- 기본 요소
    public static void Stretch(RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        ResolveFont();

        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>레이아웃 그룹 안에서 쓸 크기를 지정한다. 음수는 "지정 안 함".</summary>
    public static LayoutElement Layout(Component c, float width = -1f, float height = -1f, float flexibleWidth = -1f)
    {
        LayoutElement le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();

        if (width >= 0f)
        {
            le.minWidth = width;
            le.preferredWidth = width;
        }

        if (height >= 0f)
        {
            le.minHeight = height;
            le.preferredHeight = height;
        }

        if (flexibleWidth >= 0f) le.flexibleWidth = flexibleWidth;
        return le;
    }

    /// <summary>섹션 제목 줄.</summary>
    public static TextMeshProUGUI Header(Transform parent, string text)
    {
        TextMeshProUGUI t = Text(parent, text, 28, Warn);
        Layout(t, height: 52f);
        return t;
    }

    /// <summary>설명 문장(회색, 여러 줄 가능).</summary>
    public static TextMeshProUGUI Note(Transform parent, string text, float height = 64f)
    {
        TextMeshProUGUI t = Text(parent, text, 22, new Color(0.75f, 0.75f, 0.8f, 1f));
        Layout(t, height: height);
        return t;
    }

    public static Button Button(Transform parent, string label, Color color, float fontSize, UnityAction onClick,
        float width = -1f, float height = 76f)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = color;

        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        ColorBlock colors = b.colors;
        colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        b.colors = colors;

        LayoutElement le = Layout(b, width: width, height: height);
        if (width < 0f) le.flexibleWidth = 1f;

        TextMeshProUGUI t = Text(go.transform, label, fontSize, Color.white, TextAlignmentOptions.Center);
        Stretch(t.rectTransform, 4f, 4f, 2f, 2f);

        if (onClick != null) b.onClick.AddListener(onClick);
        return b;
    }

    public static void SetLabel(Button b, string text)
    {
        if (b == null) return;
        TextMeshProUGUI t = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (t != null) t.text = text;
    }

    /// <summary>가로 한 줄(버튼/라벨을 나란히 놓는 용도).</summary>
    public static RectTransform Row(Transform parent, float height, bool background = false)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var h = go.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.padding = new RectOffset(10, 10, 4, 4);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;

        if (background)
        {
            var img = go.AddComponent<Image>();
            img.color = RowBg;
            img.raycastTarget = false;
        }

        return (RectTransform)go.transform;
    }

    /// <summary>숫자만 입력하는 칸(레거시 InputField, 숫자라서 기본 폰트로 충분하다).</summary>
    public static InputField IntInput(Transform parent, string initial, float width, float height, int charLimit)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.18f, 1f);
        Layout(go.GetComponent<InputField>(), width: width, height: height);

        Text text = NewLegacyText(go.transform, "Text", 38, Color.white, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 8f, 8f, 4f, 4f);
        text.supportRichText = false;

        Text placeholder = NewLegacyText(go.transform, "Placeholder", 32, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter);
        Stretch(placeholder.rectTransform, 8f, 8f, 4f, 4f);
        placeholder.text = "1-100";

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = InputField.ContentType.IntegerNumber;
        input.characterLimit = charLimit;
        input.text = initial;
        return input;
    }

    private static Text NewLegacyText(Transform parent, string name, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = LegacyFont;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.raycastTarget = false;
        return t;
    }

    // ---------------------------------------------------------------- 스크롤
    /// <summary>세로 스크롤 영역을 만들고 내용물을 담을 Content를 돌려준다. Content는 세로 레이아웃이다.</summary>
    public static ScrollRect Scroll(Transform parent, out RectTransform content)
    {
        var go = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);

        // 거의 투명하지만 레이캐스트는 받아서 빈 곳을 드래그해도 스크롤된다.
        var bg = go.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.01f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(go.transform, false);
        Stretch((RectTransform)viewport.transform);

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 24);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = go.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = content;
        return scroll;
    }

    /// <summary>자식을 모두 지운다(비활성화 후 Destroy 해서 같은 프레임의 레이아웃에서 빠지게 한다).</summary>
    public static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            Object.Destroy(child);
        }
    }
}
#endif
