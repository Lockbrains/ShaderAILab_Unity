using UnityEditor;
using UnityEngine;

namespace ShaderAILab.Editor.LLM
{
    /// <summary>
    /// Stores LLM provider configuration. API keys are kept in EditorPrefs
    /// (not serialized to disk / git). Non-sensitive settings are ScriptableObject fields.
    /// </summary>
    public class LLMSettings : ScriptableObject
    {
        const string AssetPath = "Assets/ShaderAILab/Editor/Resources/LLMSettings.asset";

        // Provider selection
        public LLMProviderType ActiveProvider = LLMProviderType.OpenAI;
        public string ActiveModel = "gpt-5.2";
        public float Temperature = 0.2f;
        public int MaxTokens = 8192;

        // Base URLs (non-sensitive)
        public string OpenAIBaseUrl = "https://api.openai.com/v1";
        public string AnthropicBaseUrl = "https://api.anthropic.com/v1";
        public string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        public string OllamaBaseUrl = "http://localhost:11434";

        // API keys stored in EditorPrefs for security
        public string OpenAIApiKey
        {
            get => EditorPrefs.GetString("ShaderAILab_OpenAI_Key", "");
            set => EditorPrefs.SetString("ShaderAILab_OpenAI_Key", value);
        }

        public string AnthropicApiKey
        {
            get => EditorPrefs.GetString("ShaderAILab_Anthropic_Key", "");
            set => EditorPrefs.SetString("ShaderAILab_Anthropic_Key", value);
        }

        public string GeminiApiKey
        {
            get => EditorPrefs.GetString("ShaderAILab_Gemini_Key", "");
            set => EditorPrefs.SetString("ShaderAILab_Gemini_Key", value);
        }

        static LLMSettings _instance;

        public static LLMSettings GetOrCreate()
        {
            if (_instance != null) return _instance;

            _instance = Resources.Load<LLMSettings>("LLMSettings");
            if (_instance != null) return _instance;

            _instance = CreateInstance<LLMSettings>();

#if UNITY_EDITOR
            string dir = System.IO.Path.GetDirectoryName(AssetPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(_instance, AssetPath);
            AssetDatabase.SaveAssets();
#endif

            return _instance;
        }

        public void Save()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
#endif
        }
    }

    public enum LLMProviderType
    {
        OpenAI,
        Anthropic,
        Gemini,
        Ollama
    }
}
