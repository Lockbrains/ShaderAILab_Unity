using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShaderAILab.Editor.LLM.Providers
{
    /// <summary>
    /// Provider for locally hosted models via Ollama (http://localhost:11434).
    /// </summary>
    public class OllamaProvider : ILLMProvider
    {
        public string ProviderName => "Ollama (Local)";

        public string[] AvailableModels => new[]
        {
            "qwen2.5-coder:32b",
            "qwen2.5-coder:14b",
            "deepseek-coder-v3:33b",
            "deepseek-coder:33b",
            "codellama:34b",
            "llama3.3:70b",
            "llama3:8b",
            "mistral:7b"
        };

        string BaseUrl
        {
            get
            {
                string url = LLMSettings.GetOrCreate().OllamaBaseUrl;
                return string.IsNullOrEmpty(url) ? "http://localhost:11434" : url;
            }
        }

        public bool ValidateSettings()
        {
            return true; // Ollama doesn't need an API key
        }

        public async Task<LLMResponse> GenerateAsync(LLMRequest request, string model)
        {
            try
            {
                var body = new
                {
                    model,
                    system = request.SystemPrompt ?? "",
                    prompt = request.UserPrompt,
                    stream = false,
                    options = new
                    {
                        temperature = request.Temperature,
                        num_predict = request.MaxTokens > 0 ? request.MaxTokens : 4096
                    }
                };

                string json = JsonConvert.SerializeObject(body);
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{BaseUrl}/api/generate", content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return new LLMResponse { Success = false, Error = $"HTTP {response.StatusCode}: {responseBody}" };

                    var parsed = JObject.Parse(responseBody);
                    string text = parsed["response"]?.ToString() ?? "";

                    return new LLMResponse { Content = text, Success = true };
                }
            }
            catch (Exception ex)
            {
                return new LLMResponse { Success = false, Error = $"Ollama error (is it running?): {ex.Message}" };
            }
        }

        public async Task<LLMResponse> GenerateStreamAsync(LLMRequest request, string model, Action<string> onChunk)
        {
            try
            {
                var body = new
                {
                    model,
                    system = request.SystemPrompt ?? "",
                    prompt = request.UserPrompt,
                    stream = true,
                    options = new
                    {
                        temperature = request.Temperature,
                        num_predict = request.MaxTokens > 0 ? request.MaxTokens : 4096
                    }
                };

                string json = JsonConvert.SerializeObject(body);
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var httpContent = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var httpRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                        $"{BaseUrl}/api/generate") { Content = httpContent };

                    var response = await client.SendAsync(httpRequest, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errBody = await response.Content.ReadAsStringAsync();
                        return new LLMResponse { Success = false, Error = $"HTTP {response.StatusCode}: {errBody}" };
                    }

                    var sb = new StringBuilder();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                var obj = JObject.Parse(line);
                                string token = obj["response"]?.ToString();
                                bool done = obj["done"]?.Value<bool>() ?? false;

                                if (!string.IsNullOrEmpty(token))
                                {
                                    sb.Append(token);
                                    onChunk?.Invoke(token);
                                }

                                if (done) break;
                            }
                            catch { /* skip malformed lines */ }
                        }
                    }

                    return new LLMResponse { Content = sb.ToString(), Success = true };
                }
            }
            catch (Exception ex)
            {
                return new LLMResponse { Success = false, Error = $"Ollama error (is it running?): {ex.Message}" };
            }
        }
    }
}
