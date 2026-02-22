using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace LD.FloatingTextRenderFeature
{
    /// <summary>
    /// Abstract base for pluggable floating-text animation strategies.
    /// Subclass this ScriptableObject to implement custom easing, motion, or fade curves.
    /// </summary>
    public abstract class FloatingTextAnimator : ScriptableObject
    {
        /// <summary>
        /// Evaluate animation values at the given time.
        /// </summary>
        /// <param name="elapsed">Seconds since spawn.</param>
        /// <param name="duration">Total lifetime of this entry.</param>
        /// <param name="yOffset">Vertical offset in world units.</param>
        /// <param name="scaleFactor">Scale multiplier (applied on top of digitSize * baseScale).</param>
        /// <param name="alpha">Opacity (0-1).</param>
        public abstract void Evaluate(float elapsed, float duration,
            out float yOffset, out float scaleFactor, out float alpha);

        /// <summary>
        /// Batch-evaluate animation for all entries. Override to provide a Burst-compiled job.
        /// Default implementation falls back to a managed loop calling Evaluate() per entry.
        /// </summary>
        public virtual JobHandle ScheduleEvaluateBatch(
            NativeArray<FloatingTextEntryNative> entries,
            NativeArray<AnimationResult> results)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                Evaluate(e.Elapsed, e.Duration, out float y, out float s, out float a);
                results[i] = new AnimationResult { YOffset = y, ScaleFactor = s, Alpha = a };
            }
            return default;
        }
    }
}
