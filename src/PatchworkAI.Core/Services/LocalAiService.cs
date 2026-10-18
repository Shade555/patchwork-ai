using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public class AiColumnMappingProposal
    {
        [JsonPropertyName("original_name")]
        public string OriginalName { get; set; } = string.Empty;

        [JsonPropertyName("suggested_name")]
        public string SuggestedName { get; set; } = string.Empty;

        [JsonPropertyName("rationale")]
        public string Rationale { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; } = 0.8;
    }

    public class AiNormalizationRuleProposal
    {
        [JsonPropertyName("column_name")]
        public string ColumnName { get; set; } = string.Empty;

        [JsonPropertyName("canonical_value")]
        public string CanonicalValue { get; set; } = string.Empty;

        [JsonPropertyName("variant_values")]
        public List<string> VariantValues { get; set; } = new();

        [JsonPropertyName("rationale")]
        public string Rationale { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; } = 0.8;
    }

    public class AiAnalysisResult
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("column_mappings")]
        public List<AiColumnMappingProposal> ColumnMappings { get; set; } = new();

        [JsonPropertyName("normalization_rules")]
        public List<AiNormalizationRuleProposal> NormalizationRules { get; set; } = new();

        [JsonPropertyName("identified_risks")]
        public List<string> IdentifiedRisks { get; set; } = new();

        public bool IsValid => ColumnMappings != null && NormalizationRules != null;
    }

    public interface ILocalAiService
    {
        Task<LocalModelStatus> CheckConnectionAsync(string? endpointUrl = null, string? modelName = null, CancellationToken cancellationToken = default);
        Task<AiAnalysisResult?> AnalyzeDatasetAsync(ImportedDataset dataset, string? endpointUrl = null, string? modelName = null, CancellationToken cancellationToken = default);
    }

    public class LocalAiService : ILocalAiService
    {
        private readonly HttpClient _httpClient;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public LocalAiService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(35)
            };
        }

        public async Task<LocalModelStatus> CheckConnectionAsync(string? endpointUrl = null, string? modelName = null, CancellationToken cancellationToken = default)
        {
            endpointUrl ??= "http://localhost:11434";
            modelName ??= "llama3.1:latest";

            var status = new LocalModelStatus
            {
                EndpointUrl = endpointUrl,
                ConfiguredModel = modelName
            };

            var sw = Stopwatch.StartNew();

            try
            {
                var tagsUrl = $"{endpointUrl.TrimEnd('/')}/api/tags";
                using var response = await _httpClient.GetAsync(tagsUrl, cancellationToken);

                sw.Stop();
                status.ResponseTimeMs = sw.ElapsedMilliseconds;

                if (!response.IsSuccessStatusCode)
                {
                    status.IsAvailable = false;
                    status.ErrorMessage = $"Ollama responded with HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                    return status;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modelsElem.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameProp))
                        {
                            status.AvailableModels.Add(nameProp.GetString() ?? "");
                        }
                    }
                }

                status.IsAvailable = true;

                // Check if configured model exists or default to first available
                if (status.AvailableModels.Count > 0 && !status.AvailableModels.Contains(modelName))
                {
                    // Check without tag (e.g. "llama3.1" matches "llama3.1:latest")
                    var match = status.AvailableModels.FirstOrDefault(m => m.StartsWith(modelName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        status.ConfiguredModel = match;
                    }
                }

                return status;
            }
            catch (Exception ex)
            {
                sw.Stop();
                status.ResponseTimeMs = sw.ElapsedMilliseconds;
                status.IsAvailable = false;
                status.ErrorMessage = $"Cannot connect to local Ollama runtime: {ex.Message}";
                return status;
            }
        }

        public async Task<AiAnalysisResult?> AnalyzeDatasetAsync(ImportedDataset dataset, string? endpointUrl = null, string? modelName = null, CancellationToken cancellationToken = default)
        {
            if (dataset == null || dataset.Columns.Count == 0)
            {
                return null;
            }

            endpointUrl ??= "http://localhost:11434";
            modelName ??= "llama3.1:latest";

            // Verify connectivity first
            var connStatus = await CheckConnectionAsync(endpointUrl, modelName, cancellationToken);
            if (!connStatus.IsAvailable)
            {
                return null;
            }

            // Construct privacy-preserving prompt: ONLY column names, inferred types, and 3-5 distinct sample values
            var columnSummaries = new List<object>();
            foreach (var col in dataset.Columns)
            {
                columnSummaries.Add(new
                {
                    column_name = col.Name,
                    inferred_type = col.InferredType,
                    unique_count = col.UniqueCount,
                    null_count = col.NullCount,
                    sample_values = col.SampleValues.Take(4).ToList()
                });
            }

            var promptData = new
            {
                dataset_name = dataset.FileName,
                total_rows = dataset.TotalRows,
                columns = columnSummaries
            };

            var promptDataJson = JsonSerializer.Serialize(promptData);

            var systemPrompt = @"You are a data cleaning assistant for Patchwork AI.
Analyze the column names and representative sample values from a user's business spreadsheet.
Respond ONLY with a valid JSON object matching this exact schema:
{
  ""summary"": ""brief 1-2 sentence assessment of data cleanliness"",
  ""column_mappings"": [
    {
      ""original_name"": ""unnamed or abbreviated column name"",
      ""suggested_name"": ""Descriptive_Business_Name"",
      ""rationale"": ""reason for renaming"",
      ""confidence"": 0.9
    }
  ],
  ""normalization_rules"": [
    {
      ""column_name"": ""name of categorical column"",
      ""canonical_value"": ""Standard Value"",
      ""variant_values"": [""variant1"", ""variant2""],
      ""rationale"": ""reason for merging synonyms or fixing typos"",
      ""confidence"": 0.85
    }
  ],
  ""identified_risks"": [
    ""risk description""
  ]
}
DO NOT include markdown code blocks, preamble, or commentary. Output raw JSON only.";

            var userPrompt = $"Analyze this dataset metadata and suggest column improvements and category normalizations:\n{promptDataJson}";

            var requestBody = new
            {
                model = connStatus.ConfiguredModel,
                prompt = $"{systemPrompt}\n\nUser Request:\n{userPrompt}",
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0.1,
                    top_p = 0.9
                }
            };

            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                var generateUrl = $"{endpointUrl.TrimEnd('/')}/api/generate";
                using var response = await _httpClient.PostAsync(generateUrl, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);

                if (!doc.RootElement.TryGetProperty("response", out var rawTextProp))
                {
                    return null;
                }

                var rawText = rawTextProp.GetString();
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    return null;
                }

                // Clean potential leading/trailing markdown fences if present
                rawText = rawText.Trim();
                if (rawText.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                {
                    rawText = rawText[7..];
                }
                else if (rawText.StartsWith("```"))
                {
                    rawText = rawText[3..];
                }
                if (rawText.EndsWith("```"))
                {
                    rawText = rawText[..^3];
                }
                rawText = rawText.Trim();

                // Validate structured JSON
                var parsed = JsonSerializer.Deserialize<AiAnalysisResult>(rawText, JsonOptions);
                if (parsed == null || !parsed.IsValid)
                {
                    return null;
                }

                // Strict sanity filtering: ensure column names actually exist in dataset
                var validColNames = new HashSet<string>(dataset.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                parsed.ColumnMappings = parsed.ColumnMappings
                    .Where(m => validColNames.Contains(m.OriginalName) && !string.IsNullOrWhiteSpace(m.SuggestedName))
                    .ToList();

                parsed.NormalizationRules = parsed.NormalizationRules
                    .Where(r => validColNames.Contains(r.ColumnName) && !string.IsNullOrWhiteSpace(r.CanonicalValue) && r.VariantValues.Count > 0)
                    .ToList();

                return parsed;
            }
            catch
            {
                return null;
            }
        }
    }
}
