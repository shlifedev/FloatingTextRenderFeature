using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LD.FloatingTextRenderFeature
{
    public class FloatingTextManager : MonoBehaviour
    {
        private const int MaxInstances = 1023;
        private const int MaxCharsPerEntry = 8; // 8 chars × 8 bits = 64 bits (long)

        [Header("Resource")]
        private const string ShaderName = "LD/FloatingTextRenderFeature/TextInstanced";

        [Header("Layout")]
        [Tooltip("Horizontal spacing between each character in world units.")]
        [SerializeField] private float digitWidth = 0.35f;
        [Tooltip("Base scale of each character quad in world units.")]
        [SerializeField] private float digitSize = 0.4f;

        [Header("Animation")]
        [Tooltip("Pluggable animation strategy. If not assigned, uses built-in default.")]
        [SerializeField] private FloatingTextAnimator _animator;
        [Tooltip("Default lifetime in seconds if no duration is specified when calling Show().")]
        [SerializeField] private float defaultDuration = 0.8f;

        [Header("Atlas")]
        [SerializeField] private FloatingTextAtlas _atlas;

        [Header("Test")]
        [SerializeField] private float spamInterval = 0.05f;

        private static FloatingTextManager _instance;
        public static FloatingTextManager Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        // char → atlas index mapping (built from atlas.characters at Initialize)
        private Dictionary<char, int> _charToIndex;

        private struct FloatingTextEntry
        {
            public Vector3 OriginPos;
            public float Elapsed;
            public float Duration;
            public float BaseScale;
            public long PackedChars;
            public byte CharCount;
        }

        private readonly List<FloatingTextEntry> _entries = new();

        private Mesh _quadMesh;

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
        internal Material AtlasMaterial => _atlasMaterial;
        internal Matrix4x4[] AtlasMatrices => _atlasMatrices;
        internal int AtlasCount => _atlasCount;

        private bool _initialized;

#if UNITY_EDITOR
            private string _testText;
        private float _testDuration;
        private float _testScale = 1.5f;
        private float _testX;
        private float _testY;
        private int _testSpawnCount = 10;
        private string _testDurationStr;
        private string _testScaleStr;
        private string _testSpawnCountStr;
        private string _testXStr;
        private string _testYStr;
        private bool _spamActive;
        private float _spamTimer;
#endif

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
#if UNITY_EDITOR
            _testText = "1,234";
            _testDuration = defaultDuration;
            _testDurationStr = _testDuration.ToString("F2");
            _testScaleStr = _testScale.ToString("F2");
            _testSpawnCountStr = _testSpawnCount.ToString();
            _testXStr = _testX.ToString("F1");
            _testYStr = _testY.ToString("F1");
#endif
        }

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            _quadMesh = CreateQuadMesh();

            if (_atlas == null || _atlas.atlasTexture == null)
            {
                Debug.LogError("[FloatingTextManager] FloatingTextAtlas is not assigned or has no texture. Atlas is required.");
                return;
            }

            // Build char → index lookup from atlas
            _charToIndex = new Dictionary<char, int>();
            if (_atlas.characters != null)
            {
                for (int i = 0; i < _atlas.characters.Length; i++)
                    _charToIndex[_atlas.characters[i]] = i;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[FloatingTextManager] Shader not found: {ShaderName}");
                return;
            }

            _atlasMaterial = new Material(shader)
            {
                mainTexture = _atlas.atlasTexture,
                enableInstancing = true,
            };
            _atlasMaterial.SetFloat("_Columns", _atlas.columns);
            _atlasMaterial.SetFloat("_Rows", _atlas.rows);

            // Pass half-padding in UV space so shader can offset into content area
            if (_atlas.atlasTexture != null && _atlas.cellPadding > 0)
            {
                float halfPadU = (_atlas.cellPadding * 0.5f) / _atlas.atlasTexture.width;
                float halfPadV = (_atlas.cellPadding * 0.5f) / _atlas.atlasTexture.height;
                _atlasMaterial.SetVector("_PaddingUV", new Vector4(halfPadU, halfPadV, 0, 0));
            }
            _atlasMatrices = new Matrix4x4[MaxInstances];

            _initialized = true;
            Debug.Log($"[FloatingTextManager] Initialized (Atlas). Columns={_atlas.columns} Rows={_atlas.rows} Characters={_atlas.characters?.Length ?? 0}");
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (!_spamActive) return;
            _spamTimer += Time.deltaTime;
            if (_spamTimer < spamInterval) return;
            _spamTimer = 0f;
            var rng = UnityEngine.Random.insideUnitCircle * 1.5f;
            Show(new Vector3(_testX + rng.x, _testY + rng.y, 0f), _testText, _testDuration, _testScale);
        }
