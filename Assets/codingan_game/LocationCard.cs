using System.Collections;
using UnityEngine;

/// <summary>
/// Location title card ("11 PM / LAW FIRM").
/// Drops in slightly from above while fading in, holds, then fades out.
/// Put the torn paper Image and the title text as children of this object.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class LocationCard : MonoBehaviour
{
    [Header("Timing (seconds)")]
    public float startDelay = 0.5f;
    public float fadeInTime = 0.8f;
    public float holdTime = 3f;
    public float fadeOutTime = 1f;

    [Header("Motion")]
    [Tooltip("How far (in canvas pixels) the card settles down while fading in.")]
    public float dropDistance = 14f;

    CanvasGroup group;
    RectTransform rect;
    Vector2 restPosition;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        rect = transform as RectTransform;
        if (rect != null)
            restPosition = rect.anchoredPosition;

        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    IEnumerator Start()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        // Fade in + settle
        float t = 0f;
        while (t < fadeInTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeInTime));
            group.alpha = k;
            if (rect != null)
                rect.anchoredPosition = restPosition + Vector2.up * dropDistance * (1f - k);
            yield return null;
        }

        group.alpha = 1f;
        if (rect != null)
            rect.anchoredPosition = restPosition;

        yield return new WaitForSeconds(holdTime);

        // Fade out
        t = 0f;
        while (t < fadeOutTime)
        {
            t += Time.deltaTime;
            group.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeOutTime));
            yield return null;
        }

        group.alpha = 0f;
    }
}
