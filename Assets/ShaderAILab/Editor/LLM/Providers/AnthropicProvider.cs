using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShaderAILab.Editor.LLM.Providers
{
    public class AnthropicProvider : ILLMProvider
    {
        const string DefaultBaseUrl = "https://api.anthropic.com/v1";

        public string ProviderName => "Anthropic";

        public string[] AvailableModels => new[]
        {
            "claude-opus-4-6",
            "claude-sonnet-4-6",
            "claude-haiku-4-5",
            "claude-sonnet-4-20250514"
        };

        string ApiKey => LLMSettings.GetOrCreate().AnthropicApiKey;
        string BaseUrl => string.IsNullOrEmpty(LLMSettings.GetOrCreate().AnthropicBaseUrl)
            ? DefaultBaseUrl
            : LLMSettings.GetOrCreate().AnthropicBaseUrl;

        public bool ValidateSettings()
        {
            return !string.IsNullOrEmpty(ApiKey);
        }

        public async Task<LLMResponse> GenerateAsync(LLMRequest request, string model)
        {
            if (!ValidateSettings())
                return new LLMResponse { Success = false, Error = "Anthropic API key not configured." };

            try
            {
                var body = BuildRequestBody(request, model, stream: false);
                string json = JsonConvert.SerializeObject(body);

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("x-api-key", ApiKey);
                    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                    var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{BaseUrl}/messages", content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return new LLMResponse { Success = false, Error = $"HTTP {response.StatusCode}: {responseBody}" };

                    var parsed = JObject.Parse(responseBody);
                    string text = "";
                    var contentArr = parsed["content"] as JArray;
                    if (contentArr != null)
                    {
                        foreach (var block in contentArr)
                        {
                            if (block["type"]?.ToString() == "text")
                                text += block["text"]?.ToString() ?? "";
                        }
                    }

                    int inputTokens = parsed["usage"]?["input_tokens"]?.Value<int>() ?? 0;
                    int outputTokens = parsed["usage"]?["output_tokens"]?.Value<int>() ?? 0;

                    return new LLMResponse { Content = text, Success = true, TokensUsed = inputTokens + outputTokens };
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
                return new LLMResponse { Success = false, Error = "Anthropic API key not configured." };

            try
            {
                var body = BuildRequestBody(request, model, stream: true);
                string json = JsonConvert.SerializeObject(body);

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("x-api-key", ApiKey);
                    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                    var httpContent = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    var httpRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                        $"{BaseUrl}/messages") { Content = httpContent };

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

                            try
                            {
                                var evt = JObject.Parse(data);
                                string type = evt["type"]?.ToString();
                                if (type == "content_block_delta")
                                {
                                    string delta = evt["delta"]?["text"]?.ToString();
                                    if (!string.IsNullOrEmpty(delta))
                                    {
                                        sb.Append(delta);
                                        onChunk?.Invoke(delta);
                                    }
                                }
                                else if (type == "message_stop")
                                {
                                    break;
                                }
                            }
                            catch { /* skip malformed events */ }
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
                system = request.SystemPrompt ?? "",
                messages = new[]
                {
                    new { role = "user", content = request.UserPrompt }
                },
                temperature = request.Temperature,
                max_tokens = request.MaxTokens > 0 ? request.MaxTokens : 4096,
                stream
            };
        }
    }
}
