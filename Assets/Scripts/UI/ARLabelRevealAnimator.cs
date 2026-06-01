using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class ARLabelRevealAnimator : MonoBehaviour
{
    [SerializeField] private float durationSeconds = 0.28f;
    [SerializeField] private float startScale = 0.90f;

    private Coroutine revealRoutine;

    public void Play(float delaySeconds = 0f)
    {
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
        }

        revealRoutine = StartCoroutine(Reveal(delaySeconds));
    }

    private IEnumerator Reveal(float delaySeconds)
    {
        Vector3 targetScale = transform.localScale;
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        transform.localScale = targetScale * Mathf.Clamp(startScale, 0.1f, 1f);
        canvasGroup.alpha = 0f;

        if (delaySeconds > 0f)
        {
            yield return new WaitForSeconds(delaySeconds);
        }

        float duration = Mathf.Max(0.01f, durationSeconds);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.localScale = Vector3.Lerp(targetScale * startScale, targetScale, t);
            canvasGroup.alpha = t;
            yield return null;
        }

        transform.localScale = targetScale;
        canvasGroup.alpha = 1f;
        revealRoutine = null;
    }
}
