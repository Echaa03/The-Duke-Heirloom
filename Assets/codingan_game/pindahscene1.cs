using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    public string nextScene = "dialogue";
    public float delay = 10f;
    public float Fadetime = 1.0f;
    public CanvasGroup Text;

  
    IEnumerator Start()
    {
        yield return new WaitForSeconds(delay);

        float time = 0;
        while(time < Fadetime)
        {
            time += Time.deltaTime;
            Text.alpha = 1 - time / Fadetime;
            yield return null;
            
        }
        SceneManager.LoadScene(nextScene);
    }
}