using UnityEditor;
using UnityEngine;

namespace ShaderAILab.Editor.LLM
{
    public class LLMSettingsWindow : EditorWindow
    {
        string _openaiKey;
        string _anthropicKey;
        string _geminiKey;
        Vector2 _scrollPos;
        bool _showOpenAIKey;
        bool _showAnthropicKey;
        bool _showGeminiKey;

        static readonly string[] OpenAIModels    = { "gpt-5.2", "gpt-5.2-pro", "gpt-5-mini", "gpt-4.1", "gpt-4o" };
        static readonly string[] AnthropicModels = { "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5", "claude-sonnet-4-20250514" };
        static readonly string[] GeminiModels    = { "gemini-3.1-pro-preview", "gemini-3-pro-preview", "gemini-3-flash-preview", "gemini-2.5-pro", "gemini-2.5-flash" };
        static readonly string[] OllamaModels    = { "qwen2.5-coder:32b", "deepseek-coder:33b", "codellama:34b", "llama3:8b" };

        [MenuItem("ShaderAILab/LLM Settings")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<LLMSettingsWindow>("LLM Settings");
            wnd.minSize = new Vector2(420, 600);
        }

        void OnEnable()
        {
            var s = LLMSettings.GetOrCreate();
            _openaiKey = s.OpenAIApiKey;
            _anthropicKey = s.AnthropicApiKey;
            _geminiKey = s.GeminiApiKey;
        }

        void OnGUI()
        {
            var settings = LLMSettings.GetOrCreate();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.LabelField("LLM Provider Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            // Provider selection
            settings.ActiveProvider = (LLMProviderType)EditorGUILayout.EnumPopup("Active Provider", settings.ActiveProvider);

            // Model dropdown based on provider
            EditorGUILayout.Space(4);
            string[] modelList = GetModelsForProvider(settings.ActiveProvider);
            int currentIdx = System.Array.IndexOf(modelList, settings.ActiveModel);
            if (currentIdx < 0) currentIdx = 0;
            int newIdx = EditorGUILayout.Popup("Model", currentIdx, modelList);
            settings.ActiveModel = modelList[newIdx];

            // Or type custom model name
            settings.ActiveModel = EditorGUILayout.TextField("Custom Model ID", settings.ActiveModel);

            // Temperature & tokens
            EditorGUILayout.Space(4);
            settings.Temperature = EditorGUILayout.Slider("Temperature", settings.Temperature, 0f, 2f);
            settings.MaxTokens = EditorGUILayout.IntSlider("Max Tokens", settings.MaxTokens, 256, 32768);

            // --- OpenAI ---
            EditorGUILayout.Space(12);
            DrawSectionHeader("OpenAI", "gpt-5.2, gpt-5.2-pro, gpt-5-mini, gpt-4.1");
            settings.OpenAIBaseUrl = EditorGUILayout.TextField("Base URL", settings.OpenAIBaseUrl);
            DrawApiKeyField("API Key", ref _openaiKey, ref _showOpenAIKey);

            // --- Anthropic ---
            EditorGUILayout.Space(12);
            DrawSectionHeader("Anthropic", "claude-opus-4-6, claude-sonnet-4-6, claude-haiku-4-5");
            settings.AnthropicBaseUrl = EditorGUILayout.TextField("Base URL", settings.AnthropicBaseUrl);
            DrawApiKeyField("API Key", ref _anthropicKey, ref _showAnthropicKey);

            // --- Gemini ---
            EditorGUILayout.Space(12);
            DrawSectionHeader("Google Gemini", "gemini-3.1-pro-preview, gemini-3-pro-preview, gemini-3-flash-preview");
            settings.GeminiBaseUrl = EditorGUILayout.TextField("Base URL", settings.GeminiBaseUrl);
            DrawApiKeyField("API Key", ref _geminiKey, ref _showGeminiKey);

            // --- Ollama ---
            EditorGUILayout.Space(12);
            DrawSectionHeader("Ollama (Local)", "qwen2.5-coder:32b, deepseek-coder:33b, codellama:34b");
            settings.OllamaBaseUrl = EditorGUILayout.TextField("Base URL", settings.OllamaBaseUrl);

            // Save
            EditorGUILayout.Space(16);
            if (GUILayout.Button("Save Settings", GUILayout.Height(30)))
            {
                settings.OpenAIApiKey = _openaiKey;
                settings.AnthropicApiKey = _anthropicKey;
                settings.GeminiApiKey = _geminiKey;
                settings.Save();
                Debug.Log("[ShaderAILab] LLM settings saved.");
                ShowNotification(new GUIContent("Settings saved"));
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "API keys are stored in EditorPrefs (local machine only) and never committed to version control.",
                MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        static string[] GetModelsForProvider(LLMProviderType provider)
        {
            switch (provider)
            {
                case LLMProviderType.OpenAI:    return OpenAIModels;
                case LLMProviderType.Anthropic:  return AnthropicModels;
                case LLMProviderType.Gemini:     return GeminiModels;
                case LLMProviderType.Ollama:     return OllamaModels;
                default:                         return OpenAIModels;
            }
        }

        static void DrawSectionHeader(string title, string hint)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.gray } };
            EditorGUILayout.LabelField(hint, style);
        }

        static void DrawApiKeyField(string label, ref string key, ref bool show)
        {
            EditorGUILayout.BeginHorizontal();
            key = show
                ? EditorGUILayout.TextField(label, key)
                : EditorGUILayout.PasswordField(label, key);
            if (GUILayout.Button(show ? "Hide" : "Show", GUILayout.Width(50)))
                show = !show;
            EditorGUILayout.EndHorizontal();
        }
    }
}
