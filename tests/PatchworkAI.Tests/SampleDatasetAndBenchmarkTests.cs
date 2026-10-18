using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PatchworkAI.Core.Models;
using PatchworkAI.Core.Services;
using Xunit;

namespace PatchworkAI.Tests
{
    public class SampleDatasetAndBenchmarkTests
    {
        private readonly SampleDataService _sampleService = new();
        private readonly DataProfiler _profiler = new();
        private readonly TransformationPlanner _planner = new();
        private readonly LocalAiService _aiService = new();

        [Fact]
        public void CreateSyntheticSupplierDataset_ContainsExpectedSchemaAndIntentionalErrors()
        {
            // Act
            var dataset = _sampleService.CreateSyntheticSupplierDataset();

            // Assert structure
            dataset.TotalRows.Should().Be(10);
            dataset.TotalColumns.Should().Be(7);
            dataset.Columns.Select(c => c.Name).Should().Contain(new[]
            {
                "SKU", "Product Name", "Category", "Unit Price", "Quantity", "Last Updated", "Contact Email"
            });

            // Act: Run profiler
            var issues = _profiler.ProfileDataset(dataset);

            // Assert intentional error detection
            issues.Should().NotBeEmpty();

            // Whitespace detected
            issues.Any(i => i.Category == IssueCategory.LeadingTrailingWhitespace).Should().BeTrue();

            // Casing detected
            issues.Any(i => i.Category == IssueCategory.InconsistentCapitalization).Should().BeTrue();

            // Numbers as text detected ($1,249.99 and "30")
            issues.Any(i => i.Category == IssueCategory.NumbersStoredAsText).Should().BeTrue();

            // Duplicates detected
            issues.Any(i => i.Category == IssueCategory.DuplicateRows).Should().BeTrue();

            // Missing values detected (N/A)
            issues.Any(i => i.Category == IssueCategory.MissingValues).Should().BeTrue();

            // Mixed dates detected
            issues.Any(i => i.Category == IssueCategory.InconsistentDateFormats).Should().BeTrue();

            // Invalid email detected
            issues.Any(i => i.Category == IssueCategory.InvalidEmails).Should().BeTrue();

            // Suspicious outliers detected (-5 and 999999.00)
            issues.Any(i => i.Category == IssueCategory.SuspiciousOutliers).Should().BeTrue();
        }

        [Fact]
        public async Task BenchmarkService_ExecutesAndMeasuresDeterministicPerformance()
        {
            // Arrange
            var benchmarkService = new BenchmarkService(_sampleService, _profiler, _planner, _aiService);

            // Act
            var result = await benchmarkService.RunBenchmarkAsync();

            // Assert
            result.Should().NotBeNull();
            result.TotalEvaluationRows.Should().Be(10);
            result.DeterministicIssuesDetected.Should().BeGreaterThan(5);
            result.DetectedCategories.Count.Should().BeGreaterThanOrEqualTo(5);
            result.DeterministicProfilingTimeMs.Should().BeGreaterThanOrEqualTo(0);
            result.DeterministicTransformationsGenerated.Should().BeGreaterThan(0);
            result.SummaryNotes.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task BenchmarkService_LiveOllamaInference_MeasuresAccurateMetrics()
        {
            var status = await _aiService.CheckConnectionAsync("http://localhost:11434", "gemma2:2b");
            if (!status.IsAvailable) return;

            var benchmarkService = new BenchmarkService(_sampleService, _profiler, _planner, _aiService);
            var result = await benchmarkService.RunBenchmarkAsync("http://localhost:11434", status.ConfiguredModel);

            result.AiModelAvailable.Should().BeTrue();
            result.DeterministicIssuesDetected.Should().BeGreaterThan(5);
        }
    }
}
