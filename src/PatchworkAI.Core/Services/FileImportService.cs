using System;
using System.Collections.Generic;
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
    public interface IFileImportService
    {
        Task<ImportedDataset> ImportFileAsync(string filePath, string? sheetName = null, int maxPreviewRows = -1);
        Task<List<string>> GetWorksheetNamesAsync(string filePath);
    }

    public class FileImportService : IFileImportService
    {
        public async Task<List<string>> GetWorksheetNamesAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".csv")
            {
                return new List<string> { "Default" };
            }

            if (extension != ".xlsx")
            {
                throw new NotSupportedException($"Unsupported file format: {extension}. Only .csv and .xlsx files are supported.");
            }

            return await Task.Run(() =>
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(stream);
                return workbook.Worksheets.Select(ws => ws.Name).ToList();
            });
        }

        public async Task<ImportedDataset> ImportFileAsync(string filePath, string? sheetName = null, int maxPreviewRows = -1)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The specified file does not exist: {filePath}");
            }

            var fileInfo = new FileInfo(filePath);
            var extension = fileInfo.Extension.ToLowerInvariant();

            return extension switch
            {
                ".csv" => await ImportCsvAsync(fileInfo, maxPreviewRows),
                ".xlsx" => await ImportXlsxAsync(fileInfo, sheetName, maxPreviewRows),
                _ => throw new NotSupportedException(
                    $"Unsupported file extension '{extension}'. Patchwork AI supports CSV (.csv) and Microsoft Excel (.xlsx) files.")
            };
        }

        private async Task<ImportedDataset> ImportCsvAsync(FileInfo fileInfo, int maxRows)
        {
            return await Task.Run(() =>
            {
                var dataset = new ImportedDataset
                {
                    FilePath = fileInfo.FullName,
                    FileName = fileInfo.Name,
                    FileSizeBytes = fileInfo.Length,
                    Format = DatasetFormat.Csv,
                    SheetNames = new List<string> { "Default" },
                    SelectedSheet = "Default"
                };

                // Detect encoding safely
                var encoding = DetectEncoding(fileInfo.FullName);

                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    BadDataFound = null,
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    DetectDelimiter = true,
                    TrimOptions = TrimOptions.None,
                    IgnoreBlankLines = true
                };

                using var stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, encoding);
                using var csv = new CsvReader(reader, csvConfig);

                if (!csv.Read())
                {
                    dataset.Columns = new List<DatasetColumn>();
                    dataset.Rows = new List<DataRowModel>();
                    return dataset;
                }

                csv.ReadHeader();
                var headerRecord = csv.HeaderRecord ?? Array.Empty<string>();

                var columns = new List<DatasetColumn>();
                for (int i = 0; i < headerRecord.Length; i++)
                {
                    var colName = string.IsNullOrWhiteSpace(headerRecord[i]) ? $"Column_{i + 1}" : headerRecord[i];
                    columns.Add(new DatasetColumn
                    {
                        Index = i,
                        Name = colName,
                        OriginalName = headerRecord[i]
                    });
                }
                dataset.Columns = columns;

                var rows = new List<DataRowModel>();
                int rowIndex = 1;

                while (csv.Read())
                {
                    if (maxRows > 0 && rows.Count >= maxRows)
                    {
                        break;
                    }

                    var values = new List<string?>();
                    for (int i = 0; i < columns.Count; i++)
                    {
                        string? fieldVal = csv.GetField(i);
                        values.Add(fieldVal);
                    }

                    rows.Add(new DataRowModel
                    {
                        RowIndex = rowIndex++,
                        Values = values,
                        OriginalValues = new List<string?>(values),
                        IsModified = false
                    });
                }

                dataset.Rows = rows;
                InferColumnProperties(dataset);

                return dataset;
            });
        }

        private async Task<ImportedDataset> ImportXlsxAsync(FileInfo fileInfo, string? sheetName, int maxRows)
        {
            return await Task.Run(() =>
            {
                var dataset = new ImportedDataset
                {
                    FilePath = fileInfo.FullName,
                    FileName = fileInfo.Name,
                    FileSizeBytes = fileInfo.Length,
                    Format = DatasetFormat.Xlsx
                };

                using var stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(stream);

                dataset.SheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();

                IXLWorksheet worksheet;
                if (!string.IsNullOrEmpty(sheetName) && workbook.Worksheets.Contains(sheetName))
                {
                    worksheet = workbook.Worksheet(sheetName);
                    dataset.SelectedSheet = sheetName;
                }
                else
                {
                    worksheet = workbook.Worksheets.First();
                    dataset.SelectedSheet = worksheet.Name;
                }

                var range = worksheet.RangeUsed();
                if (range == null)
                {
                    dataset.Columns = new List<DatasetColumn>();
                    dataset.Rows = new List<DataRowModel>();
                    return dataset;
                }

                var firstRow = range.FirstRow();
                int totalColumns = range.ColumnCount();

                var columns = new List<DatasetColumn>();
                for (int col = 1; col <= totalColumns; col++)
                {
                    var cellVal = firstRow.Cell(col).GetString();
                    var colName = string.IsNullOrWhiteSpace(cellVal) ? $"Column_{col}" : cellVal;
                    columns.Add(new DatasetColumn
                    {
                        Index = col - 1,
                        Name = colName,
                        OriginalName = cellVal
                    });
                }
                dataset.Columns = columns;

                var rows = new List<DataRowModel>();
                int rowIndex = 1;

                var dataRows = range.RowsUsed().Skip(1);
                foreach (var row in dataRows)
                {
                    if (maxRows > 0 && rows.Count >= maxRows)
                    {
                        break;
                    }

                    var values = new List<string?>();
                    for (int col = 1; col <= totalColumns; col++)
                    {
                        var cell = row.Cell(col);
                        string? formattedVal = null;

                        if (!cell.IsEmpty())
                        {
                            // ClosedXML format awareness
                            if (cell.DataType == XLDataType.DateTime)
                            {
                                try
                                {
                                    var dt = cell.GetDateTime();
                                    formattedVal = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).TrimEnd("00:00:00".ToCharArray()).Trim();
                                }
                                catch
                                {
                                    formattedVal = cell.GetFormattedString();
                                }
                            }
                            else
                            {
                                formattedVal = cell.GetFormattedString();
                            }
                        }

                        values.Add(formattedVal);
                    }

                    rows.Add(new DataRowModel
                    {
                        RowIndex = rowIndex++,
                        Values = values,
                        OriginalValues = new List<string?>(values),
                        IsModified = false
                    });
                }

                dataset.Rows = rows;
                InferColumnProperties(dataset);

                return dataset;
            });
        }

        private static void InferColumnProperties(ImportedDataset dataset)
        {
            for (int colIdx = 0; colIdx < dataset.Columns.Count; colIdx++)
            {
                var column = dataset.Columns[colIdx];
                int nullCount = 0;
                var distinctValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sampleList = new List<string>();

                int intCount = 0;
                int decimalCount = 0;
                int dateCount = 0;
                int boolCount = 0;
                int textCount = 0;

                foreach (var row in dataset.Rows)
                {
                    var val = colIdx < row.Values.Count ? row.Values[colIdx] : null;

                    if (string.IsNullOrWhiteSpace(val))
                    {
                        nullCount++;
                        continue;
                    }

                    distinctValues.Add(val);
                    if (sampleList.Count < 5 && !sampleList.Contains(val))
                    {
                        sampleList.Add(val);
                    }

                    var trimmed = val.Trim();
                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        intCount++;
                    }
                    else if (decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out _) ||
                             decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out _))
                    {
                        decimalCount++;
                    }
                    else if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                             DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                    {
                        dateCount++;
                    }
                    else if (bool.TryParse(trimmed, out _))
                    {
                        boolCount++;
                    }
                    else
                    {
                        textCount++;
                    }
                }

                column.NullCount = nullCount;
                column.UniqueCount = distinctValues.Count;
                column.SampleValues = sampleList;

                int nonNull = dataset.Rows.Count - nullCount;
                if (nonNull == 0)
                {
                    column.InferredType = "Empty";
                }
                else if (intCount == nonNull)
                {
                    column.InferredType = "Integer";
                }
                else if (intCount + decimalCount == nonNull)
                {
                    column.InferredType = "Decimal";
                }
                else if (dateCount == nonNull)
                {
                    column.InferredType = "DateTime";
                }
                else if (boolCount == nonNull)
                {
                    column.InferredType = "Boolean";
                }
                else if (intCount > 0 || decimalCount > 0 || dateCount > 0)
                {
                    column.InferredType = "Mixed";
                }
                else
                {
                    column.InferredType = "String";
                }
            }
        }

        private static Encoding DetectEncoding(string filePath)
        {
            var bom = new byte[4];
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.Read(bom, 0, 4);
            }

            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return Encoding.UTF8;
            if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode;
            if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode;
            if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff) return Encoding.UTF32;

            return new UTF8Encoding(false); // Default to UTF-8 without BOM
        }
    }
}
