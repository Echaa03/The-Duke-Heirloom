using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Satu manager untuk semua BG & CG. Dipanggil dari DialogueManager lewat tag Ink:
///
///   #bg: BG-02            crossfade ke BG-02
///   #bg_black: BG-03      fade ke hitam -> ganti BG -> fade balik (pindah lokasi)
///   #black                layar hitam (keluar dengan #bg / #bg_black)
///   #zoom: 0.75, 0.55, 2  zoom ke titik (x,y) dengan skala 2. Opsional angka ke-4 = durasi
///   #zoomreset            balik ke zoom normal
///
/// Tidak perlu bikin Unity Scene baru tiap ganti background.
/// </summary>
public class BackgroundManager : MonoBehaviour
{
    [System.Serializable]
    public class BGEntry
    {
        public string id;      // contoh: "BG-01", "BG-02", "CG-03_empty", "CG-03_deborah"
        public Sprite sprite;
    }

    public static BackgroundManager Instance { get; private set; }

    [Header("Dua Image full screen (anchor stretch). Back di bawah, Front di atas.")]
    [SerializeField] Image backLayer;
    [SerializeField] Image frontLayer;

    [Header("Image hitam full screen di atas BG (di bawah textbox), untuk fade to black")]
    [SerializeField] Image blackOverlay;

    [Header("Daftar semua BG/CG")]
    [SerializeField] List<BGEntry> backgrounds = new List<BGEntry>();
    [Tooltip("BG yang langsung tampil saat scene mulai (boleh kosong).")]
    [SerializeField] string startId = "";
    [SerializeField] float defaultFade = 0.6f;

    Dictionary<string, Sprite> lookup;
    Coroutine fadeRoutine;
    Coroutine zoomRoutine;

    public string CurrentId { get; private set; }

    /// <summary>True selama transisi lewat hitam. DialogueManager menahan teks selama ini.</summary>
    public bool BlocksDialogue { get; private set; }

    void Awake()
    {
        Instance = this;
        lookup = new Dictionary<string, Sprite>();
        foreach (var bg in backgrounds)
            if (!string.IsNullOrEmpty(bg.id) && bg.sprite != null)
                lookup[bg.id.Trim()] = bg.sprite;

        setalpha(backLayer, 0f);
        setalpha(frontLayer, 0f);
        if (blackOverlay != null)
        {
            setalpha(blackOverlay, 0f);
            blackOverlay.raycastTarget = false;
        }

        backLayer.raycastTarget = false;
        frontLayer.raycastTarget = false;

        if (!string.IsNullOrEmpty(startId) && tryget(startId, out var first))
        {
            frontLayer.sprite = first;
            fitcover(frontLayer);
            setalpha(frontLayer, 1f);
            CurrentId = startId;
        }
    }

    // ---------- Dipanggil dari tag Ink ----------

    /// <summary>Baca satu tag Ink. Return true kalau tag-nya milik BackgroundManager.</summary>
    public bool handletag(string key, string value)
    {
        switch (key)
        {
            case "bg":        show(value); return true;
            case "bg_black":  showthroughblack(value); return true;
            case "black":     fadetoblack(); return true;
            case "zoom":      zoomfromtag(value); return true;
            case "zoomreset": resetzoom(); return true;
        }

        return false;
    }

    // ---------- API utama ----------

    /// <summary>Crossfade langsung dari BG lama ke BG baru.</summary>
    public void show(string id, float duration = -1f)
    {
        id = id.Trim();
        if (!tryget(id, out var sprite))
            return;
        if (id == CurrentId && (blackOverlay == null || blackOverlay.color.a <= 0f))
            return;
        startfade(crossfade(sprite, duration < 0 ? defaultFade : duration));
        CurrentId = id;
    }

    /// <summary>Fade ke hitam, ganti BG, fade balik. Cocok untuk pindah lokasi.</summary>
    public void showthroughblack(string id, float duration = -1f)
    {
        id = id.Trim();
        if (!tryget(id, out var sprite))
            return;
        startfade(throughblack(sprite, duration < 0 ? defaultFade : duration));
        CurrentId = id;
    }

    /// <summary>Layar hitam saja. Teks tetap bisa tampil di atasnya (narasi di layar hitam).</summary>
    public void fadetoblack(float duration = -1f)
    {
        startfade(fadeimage(blackOverlay, 1f, duration < 0 ? defaultFade : duration));
    }

    /// <summary>
    /// Zoom/pan ke titik di BG yang sedang tampil.
    /// focus: (0,0) = kiri bawah gambar, (1,1) = kanan atas, (0.5,0.5) = tengah.
    /// </summary>
    public void zoomto(Vector2 focus, float scale, float duration = 1f)
    {
        if (zoomRoutine != null)
            StopCoroutine(zoomRoutine);
        zoomRoutine = StartCoroutine(zoom(focus, scale, duration));
    }

    public void resetzoom(float duration = 0.5f)
    {
        zoomto(new Vector2(0.5f, 0.5f), 1f, duration);
    }

    // ---------- Internal ----------

