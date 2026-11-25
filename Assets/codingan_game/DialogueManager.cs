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
    public InspectionMode inspection; 
    public CharacterSprite Professor;
    [TextArea] public string inspectionHint = "<i> Click anything in the document that looks important.</i>";

    const string InspectionFlow = "inspection";
    const string InspectionDoneVar = "inspection_done"; // Ink set ~ inspection_done = true -> inspection selesai
    bool inInspection = false;
    bool clueRunning = false;
    bool clueHadText = false;
    string pendingInspect;   // dari #inspect
    string pendingOpenDoc;   // dari #open
    bool pendingComplete;    // dari #complete

    // ===== KHUSUS TESTING (cuma jalan di Unity Editor, tidak ikut ke build) =====
    [Header("Testing (Editor only)")]
    [Tooltip("Centang ini supaya semua investigasi (#inspect) dilewati saat Play di Editor.")]
    public bool skipInspections = false;
    [Tooltip("Tombol untuk langsung menyelesaikan investigasi yang sedang terbuka.")]
    public KeyCode skipInspectionKey = KeyCode.F8;


    void Start()
    {
        story = new Story(inkFile.storyJson);

        if (inspection == null) 
            inspection = GetComponent<InspectionMode>();
        if (inspection == null) 
            inspection = gameObject.AddComponent<InspectionMode>();


        inspection.HotspotClicked += onhotspotclicked;

        nextline();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // jangan bisa klik selama pindah lokasi (layar fade hitam)
        if (BackgroundManager.Instance != null && BackgroundManager.Instance.BlocksDialogue)
            return;
        // jangan bisa klik selama telpon masih berdering (nunggu diangkat)
        if (PhoneCallUI.Instance != null && PhoneCallUI.Instance.BlocksDialogue)
            return;

        if (isTyping)
        {
            StopCoroutine(typing);
            DialogueText.maxVisibleCharacters = 9099999;
            isTyping = false;
            return;
        }

        nextline();
    }

    void nextline()
    {
     
        if (pendingInspect != null)
        {
            string doc = pendingInspect;
            pendingInspect = null;
#if UNITY_EDITOR
            if (skipInspections)
            {
                Debug.Log("[DialogueManager] (Testing) Investigasi '" + doc + "' dilewati.");
                nextline();
                return;
            }
#endif
            startinspection(doc);
            return;
        }

        // zoom out
        if (inInspection && !clueRunning)
        {
            inspection.zoomout();
            return;
        }

        if (!story.canContinue)
        {
            if (inInspection)
                finishclue();
            return;
        }

        string line = story.Continue().Trim();
        readtags(story.currentTags);

        // baris kosong (misalnya knot yang langsung -> DONE) -> langsung lanjut
        if (line.Length == 0)
        {
            nextline();
            return;
        }

        if (inInspection) 
            clueHadText = true;
        showline(line);
    }

    void showline(string line)
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

        typing = StartCoroutine(typingeffectyeet());
    }

    void readtags(List <string> tags)
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
                case "evidence": inspection.showevidence(value);
                    break;
                case "complete": pendingComplete = true;
                    break;
                case "prof": Professor.setexpression(value);
                    break;
                default:
                  //PhoneCallUI

                    if (PhoneCallUI.Instance != null && PhoneCallUI.Instance.handletag(key, value))
                        break;

                    //  BackgroundManager

                    if (BackgroundManager.Instance != null)
                        BackgroundManager.Instance.handletag(key, value);
                    break;
            }
        }
    }


    void startinspection(string docName)
    {
        if (!inspection.open(docName, (RectTransform)transform, DialogueText.font))
        {
            Debug.LogWarning("[DialogueManager] Inspection '" + docName + "' tidak bisa dibuka, cerita lanjut.");
            nextline();
            return;
        }

        inInspection = true;
        clueRunning = false;
        pendingOpenDoc = null;
        pendingComplete = false;

        //showing hint
        story.SwitchFlow(InspectionFlow);
        showhint();
    }

    void onhotspotclicked(string hotspotId)
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

        inspection.zoomto(hotspotId);
        inspection.markseen(hotspotId);
        inspection.Busy = true;
        clueRunning = true;
        clueHadText = false;
        nextline();
    }

    // dipanggil waktu baris terakhir sebuah clue sudah di-klik
    void finishclue()
    {
        clueRunning = false;
        inspection.Busy = false;

        if (pendingComplete || inkflag(InspectionDoneVar))
        {
            pendingComplete = false;
            pendingOpenDoc = null;
            endinspection();
            return;
        }

        if (pendingOpenDoc != null)
        {
            string doc = pendingOpenDoc;
            pendingOpenDoc = null;
            if (!inspection.switchto(doc)) 
                inspection.zoomout(); // if dok gd den zom out
        }
        else if (clueHadText)
        {
            inspection.zoomout();
        }

        showhint();
    }

    void endinspection()
    {
        inInspection = false;
        clueRunning = false;
        if (inkflag(InspectionDoneVar))
            story.variablesState[InspectionDoneVar] = false; // reset for next inspct

        story.SwitchToDefaultFlow();
        inspection.close();
        nextline(); // after this the game continue like usual
    }

    bool inkflag(string name)
    {
        object v = story.variablesState[name]; 
        if (v is bool b) 
            return b;
        if (v is int i) 
            return i != 0;
        return false;
    }

    void showhint()
    {
        if (typing != null) 
            StopCoroutine(typing);

        isTyping = false;
        NameText.text = "";
        DialogueText.text = inspectionHint;
        DialogueText.maxVisibleCharacters = 9099999;
    }

#if UNITY_EDITOR
    void Update()
    {
        if (inInspection && skipkeypressed())
        {
            Debug.Log("[DialogueManager] (Testing) Investigasi diselesaikan lewat tombol " + skipInspectionKey + ".");
            debugskipinspection();
        }
    }

    bool skipkeypressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null)
            return false;
        switch (skipInspectionKey)
        {
            case KeyCode.F8:  return kb.f8Key.wasPressedThisFrame;
            case KeyCode.F9:  return kb.f9Key.wasPressedThisFrame;
            case KeyCode.F10: return kb.f10Key.wasPressedThisFrame;
            case KeyCode.F12: return kb.f12Key.wasPressedThisFrame;
            default:          return kb.f8Key.wasPressedThisFrame;
        }
#else
        return Input.GetKeyDown(skipInspectionKey);
#endif
    }
#endif

    [ContextMenu("Debug: Skip inspection")]
    void debugskipinspection()
    {
        if (!inInspection)
            return;

        // hentikan clue / typing yang masih jalan supaya tidak nyangkut
        if (typing != null)
            StopCoroutine(typing);
        isTyping = false;
        clueRunning = false;
        pendingOpenDoc = null;
        pendingComplete = false;
        inspection.Busy = false;

        endinspection();
    }

    IEnumerator typingeffectyeet()
    {
        //panggil bool typing nya pas text nya mau munucl
        isTyping = true;
        DialogueText.maxVisibleCharacters = 0; //this will hide everythign

        // tunggu sampai transisi BG lewat hitam selesai, baru teks diketik
        while (BackgroundManager.Instance != null && BackgroundManager.Instance.BlocksDialogue)
            yield return null;
        // tunggu telpon diangkat dulu (nada sambung + bunyi klik), baru teks diketik
        while (PhoneCallUI.Instance != null && PhoneCallUI.Instance.BlocksDialogue)
            yield return null;

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
