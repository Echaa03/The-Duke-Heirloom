#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools > Duke Heirloom > Set Up Phone Call
/// Bikin UI telpon (dari layer Figma "UI phone screen") di scene yang aktif dan sambungkan ke textbox.
///
/// 1. Export layer Figma (PNG, 2x) ke folder  Assets/UI Assets/Phone
/// 2. SFX ada di Assets/Audio/Phone (ringback, pickup, hangup, glitch). Boleh diganti file lain
///    asal namanya masih mengandung kata ring / pickup / hangup / glitch.
/// 3. Buka scene dialogue, klik menu ini. Aman dijalankan ulang (setting di Inspector tetap disimpan).
///
/// Ukuran & posisi di bawah ini diambil dari frame Figma (satuan px Figma = unit Canvas 1920x1080).
/// </summary>
public static class PhoneCallSetup
{
    const string SpriteFolder = "Assets/UI Assets/Phone";
    const string AudioFolder = "Assets/Audio/Phone";
    const string RootName = "PhoneCall";

    // ukuran gambar HP normal di Figma
    static readonly Vector2 PhoneSize = new Vector2(656f, 1080f);
    // layer gradasi / nama (414x900), relatif ke tengah HP
    static readonly Vector2 ScreenPos = new Vector2(47f, -33f);
    static readonly Vector2 ScreenSize = new Vector2(414f, 900f);
    // bar merah-biru (Group 1)
    static readonly Vector2 BarsPos = new Vector2(31f, -30f);
    static readonly Vector2 BarsSize = new Vector2(425f, 861f);
    // layar HP sebenarnya (untuk scanline)
    static readonly Vector2 ScanPos = new Vector2(42f, -17f);
    static readonly Vector2 ScanSize = new Vector2(392f, 850f);
    // HP miring (Group 2)
    static readonly Vector2 TiltSize = new Vector2(763.6f, 1080f);
    static readonly Vector2 GlitchFullPos = new Vector2(106.2f, -9.9f);
    static readonly Vector2 GlitchFullSize = new Vector2(492.7f, 900.7f);
    static readonly Vector2 TiltNamePos = new Vector2(12f, -22f);
    static readonly Vector2 TiltNameSize = new Vector2(506.3f, 935f);
    // teks status ("calling mobile..." / 00:42) dari atas layer nama
    const float StatusTop = 93f;
    const float StatusFontSize = 21f;

    [MenuItem("Tools/Duke Heirloom/Set Up Phone Call")]
    static void setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();

        var dialogue = roots.SelectMany(r => r.GetComponentsInChildren<DialogueManager>(true)).FirstOrDefault();
        if (dialogue == null || dialogue.DialogueText == null)
        {
            EditorUtility.DisplayDialog("Phone Call", "DialogueManager (dengan Dialogue Text) tidak ditemukan.\nBuka scene 'dialogue' dulu.", "OK");
            return;
        }

        Canvas canvas = dialogue.DialogueText.GetComponentInParent<Canvas>(true).rootCanvas;
        Transform root = canvas.transform;

        // textbox = object paling atas (anak langsung Canvas) yang berisi Dialogue Text
        Transform tb = dialogue.DialogueText.transform;
        while (tb.parent != null && tb.parent != root)
            tb = tb.parent;
        RectTransform textbox = tb.parent == root ? (RectTransform)tb : null;

