using System;
using System.Collections.Generic;

namespace ShaderAILab.Editor.Core
{
    public enum ShaderSectionType
    {
        Properties,
        Constants,
        Vertex,
        Fragment,
        Helper,
        Global,
        Unknown
    }

    [Serializable]
    public class ShaderBlock
    {
        public string Id;
        public string Title;
        public string Intent;
        public string Code;
        public ShaderSectionType Section;
        public List<string> ReferencedParams;
        public int StartLine;
        public int EndLine;
        public bool IsEnabled;

        public ShaderBlock()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            ReferencedParams = new List<string>();
            Section = ShaderSectionType.Unknown;
            Code = string.Empty;
            Title = string.Empty;
            Intent = string.Empty;
            IsEnabled = true;
        }

        public ShaderBlock(string title, string intent, ShaderSectionType section) : this()
        {
            Title = title;
            Intent = intent;
            Section = section;
        }
    }
}