#endif

        private void LateUpdate()
        {
            if (!_initialized || _entries.Count == 0) return;

            _atlasCount = 0;

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
                    PackedChars = e.PackedChars,
                    CharCount = e.CharCount,
                };
                _writeOffsets[i] = totalDigits;
                totalDigits += e.CharCount;
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
                Output = _digitOutputs,
            };
            JobHandle buildHandle = buildJob.Schedule(count, 32, animHandle);
            buildHandle.Complete();

            // ── Phase 5: Scatter DigitOutput → atlas matrices (main thread) ──
            for (int i = 0; i < totalDigits; i++)
            {
                if (_atlasCount >= MaxInstances) break;
                _atlasMatrices[_atlasCount] = ToMatrix(_digitOutputs[i].Matrix);
                _atlasCount++;
            }
        }

        /// <summary>
        /// Show floating text with an integer value (legacy API).
        /// Internally converts to string and uses PackChars.
        /// </summary>
        public void Show(Vector3 worldPos, int damage, float duration = 0f, float scale = 1f)
        {
            if (!_initialized) return;
            int absDamage = Mathf.Abs(damage);
            string text = absDamage.ToString();
            Show(worldPos, text, duration, scale);
        }

        /// <summary>
        /// Show floating text with an arbitrary string.
        /// Each character must exist in the atlas's characters array.
        /// </summary>
        public void Show(Vector3 worldPos, string text, float duration = 0f, float scale = 1f)
        {
            if (!_initialized || string.IsNullOrEmpty(text)) return;
            long packed = PackChars(text, out byte charCount);
            if (charCount == 0) return;
            _entries.Add(new FloatingTextEntry
            {
                OriginPos = worldPos,
                Elapsed = 0f,
                Duration = duration > 0f ? duration : defaultDuration,
                BaseScale = scale,
                PackedChars = packed,
                CharCount = charCount,
            });
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (Selection.activeGameObject != gameObject) return;

            const float panelW = 295f;
            const float panelH = 420f;

            GUILayout.BeginArea(new Rect(10, 10, panelW, panelH));
            GUI.Box(new Rect(0, 0, panelW, panelH), "Floating Text Tester");
            GUILayout.Space(28);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Text:", GUILayout.Width(80));
            _testText = GUILayout.TextField(_testText);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Duration:", GUILayout.Width(80));
            _testDurationStr = GUILayout.TextField(_testDurationStr);
            if (float.TryParse(_testDurationStr, out float dur)) _testDuration = dur;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(80));
            _testScaleStr = GUILayout.TextField(_testScaleStr);
            if (float.TryParse(_testScaleStr, out float scl)) _testScale = scl;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn Count:", GUILayout.Width(80));
            _testSpawnCountStr = GUILayout.TextField(_testSpawnCountStr);
            if (int.TryParse(_testSpawnCountStr, out int cnt)) _testSpawnCount = cnt;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("X Pos:", GUILayout.Width(80));
            _testXStr = GUILayout.TextField(_testXStr);
            if (float.TryParse(_testXStr, out float xv)) _testX = xv;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Y Pos:", GUILayout.Width(80));
            _testYStr = GUILayout.TextField(_testYStr);
            if (float.TryParse(_testYStr, out float yv)) _testY = yv;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (GUILayout.Button("Spawn Once"))
                Show(new Vector3(_testX, _testY, 0f), _testText, _testDuration, _testScale);

            if (GUILayout.Button($"Spawn {_testSpawnCount}"))
            {
                for (int i = 0; i < _testSpawnCount; i++)
                {
                    var rng = UnityEngine.Random.insideUnitCircle * 1.5f;
                    Show(new Vector3(_testX + rng.x, _testY + rng.y, 0f), _testText, _testDuration, _testScale);
                }
            }

            string spamLabel = _spamActive ? "Spam: ON  (Toggle Off)" : "Spam (Toggle On)";
            if (GUILayout.Button(spamLabel))
            {
                _spamActive = !_spamActive;
                _spamTimer = 0f;
            }

            if (GUILayout.Button("Clear All"))
                _entries.Clear();

            GUILayout.Space(6);
            GUILayout.Label($"Active Count: {_entries.Count}");
            GUILayout.Label($"Draw Calls (est): {CountActiveSpriteTypes()}");
            GUILayout.Label(_initialized ? "Status: Ready" : "Status: Initializing...");

            GUILayout.EndArea();
        }
#endif

        private void OnDestroy()
        {
            DisposeNativeArrays();

            if (_atlasMaterial != null) Destroy(_atlasMaterial);
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

        /// <summary>
        /// Pack a string into a long using 8-bit per character (max 8 chars).
        /// Each character is mapped to its atlas index via _charToIndex.
        /// Characters not found in the atlas are skipped.
        /// </summary>
        private long PackChars(string text, out byte charCount)
        {
            long packed = 0;
            byte count = 0;
            for (int i = 0; i < text.Length && count < MaxCharsPerEntry; i++)
            {
                if (_charToIndex.TryGetValue(text[i], out int idx))
                {
                    packed |= (long)(idx & 0xFF) << ((MaxCharsPerEntry - 1 - count) * 8);
                    count++;
                }
            }
            // Align packed chars to LSB so unpacking with charLen works correctly
            if (count < MaxCharsPerEntry)
                packed >>= (MaxCharsPerEntry - count) * 8;
            charCount = count;
            return packed;
        }

        private int CountActiveSpriteTypes()
        {
            return _atlasCount > 0 ? 1 : 0;
        }

    }
}
