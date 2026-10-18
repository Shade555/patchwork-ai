using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PatchworkAI.Core.Models;
using PatchworkAI.Core.Services;
using Xunit;

namespace PatchworkAI.Tests
{
    public class DataProfilerTests
    {
        private readonly DataProfiler _profiler = new();

        [Fact]
        public void ProfileDataset_DetectsLeadingTrailingWhitespace()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Item", InferredType = "String" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "  Laptop  " } },
                    new() { RowIndex = 2, Values = new List<string?> { "Mouse" } }
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var whitespaceIssue = issues.FirstOrDefault(i => i.Category == IssueCategory.LeadingTrailingWhitespace);
            whitespaceIssue.Should().NotBeNull();
            whitespaceIssue!.AffectedRowCount.Should().Be(1);
            whitespaceIssue.CanAutoApply.Should().BeTrue();
        }

        [Fact]
        public void ProfileDataset_DetectsNumbersStoredAsText()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Price", InferredType = "Mixed" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "$1,250.00" } },
                    new() { RowIndex = 2, Values = new List<string?> { "340.50" } },
                    new() { RowIndex = 3, Values = new List<string?> { "\"450\"" } }
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var numIssue = issues.FirstOrDefault(i => i.Category == IssueCategory.NumbersStoredAsText);
            numIssue.Should().NotBeNull();
            numIssue!.AffectedRowCount.Should().Be(2); // $1,250.00 and "450"
        }

        [Fact]
        public void ProfileDataset_DetectsMixedDateFormatsAndFlagsAmbiguity()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "OrderDate", InferredType = "DateTime" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "2026-09-15" } },
                    new() { RowIndex = 2, Values = new List<string?> { "09/20/2026" } },
                    new() { RowIndex = 3, Values = new List<string?> { "04/05/2026" } } // Ambiguous month vs day
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var dateIssues = issues.Where(i => i.Category == IssueCategory.InconsistentDateFormats).ToList();
            dateIssues.Should().NotBeEmpty();

            // Check that ambiguity is explicitly reported
            var ambiguousIssue = dateIssues.FirstOrDefault(i => !string.IsNullOrEmpty(i.AmbiguityReason));
            ambiguousIssue.Should().NotBeNull();
            ambiguousIssue!.CanAutoApply.Should().BeFalse();
        }

        [Fact]
        public void ProfileDataset_DetectsMissingValuesSentinel()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Department", InferredType = "String" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "Engineering" } },
                    new() { RowIndex = 2, Values = new List<string?> { "N/A" } },
                    new() { RowIndex = 3, Values = new List<string?> { "-" } },
                    new() { RowIndex = 4, Values = new List<string?> { null } }
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var missing = issues.FirstOrDefault(i => i.Category == IssueCategory.MissingValues);
            missing.Should().NotBeNull();
            missing!.AffectedRowCount.Should().Be(3);
        }

        [Fact]
        public void ProfileDataset_DetectsExactDuplicatesAndNearDuplicates()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "SKU", InferredType = "String" },
                    new() { Index = 1, Name = "Name", InferredType = "String" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "SKU-101", "Widget A" } },
                    new() { RowIndex = 2, Values = new List<string?> { "SKU-101", "Widget A" } }, // Exact duplicate
                    new() { RowIndex = 3, Values = new List<string?> { "sku-101", "Widget A Deluxe" } } // Near duplicate SKU
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var exactDup = issues.FirstOrDefault(i => i.Category == IssueCategory.DuplicateRows && i.Column == "All Columns");
            exactDup.Should().NotBeNull();
            exactDup!.AffectedRowCount.Should().Be(1);
            exactDup.CanAutoApply.Should().BeTrue();

            var candidateDup = issues.FirstOrDefault(i => i.Category == IssueCategory.DuplicateRows && i.Column == "SKU");
            candidateDup.Should().NotBeNull();
            candidateDup!.AmbiguityReason.Should().NotBeNullOrEmpty();
            candidateDup.CanAutoApply.Should().BeFalse();
        }

        [Fact]
        public void ProfileDataset_DetectsSuspiciousOutliers()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Quantity", InferredType = "Decimal" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "10" } },
                    new() { RowIndex = 2, Values = new List<string?> { "15" } },
                    new() { RowIndex = 3, Values = new List<string?> { "20" } },
                    new() { RowIndex = 4, Values = new List<string?> { "25" } },
                    new() { RowIndex = 5, Values = new List<string?> { "-5" } } // Negative inventory
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var outlier = issues.FirstOrDefault(i => i.Category == IssueCategory.SuspiciousOutliers);
            outlier.Should().NotBeNull();
            outlier!.AffectedRowCount.Should().Be(1);
        }

        [Fact]
        public void ProfileDataset_DetectsInvalidEmailAddresses()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Contact Email", InferredType = "String", SampleValues = new List<string> { "user@domain.com" } }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "valid@company.com" } },
                    new() { RowIndex = 2, Values = new List<string?> { "invalid..email@" } }
                }
            };

            // Act
            var issues = _profiler.ProfileDataset(dataset);

            // Assert
            var emailIssue = issues.FirstOrDefault(i => i.Category == IssueCategory.InvalidEmails);
            emailIssue.Should().NotBeNull();
            emailIssue!.AffectedRowCount.Should().Be(1);
        }
    }
}
