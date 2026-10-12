using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Inspection mode: shows a document (police report, phone log, bank statement) over the scene,
/// with clickable hotspots. No manual scene setup is needed: the whole panel is built at runtime
/// behind the textbox, so the textbox always stays on top.
///
/// Driven by DialogueManager through the Ink tags in prologue_inspection.ink:
///   #inspect: police_report   -> Open("police_report")
///   click HS_01_A             -> HotspotClicked("HS_01_A") -> Ink knot inspect_01_A
///   #open:INSPECTION-02       -> SwitchTo("INSPECTION-02")
///   #evidence:TIMELINE        -> ShowEvidence("TIMELINE")
///   #complete                 -> Close()
///
/// Document images are loaded from Assets/Resources/Inspection/ (e.g. INSPECTION-02.png),
/// unless a Sprite is assigned directly in the Inspector.
///
/// Hotspot positions are in pixels of a 1240 x 1754 page (A4 at 150 dpi), measured from the
/// TOP-LEFT corner, so they match the measurements from the report image directly.
/// Turn on "Show Hotspot Areas" in the Inspector to see them while adjusting.
/// </summary>
public class InspectionMode : MonoBehaviour
{
    [Serializable]
    public class Hotspot
    {
        [Tooltip("HS_01_A runs the Ink knot inspect_01_A")]
        public string id;
        [Tooltip("x, y, width, height in page pixels, from the TOP-LEFT of the page")]
        public Rect area;

        public Hotspot() { }
        public Hotspot(string id, float x, float y, float w, float h)
        {
            this.id = id;
            area = new Rect(x, y, w, h);
        }
    }

    [Serializable]
    public class Document
    {
        [Tooltip("Used by #open:INSPECTION-02")]
        public string id;
        [Tooltip("Used by #inspect: police_report")]
        public string inkName;
        public string tabLabel;
        [Tooltip("Path inside a Resources folder, without extension")]
        public string resourcePath;
        [Tooltip("Optional. If empty, the image is loaded from resourcePath")]
        public Sprite sprite;
        [Tooltip("Page size the hotspot coordinates are measured in")]
        public Vector2 pageSize = new Vector2(1240, 1754);
        public List<Hotspot> hotspots = new List<Hotspot>();

        [NonSerialized] public bool unlocked;
    }

    [Header("Documents")]
    public List<Document> documents = new List<Document>
    {
        new Document
        {
            id = "INSPECTION-01", inkName = "police_report", tabLabel = "Police Report",
            resourcePath = "Inspection/INSPECTION-01",
            hotspots = new List<Hotspot>
            {
                new Hotspot("HS_01_A",  88, 1048, 1056,  70),  // 3.1 witness statement
                new Hotspot("HS_01_B",  88, 1126, 1056,  70),  // 3.2 family statement
                new Hotspot("HS_01_C",  88, 1256, 1056,  52),  // 4.1 last device activity
                new Hotspot("HS_01_D",  88, 1318, 1056,  52),  // 4.2 mirror purchase
                new Hotspot("HS_01_E",  88, 1380, 1056,  52),  // 4.3 cashier input error
                new Hotspot("HS_01_F",  88, 1564,  717,  36),  // priority: standard
                new Hotspot("HS_01_G", 892,  410,  254, 324),  // photograph
            }
        },
        // Placeholder positions: adjust once the INSPECTION-02 image exists.
        new Document
        {
            id = "INSPECTION-02", inkName = "phone_log", tabLabel = "Device Log",
            resourcePath = "Inspection/INSPECTION-02",
            hotspots = new List<Hotspot>
            {
                new Hotspot("HS_02_A", 88,  400, 1056, 100),   // 21:06 Riverside Station
                new Hotspot("HS_02_B", 88,  520, 1056, 100),   // gap 21:06 - 22:30
                new Hotspot("HS_02_C", 88,  640, 1056, 100),   // 23:40 last registration
                new Hotspot("HS_02_D", 88,  760, 1056, 100),   // nothing after 23:41
                new Hotspot("HS_02_E", 88,  900, 1056, 500),   // map
            }
        },
        new Document
        {
            id = "INSPECTION-03", inkName = "bank_statement", tabLabel = "Bank Statement",
            resourcePath = "Inspection/bank statement",
            pageSize = new Vector2(1240, 1438),
            hotspots = new List<Hotspot>
            {
                new Hotspot("HS_03_A",  60,  716, 1080,  68),  // 05 SEP antique hand mirror
                new Hotspot("HS_03_B", 780, 1110,  366, 208),  // police sticky note
                new Hotspot("HS_03_C",  60, 1021, 1080,  68),  // 15 SEP campus cafeteria
            }
        },
    };

