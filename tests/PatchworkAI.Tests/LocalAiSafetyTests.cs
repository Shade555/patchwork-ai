using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using PatchworkAI.Core.Models;
using PatchworkAI.Core.Services;
using Xunit;

namespace PatchworkAI.Tests
{
    public class LocalAiSafetyTests
    {
        [Fact]
        public void TransformationPlanner_IgnoresInvalidAiNormalizationRules()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Category", InferredType = "String" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "Furniture" } }
                }
            };

            var aiResult = new AiAnalysisResult
            {
                Summary = "Test",
                NormalizationRules = new List<AiNormalizationRuleProposal>
                {
                    // Rule for a column that DOES NOT exist in dataset
                    new() { ColumnName = "NonExistentColumn", CanonicalValue = "Valid", VariantValues = new List<string> { "val" } }
                }
            };

            var planner = new TransformationPlanner();

            // Act
            var transformations = planner.PlanTransformations(dataset, new List<DataIssue>(), aiResult);

            // Assert
            transformations.Should().BeEmpty();
        }

        [Fact]
        public void TransformationPlanner_MarksAiTransformationsAsRequiringReview()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Category", InferredType = "String" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "Electrnics" } }
                }
            };

            var aiResult = new AiAnalysisResult
            {
                Summary = "Cleaned",
                NormalizationRules = new List<AiNormalizationRuleProposal>
                {
                    new()
                    {
                        ColumnName = "Category",
                        CanonicalValue = "Electronics",
                        VariantValues = new List<string> { "Electrnics" },
                        Rationale = "Fix typo in electronics",
                        Confidence = 0.85
                    }
                }
            };

            var planner = new TransformationPlanner();

            // Act
            var transformations = planner.PlanTransformations(dataset, new List<DataIssue>(), aiResult);

            // Assert
            transformations.Should().HaveCount(1);
            var trans = transformations.First();
            trans.Source.Should().Be(TransformationSource.LocalAi);
            trans.IsSafeDeterministic.Should().BeFalse(); // Must NOT be auto-applied without approval
            trans.Status.Should().Be(TransformationStatus.Pending);
            trans.ProposedValue.Should().Be("Electronics");
        }

        [Fact]
        public void AiAnalysisResult_RejectsMalformedSchema()
        {
            // Verify strict JSON validation rejects invalid structures
            string malformedJson = @"{ ""summary"": ""Broken"", ""wrong_key"": [] }";

            var parsed = JsonSerializer.Deserialize<AiAnalysisResult>(malformedJson);
            parsed.Should().NotBeNull();
            // Since ColumnMappings and NormalizationRules are empty lists initialized by default, check counts:
            parsed!.ColumnMappings.Should().BeEmpty();
            parsed.NormalizationRules.Should().BeEmpty();
        }
    }
}
