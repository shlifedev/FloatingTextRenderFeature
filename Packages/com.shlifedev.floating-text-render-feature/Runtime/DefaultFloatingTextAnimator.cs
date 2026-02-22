using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace LD.FloatingTextRenderFeature
{
    /// <summary>
    /// Default animation: float up (EaseOutQuad), pop-in scale (EaseOutBack), fade out (EaseInQuad).
    /// </summary>
    [CreateAssetMenu(menuName = "Floating Text/Default Animator")]
    public class DefaultFloatingTextAnimator : FloatingTextAnimator
    {
        [Tooltip("Total vertical distance the text floats upward.")]
        [SerializeField] private float moveHeight = 0.8f;

        [Tooltip("Duration of the pop-in scale animation (EaseOutBack).")]
        [SerializeField] private float scaleDuration = 0.3f;

        [Tooltip("Delay in seconds before the fade-out begins.")]
        [SerializeField] private float fadeDelay = 0.4f;

        [Tooltip("Duration of the fade-out alpha transition after fadeDelay.")]
        [SerializeField] private float fadeDuration = 0.4f;

        public override void Evaluate(float elapsed, float duration,
            out float yOffset, out float scaleFactor, out float alpha)
        {
            float t = elapsed / duration;
            yOffset = moveHeight * EaseOutQuad(t);

            float scalePct = Mathf.Clamp01(elapsed / scaleDuration);
            scaleFactor = EaseOutBack(scalePct);

            float alphaElapsed = elapsed - fadeDelay;
            alpha = alphaElapsed <= 0f
                ? 1f
                : 1f - EaseInQuad(Mathf.Clamp01(alphaElapsed / fadeDuration));
        }

        public override JobHandle ScheduleEvaluateBatch(
            NativeArray<FloatingTextEntryNative> entries,
            NativeArray<AnimationResult> results)
        {
            return new EvaluateAnimationJob
            {
                Entries = entries,
                Params = new DefaultAnimationParams
                {
                    MoveHeight = moveHeight,
                    ScaleDuration = scaleDuration,
                    FadeDelay = fadeDelay,
                    FadeDuration = fadeDuration,
                },
                Results = results,
            }.Schedule(entries.Length, 64);
        }

        private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static float EaseInQuad(float t) => t * t;
    }
}
