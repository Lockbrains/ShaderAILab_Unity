using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShaderAILab.Editor.LLM.Providers
{
    public class GeminiProvider : ILLMProvider
    {
        const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public string ProviderName => "Google Gemini";

        public string[] AvailableModels => new[]
        {
            "gemini-3.1-pro-preview",
            "gemini-3-pro-preview",
            "gemini-3-flash-preview",
            "gemini-2.5-pro",
            "gemini-2.5-flash"
        };

        string ApiKey => LLMSettings.GetOrCreate().GeminiApiKey;
        string BaseUrl
        {
            get
            {
                string url = LLMSettings.GetOrCreate().GeminiBaseUrl;
                return string.IsNullOrEmpty(url) ? DefaultBaseUrl : url;
            }
        }

        public bool ValidateSettings()
        {
            return !string.IsNullOrEmpty(ApiKey);
        }

        public async Task<LLMResponse> GenerateAsync(LLMRequest request, string model)
        {
            if (!ValidateSettings())
                return new LLMResponse { Success = false, Error = "Gemini API key not configured." };

            try
            {
                var body = BuildRequestBody(request);
                string json = JsonConvert.SerializeObject(body);
                string url = $"{BaseUrl}/models/{model}:generateContent?key={ApiKey}";

                using (var client = new System.Net.Http.HttpClient())
                {
                    var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(url, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return new LLMResponse { Success = false, Error = $"HTTP {response.StatusCode}: {responseBody}" };

                    var parsed = JObject.Parse(responseBody);
                    string text = ExtractText(parsed);
                    int tokens = parsed["usageMetadata"]?["totalTokenCount"]?.Value<int>() ?? 0;

                    return new LLMResponse { Content = text, Success = true, TokensUsed = tokens };
                }
            }
            catch (Exception ex)
            {
                return new LLMResponse { Success = false, Error = ex.Message };
            }
        }

        public async Task<LLMResponse> GenerateStreamAsync(LLMRequest request, string model, Action<string> onChunk)
        {
            if (!ValidateSettings())
                return new LLMResponse { Success = false, Error = "Gemini API key not configured." };

            try
            {
                var body = BuildRequestBody(request);
                string json = JsonConvert.SerializeObject(body);
                string url = $"{BaseUrl}/models/{model}:streamGenerateContent?alt=sse&key={ApiKey}";

                using (var client = new System.Net.Http.HttpClient())
                {
                    var httpContent = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var httpRequest = new System.Net.Http.HttpRequestMessage(
                        System.Net.Http.HttpMethod.Post, url) { Content = httpContent };

                    var response = await client.SendAsync(httpRequest,
                        System.Net.Http.HttpCompletionOption.ResponseHeadersRead);

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
                            if (!line.StartsWith("data: ")) continue;
                            string data = line.Substring(6).Trim();
                            if (string.IsNullOrEmpty(data)) continue;

                            try
                            {
                                var chunk = JObject.Parse(data);
                                string text = ExtractText(chunk);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    sb.Append(text);
                                    onChunk?.Invoke(text);
                                }
                            }
                            catch { /* skip malformed chunks */ }
                        }
                    }

                    return new LLMResponse { Content = sb.ToString(), Success = true };
                }
            }
            catch (Exception ex)
            {
                return new LLMResponse { Success = false, Error = ex.Message };
            }
        }

        object BuildRequestBody(LLMRequest request)
        {
            return new
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = request.SystemPrompt ?? "" } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = request.UserPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = request.Temperature,
                    maxOutputTokens = request.MaxTokens > 0 ? request.MaxTokens : 8192
                }
            };
        }

        static string ExtractText(JObject response)
        {
            var candidates = response["candidates"] as JArray;
            if (candidates == null || candidates.Count == 0) return "";

            var parts = candidates[0]?["content"]?["parts"] as JArray;
            if (parts == null) return "";

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                string text = part["text"]?.ToString();
                if (!string.IsNullOrEmpty(text))
                    sb.Append(text);
            }
            return sb.ToString();
        }
    }
}
