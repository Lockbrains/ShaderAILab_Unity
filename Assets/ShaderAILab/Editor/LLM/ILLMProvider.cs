using System;
using System.Threading.Tasks;

namespace ShaderAILab.Editor.LLM
{
    public struct LLMRequest
    {
        public string SystemPrompt;
        public string UserPrompt;
        public float Temperature;
        public int MaxTokens;
    }

    public struct LLMResponse
    {
        public string Content;
        public bool Success;
        public string Error;
        public int TokensUsed;
    }

    /// <summary>
    /// Abstract interface for LLM providers. Implementations handle
    /// HTTP communication with specific API endpoints.
    /// </summary>
    public interface ILLMProvider
    {
        string ProviderName { get; }
        string[] AvailableModels { get; }

        Task<LLMResponse> GenerateAsync(LLMRequest request, string model);

        /// <summary>
        /// Stream-based generation. Calls onChunk for each token/chunk received.
        /// Returns the full accumulated response when complete.
        /// </summary>
        Task<LLMResponse> GenerateStreamAsync(LLMRequest request, string model, Action<string> onChunk);

        bool ValidateSettings();
    }
}