    [Header("Look")]
    [Range(0f, 1f)] public float dimAlpha = 0.65f;
    public Color highlightColor = new Color(1f, 0.85f, 0.25f, 1f);
    [Range(0f, 1f)] public float hoverAlpha = 0.25f;
    [Range(0f, 1f)] public float seenAlpha = 0.12f;
    [Tooltip("Debug: show every hotspot so you can line them up with the image.")]
    public bool showHotspotAreas = false;

    [Header("Layout (canvas units)")]
    public float marginTop = 30f;
    public float marginSides = 40f;
    [Tooltip("Gap between the bottom of the document area and the top of the textbox.")]
    public float gapAboveTextbox = 12f;
    [Tooltip("Empty space kept around a hotspot when zooming in (page pixels).")]
    public float focusPadding = 40f;
    public float maxZoom = 2.5f;

    [Header("Timing (seconds)")]
    public float fadeDuration = 0.35f;
    public float zoomDuration = 0.5f;
    public float swapDuration = 0.2f;
    public float evidenceHold = 3f;

    /// <summary>Raised when the player clicks a hotspot (and Busy is false).</summary>
    public event Action<string> HotspotClicked;

    /// <summary>Set by DialogueManager while a clue's lines are playing. Hotspots ignore clicks then.</summary>
    public bool Busy { get; set; }
    public bool IsOpen { get; private set; }

    // runtime UI
    RectTransform root, viewport, page, tabBar;
    CanvasGroup rootGroup, pageGroup, evidenceGroup;
    Image pageImage;
    TMP_Text evidenceText;
    TMP_FontAsset font;
    Document current;
    readonly Dictionary<string, Image> hotspotImages = new Dictionary<string, Image>();
    readonly HashSet<string> seen = new HashSet<string>();
    readonly Dictionary<string, Image> tabImages = new Dictionary<string, Image>();
    Coroutine panelRoutine, zoomRoutine, evidenceRoutine;

    // ------------------------------------------------------------------ public API

    /// <summary>Open the panel on a document (by inkName or id). Returns false if it can't be shown.</summary>
    public bool Open(string docName, RectTransform textbox, TMP_FontAsset textFont)
    {
        var doc = FindDocument(docName);
        if (doc == null || LoadSprite(doc) == null) return false;

        font = textFont;
        if (root == null) Build(textbox);

        doc.unlocked = true;
        IsOpen = true;
        root.gameObject.SetActive(true);
        rootGroup.blocksRaycasts = true;
        ShowDocument(doc);

        Restart(ref panelRoutine, FadeRoot(1f, null));
        return true;
    }

    public void Close(Action onClosed = null)
    {
        if (root == null || !IsOpen) { onClosed?.Invoke(); return; }
        IsOpen = false;
        Busy = false;
        rootGroup.blocksRaycasts = false;
        foreach (var d in documents) d.unlocked = false; // next inspection starts with only its own document
        Restart(ref panelRoutine, FadeRoot(0f, () =>
        {
            root.gameObject.SetActive(false);
            onClosed?.Invoke();
        }));
    }

