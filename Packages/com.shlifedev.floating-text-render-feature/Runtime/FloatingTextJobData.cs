using Unity.Mathematics;
using Unity.Collections;

namespace LD.FloatingTextRenderFeature
{
    public struct FloatingTextEntryNative
    {
        public float3 OriginPos;
        public float Elapsed;
        public float Duration;
        public float BaseScale;
        public FixedList32Bytes<byte> DigitIndices;
    }

    public struct AnimationResult
    {
        public float YOffset;
        public float ScaleFactor;
        public float Alpha;
    }

    public struct DigitOutput
    {
        public int CharIndex;
        public float4x4 Matrix;
    }

    public struct DefaultAnimationParams
    {
        public float MoveHeight;
        public float ScaleDuration;
        public float FadeDelay;
        public float FadeDuration;
    }
}
