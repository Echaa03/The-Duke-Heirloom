using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
///
/// ACCESSIBILITY (toolbar on the right side of the panel, choices are remembered):
///   - "Zoom In" / "Whole Page": reading size (page fills the width, scroll with the mouse
///     wheel or drag it) or the whole page at once.
///   - "Read as Text": the document as plain, large text with A- / A+ size buttons.
///     Clues are links in the text, so the whole inspection can be played from there.
///     Text comes from Document.readableText. Mark a clue like this: [[HS_01_A|the clue text]]
///   - "Hints: On/Off": clickable areas get a soft pulsing frame (and are underlined in the text view).
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
        [Tooltip("Plain-text version for the 'Read as Text' view. Mark clues as [[HS_01_A|clue text]]. " +
                 "Leave empty to hide the button for this document.")]
        [TextArea(6, 30)] public string readableText;

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
            },
            readableText = PoliceReportText,
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
            },
            readableText = BankStatementText,
        },
    };

    [Header("Look")]
    [Range(0f, 1f)] public float dimAlpha = 0.8f;
    public Color highlightColor = new Color(1f, 0.85f, 0.25f, 1f);
    [Range(0f, 1f)] public float hoverAlpha = 0.25f;
    [Range(0f, 1f)] public float seenAlpha = 0.12f;
    [Tooltip("Debug: show every hotspot so you can line them up with the image.")]
    public bool showHotspotAreas = false;

    [Header("Accessibility defaults (the player's own choice is saved and wins)")]
    [Tooltip("Start in reading size (page fills the width) instead of showing the whole page.")]
    public bool startZoomedIn = false;
    [Tooltip("Show a soft frame around clickable areas.")]
    public bool hintsOnByDefault = true;
    [Tooltip("Width of the page in reading size (canvas units, 1920 x 1080 reference).")]
    public float readingWidth = 1300f;
    [Tooltip("Thickness of the hint frame on screen (canvas units).")]
    public float hintFrameThickness = 4f;
    public float scrollStep = 160f;
    public float readerFontSize = 36f;
    public float readerMinFontSize = 24f;
    public float readerMaxFontSize = 64f;

    [Header("Layout (canvas units)")]
    public float marginTop = 30f;
    public float marginSides = 40f;
    [Tooltip("Space kept free on both sides of the document for the tabs and the toolbar.")]
    public float sideReserve = 280f;
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

    const string PrefZoom = "inspection_zoomed_in";
    const string PrefHints = "inspection_hints";
    const string PrefReaderSize = "inspection_reader_font_size";

    static readonly Color ButtonColor = new Color(0.1f, 0.1f, 0.12f, 0.92f);
    static readonly Color ButtonHoverColor = new Color(0.25f, 0.24f, 0.28f, 0.97f);
    static readonly Color ButtonTextColor = new Color(0.96f, 0.93f, 0.86f);
    static readonly Color PaperColor = new Color(0.95f, 0.93f, 0.88f, 1f);
    static readonly Color InkColor = new Color(0.1f, 0.1f, 0.11f, 1f);

    // runtime UI
    RectTransform root, viewport, page, tabBar;
    CanvasGroup rootGroup, pageGroup, evidenceGroup;
    Image pageImage;
    TMP_Text evidenceText;
    TMP_FontAsset font;
    Canvas canvas;
    Document current;
    readonly Dictionary<string, Image> hotspotImages = new Dictionary<string, Image>();
    readonly Dictionary<string, Image[]> hotspotFrames = new Dictionary<string, Image[]>();
    readonly HashSet<string> seen = new HashSet<string>();
    readonly HashSet<string> hovered = new HashSet<string>();
    readonly Dictionary<string, Image> tabImages = new Dictionary<string, Image>();
    Coroutine panelRoutine, zoomRoutine, evidenceRoutine;

    // accessibility state
    bool zoomedIn, hintsOn;
    float readerSize;
    TMP_Text zoomButtonLabel, hintsButtonLabel;
    GameObject readButton;

    // reader ("Read as Text")
    RectTransform readerRoot, readerContent;
    CanvasGroup readerGroup;
    ScrollRect readerScroll;
    TMP_Text readerTitle, readerBody, readerFooter;
    public bool ReaderOpen { get; private set; }

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
        CloseReader();
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
        float rest = RestScale();
        float scale = Mathf.Min(vp.width / (a.width + focusPadding * 2f),
                                vp.height / (a.height + focusPadding * 2f));
        scale = Mathf.Clamp(scale, rest, Mathf.Max(rest, maxZoom));

        Restart(ref zoomRoutine, ZoomRoutine(scale, Clamp(-center * scale, scale)));
    }

    /// <summary>Back to the resting view (whole page, or reading size keeping the current spot).</summary>
    public void ZoomOut()
    {
        if (current == null || page == null) return;
        float rest = RestScale();
        float cur = Mathf.Max(0.0001f, page.localScale.x);
        Vector2 pos = page.anchoredPosition * (rest / cur);
        Restart(ref zoomRoutine, ZoomRoutine(rest, Clamp(pos, rest)));
    }

    public void MarkSeen(string hotspotId)
    {
        seen.Add(hotspotId);
        RefreshHotspot(hotspotId);
        if (ReaderOpen) RefreshReaderText();
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
        canvas = parent.GetComponentInParent<Canvas>();
        if (canvas != null) canvas = canvas.rootCanvas;

        zoomedIn = PlayerPrefs.GetInt(PrefZoom, startZoomedIn ? 1 : 0) == 1;
        hintsOn = PlayerPrefs.GetInt(PrefHints, hintsOnByDefault ? 1 : 0) == 1;
        readerSize = Mathf.Clamp(PlayerPrefs.GetFloat(PrefReaderSize, readerFontSize), readerMinFontSize, readerMaxFontSize);

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
        // It is clickable (zoom out) and can be dragged / scrolled to move the page.
        var vpImage = NewImage("Viewport", root, Color.clear);
        viewport = vpImage.rectTransform;
        Stretch(viewport);
        var corners = new Vector3[4];
        textbox.GetWorldCorners(corners);
        float textboxTop = parent.InverseTransformPoint(corners[1]).y - parent.rect.yMin;
        // keep the sides free for the tabs (left) and the toolbar / evidence pop-up (right)
        float side = marginSides + sideReserve;
        viewport.offsetMin = new Vector2(side, textboxTop + gapAboveTextbox);
        viewport.offsetMax = new Vector2(-side, -marginTop);
        viewport.gameObject.AddComponent<RectMask2D>();
        AddClick(viewport.gameObject, OnBackgroundClicked);
        var pan = viewport.gameObject.AddComponent<InspectionPanTarget>();
        pan.onDrag = d => Pan(d / CanvasScale());
        pan.onScroll = OnScroll;

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

        BuildToolbar();
        BuildReader(parent);
    }

    void BuildToolbar()
    {
        // Right side, under the "Evidence noted" pop-up.
        var bar = NewRect("Toolbar", root);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 1f);
        bar.anchoredPosition = new Vector2(-marginSides, -marginTop - 96f - 20f);
        bar.sizeDelta = new Vector2(250f, 220f);

        readButton = MakeButton(bar, "Read as Text", new Vector2(0f, 0f), new Vector2(250f, 60f), OpenReader, out _);
        MakeButton(bar, "", new Vector2(0f, -72f), new Vector2(250f, 60f), ToggleZoom, out zoomButtonLabel);
        MakeButton(bar, "", new Vector2(0f, -144f), new Vector2(250f, 60f), ToggleHints, out hintsButtonLabel);
        RefreshToolbar();
    }

    void BuildReader(RectTransform parent)
    {
        // A separate layer above everything (also above the textbox).
        readerRoot = NewRect("InspectionReader", parent);
        Stretch(readerRoot);
        readerRoot.SetAsLastSibling();
        readerGroup = readerRoot.gameObject.AddComponent<CanvasGroup>();

        var backdrop = NewImage("Backdrop", readerRoot, new Color(0f, 0f, 0f, 0.88f));
        Stretch(backdrop.rectTransform);
        AddClick(backdrop.gameObject, null); // swallow clicks so they don't reach the textbox

        var card = NewImage("Card", readerRoot, PaperColor);
        var cardRt = card.rectTransform;
        cardRt.anchorMin = new Vector2(0.5f, 0f);
        cardRt.anchorMax = new Vector2(0.5f, 1f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(1500f, -60f); // 30 top + 30 bottom
        cardRt.anchoredPosition = Vector2.zero;

        // Title bar
        var titleBar = NewImage("TitleBar", cardRt, new Color(0.12f, 0.12f, 0.14f, 1f));
        var tb = titleBar.rectTransform;
        tb.anchorMin = new Vector2(0f, 1f);
        tb.anchorMax = new Vector2(1f, 1f);
        tb.pivot = new Vector2(0.5f, 1f);
        tb.sizeDelta = new Vector2(0f, 84f);
        tb.anchoredPosition = Vector2.zero;

        readerTitle = NewText("Title", tb, 34f, ButtonTextColor);
        readerTitle.fontStyle = FontStyles.Bold;
        readerTitle.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(readerTitle.rectTransform);
        readerTitle.rectTransform.offsetMin = new Vector2(32f, 0f);
        readerTitle.rectTransform.offsetMax = new Vector2(-520f, 0f);

        var buttons = NewRect("Buttons", tb);
        buttons.anchorMin = buttons.anchorMax = buttons.pivot = new Vector2(1f, 0.5f);
        buttons.anchoredPosition = new Vector2(-12f, 0f);
        buttons.sizeDelta = new Vector2(500f, 60f);
        MakeButton(buttons, "A-", new Vector2(-370f, 0f), new Vector2(110f, 60f), () => ChangeReaderSize(-4f), out _, new Vector2(1f, 1f));
        MakeButton(buttons, "A+", new Vector2(-250f, 0f), new Vector2(110f, 60f), () => ChangeReaderSize(4f), out _, new Vector2(1f, 1f));
        MakeButton(buttons, "Close", new Vector2(0f, 0f), new Vector2(230f, 60f), CloseReader, out _, new Vector2(1f, 1f));

        // Footer (hint about links)
        readerFooter = NewText("Footer", cardRt, 26f, new Color(0.3f, 0.28f, 0.25f));
        var fr = readerFooter.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(1f, 0f);
        fr.pivot = new Vector2(0.5f, 0f);
        fr.sizeDelta = new Vector2(-64f, 64f);
        fr.anchoredPosition = new Vector2(0f, 8f);
        readerFooter.alignment = TextAlignmentOptions.Midline;

        // Scroll area
        var scrollGo = NewRect("Scroll", cardRt);
        Stretch(scrollGo);
        scrollGo.offsetMin = new Vector2(0f, 76f);
        scrollGo.offsetMax = new Vector2(0f, -84f);
        readerScroll = scrollGo.gameObject.AddComponent<ScrollRect>();
        readerScroll.horizontal = false;
        readerScroll.vertical = true;
        readerScroll.movementType = ScrollRect.MovementType.Clamped;
        readerScroll.scrollSensitivity = 60f;

        var vp = NewImage("Viewport", scrollGo, Color.clear);
        Stretch(vp.rectTransform);
        vp.rectTransform.offsetMax = new Vector2(-40f, 0f); // room for the scrollbar
        vp.gameObject.AddComponent<RectMask2D>();
        readerScroll.viewport = vp.rectTransform;

        readerBody = NewText("Content", vp.rectTransform, readerSize, InkColor);
        readerBody.raycastTarget = true;
        readerBody.richText = true;
        readerBody.textWrappingMode = TextWrappingModes.Normal;
        readerBody.alignment = TextAlignmentOptions.TopLeft;
        readerBody.margin = new Vector4(56f, 32f, 40f, 48f);
        readerBody.lineSpacing = 12f;
        readerBody.paragraphSpacing = 18f;
        readerContent = readerBody.rectTransform;
        readerContent.anchorMin = new Vector2(0f, 1f);
        readerContent.anchorMax = new Vector2(1f, 1f);
        readerContent.pivot = new Vector2(0.5f, 1f);
        readerContent.sizeDelta = Vector2.zero;
        readerContent.anchoredPosition = Vector2.zero;
        var fitter = readerBody.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        readerScroll.content = readerContent;
        var links = readerBody.gameObject.AddComponent<InspectionLinkTarget>();
        links.text = readerBody;
        links.onLink = OnReaderLinkClicked;
        // our own wheel handling (same feel on every mouse / touchpad); dragging stays with the ScrollRect
        readerBody.gameObject.AddComponent<InspectionScrollTarget>().onScroll = OnReaderScroll;

        // Scrollbar (for players who can't use a scroll wheel)
        var sbImg = NewImage("Scrollbar", scrollGo, new Color(0f, 0f, 0f, 0.12f));
        var sb = sbImg.rectTransform;
        sb.anchorMin = new Vector2(1f, 0f);
        sb.anchorMax = new Vector2(1f, 1f);
        sb.pivot = new Vector2(1f, 0.5f);
        sb.sizeDelta = new Vector2(28f, -16f);
        sb.anchoredPosition = new Vector2(-8f, 0f);
        var area = NewRect("Sliding Area", sb);
        Stretch(area);
        var handle = NewImage("Handle", area, new Color(0.25f, 0.24f, 0.27f, 0.85f));
        Stretch(handle.rectTransform);
        var scrollbar = sbImg.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        readerScroll.verticalScrollbar = scrollbar;
        readerScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        readerRoot.gameObject.SetActive(false);
    }

    void ShowDocument(Document doc)
    {
        current = doc;
        pageImage.sprite = LoadSprite(doc);
        if (doc.pageSize.x <= 0f || doc.pageSize.y <= 0f) doc.pageSize = pageImage.sprite.rect.size;
        page.sizeDelta = doc.pageSize;
        page.localScale = Vector3.one * RestScale();
        page.anchoredPosition = RestPosition();
        pageGroup.alpha = 1f;

        // rebuild hotspots for this page
        foreach (Transform child in page) Destroy(child.gameObject);
        hotspotImages.Clear();
        hotspotFrames.Clear();
        hovered.Clear();
        foreach (var hs in doc.hotspots)
        {
            var img = NewImage(hs.id, page, Color.clear);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(hs.area.x, -hs.area.y);
            rt.sizeDelta = hs.area.size;
            hotspotImages[hs.id] = img;
            hotspotFrames[hs.id] = BuildFrame(rt);

            string id = hs.id;
            var target = AddClick(img.gameObject, () => OnHotspotClicked(id));
            target.onEnter = () => { hovered.Add(id); RefreshHotspot(id); };
            target.onExit = () => { hovered.Remove(id); RefreshHotspot(id); };
            RefreshHotspot(id);
        }
        lastFrameScale = -1f; // new frames need their thickness set
        UpdateFrameThickness();

        RebuildTabs();
        RefreshToolbar();
    }

    /// <summary>Four thin edges around a hotspot, used for hints.</summary>
    Image[] BuildFrame(RectTransform hs)
    {
        var edges = new Image[4];
        // top, bottom, left, right
        Vector2[] min = { new Vector2(0, 1), new Vector2(0, 0), new Vector2(0, 0), new Vector2(1, 0) };
        Vector2[] max = { new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        Vector2[] piv = { new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0, 0.5f), new Vector2(1, 0.5f) };
        for (int i = 0; i < 4; i++)
        {
            var e = NewImage("Frame", hs, Color.clear);
            e.raycastTarget = false;
            var rt = e.rectTransform;
            rt.anchorMin = min[i];
            rt.anchorMax = max[i];
            rt.pivot = piv[i];
            rt.anchoredPosition = Vector2.zero;
            edges[i] = e;
        }
        return edges;
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

    void RefreshHotspot(string id)
    {
        if (!hotspotImages.TryGetValue(id, out var img) || img == null) return;
        bool isSeen = seen.Contains(id);
        bool isHovered = hovered.Contains(id) && !Busy;

        float a = 0f;
        if (isSeen) a = seenAlpha;
        if (isHovered) a = Mathf.Max(a, hoverAlpha);
        var c = highlightColor;
        if (showHotspotAreas && a <= 0f) { c = new Color(1f, 0.2f, 0.2f); a = 0.3f; }
        c.a = a;
        img.color = c;

        // the frame: solid on hover, pulsing (see Update) for unseen clues when hints are on
        if (hotspotFrames.TryGetValue(id, out var frame))
        {
            float fa = isHovered ? 1f : (hintsOn && !isSeen ? PulseAlpha() : 0f);
            var fc = highlightColor;
            fc.a = fa;
            foreach (var e in frame) e.color = fc;
        }
    }

    void RefreshToolbar()
    {
        if (zoomButtonLabel != null) zoomButtonLabel.text = zoomedIn ? "Whole Page" : "Zoom In";
        if (hintsButtonLabel != null) hintsButtonLabel.text = hintsOn ? "Hints: On" : "Hints: Off";
        if (readButton != null) readButton.SetActive(current != null && !string.IsNullOrWhiteSpace(current.readableText));
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (!IsOpen || current == null) return;
        if (hintsOn)
            foreach (var id in hotspotFrames.Keys) RefreshHotspot(id);
    }

    void LateUpdate()
    {
        if (IsOpen) UpdateFrameThickness();
    }

    float lastFrameScale = -1f;
    void UpdateFrameThickness()
    {
        if (page == null) return;
        float s = page.localScale.x;
        if (Mathf.Approximately(s, lastFrameScale)) return;
        lastFrameScale = s;
        float t = hintFrameThickness / Mathf.Max(0.0001f, s); // keep it the same thickness on screen
        foreach (var frame in hotspotFrames.Values)
        {
            frame[0].rectTransform.sizeDelta = new Vector2(0f, t);
            frame[1].rectTransform.sizeDelta = new Vector2(0f, t);
            frame[2].rectTransform.sizeDelta = new Vector2(t, 0f);
            frame[3].rectTransform.sizeDelta = new Vector2(t, 0f);
        }
    }

    static float PulseAlpha() => Mathf.Lerp(0.35f, 0.9f, (Mathf.Sin(Time.unscaledTime * 3f) + 1f) * 0.5f);

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

    void OnScroll(Vector2 delta)
    {
        Vector2 n = ScrollTicks(delta);
        Pan(new Vector2(-n.x * scrollStep, -n.y * scrollStep));
    }

    float lastSmallScroll = -1f;
    /// <summary>
    /// Mouse wheels, touchpads and input setups report very different scroll values
    /// (0.01 ... 120 per notch). Every scroll event moves exactly one step, and the tiny
    /// events a touchpad sends many times a second are limited to ~25 steps per second.
    /// </summary>
    Vector2 ScrollTicks(Vector2 delta)
    {
        Vector2 n = new Vector2(Mathf.Abs(delta.x) > 10f ? delta.x / 120f : delta.x,
                                Mathf.Abs(delta.y) > 10f ? delta.y / 120f : delta.y);
        bool small = Mathf.Abs(n.x) < 1f && Mathf.Abs(n.y) < 1f;
        if (small)
        {
            if (Time.unscaledTime - lastSmallScroll < 0.04f) return Vector2.zero;
            lastSmallScroll = Time.unscaledTime;
        }
        float Tick(float v) => Mathf.Approximately(v, 0f) ? 0f : Mathf.Sign(v);
        // only the main direction, so a slightly diagonal wheel doesn't drift sideways
        return Mathf.Abs(n.y) >= Mathf.Abs(n.x) ? new Vector2(0f, Tick(n.y)) : new Vector2(Tick(n.x), 0f);
    }

    void OnReaderScroll(Vector2 delta)
    {
        float y = ScrollTicks(delta).y;
        if (Mathf.Approximately(y, 0f)) return;
        readerScroll.StopMovement();
        float max = Mathf.Max(0f, readerContent.rect.height - readerScroll.viewport.rect.height);
        var p = readerContent.anchoredPosition;
        p.y = Mathf.Clamp(p.y - y * readerSize * 3f, 0f, max); // about three lines per notch
        readerContent.anchoredPosition = p;
    }

    void Pan(Vector2 delta)
    {
        if (current == null || page == null) return;
        StopRoutine(ref zoomRoutine);
        page.anchoredPosition = Clamp(page.anchoredPosition + delta, page.localScale.x);
    }

    void ToggleZoom()
    {
        zoomedIn = !zoomedIn;
        PlayerPrefs.SetInt(PrefZoom, zoomedIn ? 1 : 0);
        RefreshToolbar();
        if (current == null) return;
        Vector2 target = zoomedIn ? RestPosition() : Vector2.zero;
        Restart(ref zoomRoutine, ZoomRoutine(RestScale(), target));
    }

    void ToggleHints()
    {
        hintsOn = !hintsOn;
        PlayerPrefs.SetInt(PrefHints, hintsOn ? 1 : 0);
        RefreshToolbar();
        foreach (var id in hotspotImages.Keys) RefreshHotspot(id);
    }

    // ------------------------------------------------------------------ reader ("Read as Text")

    void OpenReader()
    {
        if (current == null || string.IsNullOrWhiteSpace(current.readableText)) return;
        ReaderOpen = true;
        readerRoot.SetAsLastSibling();
        readerRoot.gameObject.SetActive(true);
        readerTitle.text = current.tabLabel;
        readerBody.fontSize = readerSize;
        RefreshReaderText();
        readerScroll.verticalNormalizedPosition = 1f;
    }

    void CloseReader()
    {
        ReaderOpen = false;
        if (readerRoot != null) readerRoot.gameObject.SetActive(false);
    }

    void ChangeReaderSize(float step)
    {
        readerSize = Mathf.Clamp(readerSize + step, readerMinFontSize, readerMaxFontSize);
        PlayerPrefs.SetFloat(PrefReaderSize, readerSize);
        float keep = readerScroll.verticalNormalizedPosition;
        readerBody.fontSize = readerSize;
        LayoutRebuilder.ForceRebuildLayoutImmediate(readerContent);
        readerScroll.verticalNormalizedPosition = keep;
    }

    static readonly Regex ClueMarkup = new Regex(@"\[\[(\w+)\|(.*?)\]\]", RegexOptions.Singleline);

    void RefreshReaderText()
    {
        if (current == null || readerBody == null) return;
        readerBody.text = ClueMarkup.Replace(current.readableText.Trim(), m =>
        {
            string id = m.Groups[1].Value, body = m.Groups[2].Value;
            if (!hintsOn) return $"<link=\"{id}\">{body}</link>";
            return seen.Contains(id)
                ? $"<link=\"{id}\"><color=#6E6352>{body}</color></link>"
                : $"<link=\"{id}\"><color=#8A4B00><u>{body}</u></color></link>";
        });
        readerFooter.text = hintsOn
            ? "Underlined lines are clues: click one to examine it."
            : "Click any line that looks important to examine it.";
        LayoutRebuilder.ForceRebuildLayoutImmediate(readerContent);
    }

    void OnReaderLinkClicked(string id)
    {
        if (Busy || !IsOpen) return;
        if (current == null || current.hotspots.Find(h => h.id == id) == null) return;
        CloseReader();
        OnHotspotClicked(id);
    }

    // ------------------------------------------------------------------ animation

    IEnumerator FadeRoot(float target, Action done)
    {
        float start = rootGroup.alpha;
        Vector2 rest = target > 0.5f ? RestPosition() : page.anchoredPosition;
        Vector2 from = target > 0.5f ? rest + new Vector2(0f, -60f) : rest;
        Vector2 to = target > 0.5f ? rest : rest + new Vector2(0f, -30f);
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
        Vector2 rest = RestPosition();
        Vector2 from = rest + new Vector2(0f, -30f);
        yield return Tween(swapDuration, e =>
        {
            pageGroup.alpha = e;
            page.anchoredPosition = Vector2.Lerp(from, rest, e);
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

    /// <summary>Page fills the reading width (never smaller than the whole-page view).</summary>
    float ReadScale()
    {
        Vector2 size = current != null ? current.pageSize : page.sizeDelta;
        if (size.x <= 0f) return FullScale();
        float width = Mathf.Min(readingWidth, viewport.rect.width * 0.96f);
        return Mathf.Max(FullScale(), width / size.x);
    }

    float RestScale() => zoomedIn ? ReadScale() : FullScale();

    /// <summary>Whole page: centred. Reading size: the top of the page.</summary>
    Vector2 RestPosition() => zoomedIn ? Clamp(new Vector2(0f, -1e6f), RestScale()) : Vector2.zero;

    /// <summary>Keeps the page edges from sliding into view when zoomed in.</summary>
    Vector2 Clamp(Vector2 pos, float scale)
    {
        Vector2 half = current.pageSize * scale * 0.5f;
        Vector2 vpHalf = viewport.rect.size * 0.5f;
        float limX = Mathf.Max(0f, half.x - vpHalf.x);
        float limY = Mathf.Max(0f, half.y - vpHalf.y);
        return new Vector2(Mathf.Clamp(pos.x, -limX, limX), Mathf.Clamp(pos.y, -limY, limY));
    }

    float CanvasScale() => canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

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

    GameObject MakeButton(RectTransform parent, string text, Vector2 pos, Vector2 size, Action onClick,
                          out TMP_Text label, Vector2? anchor = null)
    {
        Vector2 a = anchor ?? new Vector2(1f, 1f);
        var bg = NewImage("Button " + text, parent, ButtonColor);
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = a;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        label = NewText("Label", rt, 28f, ButtonTextColor);
        label.text = text;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        Stretch(label.rectTransform);

        var t = AddClick(bg.gameObject, onClick);
        t.onEnter = () => bg.color = ButtonHoverColor;
        t.onExit = () => bg.color = ButtonColor;
        return bg.gameObject;
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

    // ------------------------------------------------------------------ readable text
    // Transcribed from the document images. Keep these in sync if the images change.
    // [[HS_xx_x|...]] marks a clue: it becomes a clickable link in the "Read as Text" view.

    const string PoliceReportText =
@"<b>METROPOLITAN POLICE DEPARTMENT</b>
Criminal Investigation Division  -  Form MP-01

<size=130%><b>MISSING PERSON REPORT</b></size>

<b>Case no.:</b> MPU/2026/09/0417
<b>Date filed:</b> 16 SEP 2026, 09:12 AM
<b>Reporting party:</b> Parents (Yu)
<b>Case officer:</b> Det. Insp. R. Juan

<b>1. SUBJECT INFORMATION</b>
<b>Full name:</b> SIERRA YU PENG
<b>Sex / age:</b> Female / 21
<b>Occupation:</b> Undergraduate student, Faculty of Pharmacy (Yr. 3)
<b>Height / build:</b> 162 cm / slim
<b>Hair / eyes:</b> Black, shoulder-length / dark brown
<b>Last seen wearing:</b> Cream knit cardigan, white blouse, dark jeans, canvas tote bag
<b>Personal effects:</b> Mobile phone (black), wallet, student ID card
[[HS_01_G|<b>Photograph of subject:</b> attached to the report.]]

<b>2. CIRCUMSTANCES OF DISAPPEARANCE</b>
<b>Last seen (date/time):</b> Tue, 15 SEP 2026, approx. 21:00
<b>Place last seen:</b> Lab Block B, Faculty of Pharmacy (main campus)
<b>Usual route home:</b> Campus -> Riverside Stn. (train) -> Greenhill Residential
Subject left Lab Block B after a group study session and told her companion she was going straight home. Subject did not arrive at her residence. Family called her phone from approx. 22:30 onward; calls were not answered. Report filed by parents the following morning.

<b>3. STATEMENTS</b>
[[HS_01_A|<b>3.1 Witness:</b> Jimmy (friend / classmate), taken 16 SEP 2026, 11:05
""She left around nine. She said she was going straight home because we had a morning lab the next day.""]]

[[HS_01_B|<b>3.2 Family:</b> Mrs. L. Yu (mother), taken 16 SEP 2026, 09:12
""Sierra never came home that night. She always texts me when she gets on the train. That night, nothing.""]]

<b>4. INVESTIGATIVE NOTES</b>
[[HS_01_C|<b>4.1</b> Last device activity: 15 SEP 2026, 23:40, registered to cell site OSD-07 (Old Shopping District). Location is not on subject's usual route home.]]

[[HS_01_D|<b>4.2</b> Bank record shows a debit dated 08 SEP 2026 described as ""ANTIQUE HAND MIRROR FOR SALE"" at a merchant in the Old Shopping District. See Attachment C.]]

[[HS_01_E|<b>4.3</b> Merchant interviewed 21 SEP 2026. Owner states the store has never stocked mirrors. Entry assessed as cashier input error. No further action.]]

<b>4.4</b> Area canvass / CCTV request: pending.

<b>5. CASE STATUS</b>
<b>Status:</b> OPEN  (stamped: CASE OPEN - UNRESOLVED)
[[HS_01_F|<b>Priority:</b> Standard]]
<b>Last updated:</b> 29 SEP 2026
Signed: Det. Insp. R. Juan, Badge 40217

<size=80%>Page 1 of 1  -  MPU/2026/09/0417  -  Copy released to: Faculty of Law, Legal Aid Clinic</size>";

    const string BankStatementText =
@"<b>HAPPYCAT BANK</b>  -  Personal Banking
<size=130%><b>Account Statement</b></size>
Statement period 01 Sep 2026 - 15 Sep 2026
Issued on 18 September on request of law enforcement
Attachment B  -  MPU/2026/09/0417

<b>Account holder:</b> SIERRA YU PENG
<b>Account:</b> Student Savings, ending 5530
<b>Currency:</b> $

<b>TRANSACTIONS</b>  (date  -  description  -  debit / transfer  -  balance)

<b>02 SEP</b>  QR payment, Campus Cafeteria (Faculty of Pharmacy canteen)  -  debit 2  -  balance 150
<b>03 SEP</b>  E-wallet top-up (Happy Cat E-Wallet)  -  debit 6  -  balance 142
<b>03 SEP</b>  Debit card, University Bookstore (Main Campus)  -  debit 10  -  balance 132
<b>03 SEP</b>  MRT (123 st Avenue)  -  debit 2  -  balance 130
<b>05 SEP</b>  Transfer from Alena Yu (monthly allowance)  -  transfer 600  -  balance 730
[[HS_03_A|<b>05 SEP</b>  Debit card, Heavenbliss Curious & Goods: ""ANTIQUE HAND MIRROR FOR SALE""  -  debit 20  -  balance 710]]
<b>05 SEP</b>  Debit card, City Pharmacy (70% alcohol spray)  -  debit 3  -  balance 707
<b>06 SEP</b>  Auto debit, Watchflix  -  debit 4.5  -  balance 702.5
<b>12 SEP</b>  QR payment, Noodle House  -  debit 5  -  balance 697.5
[[HS_03_C|<b>15 SEP</b>  QR payment, Campus Cafeteria (Faculty of Pharmacy canteen)  -  debit 3  -  balance 694.5]]

-END OF STATEMENT PERIOD-

[[HS_03_B|<b>Handwritten sticky note:</b> ""05/09 mirror, police visited shop on 21/09. Owner: 'never sold mirrors.' prob. cashier input error.""]]

<size=80%>Important information: card transaction descriptions are supplied by the merchant's point-of-sale terminal and are reproduced as received. Please report any unrecognised transaction to Meridian Bank within 30 days. This statement was issued for disclosure purposes and is not valid for any other use.</size>";
}

/// <summary>Small click / hover receiver used by InspectionMode's runtime UI.</summary>
public class InspectionClickTarget : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Action onClick, onEnter, onExit;
    public void OnPointerClick(PointerEventData e) => onClick?.Invoke();
    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
    public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
}

/// <summary>Drag and mouse-wheel receiver for moving the document around.</summary>
public class InspectionPanTarget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public Action<Vector2> onDrag, onScroll;
    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) => onDrag?.Invoke(e.delta);
    public void OnEndDrag(PointerEventData e) { }
    public void OnScroll(PointerEventData e) => onScroll?.Invoke(e.scrollDelta);
}

/// <summary>Mouse-wheel only receiver (does not take drags away from a ScrollRect).</summary>
public class InspectionScrollTarget : MonoBehaviour, IScrollHandler
{
    public Action<Vector2> onScroll;
    public void OnScroll(PointerEventData e) => onScroll?.Invoke(e.scrollDelta);
}

/// <summary>Clicks on &lt;link="..."&gt; tags inside a TextMeshPro text.</summary>
public class InspectionLinkTarget : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text text;
    public Action<string> onLink;

    public void OnPointerClick(PointerEventData e)
    {
        if (text == null) return;
        int i = TMP_TextUtilities.FindIntersectingLink(text, e.position, e.pressEventCamera);
        if (i >= 0) onLink?.Invoke(text.textInfo.linkInfo[i].GetLinkID());
    }
}
