using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LD.FloatingTextRenderFeature.Editor
{
    public class FontSpriteGeneratorWindow : EditorWindow
    {
        private Font _font;
        private int _fontSize = 64;
        private Color _textColor = Color.white;
        private string _characters = "0123456789,.";
        private string _outputPath = "Assets/Sprites/FloatingText";
        private const string FilePrefix = "floating_text_";

        private HelpBox _charCountBox;
        private HelpBox _statusBox;
        private VisualElement _previewSection;
        private VisualElement _previewContainer;
        private Button _generateButton;

        [MenuItem("Window/Floating Text/Font Sprite Generator")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<FontSpriteGeneratorWindow>("Font Sprite Generator");
            wnd.minSize = new Vector2(380, 520);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.paddingTop = 8;
            scrollView.style.paddingBottom = 8;
            scrollView.style.paddingLeft = 12;
            scrollView.style.paddingRight = 12;
            root.Add(scrollView);

            var header = new Label("Font Sprite Generator");
            header.style.fontSize = 18;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 8;
            scrollView.Add(header);

            scrollView.Add(BuildFontSettingsSection());
            scrollView.Add(BuildCharactersSection());
            scrollView.Add(BuildOutputSection());

            _generateButton = new Button(Generate) { text = "Generate Sprites" };
            _generateButton.style.height = 32;
            _generateButton.style.marginTop = 8;
            _generateButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            scrollView.Add(_generateButton);
            UpdateGenerateButtonState();

            _statusBox = new HelpBox("", HelpBoxMessageType.None);
            _statusBox.style.display = DisplayStyle.None;
            _statusBox.style.marginTop = 6;
            scrollView.Add(_statusBox);

            _previewSection = BuildSection("Preview");
            _previewSection.style.display = DisplayStyle.None;
            _previewContainer = new VisualElement();
            _previewContainer.style.flexDirection = FlexDirection.Row;
            _previewContainer.style.flexWrap = Wrap.Wrap;
            _previewSection.Add(_previewContainer);
            scrollView.Add(_previewSection);
        }

        private VisualElement BuildSection(string title)
        {
            var section = new VisualElement();
            section.style.marginTop = 6;
            section.style.marginBottom = 2;
            section.style.paddingTop = 8;
            section.style.paddingBottom = 8;
            section.style.paddingLeft = 10;
            section.style.paddingRight = 10;
            section.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
            ApplyBorder(section, 1, 4, new Color(0.15f, 0.15f, 0.15f, 0.5f));

            var label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 6;
            section.Add(label);

            return section;
        }

        private VisualElement BuildFontSettingsSection()
        {
            var section = BuildSection("Font Settings");

            var fontField = new ObjectField("Font") { objectType = typeof(Font), value = _font };
            fontField.RegisterValueChangedCallback(evt =>
            {
                _font = evt.newValue as Font;
                UpdateGenerateButtonState();
            });
            section.Add(fontField);

            var sizeSlider = new SliderInt("Font Size", 8, 256) { value = _fontSize, showInputField = true };
            sizeSlider.RegisterValueChangedCallback(evt => _fontSize = evt.newValue);
            section.Add(sizeSlider);

            var colorField = new ColorField("Text Color") { value = _textColor };
            colorField.RegisterValueChangedCallback(evt => _textColor = evt.newValue);
            section.Add(colorField);

            return section;
        }

        private VisualElement BuildCharactersSection()
        {
            var section = BuildSection("Characters");

            var charField = new TextField("Characters") { value = _characters };
            charField.RegisterValueChangedCallback(evt =>
            {
                _characters = evt.newValue;
                UpdateCharCount();
            });
            section.Add(charField);

            _charCountBox = new HelpBox($"{GetUniqueChars(_characters).Count} unique character(s)",
                HelpBoxMessageType.None);
            section.Add(_charCountBox);

            return section;
        }

        private VisualElement BuildOutputSection()
        {
            var section = BuildSection("Output");

            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.alignItems = Align.Center;

            var pathField = new TextField("Output Path") { value = _outputPath };
            pathField.style.flexGrow = 1;
            pathField.RegisterValueChangedCallback(evt => _outputPath = evt.newValue);
            pathRow.Add(pathField);

            var browseBtn = new Button(() =>
            {
                string picked = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                if (!string.IsNullOrEmpty(picked))
                {
                    if (picked.StartsWith(Application.dataPath))
                    {
                        _outputPath = "Assets" + picked.Substring(Application.dataPath.Length);
                        pathField.SetValueWithoutNotify(_outputPath);
                    }
                    else
                    {
                        Debug.LogWarning("[FontSpriteGenerator] Output path must be inside the Assets folder.");
                    }
                }
            }) { text = "..." };
            browseBtn.style.width = 30;
            browseBtn.style.marginLeft = 4;
            pathRow.Add(browseBtn);
            section.Add(pathRow);

            return section;
        }

        private void UpdateCharCount()
        {
            _charCountBox.text = $"{GetUniqueChars(_characters).Count} unique character(s)";
        }

        private void UpdateGenerateButtonState()
        {
            _generateButton?.SetEnabled(_font != null);
        }

        private void ShowStatus(string message, HelpBoxMessageType type)
        {
            _statusBox.text = message;
            _statusBox.messageType = type;
            _statusBox.style.display = DisplayStyle.Flex;
        }

        private void RefreshPreview(List<string> paths)
        {
            _previewContainer.Clear();

            foreach (string path in paths)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                var img = new Image { image = tex };
                img.style.width = 64;
                img.style.height = 64;
                img.style.marginRight = 4;
                img.style.marginBottom = 4;
                ApplyBorder(img, 1, 2, new Color(0.3f, 0.3f, 0.3f, 0.5f));
                _previewContainer.Add(img);
            }

            _previewSection.style.display = paths.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void ApplyBorder(VisualElement el, float width, float radius, Color color)
        {
            el.style.borderTopWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width;
            el.style.borderRightWidth = width;
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
            el.style.borderTopColor = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor = color;
            el.style.borderRightColor = color;
        }

        private void Generate()
        {
            _statusBox.style.display = DisplayStyle.None;
            _previewContainer.Clear();
            _previewSection.style.display = DisplayStyle.None;

            if (_font == null)
            {
                ShowStatus("Font is not assigned.", HelpBoxMessageType.Error);
                return;
            }
            if (string.IsNullOrEmpty(_characters))
            {
                ShowStatus("Characters field is empty.", HelpBoxMessageType.Error);
                return;
            }
            if (string.IsNullOrEmpty(_outputPath))
            {
                ShowStatus("Output path is empty.", HelpBoxMessageType.Error);
                return;
            }

            var chars = GetUniqueChars(_characters);
            if (chars.Count == 0)
            {
                ShowStatus("No valid characters to generate.", HelpBoxMessageType.Error);
                return;
            }

            string fullDir = Path.GetFullPath(_outputPath);
            Directory.CreateDirectory(fullDir);

            _font.RequestCharactersInTexture(new string(chars.ToArray()), _fontSize, FontStyle.Normal);

            int generated = 0;
            int skipped = 0;
            var generatedPaths = new List<string>();

            foreach (char c in chars)
            {
                if (!_font.GetCharacterInfo(c, out CharacterInfo info, _fontSize, FontStyle.Normal))
                {
                    Debug.LogWarning($"[FontSpriteGenerator] Character '{c}' not found in font, skipping.");
                    skipped++;
                    continue;
                }

                int glyphW = Mathf.Max(Mathf.Abs(info.maxX - info.minX), 1);
                int glyphH = Mathf.Max(Mathf.Abs(info.maxY - info.minY), 1);

                if (glyphW < 2 && info.advance > 0)
                    glyphW = info.advance;

                var rt = RenderTexture.GetTemporary(glyphW, glyphH, 0, RenderTextureFormat.ARGB32);
                var prevRT = RenderTexture.active;
                RenderTexture.active = rt;

                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, glyphW, glyphH, 0);

                Material fontMat = _font.material;
                fontMat.SetPass(0);

                GL.Begin(GL.QUADS);
                GL.Color(_textColor);

                GL.TexCoord(info.uvTopLeft);
                GL.Vertex3(0, 0, 0);

                GL.TexCoord(info.uvTopRight);
                GL.Vertex3(glyphW, 0, 0);

                GL.TexCoord(info.uvBottomRight);
                GL.Vertex3(glyphW, glyphH, 0);

                GL.TexCoord(info.uvBottomLeft);
                GL.Vertex3(0, glyphH, 0);

                GL.End();
                GL.PopMatrix();

                var charTex = new Texture2D(glyphW, glyphH, TextureFormat.ARGB32, false);
                charTex.ReadPixels(new Rect(0, 0, glyphW, glyphH), 0, 0);
                charTex.Apply();

                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(rt);

                string fileName = $"{FilePrefix}{c}.png";
                string filePath = Path.Combine(_outputPath, fileName);
                byte[] pngData = charTex.EncodeToPNG();
                File.WriteAllBytes(Path.GetFullPath(filePath), pngData);
                generatedPaths.Add(filePath);

                DestroyImmediate(charTex);
                generated++;
            }

            AssetDatabase.Refresh();

            // Configure import settings: Sprite type, Point filter, RGBA32, optimized max size
            int maxSize = Mathf.Max(32, Mathf.NextPowerOfTwo(_fontSize));
            foreach (string path in generatedPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = _fontSize;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;

                    var settings = importer.GetDefaultPlatformTextureSettings();
                    settings.format = TextureImporterFormat.RGBA32;
                    settings.maxTextureSize = maxSize;
                    importer.SetPlatformTextureSettings(settings);

                    importer.SaveAndReimport();
                }
            }

            // Register to Addressables "FloatingTextGroup"
            var aaSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (aaSettings == null)
                aaSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);

            if (aaSettings != null)
            {
                var group = aaSettings.FindGroup("FloatingTextGroup")
                    ?? aaSettings.CreateGroup("FloatingTextGroup", false, false, true,
                        null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

                foreach (string path in generatedPaths)
                {
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        var entry = aaSettings.CreateOrMoveEntry(guid, group, false, false);
                        entry.address = path;
                    }
                }

                aaSettings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
                Debug.Log($"[FontSpriteGenerator] Added {generatedPaths.Count} sprite(s) to Addressables group 'FloatingTextGroup'.");
            }
            else
            {
                Debug.LogWarning("[FontSpriteGenerator] Addressable Asset Settings not found. Please initialize Addressables first.");
            }

            RefreshPreview(generatedPaths);

            string msg = $"Generated {generated} sprite(s) at '{_outputPath}'.";
            if (skipped > 0)
                msg += $" Skipped {skipped} character(s).";

            ShowStatus(msg, skipped > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            Debug.Log($"[FontSpriteGenerator] {msg}");
        }

        private static List<char> GetUniqueChars(string input)
        {
            var seen = new HashSet<char>();
            var result = new List<char>();
            foreach (char c in input)
            {
                if (seen.Add(c))
                    result.Add(c);
            }
            return result;
        }
    }
}