    void zoomfromtag(string value)
    {
        // format: x, y, skala[, durasi]
        string[] p = value.Split(',');
        if (p.Length < 3)
        {
            Debug.LogWarning("[BackgroundManager] Format #zoom salah. Contoh: #zoom: 0.75, 0.55, 2");
            return;
        }

        float x = parse(p[0], 0.5f), y = parse(p[1], 0.5f), s = parse(p[2], 1f);
        float d = p.Length > 3 ? parse(p[3], 1f) : 1f;
        zoomto(new Vector2(x, y), s, d);
    }

    static float parse(string s, float fallback)
    {
        return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;
    }

    bool tryget(string id, out Sprite sprite)
    {
        if (lookup.TryGetValue(id, out sprite))
            return true;
        Debug.LogWarning($"[BackgroundManager] BG id '{id}' belum didaftarkan di list Backgrounds.");
        return false;
    }

    void startfade(IEnumerator routine)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        BlocksDialogue = false;
        fadeRoutine = StartCoroutine(wrap(routine));
    }

    IEnumerator wrap(IEnumerator routine)
    {
        yield return routine;
        fadeRoutine = null;
    }

    IEnumerator crossfade(Sprite next, float duration)
    {
        // Kalau layar sedang hitam, buka hitamnya sekalian
        if (blackOverlay != null && blackOverlay.color.a > 0f)
            StartCoroutine(fadeimage(blackOverlay, 0f, duration));

        // BG baru disiapkan di layer belakang, BG lama (depan) di-fade out
        stopzoom();
        backLayer.sprite = next;
        resettransform(backLayer.rectTransform);
        fitcover(backLayer);
        setalpha(backLayer, 1f);

        yield return fadeimage(frontLayer, 0f, duration);

        // Tukar: BG baru pindah ke layer depan
        frontLayer.sprite = next;
        resettransform(frontLayer.rectTransform);
        fitcover(frontLayer);
        setalpha(frontLayer, 1f);
        setalpha(backLayer, 0f);
    }

    IEnumerator throughblack(Sprite next, float duration)
    {
        BlocksDialogue = true;
        yield return fadeimage(blackOverlay, 1f, duration);

        stopzoom();
        frontLayer.sprite = next;
        resettransform(frontLayer.rectTransform);
        fitcover(frontLayer);
        setalpha(frontLayer, 1f);
        setalpha(backLayer, 0f);

        yield return new WaitForSeconds(0.2f);
        yield return fadeimage(blackOverlay, 0f, duration);
        BlocksDialogue = false;
    }

    IEnumerator fadeimage(Image img, float target, float duration)
    {
        if (img == null)
            yield break;
        float start = img.color.a;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            setalpha(img, Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }

        setalpha(img, target);
    }

    IEnumerator zoom(Vector2 focus, float scale, float duration)
    {
        RectTransform rt = frontLayer.rectTransform;
        Vector3 startScale = rt.localScale;
        Vector2 startPos = rt.anchoredPosition;

        Vector3 endScale = Vector3.one * scale;
        Vector2 endPos = (new Vector2(0.5f, 0.5f) - focus) * rt.rect.size * scale;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            rt.localScale = Vector3.Lerp(startScale, endScale, k);
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, k);
            yield return null;
        }

        rt.localScale = endScale;
        rt.anchoredPosition = endPos;
        zoomRoutine = null;
    }

    void stopzoom()
    {
        if (zoomRoutine != null)
            StopCoroutine(zoomRoutine);
        zoomRoutine = null;
    }

    void Start()
    {
        // Ulangi setelah Canvas Scaler selesai menghitung ukuran layar
        fitcover(frontLayer);
        fitcover(backLayer);
    }

    /// <summary>
    /// Bikin BG menutupi layar penuh TANPA gepeng (seperti "cover" di CSS).
    /// Kalau rasio gambar beda dengan layar, sisi yang kelebihan akan terpotong sedikit,
    /// bukan ditarik/di-stretch.
    /// </summary>
    static void fitcover(Image img)
    {
        if (img == null || img.sprite == null)
            return;

        RectTransform rt = img.rectTransform;
        RectTransform parent = rt.parent as RectTransform;

        // ukuran layar, kalau belum kebaca pakai 1920x1080
        float screenWidth = 1920f;
        float screenHeight = 1080f;
        if (parent != null && parent.rect.width > 0f && parent.rect.height > 0f)
        {
            screenWidth = parent.rect.width;
            screenHeight = parent.rect.height;
        }

        float imgRatio = img.sprite.rect.width / img.sprite.rect.height;
        float screenRatio = screenWidth / screenHeight;

        float width;
        float height;
        if (imgRatio > screenRatio)
        {
            // gambar lebih lebar: potong kiri-kanan
            height = screenHeight;
            width = screenHeight * imgRatio;
        }
        else
        {
            // gambar lebih tinggi: potong atas-bawah
            width = screenWidth;
            height = screenWidth / imgRatio;
        }

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        img.preserveAspect = false;
    }

    static void resettransform(RectTransform rt)
    {
        rt.localScale = Vector3.one;
        rt.anchoredPosition = Vector2.zero;
    }

    static void setalpha(Image img, float a)
    {
        if (img == null)
            return;
        var c = img.color;
        c.a = a;
        img.color = c;
    }
}
