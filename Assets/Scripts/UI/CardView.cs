using BoardGameSimulator.Poker;
using UnityEngine;
using UnityEngine.UI;

namespace BoardGameSimulator.UI
{
    [RequireComponent(typeof(Image))]
    public class CardView : MonoBehaviour
    {
        [SerializeField] private CardSpriteLibrary spriteLibrary;
        [SerializeField] private bool faceDownByDefault;

        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
            if (faceDownByDefault)
            {
                _image.sprite = spriteLibrary.GetBack();
            }
        }

        public void SetCard(PokerCard card, bool faceUp)
        {
            _image.sprite = faceUp ? spriteLibrary.GetFront(card) : spriteLibrary.GetBack();
        }
    }
}
