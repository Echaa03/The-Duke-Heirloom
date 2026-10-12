using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using TMPro;
using Ink.Runtime;
using Ink.UnityIntegration;

public class DialogueManager : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text NameText;
    public TMP_Text DialogueText;
    public InkFile inkFile;

    Story story;
    public float Typingspeedyeet = 0.03f;
    bool isTyping = false;
    Coroutine typing;


    [Header("Inspection mode")]
    public InspectionMode inspection; // kalau kosong otomatis ditambahkan
    [TextArea] public string inspectionHint = "<i> Click anything in the document that looks important.</i>";

    const string InspectionFlow = "inspection";
    const string InspectionDoneVar = "inspection_done"; // Ink set ~ inspection_done = true -> inspection selesai
    bool inInspection = false;
    bool clueRunning = false;
    bool clueHadText = false;
    string pendingInspect;   // dari #inspect
    string pendingOpenDoc;   // dari #open
    bool pendingComplete;    // dari #complete


    void Start()
    {
        story = new Story(inkFile.storyJson);

        if (inspection == null) 
            inspection = GetComponent<InspectionMode>();
        if (inspection == null) 
            inspection = gameObject.AddComponent<InspectionMode>();


        inspection.HotspotClicked += OnHotspotClicked;

        NextLine();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isTyping)
        {
            StopCoroutine(typing);
            DialogueText.maxVisibleCharacters = 9099999;
            isTyping = false;
            return;
        }
        NextLine();
    }

    void NextLine()
    {
     
        if (pendingInspect != null)
        {
            string doc = pendingInspect;
            pendingInspect = null;
            StartInspection(doc);
            return;
        }

        // zoom out
        if (inInspection && !clueRunning)
        {
            inspection.ZoomOut();
            return;
        }

        if (!story.canContinue)
        {
            if (inInspection) FinishClue();
            return;
        }

        string line = story.Continue().Trim();
        ReadTags(story.currentTags);

        // baris kosong (misalnya knot yang langsung -> DONE) -> langsung lanjut
        if (line.Length == 0)
        {
            NextLine();
            return;
        }

        if (inInspection) 
            clueHadText = true;
        ShowLine(line);
    }

    void ShowLine(string line)
    {
        int colon = line.IndexOf(':');
        if (colon > 0)
        {
            NameText.text = line.Substring(0, colon).Trim();
            DialogueText.text = line.Substring(colon + 1).Trim();
        }
        else
        {
            NameText.text = "";
            DialogueText.text = line;
        }

        if (typing != null)
            StopCoroutine(typing);

        typing = StartCoroutine(Typingeffectyeet());
    }

    void ReadTags(List <string> tags)
    {
        if (tags == null) 
            
            return;

        foreach (string raw in tags)
        {
            string tag = raw.Trim();
            int colon = tag.IndexOf(':');
            string key = (colon >= 0 ? tag.Substring(0, colon) : tag).Trim().ToLowerInvariant();
            string value = colon >= 0 ? tag.Substring(colon + 1).Trim() : "";

            switch (key)
            {
                case "inspect":  pendingInspect = value; 
                    break;
                case "open":     pendingOpenDoc = value; 
                    break;
                case "evidence": inspection.ShowEvidence(value);
                    break;
                case "complete": pendingComplete = true;
                    break;
            }
        }
    }


    void StartInspection(string docName)
    {
        if (!inspection.Open(docName, (RectTransform)transform, DialogueText.font))
        {
            Debug.LogWarning("[DialogueManager] Inspection '" + docName + "' tidak bisa dibuka, cerita lanjut.");
            NextLine();
            return;
        }

        inInspection = true;
        clueRunning = false;
        pendingOpenDoc = null;
        pendingComplete = false;

        //showing hint
        story.SwitchFlow(InspectionFlow);
        ShowHint();
    }

    void OnHotspotClicked(string hotspotId)
    {
        if (!inInspection || clueRunning || isTyping) 
           
            return;

        string knot = "inspect_" + (hotspotId.StartsWith("HS_") ? hotspotId.Substring(3) : hotspotId);
        try
        {
            story.ChoosePathString(knot);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DialogueManager] Knot '" + knot + "' tidak ada di Ink: " + e.Message);
            return;
        }

        inspection.ZoomTo(hotspotId);
        inspection.MarkSeen(hotspotId);
        inspection.Busy = true;
        clueRunning = true;
        clueHadText = false;
        NextLine();
    }

    // dipanggil waktu baris terakhir sebuah clue sudah di-klik
    void FinishClue()
    {
        clueRunning = false;
        inspection.Busy = false;

        if (pendingComplete || InkFlag(InspectionDoneVar))
        {
            pendingComplete = false;
            pendingOpenDoc = null;
            EndInspection();
            return;
        }

        if (pendingOpenDoc != null)
        {
            string doc = pendingOpenDoc;
            pendingOpenDoc = null;
            if (!inspection.SwitchTo(doc)) 
                inspection.ZoomOut(); // if dok gd den zom out
        }
        else if (clueHadText)
        {
            inspection.ZoomOut();
        }

        ShowHint();
    }

    void EndInspection()
    {
        inInspection = false;
        clueRunning = false;
        if (InkFlag(InspectionDoneVar))
            story.variablesState[InspectionDoneVar] = false; // reset for next inspct

        story.SwitchToDefaultFlow();
        inspection.Close();
        NextLine(); // after this the game continue like usual
    }

    bool InkFlag(string name)
    {
        object v = story.variablesState[name]; 
        if (v is bool b) 
            return b;
        if (v is int i) 
            return i != 0;
        return false;
    }

    void ShowHint()
    {
        if (typing != null) 
            StopCoroutine(typing);

        isTyping = false;
        NameText.text = "";
        DialogueText.text = inspectionHint;
        DialogueText.maxVisibleCharacters = 9099999;
    }

    [ContextMenu("Debug: Skip inspection")]
    void DebugSkipInspection()
    {
        if (inInspection)
            EndInspection();
    }

    IEnumerator Typingeffectyeet()
    {
        //panggil bool typing nya pas text nya mau munucl
        isTyping = true;
        DialogueText.maxVisibleCharacters = 0; //this will hide everythign
        DialogueText.ForceMeshUpdate();

        int total = DialogueText.textInfo.characterCount; //ngitung jumlah length kata2nya

        for (int i = 0; i <= total; i++)
        {
            DialogueText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(Typingspeedyeet);
        }
        isTyping = false;
    }
}
