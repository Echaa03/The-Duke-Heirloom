using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;


public class CharacterSprite : MonoBehaviour
{
    //this will show the lists of expressions on the inspector
    [System.Serializable]

    public class Expression
    {
        public string name; //for ink (for eg : *smirk*
        public Sprite sprite;
    }

    public Image image;
    public Expression[] expressions; //lists of expression being used

    public void SetExpression(string expname)
    {
        if(expname == "hide")
        {
            gameObject.SetActive(false);

            return;
        }
        foreach(Expression e in expressions)
        {
            if(e.name == expname)
            {
                image.sprite = e.sprite;
                gameObject.SetActive(true);

                return;
            }
        }
    }

    
}