        var sprites = loadsprites();
        var clips = loadclips();
        var font = findfont();
        var scanTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteFolder + "/_scanlines.png");

        Undo.SetCurrentGroupName("Set Up Phone Call");
        int undoGroup = Undo.GetCurrentGroup();

        // simpan setting lama (layout, teks, timer, dll) kalau sudah pernah dibuat
        string oldJson = null;
        Transform old = root.Find(RootName);
        if (old != null)
        {
            var oldUi = old.GetComponent<PhoneCallUI>();
            if (oldUi != null)
                oldJson = EditorJsonUtility.ToJson(oldUi);
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        // ---- hierarchy ----
        var holder = newrect(RootName, root);
        stretch(holder);
        if (textbox != null)
            holder.SetSiblingIndex(textbox.GetSiblingIndex()); // di bawah textbox, di atas BG & BlackOverlay

        var phoneRoot = newrect("PhoneRoot", holder);
        place(phoneRoot, Vector2.zero, PhoneSize);
        var group = Undo.AddComponent<CanvasGroup>(phoneRoot.gameObject);
        group.blocksRaycasts = false;
        group.interactable = false;

        var shaker = newrect("Shaker", phoneRoot);
        stretch(shaker);

        // HP normal
        var normal = newrect("Normal", shaker);
        stretch(normal);
        makeimg("Phone", normal, Vector2.zero, PhoneSize, pick(sprites, "hp-normal"));
        var screen = newrect("Screen", normal);
        place(screen, ScreenPos, ScreenSize);
        makeimg("Gradient", screen, Vector2.zero, ScreenSize, pick(sprites, "gradasi"));
        var nameImg = makeimg("Name", screen, Vector2.zero, ScreenSize, pick(sprites, "nama-saja-normal"));

        var ghostCyan = makestatus("StatusGhostCyan", screen, font, new Color(0.2f, 0.95f, 1f, 0.75f));
        var ghostRed = makestatus("StatusGhostRed", screen, font, new Color(1f, 0.2f, 0.45f, 0.75f));
        var status = makestatus("Status", screen, font, new Color(1f, 1f, 1f, 0.85f));
        status.text = "calling mobile...";

        var bars = makeimg("GlitchBars", normal, BarsPos, BarsSize, pick(sprites, "bar-merah-biru"));
        RawImage scan = null;
        if (scanTex != null)
        {
            var scanRt = newrect("Scanlines", normal);
            place(scanRt, ScanPos, ScanSize);
            scan = Undo.AddComponent<RawImage>(scanRt.gameObject);
            scan.texture = scanTex;
            scan.raycastTarget = false;
            scan.color = new Color(1f, 1f, 1f, 0.55f);
            scan.uvRect = new Rect(0f, 0f, 1f, ScanSize.y / 3f); // 1 garis tiap 3 px
        }

        // HP miring (kedipan glitch)
        GameObject tiltedGo = null;
        Sprite tiltedSprite = pick(sprites, "hp-glitch-miring");
        if (tiltedSprite != null)
        {
            var tilted = newrect("Tilted", shaker);
            stretch(tilted);
            makeimg("PhoneTilted", tilted, Vector2.zero, TiltSize, tiltedSprite);
            // hp-glitch-miring-bersih_2 sudah termasuk overlay glitch-3-layar-penuh (dirender satu grup dari Figma)
            Sprite full = pick(sprites, "layar-penuh");
            if (full != null)
                makeimg("GlitchFull", tilted, GlitchFullPos, GlitchFullSize, full);
            makeimg("NameTilted", tilted, TiltNamePos, TiltNameSize, pick(sprites, "teks-phd-dude-glitch", "glitch 2"));
            tiltedGo = tilted.gameObject;
            tiltedGo.SetActive(false);
        }

        // ---- komponen ----
        var ui = Undo.AddComponent<PhoneCallUI>(holder.gameObject);
        if (oldJson != null)
            EditorJsonUtility.FromJsonOverwrite(oldJson, ui);

        var ring = Undo.AddComponent<AudioSource>(holder.gameObject);
        ring.playOnAwake = false; ring.loop = true; ring.volume = 0.6f;
        var sfx = Undo.AddComponent<AudioSource>(holder.gameObject);
        sfx.playOnAwake = false;

        var so = new SerializedObject(ui);
        set(so, "phoneRoot", phoneRoot);
        set(so, "phoneGroup", group);
        set(so, "shaker", shaker);
        set(so, "normalStack", normal.gameObject);
        set(so, "nameImage", nameImg);
        set(so, "statusText", status);
        set(so, "statusGhostCyan", ghostCyan);
        set(so, "statusGhostRed", ghostRed);
        set(so, "nameGlitchSprite", pick(sprites, "nama-saja-glitch"));
        set(so, "glitchBars", bars.sprite != null ? bars : null);
        set(so, "scanlines", scan);
        set(so, "tiltedStack", tiltedGo);
        set(so, "textbox", textbox);
        set(so, "ringSource", ring);
        set(so, "sfxSource", sfx);
        set(so, "ringClip", pickclip(clips, "ring"));
        set(so, "pickupClip", pickclip(clips, "pickup", "angkat"));
        set(so, "hangupClip", pickclip(clips, "hangup", "tutup"));
        set(so, "glitchClip", pickclip(clips, "glitch"));
        so.ApplyModifiedProperties();

        if (bars.sprite == null)
            bars.gameObject.SetActive(false);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = holder.gameObject;

        // laporan
        var missing = new List<string>();
        if (pick(sprites, "hp-normal") == null)
            missing.Add("hp-normal-bersih (WAJIB)");
        if (pick(sprites, "gradasi") == null)
            missing.Add("gradasi-atas-gelap (WAJIB)");
        if (pick(sprites, "nama-saja-normal") == null)
            missing.Add("nama-saja-normal (WAJIB)");
        if (pick(sprites, "nama-saja-glitch") == null)
            missing.Add("nama-saja-glitch (glitch)");
        if (pick(sprites, "bar-merah-biru") == null)
            missing.Add("glitch-2-bar-merah-biru (glitch)");
        if (tiltedSprite == null)
            missing.Add("hp-glitch-miring-bersih_2 (glitch parah)");
        if (pick(sprites, "teks-phd-dude-glitch", "glitch 2") == null)
            missing.Add("teks-phd-dude-glitch 2 (glitch parah)");
        if (textbox == null)
            missing.Add("textbox tidak ketemu: textbox tidak akan dikecilkan");

        string msg = "UI telpon sudah dipasang di Canvas > " + RootName + ".";
        if (missing.Count > 0)
            msg += "\n\nBelum ada:\n- " + string.Join("\n- ", missing) +
                   "\n\nExport layer-nya dari Figma ke " + SpriteFolder + ", lalu jalankan menu ini lagi.";
        EditorUtility.DisplayDialog("Phone Call", msg, "OK");
    }

    // ---------- aset ----------

    static List<Sprite> loadsprites()
    {
        var list = new List<Sprite>();
        if (!AssetDatabase.IsValidFolder(SpriteFolder))
            return list;

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null)
                continue;

            if (Path.GetFileName(path).StartsWith("_scanlines"))
            {
                // tekstur garis: diulang, tidak di-blur
                if (imp.textureType != TextureImporterType.Default || imp.wrapMode != TextureWrapMode.Repeat || imp.filterMode != FilterMode.Point)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.wrapMode = TextureWrapMode.Repeat;
                    imp.filterMode = FilterMode.Point;
                    imp.alphaIsTransparency = true;
                    imp.mipmapEnabled = false;
                    imp.SaveAndReimport();
                }

                continue;
            }

            if (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled || imp.maxTextureSize < 4096)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.maxTextureSize = 4096;
                imp.SaveAndReimport();
            }

            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null)
                list.Add(s);
        }

        return list;
    }

    static Sprite pick(List<Sprite> sprites, string key, string prefer = null)
    {
        var hits = sprites.Where(s => norm(s.name).Contains(norm(key))).ToList();
        if (hits.Count == 0)
            return null;
        if (prefer != null)
        {
            var p = hits.FirstOrDefault(s => norm(s.name).Contains(norm(prefer)));
            if (p != null)
                return p;
        }

        return hits[0];
    }

    static List<AudioClip> loadclips()
    {
        var list = new List<AudioClip>();
        if (!AssetDatabase.IsValidFolder(AudioFolder))
            return list;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (c != null)
                list.Add(c);
        }

        return list;
    }

    static AudioClip pickclip(List<AudioClip> clips, params string[] keys)
    {
        foreach (string k in keys)
        {
            var c = clips.FirstOrDefault(x => norm(x.name).Contains(norm(k)));
            if (c != null)
                return c;
        }

        return null;
    }

    static TMP_FontAsset findfont()
    {
        foreach (string name in new[] { "Inter", "LiberationSans SDF" })
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:TMP_FontAsset"))
            {
                var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null && !f.name.Contains("Fallback"))
                    return f;
            }
        }

        return TMP_Settings.defaultFontAsset;
    }

    static string norm(string s) => s.ToLowerInvariant().Replace(" ", "").Replace("_", "-");

    // ---------- bikin object ----------

    static RectTransform newrect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static void place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static Image makeimg(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite)
    {
        var rt = newrect(name, parent);
        place(rt, pos, size);
        var img = Undo.AddComponent<Image>(rt.gameObject);
        img.sprite = sprite;
        img.raycastTarget = false; // jangan halangi klik dialog
        if (sprite == null)
            img.color = new Color(1f, 1f, 1f, 0f);
        return img;
    }

    static TextMeshProUGUI makestatus(string name, Transform parent, TMP_FontAsset font, Color color)
    {
        var rt = newrect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -StatusTop);
        rt.sizeDelta = new Vector2(0f, 36f);

        var t = Undo.AddComponent<TextMeshProUGUI>(rt.gameObject);
        if (font != null)
            t.font = font;
        t.fontSize = StatusFontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.text = "";
        return t;
    }

    static void set(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null)
            p.objectReferenceValue = value;
        else
            Debug.LogWarning("[PhoneCallSetup] Field '" + prop + "' tidak ada di PhoneCallUI.");
    }
}
#endif
