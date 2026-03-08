using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LD.FloatingTextRenderFeature.Editor
{
    public class FontSpriteGeneratorWindow : EditorWindow
    {
        [Serializable]
        private class ExtraSpriteEntry
        {
            public string key = "";
            public Texture2D sprite;
        }

        private Font _font;
        private int _fontSize = 64;
        private Color _textColor = Color.white;
        private string _characters = "0123456789,.";
        private string _outputPath = "Assets/Sprites/FloatingText";
        private HelpBox _charCountBox;
        private HelpBox _statusBox;
        private Button _generateButton;
        private VisualElement _previewSection;
        private Image _previewImage;

        private readonly List<ExtraSpriteEntry> _extraSprites = new();
        private VisualElement _extraSpritesListContainer;

        [MenuItem("Window/Floating Text/Font Sprite Generator")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<FontSpriteGeneratorWindow>("Font Sprite Generator");
            wnd.minSize = new Vector2(380, 620);
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
            scrollView.Add(BuildExtraSpritesSection());
            scrollView.Add(BuildOutputSection());

            _generateButton = new Button(Generate) { text = "Generate Atlas" };
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
            _previewImage = new Image();
            _previewImage.style.maxWidth = 350;
            _previewImage.style.maxHeight = 350;
            _previewImage.scaleMode = ScaleMode.ScaleToFit;
            _previewImage.style.marginTop = 4;
            ApplyBorder(_previewImage, 1, 2, new Color(0.3f, 0.3f, 0.3f, 0.5f));
            _previewSection.Add(_previewImage);
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

        private VisualElement BuildExtraSpritesSection()
        {
            var section = BuildSection("Extra Sprites");

            var desc = new HelpBox(
                "Add custom sprites (icons, emoji, etc.) to the atlas. " +
                "Each entry needs a unique character key used to reference it at runtime.",
                HelpBoxMessageType.Info);
            section.Add(desc);

            _extraSpritesListContainer = new VisualElement();
            section.Add(_extraSpritesListContainer);

            var addBtn = new Button(() =>
            {
                _extraSprites.Add(new ExtraSpriteEntry());
                RebuildExtraSpritesList();
            }) { text = "+ Add Extra Sprite" };
            addBtn.style.marginTop = 4;
            section.Add(addBtn);

            return section;
        }

        private void RebuildExtraSpritesList()
        {
            _extraSpritesListContainer.Clear();

            for (int i = 0; i < _extraSprites.Count; i++)
            {
                int idx = i;
                var entry = _extraSprites[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;
                row.style.marginBottom = 2;

                var keyLabel = new Label("Key:");
                keyLabel.style.width = 28;
                keyLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                keyLabel.style.marginRight = 4;
                row.Add(keyLabel);

                var keyField = new TextField { value = entry.key, maxLength = 1 };
                keyField.style.width = 40;
                keyField.tooltip = "A single character used to reference this sprite at runtime (e.g. Unicode character)";
                keyField.RegisterValueChangedCallback(evt =>
                {
                    _extraSprites[idx].key = evt.newValue;
                });
                row.Add(keyField);

                var spriteField = new ObjectField("Sprite") { objectType = typeof(Texture2D), value = entry.sprite };
                spriteField.style.flexGrow = 1;
                spriteField.RegisterValueChangedCallback(evt =>
                {
                    _extraSprites[idx].sprite = evt.newValue as Texture2D;
                });
                row.Add(spriteField);

                var removeBtn = new Button(() =>
                {
                    _extraSprites.RemoveAt(idx);
                    RebuildExtraSpritesList();
                }) { text = "-" };
                removeBtn.style.width = 24;
                removeBtn.style.marginLeft = 4;
                row.Add(removeBtn);

                _extraSpritesListContainer.Add(row);
            }
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

            int skipped = 0;
            var glyphTextures = new List<Texture2D>();
            var validChars = new List<char>();

            // --- 1st pass: collect global font metrics for baseline alignment ---
            int globalMinY = int.MaxValue;
            int globalMaxY = int.MinValue;
            var charInfos = new List<(char c, CharacterInfo info)>();

            foreach (char c in chars)
            {
                if (!_font.GetCharacterInfo(c, out CharacterInfo info, _fontSize, FontStyle.Normal))
                {
                    Debug.LogWarning($"[FontSpriteGenerator] Character '{c}' not found in font, skipping.");
                    skipped++;
                    continue;
                }

                charInfos.Add((c, info));

                if (info.minY < globalMinY) globalMinY = info.minY;
                if (info.maxY > globalMaxY) globalMaxY = info.maxY;
            }

            int uniformH = Mathf.Max(globalMaxY - globalMinY, 1);

            // --- 2nd pass: render each glyph at correct baseline position ---
            foreach (var (c, info) in charInfos)
            {
                int glyphW = Mathf.Max(Mathf.Abs(info.maxX - info.minX), 1);
                int glyphH = Mathf.Max(Mathf.Abs(info.maxY - info.minY), 1);

                if (glyphW < 2 && info.advance > 0)
                    glyphW = info.advance;

                // Render into uniform-height texture for baseline alignment
                int cellH = uniformH;
                var rt = RenderTexture.GetTemporary(glyphW, cellH, 0, RenderTextureFormat.ARGB32);
                var prevRT = RenderTexture.active;
                RenderTexture.active = rt;

                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, glyphW, cellH, 0);

                Material fontMat = _font.material;
                fontMat.SetPass(0);

                // Calculate vertical position: offset from top based on baseline
                int yPos = globalMaxY - info.maxY;

                GL.Begin(GL.QUADS);
                GL.Color(_textColor);

                GL.TexCoord(info.uvTopLeft);
                GL.Vertex3(0, yPos, 0);

                GL.TexCoord(info.uvTopRight);
                GL.Vertex3(glyphW, yPos, 0);

                GL.TexCoord(info.uvBottomRight);
                GL.Vertex3(glyphW, yPos + glyphH, 0);

                GL.TexCoord(info.uvBottomLeft);
                GL.Vertex3(0, yPos + glyphH, 0);

                GL.End();
                GL.PopMatrix();

                var charTex = new Texture2D(glyphW, cellH, TextureFormat.ARGB32, false);
                charTex.ReadPixels(new Rect(0, 0, glyphW, cellH), 0, 0);
                charTex.Apply();

                RenderTexture.active = prevRT;
                RenderTexture.ReleaseTemporary(rt);

                glyphTextures.Add(charTex);
                validChars.Add(c);
            }

            // Determine cell size from font glyphs only
            int maxGlyphW = 0, maxGlyphH = 0;
            foreach (var tex in glyphTextures)
            {
                if (tex.width > maxGlyphW) maxGlyphW = tex.width;
                if (tex.height > maxGlyphH) maxGlyphH = tex.height;
            }

            // Append extra sprites (resized to fit glyph cell size)
            int extraSkipped = 0;
            foreach (var entry in _extraSprites)
            {
                if (string.IsNullOrEmpty(entry.key) || entry.sprite == null)
                {
                    extraSkipped++;
                    continue;
                }

                char key = entry.key[0];

                // Check for duplicate key
                if (validChars.Contains(key))
                {
                    Debug.LogWarning($"[FontSpriteGenerator] Extra sprite key '{key}' duplicates an existing character, skipping.");
                    extraSkipped++;
                    continue;
                }

                // Make the texture readable, then resize if larger than cell size
                var readableTex = MakeReadable(entry.sprite);
                if (maxGlyphW > 0 && maxGlyphH > 0 && (readableTex.width > maxGlyphW || readableTex.height > maxGlyphH))
                {
                    var resized = ResizeTexture(readableTex, maxGlyphW, maxGlyphH);
                    DestroyImmediate(readableTex);
                    readableTex = resized;
                }
                glyphTextures.Add(readableTex);
                validChars.Add(key);
            }

            if (glyphTextures.Count > 0)
            {
                GenerateAtlas(validChars, glyphTextures);
                foreach (var tex in glyphTextures)
                    DestroyImmediate(tex);
            }

            int generated = glyphTextures.Count;
            string msg = $"Atlas generated with {generated} character(s) at '{_outputPath}'.";
            if (skipped > 0)
                msg += $" Skipped {skipped} font character(s).";
            if (extraSkipped > 0)
                msg += $" Skipped {extraSkipped} extra sprite(s).";

            ShowStatus(msg, (skipped > 0 || extraSkipped > 0) ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            Debug.Log($"[FontSpriteGenerator] {msg}");

            RefreshAtlasPreview();
        }

        private static Texture2D MakeReadable(Texture2D source)
        {
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }

        private static Texture2D ResizeTexture(Texture2D source, int maxWidth, int maxHeight)
        {
            float aspectRatio = (float)source.width / source.height;
            int newWidth, newHeight;

            if (source.width >= source.height)
            {
                newWidth = maxWidth;
                newHeight = Mathf.Max(1, Mathf.RoundToInt(maxWidth / aspectRatio));
                if (newHeight > maxHeight)
                {
                    newHeight = maxHeight;
                    newWidth = Mathf.Max(1, Mathf.RoundToInt(maxHeight * aspectRatio));
                }
            }
            else
            {
                newHeight = maxHeight;
                newWidth = Mathf.Max(1, Mathf.RoundToInt(maxHeight * aspectRatio));
                if (newWidth > maxWidth)
                {
                    newWidth = maxWidth;
                    newHeight = Mathf.Max(1, Mathf.RoundToInt(maxWidth / aspectRatio));
                }
            }

            var rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);

            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            var resized = new Texture2D(newWidth, newHeight, TextureFormat.ARGB32, false);
            resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            return resized;
        }

        private void RefreshAtlasPreview()
        {
            string atlasPath = Path.Combine(_outputPath, "floating_text_atlas.png");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (tex != null)
            {
                _previewImage.image = tex;
                _previewSection.style.display = DisplayStyle.Flex;
            }
            else
            {
                _previewSection.style.display = DisplayStyle.None;
            }
        }

        private const int CellPadding = 4;

        private void GenerateAtlas(List<char> chars, List<Texture2D> textures)
        {
            if (textures.Count == 0) return;

            // Determine cell size (max of all glyphs)
            int cellW = 0, cellH = 0;
            foreach (var tex in textures)
            {
                if (tex.width > cellW) cellW = tex.width;
                if (tex.height > cellH) cellH = tex.height;
            }

            // Add padding to cell size
            int paddedCellW = cellW + CellPadding;
            int paddedCellH = cellH + CellPadding;

            int charCount = textures.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(charCount));
            int rows = Mathf.CeilToInt((float)charCount / cols);

            int atlasW = cols * paddedCellW;
            int atlasH = rows * paddedCellH;

            var atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);

            // Clear to transparent
            var clearPixels = new Color[atlasW * atlasH];
            atlas.SetPixels(clearPixels);

            // Copy each glyph into grid position
            for (int i = 0; i < charCount; i++)
            {
                int col = i % cols;
                int row = i / cols;

                // Row 0 = top of atlas in our grid, but texture Y=0 is bottom
                int pixelX = col * paddedCellW + CellPadding / 2;
                int pixelY = (rows - 1 - row) * paddedCellH + CellPadding / 2;

                var src = textures[i];
                var srcPixels = src.GetPixels();

                // Center glyph in cell
                int offsetX = (cellW - src.width) / 2;
                int offsetY = (cellH - src.height) / 2;

                for (int sy = 0; sy < src.height; sy++)
                {
                    for (int sx = 0; sx < src.width; sx++)
                    {
                        atlas.SetPixel(pixelX + offsetX + sx, pixelY + offsetY + sy, srcPixels[sy * src.width + sx]);
                    }
                }
            }

            atlas.Apply();

            // Save atlas PNG
            string atlasFileName = "floating_text_atlas.png";
            string atlasPath = Path.Combine(_outputPath, atlasFileName);
            byte[] pngData = atlas.EncodeToPNG();
            File.WriteAllBytes(Path.GetFullPath(atlasPath), pngData);
            DestroyImmediate(atlas);

            AssetDatabase.Refresh();

            // Configure atlas texture import settings
            var importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.spriteImportMode = SpriteImportMode.None;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = true;

                var settings = importer.GetDefaultPlatformTextureSettings();
                settings.format = TextureImporterFormat.RGBA32;
                settings.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(atlasW, atlasH)));
                importer.SetPlatformTextureSettings(settings);

                importer.SaveAndReimport();
            }

            // Create or update FloatingTextAtlas ScriptableObject
            string soPath = Path.Combine(_outputPath, "FloatingTextAtlas.asset");
            var atlasAsset = AssetDatabase.LoadAssetAtPath<FloatingTextAtlas>(soPath);
            if (atlasAsset == null)
            {
                atlasAsset = ScriptableObject.CreateInstance<FloatingTextAtlas>();
                AssetDatabase.CreateAsset(atlasAsset, soPath);
            }

            atlasAsset.atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            atlasAsset.columns = cols;
            atlasAsset.rows = rows;
            atlasAsset.cellPadding = CellPadding;
            atlasAsset.characters = chars.ToArray();

            EditorUtility.SetDirty(atlasAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[FontSpriteGenerator] Atlas generated: {atlasPath} ({cols}x{rows}, cell {cellW}x{cellH}, {charCount} chars)");
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
