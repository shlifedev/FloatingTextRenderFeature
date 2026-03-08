using UnityEngine;

namespace LD.FloatingTextRenderFeature
{
    [CreateAssetMenu(fileName = "FloatingTextAtlas", menuName = "FloatingText/Atlas")]
    public class FloatingTextAtlas : ScriptableObject
    {
        public Texture2D atlasTexture;
        public int columns;
        public int rows;
        public int cellPadding;
        public char[] characters;
    }
}