    /// <summary>Unlock and show another document (by id or inkName).</summary>
    public bool SwitchTo(string docName)
    {
        var doc = FindDocument(docName);
        if (doc == null) return false;
        if (LoadSprite(doc) == null)
        {
            Debug.LogWarning($"[InspectionMode] No image for '{doc.id}'. Put it in Assets/Resources/{doc.resourcePath}.png " +
                             "or assign the Sprite in the Inspector.", this);
            return false;
        }

        doc.unlocked = true;
        if (doc == current) { ZoomOut(); return true; }
        Restart(ref panelRoutine, SwapRoutine(doc));
        return true;
    }

    public void ZoomTo(string hotspotId)
    {
        if (current == null) return;
        var hs = current.hotspots.Find(h => h.id == hotspotId);
        if (hs == null) return;

        Vector2 size = current.pageSize;
        Rect a = hs.area;
        Vector2 center = new Vector2(a.x + a.width * 0.5f - size.x * 0.5f,
                                     size.y * 0.5f - (a.y + a.height * 0.5f));

        Rect vp = viewport.rect;
        float full = FullScale();
        float scale = Mathf.Min(vp.width / (a.width + focusPadding * 2f),
                                vp.height / (a.height + focusPadding * 2f));
        scale = Mathf.Clamp(scale, full, Mathf.Max(full, maxZoom));

        Restart(ref zoomRoutine, ZoomRoutine(scale, Clamp(-center * scale, scale)));
    }

    public void ZoomOut()
    {
        if (current == null || page == null) return;
        Restart(ref zoomRoutine, ZoomRoutine(FullScale(), Vector2.zero));
    }

    public void MarkSeen(string hotspotId)
    {
        seen.Add(hotspotId);
        RefreshHotspot(hotspotId, false);
    }

    public void ShowEvidence(string evidence)
    {
        if (root == null) return;
        string pretty = string.IsNullOrEmpty(evidence) ? "" :
            char.ToUpper(evidence[0]) + evidence.Substring(1).ToLower().Replace('_', ' ');
        evidenceText.text = "<size=70%>EVIDENCE NOTED</size>\n<b>" + pretty + "</b>";
        Restart(ref evidenceRoutine, EvidenceRoutine());
    }

    // ------------------------------------------------------------------ building the UI

    void Build(RectTransform textbox)
    {
        var parent = (RectTransform)textbox.parent;

        root = NewRect("InspectionPanel", parent);
        Stretch(root);
        root.SetSiblingIndex(textbox.GetSiblingIndex());   // directly behind the textbox
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;

        // Dim overlay: also catches "click outside" to zoom back out.
        var dim = NewImage("Dim", root, new Color(0f, 0f, 0f, dimAlpha));
        Stretch(dim.rectTransform);
        AddClick(dim.gameObject, OnBackgroundClicked);

        // Viewport: from the top of the screen down to just above the textbox.
        viewport = NewRect("Viewport", root);
        Stretch(viewport);
        var corners = new Vector3[4];
        textbox.GetWorldCorners(corners);
        float textboxTop = parent.InverseTransformPoint(corners[1]).y - parent.rect.yMin;
        viewport.offsetMin = new Vector2(marginSides, textboxTop + gapAboveTextbox);
        viewport.offsetMax = new Vector2(-marginSides, -marginTop);
        viewport.gameObject.AddComponent<RectMask2D>();

        pageImage = NewImage("Page", viewport, Color.white);
        page = pageImage.rectTransform;
        page.anchorMin = page.anchorMax = page.pivot = new Vector2(0.5f, 0.5f);
        pageGroup = page.gameObject.AddComponent<CanvasGroup>();
        AddClick(page.gameObject, OnBackgroundClicked);

        // Tabs (only visible once a second document is unlocked)
        tabBar = NewRect("Tabs", root);
        tabBar.anchorMin = tabBar.anchorMax = tabBar.pivot = new Vector2(0f, 1f);
        tabBar.anchoredPosition = new Vector2(marginSides, -marginTop);
        tabBar.sizeDelta = new Vector2(260f, 400f);

        // "Evidence noted" pop-up
        var ev = NewImage("EvidencePopup", root, new Color(0.08f, 0.08f, 0.1f, 0.9f));
        var evRt = ev.rectTransform;
        evRt.anchorMin = evRt.anchorMax = evRt.pivot = new Vector2(1f, 1f);
        evRt.anchoredPosition = new Vector2(-marginSides, -marginTop);
        evRt.sizeDelta = new Vector2(380f, 96f);
        ev.raycastTarget = false;
        evidenceGroup = ev.gameObject.AddComponent<CanvasGroup>();
        evidenceGroup.alpha = 0f;
        evidenceGroup.blocksRaycasts = false;
        evidenceText = NewText("Text", evRt, 30f, new Color(1f, 0.88f, 0.5f));
        evidenceText.alignment = TextAlignmentOptions.Center;
        Stretch(evidenceText.rectTransform);
    }

