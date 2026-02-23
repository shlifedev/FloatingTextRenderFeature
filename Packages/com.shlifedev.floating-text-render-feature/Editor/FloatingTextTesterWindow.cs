using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LD.FloatingTextRenderFeature.Editor
{
    public class FloatingTextTesterWindow : EditorWindow
    {
        [SerializeField] private FloatingTextManager _manager;

        [SerializeField] private int _testDamage = 1234;
        [SerializeField] private float _testDuration = 0.8f;
        [SerializeField] private float _testScale = 1f;
        [SerializeField] private float _testX;
        [SerializeField] private float _testY;
        [SerializeField] private int _testSpawnCount = 10;
        [SerializeField] private float _spamInterval = 0.05f;
        private bool _spamActive;
        private double _nextSpamTime;

        private Label _activeCountLabel;
        private Label _drawCallLabel;
        private Label _statusLabel;

        [MenuItem("Window/Floating Text/Floating Text Tester")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<FloatingTextTesterWindow>("Floating Text Tester");
            wnd.minSize = new Vector2(360, 420);
        }

        private void OnEnable()
        {
            EditorApplication.update += TickSpam;
            Selection.selectionChanged += SyncManagerFromSelection;
            SyncManagerFromSelection();
        }

        private void OnDisable()
        {
            EditorApplication.update -= TickSpam;
            Selection.selectionChanged -= SyncManagerFromSelection;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            var managerField = new ObjectField("Manager") { objectType = typeof(FloatingTextManager), value = _manager };
            managerField.RegisterValueChangedCallback(evt => _manager = evt.newValue as FloatingTextManager);
            root.Add(managerField);

            var damageField = new IntegerField("Damage Value") { value = _testDamage };
            damageField.RegisterValueChangedCallback(evt => _testDamage = Mathf.Max(1, evt.newValue));
            root.Add(damageField);

            var durationSlider = new Slider("Duration", 0.3f, 2.0f) { value = _testDuration, showInputField = true };
            durationSlider.RegisterValueChangedCallback(evt => _testDuration = evt.newValue);
            root.Add(durationSlider);

            var scaleSlider = new Slider("Scale", 0.1f, 3.0f) { value = _testScale, showInputField = true };
            scaleSlider.RegisterValueChangedCallback(evt => _testScale = evt.newValue);
            root.Add(scaleSlider);

            var spawnCountSlider = new SliderInt("Spawn Count", 1, 500) { value = _testSpawnCount, showInputField = true };
            spawnCountSlider.RegisterValueChangedCallback(evt => _testSpawnCount = evt.newValue);
            root.Add(spawnCountSlider);

            var xSlider = new Slider("X Pos", -10f, 10f) { value = _testX, showInputField = true };
            xSlider.RegisterValueChangedCallback(evt => _testX = evt.newValue);
            root.Add(xSlider);

            var ySlider = new Slider("Y Pos", -10f, 10f) { value = _testY, showInputField = true };
            ySlider.RegisterValueChangedCallback(evt => _testY = evt.newValue);
            root.Add(ySlider);

            var spamIntervalSlider = new Slider("Spam Interval", 0.01f, 1.0f) { value = _spamInterval, showInputField = true };
            spamIntervalSlider.RegisterValueChangedCallback(evt => _spamInterval = evt.newValue);
            root.Add(spamIntervalSlider);

            var spawnOnceButton = new Button(SpawnOnce) { text = "Spawn Once" };
            root.Add(spawnOnceButton);

            var spawnManyButton = new Button(SpawnBatch) { text = "Spawn Batch" };
            root.Add(spawnManyButton);

            var spamToggle = new Toggle("Spam Active") { value = _spamActive };
            spamToggle.RegisterValueChangedCallback(evt =>
            {
                _spamActive = evt.newValue;
                _nextSpamTime = EditorApplication.timeSinceStartup;
            });
            root.Add(spamToggle);

            var clearButton = new Button(ClearAll) { text = "Clear All" };
            root.Add(clearButton);

            _activeCountLabel = new Label();
            _drawCallLabel = new Label();
            _statusLabel = new Label();
            root.Add(_activeCountLabel);
            root.Add(_drawCallLabel);
            root.Add(_statusLabel);

            RefreshStatus();
        }

        private void TickSpam()
        {
            if (!_spamActive || _manager == null) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextSpamTime) return;
            _nextSpamTime = now + _spamInterval;

            var rng = Random.insideUnitCircle * 1.5f;
            _manager.Show(new Vector3(_testX + rng.x, _testY + rng.y, 0f), _testDamage, _testDuration, _testScale);
            RefreshStatus();
        }

        private void SpawnOnce()
        {
            if (_manager == null) return;
            _manager.Show(new Vector3(_testX, _testY, 0f), _testDamage, _testDuration, _testScale);
            RefreshStatus();
        }

        private void SpawnBatch()
        {
            if (_manager == null) return;

            for (int i = 0; i < _testSpawnCount; i++)
            {
                var rng = Random.insideUnitCircle * 1.5f;
                _manager.Show(new Vector3(_testX + rng.x, _testY + rng.y, 0f), _testDamage, _testDuration, _testScale);
            }

            RefreshStatus();
        }

        private void ClearAll()
        {
            if (_manager == null) return;
            _manager.ClearAll();
            RefreshStatus();
        }

        private void SyncManagerFromSelection()
        {
            if (Selection.activeGameObject == null) return;
            var selectedManager = Selection.activeGameObject.GetComponent<FloatingTextManager>();
            if (selectedManager == null) return;

            _manager = selectedManager;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_activeCountLabel == null) return;

            if (_manager == null)
            {
                _activeCountLabel.text = "Active Count: -";
                _drawCallLabel.text = "Draw Calls (est): -";
                _statusLabel.text = "Status: Manager not selected";
                return;
            }

            _activeCountLabel.text = "Active Count: " + _manager.ActiveCount;
            _drawCallLabel.text = "Draw Calls (est): " + _manager.ActiveDrawCallEstimate;
            _statusLabel.text = _manager.IsReady ? "Status: Ready" : "Status: Initializing...";
        }
    }
}
