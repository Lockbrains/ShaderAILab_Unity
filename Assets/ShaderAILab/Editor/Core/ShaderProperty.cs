using System;

namespace ShaderAILab.Editor.Core
{
    public enum ShaderPropertyType
    {
        Float,
        Range,
        Color,
        Vector,
        Texture2D,
        Texture3D,
        Cubemap,
        Int
    }

    [Serializable]
    public class ShaderProperty
    {
        public string Name;
        public string DisplayName;
        public ShaderPropertyType PropertyType;
        public string DefaultValue;
        public float MinValue;
        public float MaxValue;
        public string Role;
        public string RawDeclaration;

        /// <summary>
        /// For texture properties: the built-in default ("white", "black", "bump", "gray", "red").
        /// Used in the Properties block declaration, e.g. _MainTex("Tex", 2D) = "white" {}
        /// </summary>
        public string DefaultTexture;

        public ShaderProperty()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            DefaultValue = string.Empty;
            Role = string.Empty;
            RawDeclaration = string.Empty;
            DefaultTexture = string.Empty;
            MinValue = 0f;
            MaxValue = 1f;
        }

        public bool IsNumeric =>
            PropertyType == ShaderPropertyType.Float ||
            PropertyType == ShaderPropertyType.Range ||
            PropertyType == ShaderPropertyType.Int;

        public bool IsTexture =>
            PropertyType == ShaderPropertyType.Texture2D ||
            PropertyType == ShaderPropertyType.Texture3D ||
            PropertyType == ShaderPropertyType.Cubemap;
    }
}
