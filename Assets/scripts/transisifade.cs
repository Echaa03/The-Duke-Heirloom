using UnityEngine;
using System.Collections;

public class transisifade : MonoBehaviour
{
    public CanvasGroup FadeTrans;
    public float Duration = 5.0f;

    void Start()
    {
        StartCoroutine(Fade(5, 0));
    }

    public IEnumerator Fade(float from, float to)
    {
        float time = 0;
        while(time < Duration)
        {
            time += Time.deltaTime;
            FadeTrans.alpha = Mathf.Lerp(from , to, time/Duration);
            yield return null;
        }
        FadeTrans.alpha = to;
    }
}
