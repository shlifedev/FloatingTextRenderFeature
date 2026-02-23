using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace LD.FloatingTextRenderFeature
{
    public class FloatingTextManager : MonoBehaviour
    {
        private const int MaxInstances = 1023;

        [Header("Resource")]
        private const string SpriteKeyPrefix = "Assets/Sprites/FloatingText/floating_text_";
        private const string ShaderName = "LD/FloatingTextRenderFeature/TextInstanced";

        [Header("Layout")]
        [Tooltip("Horizontal spacing between each digit in world units.")]
        [SerializeField] private float digitWidth = 0.35f;
        [Tooltip("Base scale of each digit quad in world units.")]
        [SerializeField] private float digitSize = 0.4f;

        [Header("Animation")]
        [Tooltip("Pluggable animation strategy. If not assigned, uses built-in default.")]
        [SerializeField] private FloatingTextAnimator _animator;
        [Tooltip("Default lifetime in seconds if no duration is specified when calling Show().")]
        [SerializeField] private float defaultDuration = 0.8f;

        [Header("Atlas")]
        [SerializeField] private FloatingTextAtlas _atlas;

        private static readonly char[] SupportedChars =
            { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ',', '.' };

        private static FloatingTextManager _instance;
        public static FloatingTextManager Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private struct FloatingTextEntry
        {
            public Vector3 OriginPos;
            public float Elapsed;
            public float Duration;
            public float BaseScale;
            public long PackedDigits;
            public byte DigitCount;
        }

        private readonly List<FloatingTextEntry> _entries = new();

        private Mesh _quadMesh;
        private Material[] _materialArray;
        private Matrix4x4[][] _charMatrices;
        private int[] _charCounts;

        // Atlas mode
        private bool _useAtlas;
        private Material _atlasMaterial;
        private Matrix4x4[] _atlasMatrices;
        private int _atlasCount;

        // Job System NativeArrays (Persistent, grow-only)
        private NativeArray<FloatingTextEntryNative> _nativeEntries;
        private NativeArray<AnimationResult> _animResults;
        private NativeArray<int> _writeOffsets;
        private NativeArray<DigitOutput> _digitOutputs;
        private int _nativeCapacity;
        private int _digitOutputCapacity;

        internal Mesh QuadMesh => _quadMesh;
        internal Material[] MaterialArray => _materialArray;
        internal Matrix4x4[][] CharMatrices => _charMatrices;
        internal int[] CharCounts => _charCounts;
        internal int SupportedCharCount => SupportedChars.Length;
        internal bool UseAtlas => _useAtlas;
        internal Material AtlasMaterial => _atlasMaterial;
        internal Matrix4x4[] AtlasMatrices => _atlasMatrices;
        internal int AtlasCount => _atlasCount;

        private bool _initialized;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DisposeNativeArrays();
            if (_animator == null)
                _animator = ScriptableObject.CreateInstance<DefaultFloatingTextAnimator>();
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        private async UniTaskVoid InitializeAsync()
        {
            _quadMesh = CreateQuadMesh();

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[FloatingTextManager] Shader not found: {ShaderName}");
                return;
            }

            // Atlas mode: single material, single matrices array
            if (_atlas != null && _atlas.atlasTexture != null)
            {
                _useAtlas = true;
                _atlasMaterial = new Material(shader)
                {
                    mainTexture = _atlas.atlasTexture,
                    enableInstancing = true,
                };
                _atlasMaterial.SetFloat("_Columns", _atlas.columns);
                _atlasMaterial.SetFloat("_Rows", _atlas.rows);
                _atlasMatrices = new Matrix4x4[MaxInstances];

                // Still allocate legacy arrays (needed for CharCounts null-check guard)
                int n = SupportedChars.Length;
                _charMatrices = new Matrix4x4[n][];
                _charCounts = new int[n];
                _materialArray = new Material[n];
                for (int i = 0; i < n; i++)
                    _charMatrices[i] = new Matrix4x4[0];

                _initialized = true;
                Debug.Log($"[FloatingTextManager] Initialized (Atlas mode). Columns={_atlas.columns} Rows={_atlas.rows}");
                return;
            }

            // Legacy mode: per-char materials via Addressables
            int count = SupportedChars.Length;
            _charMatrices = new Matrix4x4[count][];
            _charCounts = new int[count];
            _materialArray = new Material[count];

            for (int i = 0; i < count; i++)
            {
                _charMatrices[i] = new Matrix4x4[MaxInstances];
            }

            for (int i = 0; i < count; i++)
            {
                char c = SupportedChars[i];
                var key = GetSpriteKey(c);
                var handle = Addressables.LoadAssetAsync<Sprite>(key);
                await handle.ToUniTask();
                var sprite = handle.Result;
                if (sprite == null)
                {
                    Debug.LogWarning($"[FloatingTextManager] Sprite load failed: {key}");
                    continue;
                }
                _materialArray[i] = new Material(shader)
                {
                    mainTexture = sprite.texture,
                    enableInstancing = true,
                };
            }

            _initialized = true;
            Debug.Log($"[FloatingTextManager] Initialized. Materials: {CountLoadedMaterials()}/{count}");
        }

        private void LateUpdate()
        {
            if (!_initialized || _entries.Count == 0) return;

            Array.Clear(_charCounts, 0, _charCounts.Length);

            // ── Phase 1: Update elapsed, compact expired entries ──
            float dt = Time.deltaTime;
            int ei = 0;
            while (ei < _entries.Count)
            {
                var entry = _entries[ei];
                entry.Elapsed += dt;
                if (entry.Elapsed >= entry.Duration)
                {
                    int last = _entries.Count - 1;
                    if (ei < last) _entries[ei] = _entries[last];
                    _entries.RemoveAt(last);
                    continue;
                }
                _entries[ei] = entry;
                ei++;
            }

            int count = _entries.Count;
            if (count == 0) return;

            // ── Phase 2: Copy to NativeArrays + prefix sum ──
            EnsureNativeCapacity(count);

            int totalDigits = 0;
            for (int i = 0; i < count; i++)
            {
                var e = _entries[i];
                _nativeEntries[i] = new FloatingTextEntryNative
                {
                    OriginPos = new float3(e.OriginPos.x, e.OriginPos.y, e.OriginPos.z),
                    Elapsed = e.Elapsed,
                    Duration = e.Duration,
                    BaseScale = e.BaseScale,
                    PackedDigits = e.PackedDigits,
                    DigitCount = e.DigitCount,
                };
                _writeOffsets[i] = totalDigits;
                totalDigits += e.DigitCount;
            }

            EnsureDigitOutputCapacity(totalDigits);

            // ── Phase 3: Evaluate animation (Burst job or managed fallback) ──
            JobHandle animHandle = _animator.ScheduleEvaluateBatch(
                _nativeEntries.GetSubArray(0, count),
                _animResults.GetSubArray(0, count));

            // ── Phase 4: Build digit matrices (Burst job, depends on Phase 3) ──
            var buildJob = new BuildDigitMatricesJob
            {
                Entries = _nativeEntries.GetSubArray(0, count),
                AnimResults = _animResults.GetSubArray(0, count),
                WriteOffsets = _writeOffsets.GetSubArray(0, count),
                DigitSize = digitSize,
                DigitWidth = digitWidth,
                UseAtlas = _useAtlas,
                Output = _digitOutputs,
            };
            JobHandle buildHandle = buildJob.Schedule(count, 32, animHandle);
            buildHandle.Complete();

            // ── Phase 5: Scatter DigitOutput → matrices (main thread) ──
            if (_useAtlas)
            {
                _atlasCount = 0;
                for (int i = 0; i < totalDigits; i++)
                {
                    if (_atlasCount >= MaxInstances) break;
                    _atlasMatrices[_atlasCount] = ToMatrix(_digitOutputs[i].Matrix);
                    _atlasCount++;
                }
            }
            else
            {
                for (int i = 0; i < totalDigits; i++)
                {
                    var d = _digitOutputs[i];
                    int cnt = _charCounts[d.CharIndex];
                    if (cnt >= MaxInstances) continue;
                    _charMatrices[d.CharIndex][cnt] = ToMatrix(d.Matrix);
                    _charCounts[d.CharIndex] = cnt + 1;
                }
            }
        }

        public void Show(Vector3 worldPos, int damage, float duration = 0f, float scale = 1f)
        {
            if (!_initialized) return;
            int absDamage = Mathf.Abs(damage);
            long packed = PackDigits(absDamage, out byte digitCount);
            _entries.Add(new FloatingTextEntry
            {
                OriginPos = worldPos,
                Elapsed = 0f,
                Duration = duration > 0f ? duration : defaultDuration,
                BaseScale = scale,
                PackedDigits = packed,
                DigitCount = digitCount,
            });
        }

        public void ClearAll()
        {
            _entries.Clear();
        }

        public int ActiveCount => _entries.Count;
        public int ActiveDrawCallEstimate => CountActiveSpriteTypes();
        public bool IsReady => _initialized;

        private void OnDestroy()
        {
            DisposeNativeArrays();

            if (_atlasMaterial != null) Destroy(_atlasMaterial);

            if (_materialArray != null)
                foreach (var mat in _materialArray)
                    if (mat != null) Destroy(mat);

            if (_quadMesh != null) Destroy(_quadMesh);
            _entries.Clear();
            if (_instance == this) _instance = null;
        }

        private void EnsureNativeCapacity(int required)
        {
            if (_nativeCapacity >= required) return;

            int newCapacity = Mathf.Max(required, _nativeCapacity * 2);
            newCapacity = Mathf.Max(newCapacity, 64);

            if (_nativeEntries.IsCreated) _nativeEntries.Dispose();
            if (_animResults.IsCreated) _animResults.Dispose();
            if (_writeOffsets.IsCreated) _writeOffsets.Dispose();

            _nativeEntries = new NativeArray<FloatingTextEntryNative>(newCapacity, Allocator.Persistent);
            _animResults = new NativeArray<AnimationResult>(newCapacity, Allocator.Persistent);
            _writeOffsets = new NativeArray<int>(newCapacity, Allocator.Persistent);
            _nativeCapacity = newCapacity;
        }

        private void EnsureDigitOutputCapacity(int required)
        {
            if (_digitOutputCapacity >= required) return;

            int newCapacity = Mathf.Max(required, _digitOutputCapacity * 2);
            newCapacity = Mathf.Max(newCapacity, 256);

            if (_digitOutputs.IsCreated) _digitOutputs.Dispose();
            _digitOutputs = new NativeArray<DigitOutput>(newCapacity, Allocator.Persistent);
            _digitOutputCapacity = newCapacity;
        }

        private void DisposeNativeArrays()
        {
            if (_nativeEntries.IsCreated) _nativeEntries.Dispose();
            if (_animResults.IsCreated) _animResults.Dispose();
            if (_writeOffsets.IsCreated) _writeOffsets.Dispose();
            if (_digitOutputs.IsCreated) _digitOutputs.Dispose();
            _nativeCapacity = 0;
            _digitOutputCapacity = 0;
        }

        private static unsafe Matrix4x4 ToMatrix(float4x4 src) => *(Matrix4x4*)&src;

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "FloatingTextQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static string GetSpriteKey(char c) => c switch
        {
            ',' => $"{SpriteKeyPrefix},.png",
            '.' => $"{SpriteKeyPrefix}..png",
            _ => $"{SpriteKeyPrefix}{c}.png",
        };

        private static long PackDigits(int n, out byte digitCount)
        {
            if (n == 0) { digitCount = 1; return 0; }

            long packed = 0;
            byte count = 0;
            while (n > 0)
            {
                packed |= (long)(n % 10) << (count * 4);
                count++;
                n /= 10;
            }
            digitCount = count;
            return packed;
        }

        private int CountLoadedMaterials()
        {
            if (_materialArray == null) return 0;
            int c = 0;
            foreach (var m in _materialArray) if (m != null) c++;
            return c;
        }

        private int CountActiveSpriteTypes()
        {
            if (_useAtlas) return _atlasCount > 0 ? 1 : 0;
            if (_charCounts == null) return 0;
            int c = 0;
            for (int i = 0; i < _charCounts.Length; i++)
                if (_charCounts[i] > 0) c++;
            return c;
        }

    }
}
