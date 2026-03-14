using System.Collections;
using TMPro;
using UnityEngine;

namespace BoardGameSimulator.UI
{
    public class ChipAnimator : MonoBehaviour
    {
        [SerializeField] private TMP_Text potText;
        [SerializeField] private float animDuration = 0.25f;

        public void AnimatePot(int oldValue, int newValue)
        {
            StopAllCoroutines();
            StartCoroutine(Animate(oldValue, newValue));
        }

        private IEnumerator Animate(int from, int to)
        {
            var elapsed = 0f;
            while (elapsed < animDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / animDuration);
                var value = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
                potText.text = $"底池: {value}";
                yield return null;
            }

            potText.text = $"底池: {to}";
        }
    }
}