    void ShowDocument(Document doc)
    {
        current = doc;
        pageImage.sprite = LoadSprite(doc);
        if (doc.pageSize.x <= 0f || doc.pageSize.y <= 0f) doc.pageSize = pageImage.sprite.rect.size;
        page.sizeDelta = doc.pageSize;
        page.localScale = Vector3.one * FullScale();
        page.anchoredPosition = Vector2.zero;
        pageGroup.alpha = 1f;

        // rebuild hotspots for this page
        foreach (Transform child in page) Destroy(child.gameObject);
        hotspotImages.Clear();
        foreach (var hs in doc.hotspots)
        {
            var img = NewImage(hs.id, page, Color.clear);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(hs.area.x, -hs.area.y);
            rt.sizeDelta = hs.area.size;
            hotspotImages[hs.id] = img;

            string id = hs.id;
            var target = AddClick(img.gameObject, () => OnHotspotClicked(id));
            target.onEnter = () => RefreshHotspot(id, true);
            target.onExit = () => RefreshHotspot(id, false);
            RefreshHotspot(id, false);
        }

        RebuildTabs();
    }

    void RebuildTabs()
    {
        foreach (Transform child in tabBar) Destroy(child.gameObject);
        tabImages.Clear();

        var unlocked = documents.FindAll(d => d.unlocked);
        if (unlocked.Count < 2) return;

        float y = 0f;
        foreach (var doc in unlocked)
        {
            bool active = doc == current;
            var bg = NewImage("Tab " + doc.id, tabBar,
                active ? new Color(0.95f, 0.92f, 0.85f, 1f) : new Color(0.1f, 0.1f, 0.12f, 0.85f));
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(240f, 48f);
            y -= 56f;

            var label = NewText("Label", rt, 24f, active ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.9f, 0.88f, 0.82f));
            label.text = doc.tabLabel;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);

