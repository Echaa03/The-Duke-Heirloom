#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tombol "Preview" di Inspector BackgroundManager.
/// Klik salah satu BG -> Scene view langsung menampilkan BG itu, supaya kamu bisa
/// mengatur posisi karakter/UI di atasnya. Tidak mengubah jalannya game:
/// saat Play, BG awal tetap diambil dari Start Id.
/// </summary>
[CustomEditor(typeof(BackgroundManager))]
public class BackgroundManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        serializedObject.Update();
        var front = serializedObject.FindProperty("frontLayer").objectReferenceValue as Image;
        var list = serializedObject.FindProperty("backgrounds");
        string startId = serializedObject.FindProperty("startId").stringValue;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Preview BG di Scene view (tidak mempengaruhi game)", EditorStyles.boldLabel);

        if (front == null)
        {
            EditorGUILayout.HelpBox("Front Layer belum diisi.", MessageType.Warning);
            return;
        }

        for (int i = 0; i < list.arraySize; i++)
        {
            var el = list.GetArrayElementAtIndex(i);
            string id = el.FindPropertyRelative("id").stringValue;
            var sprite = el.FindPropertyRelative("sprite").objectReferenceValue as Sprite;
            if (sprite == null)
                continue;

            bool isShown = front.sprite == sprite;
            string label = (isShown ? "▶ " : "") + id + (id == startId ? "   (start)" : "");

            using (new EditorGUI.DisabledScope(isShown))
            {
                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    Undo.RecordObject(front, "Preview BG");
                    front.sprite = sprite;
                    var c = front.color; c.a = 1f; front.color = c;
                    EditorUtility.SetDirty(front);
                    SceneView.RepaintAll();
                }
            }
        }
    }
}
#endif
