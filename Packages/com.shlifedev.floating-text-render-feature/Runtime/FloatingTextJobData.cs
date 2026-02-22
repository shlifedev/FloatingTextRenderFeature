using Unity.Mathematics;

namespace LD.FloatingTextRenderFeature
{
    public struct FloatingTextEntryNative
    {
        public float3 OriginPos;
        public float Elapsed;
        public float Duration;
        public float BaseScale;
        public long PackedDigits;
        public byte DigitCount;
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