            string id = doc.id;
            AddClick(bg.gameObject, () => { if (!Busy) SwitchTo(id); });
            tabImages[doc.id] = bg;
        }
    }

    void RefreshHotspot(string id, bool hovered)
    {
        if (!hotspotImages.TryGetValue(id, out var img) || img == null) return;
        float a = 0f;
        if (seen.Contains(id)) a = seenAlpha;
        if (hovered && !Busy) a = Mathf.Max(a, hoverAlpha);
        var c = highlightColor;
        if (showHotspotAreas && a <= 0f) { c = new Color(1f, 0.2f, 0.2f); a = 0.3f; }
        c.a = a;
        img.color = c;
    }

    // ------------------------------------------------------------------ input

    void OnHotspotClicked(string id)
    {
        if (Busy || !IsOpen) return;
        HotspotClicked?.Invoke(id);
    }

    void OnBackgroundClicked()
    {
        if (!Busy && IsOpen) ZoomOut();
    }

    // ------------------------------------------------------------------ animation

    IEnumerator FadeRoot(float target, Action done)
    {
        float start = rootGroup.alpha;
        Vector2 from = target > 0.5f ? new Vector2(0f, -60f) : page.anchoredPosition;
        Vector2 to = target > 0.5f ? Vector2.zero : page.anchoredPosition + new Vector2(0f, -30f);
        yield return Tween(fadeDuration, e =>
        {
            rootGroup.alpha = Mathf.Lerp(start, target, e);
            page.anchoredPosition = Vector2.Lerp(from, to, e);
        });
        panelRoutine = null;
        done?.Invoke();
    }

    IEnumerator SwapRoutine(Document next)
    {
        StopRoutine(ref zoomRoutine);
        float start = pageGroup.alpha;
        yield return Tween(swapDuration, e => pageGroup.alpha = Mathf.Lerp(start, 0f, e));

        ShowDocument(next);
        pageGroup.alpha = 0f;
        Vector2 from = new Vector2(0f, -30f);
        yield return Tween(swapDuration, e =>
        {
            pageGroup.alpha = e;
            page.anchoredPosition = Vector2.Lerp(from, Vector2.zero, e);
        });
        panelRoutine = null;
    }

    IEnumerator ZoomRoutine(float targetScale, Vector2 targetPos)
    {
        float startScale = page.localScale.x;
        Vector2 startPos = page.anchoredPosition;
        yield return Tween(zoomDuration, e =>
        {
            page.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, e);
            page.anchoredPosition = Vector2.Lerp(startPos, targetPos, e);
        });
        zoomRoutine = null;
    }

    IEnumerator EvidenceRoutine()
    {
        float start = evidenceGroup.alpha;
        yield return Tween(0.2f, e => evidenceGroup.alpha = Mathf.Lerp(start, 1f, e));
        yield return new WaitForSecondsRealtime(evidenceHold);
        yield return Tween(0.4f, e => evidenceGroup.alpha = 1f - e);
        evidenceRoutine = null;
    }

    // ------------------------------------------------------------------ helpers

    float FullScale()
    {
        Rect vp = viewport.rect;
        Vector2 size = current != null ? current.pageSize : page.sizeDelta;
        if (size.x <= 0f || size.y <= 0f) return 1f;
        return Mathf.Min(vp.width / size.x, vp.height / size.y) * 0.96f;
    }

    /// <summary>Keeps the page edges from sliding into view when zoomed in.</summary>
    Vector2 Clamp(Vector2 pos, float scale)
    {
        Vector2 half = current.pageSize * scale * 0.5f;
        Vector2 vpHalf = viewport.rect.size * 0.5f;
        float limX = Mathf.Max(0f, half.x - vpHalf.x);
        float limY = Mathf.Max(0f, half.y - vpHalf.y);
        return new Vector2(Mathf.Clamp(pos.x, -limX, limX), Mathf.Clamp(pos.y, -limY, limY));
    }

    Document FindDocument(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = name.Trim();
        var doc = documents.Find(d =>
            string.Equals(d.id, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.inkName, name, StringComparison.OrdinalIgnoreCase));
        if (doc == null)
            Debug.LogWarning($"[InspectionMode] No document called '{name}' in the Documents list.", this);
        return doc;
    }

    Sprite LoadSprite(Document doc)
    {
        if (doc.sprite == null && !string.IsNullOrEmpty(doc.resourcePath))
            doc.sprite = Resources.Load<Sprite>(doc.resourcePath);
        return doc.sprite;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static InspectionClickTarget AddClick(GameObject go, Action onClick)
    {
        var t = go.AddComponent<InspectionClickTarget>();
        t.onClick = onClick;
        return t;
    }

    void Restart(ref Coroutine routine, IEnumerator next)
    {
        StopRoutine(ref routine);
        routine = StartCoroutine(next);
    }

    void StopRoutine(ref Coroutine routine)
    {
        if (routine == null) return;
        StopCoroutine(routine);
        routine = null;
    }

    static IEnumerator Tween(float duration, Action<float> step)
    {
        if (duration <= 0f) { step(1f); yield break; }
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            step(t * t * (3f - 2f * t)); // smoothstep
            yield return null;
        }
        step(1f);
    }
}

/// <summary>Small click / hover receiver used by InspectionMode's runtime UI.</summary>
public class InspectionClickTarget : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Action onClick, onEnter, onExit;
    public void OnPointerClick(PointerEventData e) => onClick?.Invoke();
    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
    public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
}
