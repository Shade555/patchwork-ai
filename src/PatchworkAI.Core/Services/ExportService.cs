using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public interface IExportService
    {
        string GenerateSuggestedExportPath(string originalFilePath, DatasetFormat targetFormat);
        Task<ExportReport> ExportDatasetAsync(
            ImportedDataset dataset,
            string exportPath,
            DatasetFormat format,
            int totalDetectedIssues,
            int approvedCount,
            int rejectedCount,
            int unresolvedCount,
            List<ChangeLogEntry> appliedChanges,
            long processingTimeMs = 0);
        bool OpenFolderInExplorer(string filePath);
    }

    public class ExportService : IExportService
    {
        public string GenerateSuggestedExportPath(string originalFilePath, DatasetFormat targetFormat)
        {
            var directory = Path.GetDirectoryName(originalFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var baseName = Path.GetFileNameWithoutExtension(originalFilePath);
            var ext = targetFormat == DatasetFormat.Xlsx ? ".xlsx" : ".csv";

            var suggestedName = $"{baseName}_repaired{ext}";
            var fullPath = Path.Combine(directory, suggestedName);

            int counter = 1;
            while (File.Exists(fullPath))
            {
                suggestedName = $"{baseName}_repaired_{counter}{ext}";
                fullPath = Path.Combine(directory, suggestedName);
                counter++;
            }

            return fullPath;
        }

        public async Task<ExportReport> ExportDatasetAsync(
            ImportedDataset dataset,
            string exportPath,
            DatasetFormat format,
            int totalDetectedIssues,
            int approvedCount,
            int rejectedCount,
            int unresolvedCount,
            List<ChangeLogEntry> appliedChanges,
            long processingTimeMs = 0)
        {
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                throw new ArgumentException("Export path cannot be empty.", nameof(exportPath));
            }

            // Ensure destination directory exists
            var directory = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Safety check: Prevent overwriting the original file
            if (File.Exists(exportPath) && string.Equals(Path.GetFullPath(exportPath), Path.GetFullPath(dataset.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Export cannot overwrite the original file. Please select a different file name or destination.");
            }

            if (format == DatasetFormat.Csv)
            {
                await ExportToCsvAsync(dataset, exportPath);
            }
            else
            {
                await ExportToXlsxAsync(dataset, exportPath);
            }

            var affectedRows = dataset.Rows.Count(r => r.IsModified);
            var affectedCells = appliedChanges.Count;

            return new ExportReport
            {
                OriginalFileName = dataset.FileName,
                OriginalPath = dataset.FilePath,
                ExportPath = exportPath,
                TotalIssuesDetected = totalDetectedIssues,
                ApprovedFixesCount = approvedCount,
                RejectedSuggestionsCount = rejectedCount,
                UnresolvedWarningsCount = unresolvedCount,
                AffectedRowsCount = affectedRows,
                AffectedCellsCount = affectedCells,
                ProcessingTimeMs = processingTimeMs,
                ExportTimestamp = DateTime.UtcNow,
                ExportFormat = format,
                AppliedChanges = new List<ChangeLogEntry>(appliedChanges)
            };
        }

        private static async Task ExportToCsvAsync(ImportedDataset dataset, string exportPath)
        {
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(exportPath, false, new UTF8Encoding(true));
                using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    ShouldQuote = args => true // Safe quote mode for business CSVs
                });

                // Write headers
                foreach (var col in dataset.Columns)
                {
                    csv.WriteField(col.Name);
                }
                csv.NextRecord();

                // Write data rows
                foreach (var row in dataset.Rows)
                {
                    for (int i = 0; i < dataset.Columns.Count; i++)
                    {
                        var val = i < row.Values.Count ? row.Values[i] : "";
                        csv.WriteField(val ?? "");
                    }
                    csv.NextRecord();
                }
            });
        }

        private static async Task ExportToXlsxAsync(ImportedDataset dataset, string exportPath)
        {
            await Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var sheetName = string.IsNullOrWhiteSpace(dataset.SelectedSheet) ? "Cleaned_Data" : dataset.SelectedSheet;
                var worksheet = workbook.Worksheets.Add(sheetName);

                // Write headers with clean styling
                for (int col = 0; col < dataset.Columns.Count; col++)
                {
                    var cell = worksheet.Cell(1, col + 1);
                    cell.Value = dataset.Columns[col].Name;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 243, 246);
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                }

                // Write data rows
                for (int rowIdx = 0; rowIdx < dataset.Rows.Count; rowIdx++)
                {
                    var rowData = dataset.Rows[rowIdx];
                    for (int colIdx = 0; colIdx < dataset.Columns.Count; colIdx++)
                    {
                        var cell = worksheet.Cell(rowIdx + 2, colIdx + 1);
                        var rawVal = colIdx < rowData.Values.Count ? rowData.Values[colIdx] : null;

                        if (rawVal == null)
                        {
                            cell.Value = string.Empty;
                            continue;
                        }

                        // Check if numeric or date to format appropriately in Excel
                        var colType = dataset.Columns[colIdx].InferredType;
                        if ((colType == "Decimal" || colType == "Integer") &&
                            decimal.TryParse(rawVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                        {
                            cell.Value = num;
                        }
                        else if (colType == "DateTime" &&
                                 DateTime.TryParse(rawVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        {
                            cell.Value = dt;
                            cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        }
                        else
                        {
                            cell.Value = rawVal;
                        }
                    }
                }

                worksheet.Columns().AdjustToContents(1, 100);
                workbook.SaveAs(exportPath);
            });
        }

        public bool OpenFolderInExplorer(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                var safePath = Path.GetFullPath(filePath);
                if (File.Exists(safePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{safePath}\"",
                        UseShellExecute = true
                    });
                    return true;
                }

                var directory = Path.GetDirectoryName(safePath);
                if (Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{directory}\"",
                        UseShellExecute = true
                    });
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
