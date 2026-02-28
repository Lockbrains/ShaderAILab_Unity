using System;
using System.Threading.Tasks;
using UnityEngine;
using ShaderAILab.Editor.Core;
using ShaderAILab.Editor.LLM.Providers;

namespace ShaderAILab.Editor.LLM
{
    /// <summary>
    /// Central service that dispatches generation requests to the active LLM provider.
    /// Handles prompt construction via PromptTemplates.
    /// </summary>
    public class LLMService
    {
        static LLMService _instance;
        public static LLMService Instance => _instance ?? (_instance = new LLMService());

        ILLMProvider _openai = new OpenAIProvider();
        ILLMProvider _anthropic = new AnthropicProvider();
        ILLMProvider _gemini = new GeminiProvider();
        ILLMProvider _ollama = new OllamaProvider();

        public event Action<string> OnStreamChunk;
        public event Action<string> OnGenerationComplete;
        public event Action<string> OnError;

        public ILLMProvider ActiveProvider
        {
            get
            {
                var settings = LLMSettings.GetOrCreate();
                switch (settings.ActiveProvider)
                {
                    case LLMProviderType.OpenAI:     return _openai;
                    case LLMProviderType.Anthropic:   return _anthropic;
                    case LLMProviderType.Gemini:      return _gemini;
                    case LLMProviderType.Ollama:      return _ollama;
                    default:                          return _openai;
                }
            }
        }

        /// <summary>
        /// Generate shader code from a natural language prompt.
        /// Constructs appropriate system/user prompts based on target context.
        /// </summary>
        public async Task<string> GenerateShaderCodeAsync(string userPrompt, ShaderDocument doc, string targetContext,
            System.Collections.Generic.List<Core.ShaderCompileChecker.CompileError> compileErrors = null)
        {
            var settings = LLMSettings.GetOrCreate();
            var provider = ActiveProvider;

            if (!provider.ValidateSettings())
            {
                string err = $"{provider.ProviderName} is not configured. Please set API key in LLM Settings.";
                OnError?.Invoke(err);
                throw new InvalidOperationException(err);
            }

            string systemPrompt = PromptTemplates.BuildSystemPrompt(doc, targetContext, compileErrors);
            string fullUserPrompt = PromptTemplates.BuildUserPrompt(userPrompt, doc, targetContext, compileErrors);

            var request = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = fullUserPrompt,
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens
            };

            LLMResponse response = await provider.GenerateStreamAsync(request, settings.ActiveModel,
                chunk => OnStreamChunk?.Invoke(chunk));

            if (!response.Success)
            {
                OnError?.Invoke(response.Error);
                throw new Exception(response.Error);
            }

            string code = PromptTemplates.ExtractCodeFromResponse(response.Content);
            OnGenerationComplete?.Invoke(code);
            return code;
        }

        /// <summary>
        /// Non-streaming generation for simpler use cases.
        /// </summary>
        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt)
        {
            var settings = LLMSettings.GetOrCreate();
            var provider = ActiveProvider;

            var request = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens
            };

            var response = await provider.GenerateAsync(request, settings.ActiveModel);

            if (!response.Success)
                throw new Exception(response.Error);

            return response.Content;
        }
    }
}
