using UnityEngine;
using System.Collections;
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


    void Start()
    {
        story = new Story(inkFile.storyJson);
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
        if (!story.canContinue) 
            return;         

        string line = story.Continue().Trim();    

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

        if (isTyping != null)
            StopCoroutine(typing);
    }

    IEnumerator Typingeffectyeet()
    {
        //panggil bool typing nya pas text nya mau munucl
        isTyping = true;
        DialogueText.maxVisibleCharacters = 0; //this will hide everythign
        DialogueText.ForceMeshUpdate();

         int total = DialogueText.textInfo.characterCount; //ngitung jumlah length kata2nya 

        for(int i = 0; i < total; i++)
        {
            DialogueText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(Typingspeedyeet);
        }
        isTyping = false;
    }
}