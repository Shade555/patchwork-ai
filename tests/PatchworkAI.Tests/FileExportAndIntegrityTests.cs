using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using PatchworkAI.Core.Models;
using PatchworkAI.Core.Services;
using Xunit;

namespace PatchworkAI.Tests
{
    public class FileExportAndIntegrityTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ExportService _exportService = new();
        private readonly FileImportService _importService = new();

        public FileExportAndIntegrityTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PatchworkAITests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }
        }

        [Fact]
        public async Task FileImportAndExport_KeepsOriginalFileCompletelyUntouched()
        {
            // Arrange: create original CSV file
            var originalFilePath = Path.Combine(_tempDirectory, "original_data.csv");
            var originalContent = "ID,Name,Price\n1,  Chair  ,$150.00\n2,Table,250.00\n";
            await File.WriteAllTextAsync(originalFilePath, originalContent);

            var originalHash = ComputeSha256(originalFilePath);

            // Act 1: Import
            var dataset = await _importService.ImportFileAsync(originalFilePath);

            // Act 2: Profile & Modify dataset in-memory
            var profiler = new DataProfiler();
            var issues = profiler.ProfileDataset(dataset);
            var planner = new TransformationPlanner();
            var transformations = planner.PlanTransformations(dataset, issues);

            foreach (var t in transformations)
            {
                t.Status = TransformationStatus.Approved;
            }

            var executor = new TransformationExecutor();
            executor.ApplyApprovedTransformations(dataset, transformations);

            // Act 3: Export to distinct path
            var exportPath = _exportService.GenerateSuggestedExportPath(originalFilePath, DatasetFormat.Csv);
            await _exportService.ExportDatasetAsync(
                dataset, exportPath, DatasetFormat.Csv, issues.Count, transformations.Count, 0, 0, executor.ChangeLog);

            // Assert: Original file hash must be 100% identical!
            var currentHash = ComputeSha256(originalFilePath);
            currentHash.Should().Be(originalHash, "The original file must never be modified by analysis or export.");

            // Assert: Exported file exists and contains cleaned values
            File.Exists(exportPath).Should().BeTrue();
            var exportedContent = await File.ReadAllTextAsync(exportPath);
            exportedContent.Should().Contain("Chair"); // Trimmed
            exportedContent.Should().NotContain("  Chair  ");
        }

        [Fact]
        public async Task ExportService_ThrowsExceptionIfAttemptingToOverwriteOriginal()
        {
            var originalFilePath = Path.Combine(_tempDirectory, "input.csv");
            await File.WriteAllTextAsync(originalFilePath, "A,B\n1,2\n");

            var dataset = new ImportedDataset
            {
                FilePath = originalFilePath,
                FileName = "input.csv",
                Columns = new List<DatasetColumn> { new() { Index = 0, Name = "A" }, new() { Index = 1, Name = "B" } },
                Rows = new List<DataRowModel> { new() { RowIndex = 1, Values = new List<string?> { "1", "2" } } }
            };

            // Attempting to export to the exact same file path must throw InvalidOperationException
            Func<Task> act = async () => await _exportService.ExportDatasetAsync(
                dataset, originalFilePath, DatasetFormat.Csv, 0, 0, 0, 0, new List<ChangeLogEntry>());

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*cannot overwrite the original file*");
        }

        [Fact]
        public async Task ExportToXlsx_CanBeReadBackAccurately()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                FilePath = Path.Combine(_tempDirectory, "dummy.xlsx"),
                FileName = "dummy.xlsx",
                SelectedSheet = "Inventory",
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "SKU", InferredType = "String" },
                    new() { Index = 1, Name = "Quantity", InferredType = "Integer" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "SKU-99", "15" }, OriginalValues = new List<string?> { "SKU-99", "15" } }
                }
            };

            var exportPath = Path.Combine(_tempDirectory, "exported_test.xlsx");

            // Act
            await _exportService.ExportDatasetAsync(dataset, exportPath, DatasetFormat.Xlsx, 0, 0, 0, 0, new List<ChangeLogEntry>());

            // Assert: Read back using FileImportService
            File.Exists(exportPath).Should().BeTrue();
            var reloaded = await _importService.ImportFileAsync(exportPath);
            reloaded.TotalRows.Should().Be(1);
            reloaded.TotalColumns.Should().Be(2);
            reloaded.Columns[0].Name.Should().Be("SKU");
            reloaded.Rows[0].Values[0].Should().Be("SKU-99");
            reloaded.Rows[0].Values[1].Should().Be("15");
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}
