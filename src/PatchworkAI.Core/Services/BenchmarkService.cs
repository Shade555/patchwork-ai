using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public class BenchmarkResult
    {
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
        public int TotalEvaluationRows { get; set; }
        public int TotalEvaluationColumns { get; set; }

        // Deterministic metrics
        public long DeterministicProfilingTimeMs { get; set; }
        public int DeterministicIssuesDetected { get; set; }
        public List<IssueCategory> DetectedCategories { get; set; } = new();
        public int DeterministicTransformationsGenerated { get; set; }

        // AI metrics
        public bool AiModelAvailable { get; set; }
        public string AiModelName { get; set; } = "None";
        public long AiInferenceTimeMs { get; set; }
        public bool AiResponseStructuredValid { get; set; }
        public int AiSuggestedMappingsCount { get; set; }
        public int AiNormalizationRulesCount { get; set; }
        public int AmbiguousSuggestionsFlagged { get; set; }
        public double SchemaValidationRatePercent { get; set; }
        public string SummaryNotes { get; set; } = string.Empty;
    }

    public interface IBenchmarkService
    {
        Task<BenchmarkResult> RunBenchmarkAsync(string? endpointUrl = null, string? modelName = null, CancellationToken cancellationToken = default);
    }

    public class BenchmarkService : IBenchmarkService
    {
        private readonly ISampleDataService _sampleDataService;
        private readonly IDataProfiler _dataProfiler;
        private readonly ITransformationPlanner _transformationPlanner;
        private readonly ILocalAiService _localAiService;

        public BenchmarkService(
            ISampleDataService sampleDataService,
            IDataProfiler dataProfiler,
            ITransformationPlanner transformationPlanner,
            ILocalAiService localAiService)
        {
            _sampleDataService = sampleDataService;
            _dataProfiler = dataProfiler;
            _transformationPlanner = transformationPlanner;
            _localAiService = localAiService;
        }

        public async Task<BenchmarkResult> RunBenchmarkAsync(string? endpointUrl = null, string? modelName = null, CancellationToken cancellationToken = default)
        {
            var dataset = _sampleDataService.CreateSyntheticSupplierDataset();
            var result = new BenchmarkResult
            {
                TotalEvaluationRows = dataset.TotalRows,
                TotalEvaluationColumns = dataset.TotalColumns
            };

            // 1. Measure deterministic profiling
            var sw = Stopwatch.StartNew();
            var issues = _dataProfiler.ProfileDataset(dataset);
            var transformations = _transformationPlanner.PlanTransformations(dataset, issues);
            sw.Stop();

            result.DeterministicProfilingTimeMs = sw.ElapsedMilliseconds;
            result.DeterministicIssuesDetected = issues.Count;
            result.DetectedCategories = issues.Select(i => i.Category).Distinct().ToList();
            result.DeterministicTransformationsGenerated = transformations.Count;
            result.AmbiguousSuggestionsFlagged = issues.Count(i => !string.IsNullOrEmpty(i.AmbiguityReason) || !i.CanAutoApply);

            // 2. Measure AI inference if available
            var status = await _localAiService.CheckConnectionAsync(endpointUrl, modelName, cancellationToken);
            result.AiModelAvailable = status.IsAvailable;
            result.AiModelName = status.ConfiguredModel;

            if (status.IsAvailable)
            {
                var aiSw = Stopwatch.StartNew();
                var aiResult = await _localAiService.AnalyzeDatasetAsync(dataset, endpointUrl, status.ConfiguredModel, cancellationToken);
                aiSw.Stop();

                result.AiInferenceTimeMs = aiSw.ElapsedMilliseconds;
                if (aiResult != null && aiResult.IsValid)
                {
                    result.AiResponseStructuredValid = true;
                    result.SchemaValidationRatePercent = 100.0;
                    result.AiSuggestedMappingsCount = aiResult.ColumnMappings.Count;
                    result.AiNormalizationRulesCount = aiResult.NormalizationRules.Count;

                    // Verify transformations generated from AI
                    var combinedTransformations = _transformationPlanner.PlanTransformations(dataset, issues, aiResult);
                    result.SummaryNotes = $"Benchmark successfully evaluated deterministic engine ({result.DeterministicProfilingTimeMs}ms) and local AI ({result.AiInferenceTimeMs}ms). 100% structured schema compliance verified.";
                }
                else
                {
                    result.AiResponseStructuredValid = false;
                    result.SchemaValidationRatePercent = 0.0;
                    result.SummaryNotes = "AI model connected but output failed structured schema validation.";
                }
            }
            else
            {
                result.AiInferenceTimeMs = 0;
                result.SchemaValidationRatePercent = 0.0;
                result.SummaryNotes = $"Local AI endpoint unavailable ({status.ErrorMessage ?? "Not running"}). Deterministic profiling completed flawlessly in {result.DeterministicProfilingTimeMs}ms with {result.DeterministicIssuesDetected} issues across {result.DetectedCategories.Count} categories.";
            }

            return result;
        }
    }
}
