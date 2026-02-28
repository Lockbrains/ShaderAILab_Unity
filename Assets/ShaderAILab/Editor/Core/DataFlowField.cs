using System;

namespace ShaderAILab.Editor.Core
{
    public enum DataFlowStage
    {
        Attributes,
        Varyings,
        Global
    }

    [Serializable]
    public class DataFlowField
    {
        public string Name;
        public string HLSLType;
        public string Semantic;
        public string DisplayName;
        public DataFlowStage Stage;
        public bool IsRequired;
        public bool IsActive;
        public string Annotation;

        public DataFlowField() { }

        public DataFlowField(string name, string hlslType, string semantic,
            string displayName, DataFlowStage stage, bool isRequired)
        {
            Name = name;
            HLSLType = hlslType;
            Semantic = semantic;
            DisplayName = displayName;
            Stage = stage;
            IsRequired = isRequired;
            IsActive = isRequired;
            Annotation = string.Empty;
        }

        public DataFlowField Clone()
        {
            return new DataFlowField
            {
                Name = Name,
                HLSLType = HLSLType,
                Semantic = Semantic,
                DisplayName = DisplayName,
                Stage = Stage,
                IsRequired = IsRequired,
                IsActive = IsActive,
                Annotation = Annotation
            };
        }
    }
}
