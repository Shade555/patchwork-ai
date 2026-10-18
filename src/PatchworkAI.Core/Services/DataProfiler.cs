using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public interface IDataProfiler
    {
        List<DataIssue> ProfileDataset(ImportedDataset dataset);
    }

    public class DataProfiler : IDataProfiler
    {
        private static readonly HashSet<string> SentinelMissingValues = new(StringComparer.OrdinalIgnoreCase)
        {
            "", "na", "n/a", "null", "none", "nan", "-", "--", "undefined", "nil", "missing", "tbd"
        };

        private static readonly Regex EmailRegex = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CurrencySymbolRegex = new(
            @"[\$\€\£\¥\₹]|USD|EUR|GBP",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] AmbiguousColumnKeywords =
        {
            "unnamed", "column", "col", "field", "temp", "var", "data", "val", "misc"
        };

        public List<DataIssue> ProfileDataset(ImportedDataset dataset)
        {
            var issues = new List<DataIssue>();
            if (dataset == null || dataset.Columns.Count == 0 || dataset.Rows.Count == 0)
            {
                return issues;
            }

            // 1. Ambiguous column names
            CheckAmbiguousColumnNames(dataset, issues);

            // 2. Duplicate rows and candidate business duplicates
            CheckDuplicateRows(dataset, issues);

            // Per-column checks
            for (int colIdx = 0; colIdx < dataset.Columns.Count; colIdx++)
            {
                var column = dataset.Columns[colIdx];

                // 3. Missing values
                CheckMissingValues(dataset, colIdx, column, issues);

                // 4. Leading / trailing whitespace
                CheckWhitespace(dataset, colIdx, column, issues);

                // 5. Inconsistent capitalization & internal spacing
                CheckCapitalizationAndSpacing(dataset, colIdx, column, issues);

                // 6. Numbers stored as text
                CheckNumbersStoredAsText(dataset, colIdx, column, issues);

                // 7. Inconsistent date formats & ambiguous dates
                CheckDateFormats(dataset, colIdx, column, issues);

                // 8. Inconsistent categorical representations & typos
                CheckCategoricalInconsistencies(dataset, colIdx, column, issues);

                // 9. Mixed data types in column
                CheckMixedDataTypes(dataset, colIdx, column, issues);

                // 10. Invalid email addresses
                CheckEmailAddresses(dataset, colIdx, column, issues);

                // 11. Suspicious outliers & implausible values
                CheckSuspiciousOutliers(dataset, colIdx, column, issues);

                // 12. Inconsistent currency or numeric formatting
                CheckCurrencyAndNumericFormatting(dataset, colIdx, column, issues);
            }

            return issues;
        }

        private static void CheckAmbiguousColumnNames(ImportedDataset dataset, List<DataIssue> issues)
        {
            for (int i = 0; i < dataset.Columns.Count; i++)
            {
                var col = dataset.Columns[i];
                var name = col.Name.Trim().ToLowerInvariant();

                bool isAmbiguous = false;
                string reason = string.Empty;

                if (AmbiguousColumnKeywords.Any(k => name.StartsWith(k) || name == k))
                {
                    isAmbiguous = true;
                    reason = $"Column header '{col.Name}' uses a generic non-descriptive label.";
                }
                else if (name.Length <= 2 && !name.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    isAmbiguous = true;
                    reason = $"Column header '{col.Name}' is very short and potentially ambiguous.";
                }

                if (isAmbiguous)
                {
                    issues.Add(new DataIssue
                    {
                        Category = IssueCategory.AmbiguousColumnNames,
                        Column = col.Name,
                        ColumnIndex = i,
                        AffectedRowCount = 0,
                        AffectedRowIndices = new List<int>(),
                        ExampleValues = col.SampleValues.Take(3).ToList(),
                        Severity = IssueSeverity.Info,
                        Explanation = reason,
                        ProposedFixDescription = "Rename column to a clear, descriptive business name based on its contents.",
                        IsAiSuggested = false,
                        Confidence = 0.85,
                        AmbiguityReason = "Requires domain context to determine the best descriptive name.",
                        CanAutoApply = false
                    });
                }
            }
        }

        private static void CheckDuplicateRows(ImportedDataset dataset, List<DataIssue> issues)
        {
            // Exact duplicate detection
            var rowSignatures = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var row = dataset.Rows[i];
                var sig = string.Join("|||", row.Values.Select(v => v ?? ""));
                if (!rowSignatures.TryGetValue(sig, out var list))
                {
                    list = new List<int>();
                    rowSignatures[sig] = list;
                }
                list.Add(row.RowIndex);
            }

            var exactDuplicates = rowSignatures.Where(kvp => kvp.Value.Count > 1).ToList();
            if (exactDuplicates.Count > 0)
            {
                int affectedRows = exactDuplicates.Sum(d => d.Value.Count - 1);
                var affectedIndices = exactDuplicates.SelectMany(d => d.Value.Skip(1)).ToList();

                issues.Add(new DataIssue
                {
                    Category = IssueCategory.DuplicateRows,
                    Column = "All Columns",
                    ColumnIndex = -1,
                    AffectedRowCount = affectedRows,
                    AffectedRowIndices = affectedIndices,
                    ExampleValues = exactDuplicates.Take(3).Select(d => $"Rows {string.Join(", ", d.Value)}").ToList(),
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Found {affectedRows} exact duplicate row(s) identical across every column.",
                    ProposedFixDescription = "Remove redundant duplicate rows, keeping the primary entry.",
                    IsAiSuggested = false,
                    Confidence = 1.0,
                    CanAutoApply = true,
                    DeterministicRuleId = "REMOVE_EXACT_DUPLICATES"
                });
            }

            // Candidate business duplicates (e.g. matching SKU or matching Product Name with different casing/whitespace)
            int skuColIdx = dataset.Columns.FindIndex(c => c.Name.IndexOf("SKU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           c.Name.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                           c.Name.IndexOf("Identifier", StringComparison.OrdinalIgnoreCase) >= 0);
            if (skuColIdx >= 0)
            {
                var normalizedMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dataset.Rows.Count; i++)
                {
                    var val = dataset.Rows[i].Values[skuColIdx];
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var norm = val.Trim().ToUpperInvariant();
                        if (!normalizedMap.TryGetValue(norm, out var list))
                        {
                            list = new List<int>();
                            normalizedMap[norm] = list;
                        }
                        list.Add(dataset.Rows[i].RowIndex);
                    }
                }

                var candidateDups = normalizedMap.Where(kvp => kvp.Value.Count > 1).ToList();
                // Filter out those already covered by exact duplicate rows
                var nearDups = candidateDups.Where(cd => cd.Value.Select(r => dataset.Rows[r - 1])
                    .Select(row => string.Join("|||", row.Values.Select(v => v ?? "")))
                    .Distinct().Count() > 1).ToList();

                if (nearDups.Count > 0)
                {
                    var affectedRows = nearDups.Sum(d => d.Value.Count);
                    issues.Add(new DataIssue
                    {
                        Category = IssueCategory.DuplicateRows,
                        Column = dataset.Columns[skuColIdx].Name,
                        ColumnIndex = skuColIdx,
                        AffectedRowCount = affectedRows,
                        AffectedRowIndices = nearDups.SelectMany(d => d.Value).Distinct().ToList(),
                        ExampleValues = nearDups.Take(3).Select(d => $"SKU '{d.Key}': Rows {string.Join(", ", d.Value)}").ToList(),
                        Severity = IssueSeverity.Warning,
                        Explanation = $"Found {nearDups.Count} key identifier(s) appearing in multiple rows with differing attributes.",
                        ProposedFixDescription = "Review candidate business duplicate records to merge or reconcile differences.",
                        IsAiSuggested = false,
                        Confidence = 0.75,
                        AmbiguityReason = "Multiple items sharing a key might be intentional variants or distinct records.",
                        CanAutoApply = false
                    });
                }
            }
        }

        private static void CheckMissingValues(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var affectedRows = new List<int>();
            var examples = new List<string>();

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (val == null || SentinelMissingValues.Contains(val.Trim()))
                {
                    affectedRows.Add(dataset.Rows[i].RowIndex);
                    if (examples.Count < 3)
                    {
                        examples.Add($"Row {dataset.Rows[i].RowIndex}: '{(val ?? "<null>")}'");
                    }
                }
            }

            if (affectedRows.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.MissingValues,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = affectedRows.Count,
                    AffectedRowIndices = affectedRows,
                    ExampleValues = examples,
                    Severity = affectedRows.Count > dataset.Rows.Count * 0.5 ? IssueSeverity.Warning : IssueSeverity.Info,
                    Explanation = $"Column '{column.Name}' contains {affectedRows.Count} empty or placeholder missing values (e.g. null, N/A, -).",
                    ProposedFixDescription = "Normalize blank placeholders to consistent empty cell representation.",
                    IsAiSuggested = false,
                    Confidence = 1.0,
                    CanAutoApply = true,
                    DeterministicRuleId = "NORMALIZE_MISSING_TO_EMPTY"
                });
            }
        }

        private static void CheckWhitespace(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var affectedRows = new List<int>();
            var examples = new List<string>();

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (val != null && (val.StartsWith(' ') || val.EndsWith(' ') || val.StartsWith('\t') || val.EndsWith('\t')))
                {
                    affectedRows.Add(dataset.Rows[i].RowIndex);
                    if (examples.Count < 3)
                    {
                        examples.Add($"Row {dataset.Rows[i].RowIndex}: '{val}' → '{val.Trim()}'");
                    }
                }
            }

            if (affectedRows.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.LeadingTrailingWhitespace,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = affectedRows.Count,
                    AffectedRowIndices = affectedRows,
                    ExampleValues = examples,
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Found {affectedRows.Count} value(s) with unintended leading or trailing spaces/tabs.",
                    ProposedFixDescription = "Trim leading and trailing whitespace from affected cells.",
                    IsAiSuggested = false,
                    Confidence = 1.0,
                    CanAutoApply = true,
                    DeterministicRuleId = "TRIM_WHITESPACE"
                });
            }
        }

        private static void CheckCapitalizationAndSpacing(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var casingGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var internalSpaceRows = new List<int>();
            var spaceExamples = new List<string>();

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var trimmed = val.Trim();
                if (!casingGroups.TryGetValue(trimmed, out var list))
                {
                    list = new List<string>();
                    casingGroups[trimmed] = list;
                }
                if (!list.Contains(trimmed))
                {
                    list.Add(trimmed);
                }

                // Check redundant internal double spaces
                if (trimmed.Contains("  "))
                {
                    internalSpaceRows.Add(dataset.Rows[i].RowIndex);
                    if (spaceExamples.Count < 3)
                    {
                        var collapsed = Regex.Replace(trimmed, @"\s{2,}", " ");
                        spaceExamples.Add($"Row {dataset.Rows[i].RowIndex}: '{trimmed}' → '{collapsed}'");
                    }
                }
            }

            // Inconsistent casing (e.g. "Electronics", "electronics", "ELECTRONICS")
            var inconsistentCasing = casingGroups.Where(g => g.Value.Count > 1).ToList();
            if (inconsistentCasing.Count > 0)
            {
                var affectedCount = inconsistentCasing.Sum(g => g.Value.Count);
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.InconsistentCapitalization,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = affectedCount,
                    AffectedRowIndices = new List<int>(),
                    ExampleValues = inconsistentCasing.Take(3).Select(g => string.Join(" vs ", g.Value)).ToList(),
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Found inconsistent letter casing across {inconsistentCasing.Count} distinct value group(s).",
                    ProposedFixDescription = "Standardize casing to Title Case / dominant representation.",
                    IsAiSuggested = false,
                    Confidence = 0.9,
                    CanAutoApply = true,
                    DeterministicRuleId = "STANDARDIZE_CASING_TITLE"
                });
            }

            if (internalSpaceRows.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.InconsistentCapitalization,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = internalSpaceRows.Count,
                    AffectedRowIndices = internalSpaceRows,
                    ExampleValues = spaceExamples,
                    Severity = IssueSeverity.Info,
                    Explanation = $"Found {internalSpaceRows.Count} cell(s) with multiple consecutive internal spaces.",
                    ProposedFixDescription = "Collapse multiple consecutive internal spaces into single spaces.",
                    IsAiSuggested = false,
                    Confidence = 1.0,
                    CanAutoApply = true,
                    DeterministicRuleId = "COLLAPSE_INTERNAL_SPACES"
                });
            }
        }

        private static void CheckNumbersStoredAsText(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var affectedRows = new List<int>();
            var examples = new List<string>();

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var trimmed = val.Trim();

                // Check if string contains currency or quotes or formatted number that can be parsed as decimal
                if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
                {
                    var unquoted = trimmed.Trim('"');
                    if (decimal.TryParse(unquoted, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        affectedRows.Add(dataset.Rows[i].RowIndex);
                        if (examples.Count < 3) examples.Add($"Row {dataset.Rows[i].RowIndex}: '{val}' → '{unquoted}'");
                        continue;
                    }
                }

                if (CurrencySymbolRegex.IsMatch(trimmed))
                {
                    var cleanNum = CurrencySymbolRegex.Replace(trimmed, "").Replace(",", "").Trim();
                    if (decimal.TryParse(cleanNum, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    {
                        affectedRows.Add(dataset.Rows[i].RowIndex);
                        if (examples.Count < 3) examples.Add($"Row {dataset.Rows[i].RowIndex}: '{val}' → '{parsed.ToString(CultureInfo.InvariantCulture)}'");
                        continue;
                    }
                }

                // Numbers with commas in non-standard string form, e.g. "1,250.00"
                if (trimmed.Contains(',') && decimal.TryParse(trimmed.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                {
                    affectedRows.Add(dataset.Rows[i].RowIndex);
                    if (examples.Count < 3) examples.Add($"Row {dataset.Rows[i].RowIndex}: '{val}' → '{num.ToString(CultureInfo.InvariantCulture)}'");
                }
            }

            if (affectedRows.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.NumbersStoredAsText,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = affectedRows.Count,
                    AffectedRowIndices = affectedRows,
                    ExampleValues = examples,
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Found {affectedRows.Count} numeric values formatted with currency symbols, quotes, or comma separators.",
                    ProposedFixDescription = "Convert text-formatted numbers into clean standardized numeric values.",
                    IsAiSuggested = false,
                    Confidence = 0.95,
                    CanAutoApply = true,
                    DeterministicRuleId = "CONVERT_TEXT_TO_NUMBER"
                });
            }
        }

        private static void CheckDateFormats(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var detectedFormats = new Dictionary<string, List<int>>();
            var ambiguousDates = new List<int>();
            var examples = new List<string>();

            string[] knownFormats =
            {
                "yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy",
                "yyyy-MM-dd HH:mm:ss", "MM-dd-yyyy", "dd-MM-yyyy"
            };

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var trimmed = val.Trim();

                bool matched = false;
                foreach (var fmt in knownFormats)
                {
                    if (DateTime.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        if (!detectedFormats.TryGetValue(fmt, out var list))
                        {
                            list = new List<int>();
                            detectedFormats[fmt] = list;
                        }
                        list.Add(dataset.Rows[i].RowIndex);
                        matched = true;

                        // Check ambiguity if day <= 12 and month <= 12 for slash/dash formats
                        if (fmt.Contains('/') || fmt.Contains('-'))
                        {
                            if (dt.Day <= 12 && dt.Month <= 12 && dt.Day != dt.Month)
                            {
                                ambiguousDates.Add(dataset.Rows[i].RowIndex);
                                if (examples.Count < 3)
                                {
                                    examples.Add($"Row {dataset.Rows[i].RowIndex}: '{trimmed}' (could be {dt:MMM dd} or {dt.Day}/{dt.Month})");
                                }
                            }
                        }
                        break;
                    }
                }

                if (!matched && DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    if (!detectedFormats.TryGetValue("GeneralDateTime", out var list))
                    {
                        list = new List<int>();
                        detectedFormats["GeneralDateTime"] = list;
                    }
                    list.Add(dataset.Rows[i].RowIndex);
                }
            }

            // Only report if this column appears to be a Date column (at least 30% dates)
            int totalDateCells = detectedFormats.Values.Sum(v => v.Count);
            if (totalDateCells >= Math.Max(2, dataset.Rows.Count * 0.3))
            {
                if (detectedFormats.Count > 1)
                {
                    var formatSummary = string.Join(", ", detectedFormats.Select(df => $"{df.Key} ({df.Value.Count})"));
                    issues.Add(new DataIssue
                    {
                        Category = IssueCategory.InconsistentDateFormats,
                        Column = column.Name,
                        ColumnIndex = colIdx,
                        AffectedRowCount = totalDateCells,
                        AffectedRowIndices = detectedFormats.Values.SelectMany(v => v).Distinct().ToList(),
                        ExampleValues = detectedFormats.Keys.Take(3).ToList(),
                        Severity = IssueSeverity.Warning,
                        Explanation = $"Column '{column.Name}' contains mixed date formats: {formatSummary}.",
                        ProposedFixDescription = "Standardize all dates to ISO 8601 format (YYYY-MM-DD).",
                        IsAiSuggested = false,
                        Confidence = 0.9,
                        CanAutoApply = true,
                        DeterministicRuleId = "STANDARDIZE_DATE_ISO8601"
                    });
                }

                if (ambiguousDates.Count > 0)
                {
                    issues.Add(new DataIssue
                    {
                        Category = IssueCategory.InconsistentDateFormats,
                        Column = column.Name,
                        ColumnIndex = colIdx,
                        AffectedRowCount = ambiguousDates.Count,
                        AffectedRowIndices = ambiguousDates,
                        ExampleValues = examples,
                        Severity = IssueSeverity.Info,
                        Explanation = $"Found {ambiguousDates.Count} date value(s) where day and month are both <= 12, creating potential MM/DD vs DD/MM ambiguity.",
                        ProposedFixDescription = "Review date order before converting to prevent accidental month/day transposition.",
                        IsAiSuggested = false,
                        Confidence = 0.6,
                        AmbiguityReason = "Date order cannot be deterministically proven without locale or dataset pattern confirmation.",
                        CanAutoApply = false
                    });
                }
            }
        }

        private static void CheckCategoricalInconsistencies(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            // Only apply to text columns with repeated values
            if (column.InferredType != "String" && column.InferredType != "Mixed") return;

            var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;
                var trimmed = val.Trim();
                freq[trimmed] = freq.GetValueOrDefault(trimmed, 0) + 1;
            }

            if (freq.Count < 2 || freq.Count > 50) return; // Only categorical columns

            var items = freq.Keys.ToList();
            var typoClusters = new List<(string primary, string variant, int distance)>();

            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    var a = items[i];
                    var b = items[j];

                    if (Math.Abs(a.Length - b.Length) > 2) continue;

                    int dist = LevenshteinDistance(a.ToLowerInvariant(), b.ToLowerInvariant());
                    if (dist == 1 || (dist == 2 && a.Length >= 6))
                    {
                        // Higher frequency is primary candidate
                        if (freq[a] >= freq[b])
                        {
                            typoClusters.Add((a, b, dist));
                        }
                        else
                        {
                            typoClusters.Add((b, a, dist));
                        }
                    }
                }
            }

            if (typoClusters.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.CategoricalInconsistencies,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = typoClusters.Sum(c => freq[c.variant]),
                    AffectedRowIndices = new List<int>(),
                    ExampleValues = typoClusters.Take(3).Select(c => $"'{c.variant}' ({freq[c.variant]}x) vs canonical '{c.primary}' ({freq[c.primary]}x)").ToList(),
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Detected {typoClusters.Count} candidate typo or near-duplicate category label(s).",
                    ProposedFixDescription = "Normalize minor typos to match the predominant canonical category label.",
                    IsAiSuggested = false,
                    Confidence = 0.85,
                    AmbiguityReason = "Similar names might represent intentional distinct subcategories in some domains.",
                    CanAutoApply = false,
                    DeterministicRuleId = "NORMALIZE_CATEGORY_TYPOS"
                });
            }
        }

        private static void CheckMixedDataTypes(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            if (column.InferredType != "Mixed") return;

            int numericCount = 0;
            int textCount = 0;
            var textExamples = new List<string>();
            var affectedRows = new List<int>();

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var trimmed = val.Trim();
                if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                    decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.CurrentCulture, out _))
                {
                    numericCount++;
                }
                else
                {
                    textCount++;
                    affectedRows.Add(dataset.Rows[i].RowIndex);
                    if (textExamples.Count < 3)
                    {
                        textExamples.Add($"Row {dataset.Rows[i].RowIndex}: '{val}'");
                    }
                }
            }

            if (numericCount > 0 && textCount > 0)
            {
                bool mostlyNumeric = numericCount > textCount;
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.MixedDataTypes,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = mostlyNumeric ? textCount : numericCount,
                    AffectedRowIndices = affectedRows,
                    ExampleValues = textExamples,
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Column '{column.Name}' contains mixed types ({numericCount} numeric, {textCount} text entries).",
                    ProposedFixDescription = mostlyNumeric
                        ? "Review non-numeric text entries in numeric column (e.g. replace 'Out of Stock' with standard missing representation)."
                        : "Ensure consistent data types across column entries.",
                    IsAiSuggested = false,
                    Confidence = 0.8,
                    AmbiguityReason = "Text comments in numeric columns may carry domain information that requires manual confirmation.",
                    CanAutoApply = false
                });
            }
        }

        private static void CheckEmailAddresses(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            // Only inspect if column name or contents suggest emails
            bool isEmailCol = column.Name.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              column.SampleValues.Any(s => s.Contains('@'));

            if (!isEmailCol) return;

            var invalidRows = new List<int>();
            var examples = new List<string>();

            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var trimmed = val.Trim();
                if (!EmailRegex.IsMatch(trimmed))
                {
                    invalidRows.Add(dataset.Rows[i].RowIndex);
                    if (examples.Count < 3)
                    {
                        examples.Add($"Row {dataset.Rows[i].RowIndex}: '{val}'");
                    }
                }
            }

            if (invalidRows.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.InvalidEmails,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = invalidRows.Count,
                    AffectedRowIndices = invalidRows,
                    ExampleValues = examples,
                    Severity = IssueSeverity.Error,
                    Explanation = $"Found {invalidRows.Count} invalid email address format(s).",
                    ProposedFixDescription = "Flag invalid email addresses for manual review.",
                    IsAiSuggested = false,
                    Confidence = 0.95,
                    CanAutoApply = false
                });
            }
        }

        private static void CheckSuspiciousOutliers(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var numbers = new List<(decimal val, int rowIndex)>();
            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var trimmed = CurrencySymbolRegex.Replace(val.Trim(), "").Replace(",", "");
                if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                {
                    numbers.Add((num, dataset.Rows[i].RowIndex));
                }
            }

            if (numbers.Count < 5) return;

            var outlierRows = new List<int>();
            var examples = new List<string>();

            // Domain rule: Negative inventory or price
            bool isQuantityOrPrice = column.Name.IndexOf("qty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     column.Name.IndexOf("quantity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     column.Name.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     column.Name.IndexOf("inventory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     column.Name.IndexOf("stock", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isQuantityOrPrice)
            {
                foreach (var (val, rowIndex) in numbers)
                {
                    if (val < 0)
                    {
                        outlierRows.Add(rowIndex);
                        if (examples.Count < 3) examples.Add($"Row {rowIndex}: Negative value '{val}'");
                    }
                    else if (val >= 999990 && val <= 999999) // common placeholder like 999999
                    {
                        outlierRows.Add(rowIndex);
                        if (examples.Count < 3) examples.Add($"Row {rowIndex}: Placeholder value '{val}'");
                    }
                }
            }

            if (outlierRows.Count > 0)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.SuspiciousOutliers,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = outlierRows.Count,
                    AffectedRowIndices = outlierRows,
                    ExampleValues = examples,
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Found {outlierRows.Count} suspicious value(s) in '{column.Name}' (e.g. negative quantities or extreme placeholders).",
                    ProposedFixDescription = "Review implausible values to verify whether they represent returns, errors, or sentinel data.",
                    IsAiSuggested = false,
                    Confidence = 0.85,
                    AmbiguityReason = "Negative values might be legitimate return adjustments in certain accounting workflows.",
                    CanAutoApply = false
                });
            }
        }

        private static void CheckCurrencyAndNumericFormatting(ImportedDataset dataset, int colIdx, DatasetColumn column, List<DataIssue> issues)
        {
            var symbolCounts = new Dictionary<string, int>();
            for (int i = 0; i < dataset.Rows.Count; i++)
            {
                var val = dataset.Rows[i].Values[colIdx];
                if (string.IsNullOrWhiteSpace(val)) continue;

                var match = CurrencySymbolRegex.Match(val);
                if (match.Success)
                {
                    var sym = match.Value;
                    symbolCounts[sym] = symbolCounts.GetValueOrDefault(sym, 0) + 1;
                }
            }

            if (symbolCounts.Count > 1)
            {
                issues.Add(new DataIssue
                {
                    Category = IssueCategory.InconsistentCurrencyFormatting,
                    Column = column.Name,
                    ColumnIndex = colIdx,
                    AffectedRowCount = symbolCounts.Values.Sum(),
                    AffectedRowIndices = new List<int>(),
                    ExampleValues = symbolCounts.Select(kv => $"{kv.Key} ({kv.Value}x)").ToList(),
                    Severity = IssueSeverity.Warning,
                    Explanation = $"Column '{column.Name}' mixes different currency symbols or codes: {string.Join(", ", symbolCounts.Keys)}.",
                    ProposedFixDescription = "Standardize column to a single uniform currency representation.",
                    IsAiSuggested = false,
                    Confidence = 0.7,
                    AmbiguityReason = "Converting multi-currency values without exchange rates risks corrupting financial figures.",
                    CanAutoApply = false
                });
            }
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }
}
