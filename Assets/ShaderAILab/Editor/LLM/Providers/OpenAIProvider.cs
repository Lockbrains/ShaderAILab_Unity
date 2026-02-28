using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShaderAILab.Editor.LLM.Providers
{
    public class OpenAIProvider : ILLMProvider
    {
        public string ProviderName => "OpenAI";

        public string[] AvailableModels => new[]
        {
            "gpt-5.2",
            "gpt-5.2-pro",
            "gpt-5-mini",
            "gpt-4.1",
            "gpt-4o"
        };

        string ApiKey => LLMSettings.GetOrCreate().OpenAIApiKey;
        string BaseUrl => LLMSettings.GetOrCreate().OpenAIBaseUrl;

        public bool ValidateSettings()
        {
            return !string.IsNullOrEmpty(ApiKey);
        }

        public async Task<LLMResponse> GenerateAsync(LLMRequest request, string model)
        {
            if (!ValidateSettings())
                return new LLMResponse { Success = false, Error = "OpenAI API key not configured." };

            try
            {
                var body = BuildRequestBody(request, model, stream: false);
                string json = JsonConvert.SerializeObject(body);

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
                    var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{BaseUrl}/chat/completions", content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return new LLMResponse { Success = false, Error = $"HTTP {response.StatusCode}: {responseBody}" };

                    var parsed = JObject.Parse(responseBody);
                    string text = parsed["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
                    int tokens = parsed["usage"]?["total_tokens"]?.Value<int>() ?? 0;

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
                return new LLMResponse { Success = false, Error = "OpenAI API key not configured." };

            try
            {
                var body = BuildRequestBody(request, model, stream: true);
                string json = JsonConvert.SerializeObject(body);

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
                    var httpContent = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var httpRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                        $"{BaseUrl}/chat/completions") { Content = httpContent };

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
                            if (!line.StartsWith("data: ")) continue;
                            string data = line.Substring(6);
                            if (data == "[DONE]") break;

                            try
                            {
                                var chunk = JObject.Parse(data);
                                string delta = chunk["choices"]?[0]?["delta"]?["content"]?.ToString();
                                if (!string.IsNullOrEmpty(delta))
                                {
                                    sb.Append(delta);
                                    onChunk?.Invoke(delta);
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

        object BuildRequestBody(LLMRequest request, string model, bool stream)
        {
            return new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = request.SystemPrompt ?? "" },
                    new { role = "user", content = request.UserPrompt }
                },
                temperature = request.Temperature,
                max_tokens = request.MaxTokens > 0 ? request.MaxTokens : 4096,
                stream
            };
        }
    }
}
