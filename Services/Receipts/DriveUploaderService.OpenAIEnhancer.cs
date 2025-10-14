using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace RaymarEquipmentInventory.Services
{
    public class OpenAIEnhancer
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenAIEnhancer(HttpClient? httpClient = null, string? model = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                      ?? throw new InvalidOperationException("OPENAI_API_KEY not found in environment.");
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4" : model;
        }

        // Strongly typed result
        public class AIEnhancedReceipt
        {
            [JsonPropertyName("merchant")] public string? Merchant { get; set; }
            [JsonPropertyName("items")] public List<AIItem> Items { get; set; } = new();
            [JsonPropertyName("subtotal")] public decimal? Subtotal { get; set; }
            [JsonPropertyName("tax")] public decimal? Tax { get; set; }
            [JsonPropertyName("total")] public decimal? Total { get; set; }
            [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.5;
        }

        public class AIItem
        {
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("price")] public decimal? Price { get; set; }
        }

        /// <summary>
        /// Attempts to enhance OCR text using OpenAI.
        /// Returns structured receipt data or null on failure.
        /// </summary>
        public async Task<AIEnhancedReceipt?> TryEnhanceReceiptAsync(string rawText, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                Log.Warning("🧾 OpenAIEnhancer: Empty input text.");
                return null;
            }

            try
            {
                var systemPrompt = "You are a helpful assistant that extracts structured receipt information from messy OCR text. " +
                                   "Return a strict JSON object with keys: merchant, items (array of {description, price}), subtotal, tax, total, confidence (0-1). " +
                                   "Always ensure valid JSON. Use numeric values for amounts. If uncertain, leave fields null.";

                var userPrompt = $"Extract structured receipt data from the following OCR text:\n\n{rawText}";

                var payload = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.2
                };

                var json = JsonSerializer.Serialize(payload);
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(ct);
                    Log.Warning("⚠️ OpenAIEnhancer: API call failed ({Code}): {Error}", response.StatusCode, err);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(ct);

                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(content))
                {
                    Log.Warning("⚠️ OpenAIEnhancer: Empty response content.");
                    return null;
                }

                // Clean up JSON if model wrapped it in markdown or text
                var clean = content.Trim().Trim('`', '\n', '\r', ' ');
                var start = clean.IndexOf('{');
                var end = clean.LastIndexOf('}');
                if (start >= 0 && end > start)
                    clean = clean.Substring(start, end - start + 1);

                try
                {
                    var result = JsonSerializer.Deserialize<AIEnhancedReceipt>(
                        clean,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            NumberHandling = JsonNumberHandling.AllowReadingFromString
                        });

                    if (result != null)
                        Log.Information("✅ OpenAIEnhancer: Parsed {ItemCount} items for merchant {Merchant}.",
                            result.Items?.Count ?? 0, result.Merchant ?? "(unknown)");

                    return result;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ OpenAIEnhancer: Failed to parse AI JSON.");
                    Log.Debug("Raw AI response: {Content}", clean);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "💥 OpenAIEnhancer: Unexpected error.");
                return null;
            }
        }
    }
}


