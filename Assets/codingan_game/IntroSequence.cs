using UnityEngine;
using System.Collections;
using JetBrains.Annotations;
using Unity.VisualScripting;

public class IntroSequence : MonoBehaviour
{
    public CanvasGroup Textbox;
    public float Fadetime = 2f;


    IEnumerator Start()
    {
        Textbox.alpha = 0;
        Textbox.blocksRaycasts = false;

        float time = 0;
        while(time < Fadetime)
        {
            time += Time.deltaTime;
            Textbox.alpha = time / Fadetime;
            yield return null;
        }
        Textbox.alpha = 1;
        Textbox.blocksRaycasts = true;
    }
      










    


}
