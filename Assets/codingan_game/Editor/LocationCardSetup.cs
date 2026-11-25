#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools > Duke Heirloom > Set Up Location Card
/// Groups the torn paper + title text under one "LocationCard" object,
/// applies the dark-brown ink colour and a slight tilt, and adds the fade.
/// Everything is undoable with Ctrl+Z.
/// </summary>
public static class LocationCardSetup
{
    const string PaperPath = "Assets/UI Assets/Textbox_Torn_Night.png";
    static readonly Color32 Ink = new Color32(0x22, 0x19, 0x0E, 0xFF); // same brown as the dialogue text
    const float TextTilt = -2f;                                          // follows the front sheet's angle

    [MenuItem("Tools/Duke Heirloom/Set Up Location Card")]
    static void setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var paperSprite = AssetDatabase.LoadAllAssetsAtPath(PaperPath).OfType<Sprite>().FirstOrDefault();
        if (paperSprite == null)
        {
            EditorUtility.DisplayDialog("Location Card", "Couldn't find a sprite at\n" + PaperPath +
                "\n\nMake sure its Texture Type is 'Sprite (2D and UI)'.", "OK");
            return;
        }

        List<T> All<T>() where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToList();

        // 1. Title text: the selected object, otherwise the text that contains "PM".
        TMP_Text title = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInChildren<TMP_Text>(true) : null;
        if (title == null)
            title = All<TMP_Text>().FirstOrDefault(t => t.text != null && t.text.ToUpper().Contains("PM"));
        if (title == null)
        {
            EditorUtility.DisplayDialog("Location Card", "Couldn't find the title text.\nSelect your \"11 PM / Law Firm\" text object and run this again.", "OK");
            return;
        }

        // 2. Torn paper image already in the scene (if any).
        Image paper = All<Image>().FirstOrDefault(i => i.sprite != null && i.sprite.texture == paperSprite.texture);

        Undo.SetCurrentGroupName("Set Up Location Card");
        int undoGroup = Undo.GetCurrentGroup();

        // 3. Root object that owns the fade.
        LocationCard card = title.GetComponentInParent<LocationCard>(true);
        if (card == null)
        {
            var go = new GameObject("LocationCard", typeof(RectTransform), typeof(CanvasGroup), typeof(LocationCard));
            Undo.RegisterCreatedObjectUndo(go, "Create LocationCard");
            var rt = (RectTransform)go.transform;
            var source = (RectTransform)(paper != null ? paper.transform : title.transform);
            rt.SetParent(source.parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.position = source.position;
            rt.sizeDelta = source.rect.size;
            int index = source.GetSiblingIndex();
            if (title.transform.parent == source.parent)
                index = Mathf.Min(index, title.transform.GetSiblingIndex());
            rt.SetSiblingIndex(index);
            card = go.GetComponent<LocationCard>();
        }

        // 4. Create the paper if it isn't in the scene yet.
        if (paper == null)
        {
            var pgo = new GameObject("TornPaper", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(pgo, "Create TornPaper");
            var prt = (RectTransform)pgo.transform;
            prt.SetParent(card.transform, false);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(paperSprite.rect.width, paperSprite.rect.height) * 0.8f;
            paper = pgo.GetComponent<Image>();
            paper.sprite = paperSprite;
            paper.preserveAspect = true;
        }

        // 5. Put paper (behind) and text (in front) inside the card. World positions are kept.
        if (paper.transform.parent != card.transform)
            Undo.SetTransformParent(paper.transform, card.transform, "Parent paper");
        if (title.transform.parent != card.transform)
            Undo.SetTransformParent(title.transform, card.transform, "Parent title");
        paper.transform.SetAsFirstSibling();
        title.transform.SetAsLastSibling();

        Undo.RecordObject(paper, "Paper settings");
        paper.raycastTarget = false;

        // 6. Text styling: dark-brown ink, centred, slight tilt to follow the paper.
        Undo.RecordObject(title, "Title style");
        title.color = Ink;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        Undo.RecordObject(title.rectTransform, "Title tilt");
        title.rectTransform.localEulerAngles = new Vector3(0f, 0f, TextTilt);

        // 7. The text's own fade animation would fight the card fade, so turn it off.
        var anim = title.GetComponent<Animator>();
        if (anim != null)
        {
            Undo.RecordObject(anim, "Disable title animator");
            anim.enabled = false;
        }

        var textGroup = title.GetComponent<CanvasGroup>();
        if (textGroup != null)
        {
            Undo.RecordObject(textGroup, "Title alpha");
            textGroup.alpha = 1f;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = card.gameObject;
        Debug.Log("[LocationCard] Set up on '" + card.name + "'. Press Play to preview, Ctrl+S to save the scene.");
    }
}
#endif
