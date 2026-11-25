#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools > Duke Heirloom > Set Up Backgrounds
/// Bikin BG_Back, BG_Front, BlackOverlay + BackgroundManager di scene yang aktif,
/// lalu daftarkan SEMUA gambar di Assets/background (ID = nama file tanpa ekstensi).
/// Aman dijalankan ulang: tiap ada BG baru, taruh di folder background lalu jalankan lagi.
/// Bisa di-undo dengan Ctrl+Z.
/// </summary>
public static class BackgroundSetup
{
    const string BgFolder = "Assets/background";
    const string OldBgName = "profoffice"; // RawImage BG lama di scene dialogue

    [MenuItem("Tools/Duke Heirloom/Set Up Backgrounds")]
    static void setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();

        var dialogue = roots.SelectMany(r => r.GetComponentsInChildren<DialogueManager>(true)).FirstOrDefault();
        Canvas canvas = dialogue != null
            ? dialogue.GetComponentInParent<Canvas>(true)
            : roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Backgrounds", "Canvas tidak ditemukan di scene ini.\nBuka scene 'dialogue' dulu.", "OK");
            return;
        }

        canvas = canvas.rootCanvas;
        Transform root = canvas.transform;

        Undo.SetCurrentGroupName("Set Up Backgrounds");
        int undoGroup = Undo.GetCurrentGroup();

        // BG lama (RawImage) -> ambil warna, posisi, dan gambar awal
        Transform oldBg = root.Find(OldBgName);
        Color tint = Color.white;
        string startId = "";
        int bgIndex = 0;
        if (oldBg != null)
        {
            var raw = oldBg.GetComponent<RawImage>();
            if (raw != null)
            {
                tint = raw.color; tint.a = 1f;
                if (raw.texture != null)
                    startId = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(raw.texture));
            }

            bgIndex = oldBg.GetSiblingIndex();
        }

        // 1. Layer BG
        Image back = getorcreateimage(root, "BG_Back", tint);
        Image front = getorcreateimage(root, "BG_Front", tint);
        back.transform.SetSiblingIndex(bgIndex);
        front.transform.SetSiblingIndex(bgIndex + 1);

        // 2. Overlay hitam: di atas BG & karakter, tepat di bawah textbox
        Image black = getorcreateimage(root, "BlackOverlay", new Color(0f, 0f, 0f, 0f));
        if (dialogue != null && dialogue.transform.parent == root)
        {
            int d = dialogue.transform.GetSiblingIndex();
            int b = black.transform.GetSiblingIndex();
            black.transform.SetSiblingIndex(b < d ? d - 1 : d); // tepat sebelum textbox
        }
        else
            black.transform.SetAsLastSibling();

        // 3. Manager
        var manager = roots.SelectMany(r => r.GetComponentsInChildren<BackgroundManager>(true)).FirstOrDefault();
        if (manager == null)
        {
            var go = new GameObject("BackgroundManager");
            Undo.RegisterCreatedObjectUndo(go, "Create BackgroundManager");
            EditorSceneManager.MoveGameObjectToScene(go, scene);
            manager = Undo.AddComponent<BackgroundManager>(go);
        }

        // 4. Daftar semua gambar di Assets/background
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BgFolder });
        var entries = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(p => new
            {
                id = Path.GetFileNameWithoutExtension(p),
                sprite = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault()
            })
            .Where(e => e.sprite != null)
            .OrderBy(e => e.id)
            .ToList();

        var so = new SerializedObject(manager);
        so.FindProperty("backLayer").objectReferenceValue = back;
        so.FindProperty("frontLayer").objectReferenceValue = front;
        so.FindProperty("blackOverlay").objectReferenceValue = black;
        if (!string.IsNullOrEmpty(startId) && string.IsNullOrEmpty(so.FindProperty("startId").stringValue))
            so.FindProperty("startId").stringValue = startId;

        var list = so.FindProperty("backgrounds");
        list.ClearArray();
        for (int i = 0; i < entries.Count; i++)
        {
            list.InsertArrayElementAtIndex(i);
            var el = list.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("id").stringValue = entries[i].id;
            el.FindPropertyRelative("sprite").objectReferenceValue = entries[i].sprite;
        }

        so.ApplyModifiedProperties();

        // Preview di Editor: tampilkan BG awal di layer depan
        string sid = so.FindProperty("startId").stringValue;
        var startEntry = entries.FirstOrDefault(e => e.id == sid);
        if (startEntry != null)
        {
            Undo.RecordObject(front, "BG preview");
            front.sprite = startEntry.sprite;
            front.color = new Color(tint.r, tint.g, tint.b, 1f);
        }

        // 5. Matikan BG lama (tidak dihapus, jaga-jaga)
        if (oldBg != null && oldBg.gameObject.activeSelf)
        {
            Undo.RecordObject(oldBg.gameObject, "Disable old BG");
            oldBg.gameObject.SetActive(false);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = manager.gameObject;

        Debug.Log($"[BackgroundSetup] Selesai. {entries.Count} BG terdaftar, start = '{sid}'. ID: " +
                  string.Join(", ", entries.Select(e => e.id)));
    }

    static Image getorcreateimage(Transform parent, string name, Color color)
    {
        Transform t = parent.Find(name);
        Image img;
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            img = go.GetComponent<Image>();
            img.color = color;
        }
        else
        {
            img = t.GetComponent<Image>();
            if (img == null)
                img = Undo.AddComponent<Image>(t.gameObject);
        }

        var rt = (RectTransform)img.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        img.raycastTarget = false;
        return img;
    }
}
#endif
