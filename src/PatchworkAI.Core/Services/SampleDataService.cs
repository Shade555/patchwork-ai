using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public interface ISampleDataService
    {
        ImportedDataset CreateSyntheticSupplierDataset();
        string SaveSyntheticSampleToFile(string? targetDirectory = null);
    }

    public class SampleDataService : ISampleDataService
    {
        public ImportedDataset CreateSyntheticSupplierDataset()
        {
            var dataset = new ImportedDataset
            {
                FilePath = "IN_MEMORY_DEMO:Synthetic_Supplier_Catalog.csv",
                FileName = "Synthetic_Supplier_Catalog.csv [DEMO SAMPLE]",
                FileSizeBytes = 4096,
                Format = DatasetFormat.Csv,
                SheetNames = new List<string> { "Default" },
                SelectedSheet = "Default"
            };

            var columns = new List<DatasetColumn>
            {
                new() { Index = 0, Name = "SKU", OriginalName = "SKU", InferredType = "String" },
                new() { Index = 1, Name = "Product Name", OriginalName = "Product Name", InferredType = "String" },
                new() { Index = 2, Name = "Category", OriginalName = "Category", InferredType = "String" },
                new() { Index = 3, Name = "Unit Price", OriginalName = "Unit Price", InferredType = "Mixed" },
                new() { Index = 4, Name = "Quantity", OriginalName = "Quantity", InferredType = "Mixed" },
                new() { Index = 5, Name = "Last Updated", OriginalName = "Last Updated", InferredType = "Mixed" },
                new() { Index = 6, Name = "Contact Email", OriginalName = "Contact Email", InferredType = "String" }
            };
            dataset.Columns = columns;

            // Intentional errors introduced realistically:
            // Row 1: Normal baseline
            // Row 2: Leading/trailing whitespace in Name & Category
            // Row 3: Number stored as text with currency symbol and comma, date in slash format
            // Row 4: Duplicate SKU of Row 1 with lowercase and slight price variance (candidate business duplicate)
            // Row 5: Missing value sentinel ("N/A"), duplicate casing "electronics" vs "Electronics"
            // Row 6: Outlier negative quantity (-5), suspicious price 999999.00
            // Row 7: Typo category "Electrnics", date in DD-MM-YYYY
            // Row 8: Mixed data type in quantity ("Out of stock"), invalid email ("supplier..corp.com")
            // Row 9: Ambiguous date ("04/05/2026")
            // Row 10: Exact duplicate of Row 1

            var rawRows = new List<List<string?>>
            {
                new() { "SKU-1001", "Ergonomic Office Chair", "Office Furniture", "249.99", "45", "2026-09-15", "support@ergofurniture.com" },
                new() { "SKU-1002", "  Wireless Mechanical Keyboard  ", "  Computer Accessories  ", "89.50", "120", "2026-09-18", "sales@techgear.io" },
                new() { "SKU-1003", "4K Ultra-HD Monitor 27-inch", "Electronics", "$1,249.99", "\"30\"", "09/20/2026", "service@displays.org" },
                new() { "sku-1001", "Ergonomic Office Chair (Black)", "Office Furniture", "259.99", "40", "2026-09-22", "support@ergofurniture.com" },
                new() { "SKU-1004", "USB-C Multiport Adapter", "electronics", "45.00", "N/A", "2026-09-25", "orders@cableco.net" },
                new() { "SKU-1005", "Industrial Desk Lamp", "Office Furniture", "999999.00", "-5", "2026-09-28", "contact@lightwork.com" },
                new() { "SKU-1006", "Noise Cancelling Headphones", "Electrnics", "179.00", "85", "15-10-2026", "audio@soundlabs.com" },
                new() { "SKU-1007", "Bluetooth Conference Speaker", "Electronics", "119.00", "Out of stock", "2026-10-02", "supplier..corp.com" },
                new() { "SKU-1008", "Adjustable Standing Desk Mat", "Office Furniture", "65.50", "60", "04/05/2026", "mat@ergofurniture.com" },
                new() { "SKU-1001", "Ergonomic Office Chair", "Office Furniture", "249.99", "45", "2026-09-15", "support@ergofurniture.com" }
            };

            var rows = new List<DataRowModel>();
            for (int i = 0; i < rawRows.Count; i++)
            {
                rows.Add(new DataRowModel
                {
                    RowIndex = i + 1,
                    Values = new List<string?>(rawRows[i]),
                    OriginalValues = new List<string?>(rawRows[i]),
                    IsModified = false
                });
            }
            dataset.Rows = rows;

            // Fill sample values
            for (int colIdx = 0; colIdx < dataset.Columns.Count; colIdx++)
            {
                var samples = new List<string>();
                foreach (var r in dataset.Rows)
                {
                    var val = r.Values[colIdx];
                    if (!string.IsNullOrWhiteSpace(val) && !samples.Contains(val))
                    {
                        samples.Add(val);
                        if (samples.Count >= 5) break;
                    }
                }
                dataset.Columns[colIdx].SampleValues = samples;
            }

            return dataset;
        }

        public string SaveSyntheticSampleToFile(string? targetDirectory = null)
        {
            targetDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchworkAI", "Samples");
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var filePath = Path.Combine(targetDirectory, "synthetic_supplier_inventory.csv");
            var dataset = CreateSyntheticSupplierDataset();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", dataset.Columns.ConvertAll(c => $"\"{c.Name}\"")));

            foreach (var row in dataset.Rows)
            {
                sb.AppendLine(string.Join(",", row.Values.ConvertAll(v => $"\"{v?.Replace("\"", "\"\"") ?? ""}\"")));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }
    }
}
