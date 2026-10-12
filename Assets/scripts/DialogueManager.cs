using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using Ink.Runtime;                     

public class DialogueManager : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text NameText;
    public TMP_Text DialogueText;
    public TextAsset inkJSON;         

    Story story;                     

    void Start()
    {
        story = new Story(inkJSON.text);
        NextLine();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
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
    }
}