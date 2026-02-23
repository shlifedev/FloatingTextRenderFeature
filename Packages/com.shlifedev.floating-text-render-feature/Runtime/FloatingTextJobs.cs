using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LD.FloatingTextRenderFeature
{
    [BurstCompile]
    public struct UpdateEntriesAndMarkAliveJob : IJobParallelFor
    {
        public float DeltaTime;

        public NativeArray<FloatingTextEntryNative> Entries;
        [WriteOnly] public NativeArray<byte> AliveFlags;

        public void Execute(int i)
        {
            var e = Entries[i];
            e.Elapsed += DeltaTime;
            Entries[i] = e;
            AliveFlags[i] = (byte)(e.Elapsed < e.Duration ? 1 : 0);
        }
    }

    [BurstCompile]
    public struct PrefixSumAliveJob : IJob
    {
        [ReadOnly] public NativeArray<byte> AliveFlags;
        [WriteOnly] public NativeArray<int> AliveOffsets;
        [WriteOnly] public NativeArray<int> AliveCount;

        public void Execute()
        {
            int sum = 0;
            for (int i = 0; i < AliveFlags.Length; i++)
            {
                AliveOffsets[i] = sum;
                sum += AliveFlags[i];
            }

            AliveCount[0] = sum;
        }
    }

    [BurstCompile]
    public struct CompactAliveEntriesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FloatingTextEntryNative> Entries;
        [ReadOnly] public NativeArray<byte> AliveFlags;
        [ReadOnly] public NativeArray<int> AliveOffsets;

        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<FloatingTextEntryNative> Compacted;

        public void Execute(int i)
        {
            if (AliveFlags[i] == 0) return;
            Compacted[AliveOffsets[i]] = Entries[i];
        }
    }

    [BurstCompile]
    public struct BuildWriteOffsetsJob : IJob
    {
        [ReadOnly] public NativeArray<FloatingTextEntryNative> Entries;
        [WriteOnly] public NativeArray<int> WriteOffsets;
        [WriteOnly] public NativeArray<int> TotalDigits;

        public void Execute()
        {
            int sum = 0;
            for (int i = 0; i < Entries.Length; i++)
            {
                WriteOffsets[i] = sum;
                sum += Entries[i].DigitCount;
            }

            TotalDigits[0] = sum;
        }
    }

    [BurstCompile]
    public struct EvaluateAnimationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FloatingTextEntryNative> Entries;
        [ReadOnly] public DefaultAnimationParams Params;
        [WriteOnly] public NativeArray<AnimationResult> Results;

        public void Execute(int i)
        {
            var e = Entries[i];
            float t = e.Elapsed / e.Duration;

            // EaseOutQuad for Y offset
            float yOffset = Params.MoveHeight * (1f - (1f - t) * (1f - t));

            // EaseOutBack for scale
            float scalePct = math.saturate(e.Elapsed / Params.ScaleDuration);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = scalePct - 1f;
            float scaleFactor = 1f + c3 * u * u * u + c1 * u * u;

            // EaseInQuad for alpha fade
            float alphaElapsed = e.Elapsed - Params.FadeDelay;
            float alpha;
            if (alphaElapsed <= 0f)
            {
                alpha = 1f;
            }
            else
            {
                float at = math.saturate(alphaElapsed / Params.FadeDuration);
                alpha = 1f - at * at;
            }

            Results[i] = new AnimationResult
            {
                YOffset = yOffset,
                ScaleFactor = scaleFactor,
                Alpha = alpha,
            };
        }
    }

    [BurstCompile]
    public struct BuildDigitMatricesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FloatingTextEntryNative> Entries;
        [ReadOnly] public NativeArray<AnimationResult> AnimResults;
        [ReadOnly] public NativeArray<int> WriteOffsets;
        [ReadOnly] public float DigitSize;
        [ReadOnly] public float DigitWidth;
        [ReadOnly] public bool UseAtlas;

        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<DigitOutput> Output;

        public void Execute(int i)
        {
            var e = Entries[i];
            var anim = AnimResults[i];
            float scale = DigitSize * e.BaseScale * anim.ScaleFactor;
            float alpha = anim.Alpha;

            int digitLen = e.DigitCount;
            long packed = e.PackedDigits;

            float originX = e.OriginPos.x;
            float originY = e.OriginPos.y + anim.YOffset;
            float originZ = e.OriginPos.z;

            int baseOffset = WriteOffsets[i];

            for (int di = 0; di < digitLen; di++)
            {
                int charIdx = (int)((packed >> ((digitLen - 1 - di) * 4)) & 0xF);
                float wx = originX + (di - (digitLen - 1) * 0.5f) * DigitWidth * e.BaseScale;

                // c2.x = alpha  -> shader UNITY_MATRIX_M._m02
                // c2.y = charIndex -> shader UNITY_MATRIX_M._m12 (atlas mode only)
                var matrix = new float4x4(
                    new float4(scale, 0f, 0f, 0f),
                    new float4(0f, scale, 0f, 0f),
                    new float4(alpha, UseAtlas ? (float)charIdx : 0f, scale, 0f),
                    new float4(wx, originY, originZ, 1f)
                );

                Output[baseOffset + di] = new DigitOutput
                {
                    CharIndex = charIdx,
                    Matrix = matrix,
                };
            }
        }
    }
}
