using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public interface ITransformationPlanner
    {
        List<ProposedTransformation> PlanTransformations(
            ImportedDataset dataset,
            IEnumerable<DataIssue> issues,
            AiAnalysisResult? aiResult = null);
    }

    public class TransformationPlanner : ITransformationPlanner
    {
        private static readonly Regex CurrencyCleanRegex = new(
            @"[\$\€\£\¥\₹]|USD|EUR|GBP",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public List<ProposedTransformation> PlanTransformations(
            ImportedDataset dataset,
            IEnumerable<DataIssue> issues,
            AiAnalysisResult? aiResult = null)
        {
            var plans = new List<ProposedTransformation>();
            if (dataset == null || dataset.Rows.Count == 0) return plans;

            // 1. Process deterministic issues
            foreach (var issue in issues)
            {
                if (issue.ColumnIndex < 0 || issue.ColumnIndex >= dataset.Columns.Count)
                {
                    continue;
                }

                int colIdx = issue.ColumnIndex;
                var colName = dataset.Columns[colIdx].Name;

                switch (issue.Category)
                {
                    case IssueCategory.LeadingTrailingWhitespace:
                        foreach (var rowIdx in issue.AffectedRowIndices)
                        {
                            var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == rowIdx);
                            if (row != null && colIdx < row.Values.Count)
                            {
                                var orig = row.Values[colIdx];
                                if (orig != null)
                                {
                                    plans.Add(new ProposedTransformation
                                    {
                                        IssueId = issue.IssueId,
                                        RowIndex = rowIdx,
                                        ColumnIndex = colIdx,
                                        ColumnName = colName,
                                        OriginalValue = orig,
                                        ProposedValue = orig.Trim(),
                                        Reason = "Trim leading/trailing whitespace",
                                        Source = TransformationSource.Deterministic,
                                        Status = TransformationStatus.Pending,
                                        Confidence = 1.0,
                                        IsSafeDeterministic = true
                                    });
                                }
                            }
                        }
                        break;

                    case IssueCategory.NumbersStoredAsText:
                        foreach (var rowIdx in issue.AffectedRowIndices)
                        {
                            var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == rowIdx);
                            if (row != null && colIdx < row.Values.Count)
                            {
                                var orig = row.Values[colIdx];
                                if (orig != null)
                                {
                                    var cleaned = CurrencyCleanRegex.Replace(orig.Trim(), "").Replace(",", "").Trim().Trim('"');
                                    if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                                    {
                                        plans.Add(new ProposedTransformation
                                        {
                                            IssueId = issue.IssueId,
                                            RowIndex = rowIdx,
                                            ColumnIndex = colIdx,
                                            ColumnName = colName,
                                            OriginalValue = orig,
                                            ProposedValue = num.ToString(CultureInfo.InvariantCulture),
                                            Reason = "Convert formatted string number to clean numeric standard",
                                            Source = TransformationSource.Deterministic,
                                            Status = TransformationStatus.Pending,
                                            Confidence = 0.95,
                                            IsSafeDeterministic = true
                                        });
                                    }
                                }
                            }
                        }
                        break;

                    case IssueCategory.InconsistentCapitalization:
                        if (issue.DeterministicRuleId == "COLLAPSE_INTERNAL_SPACES")
                        {
                            foreach (var rowIdx in issue.AffectedRowIndices)
                            {
                                var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == rowIdx);
                                if (row != null && colIdx < row.Values.Count)
                                {
                                    var orig = row.Values[colIdx];
                                    if (orig != null)
                                    {
                                        var collapsed = Regex.Replace(orig, @"\s{2,}", " ");
                                        plans.Add(new ProposedTransformation
                                        {
                                            IssueId = issue.IssueId,
                                            RowIndex = rowIdx,
                                            ColumnIndex = colIdx,
                                            ColumnName = colName,
                                            OriginalValue = orig,
                                            ProposedValue = collapsed,
                                            Reason = "Collapse redundant multiple spaces into single space",
                                            Source = TransformationSource.Deterministic,
                                            Status = TransformationStatus.Pending,
                                            Confidence = 1.0,
                                            IsSafeDeterministic = true
                                        });
                                    }
                                }
                            }
                        }
                        else if (issue.DeterministicRuleId == "STANDARDIZE_CASING_TITLE")
                        {
                            // Find predominant casing or convert to Title Case
                            for (int i = 0; i < dataset.Rows.Count; i++)
                            {
                                var orig = dataset.Rows[i].Values[colIdx];
                                if (!string.IsNullOrWhiteSpace(orig))
                                {
                                    var titleCased = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(orig.Trim().ToLowerInvariant());
                                    if (titleCased != orig.Trim())
                                    {
                                        plans.Add(new ProposedTransformation
                                        {
                                            IssueId = issue.IssueId,
                                            RowIndex = dataset.Rows[i].RowIndex,
                                            ColumnIndex = colIdx,
                                            ColumnName = colName,
                                            OriginalValue = orig,
                                            ProposedValue = titleCased,
                                            Reason = "Standardize casing to Title Case",
                                            Source = TransformationSource.Deterministic,
                                            Status = TransformationStatus.Pending,
                                            Confidence = 0.9,
                                            IsSafeDeterministic = true
                                        });
                                    }
                                }
                            }
                        }
                        break;

                    case IssueCategory.InconsistentDateFormats:
                        if (issue.DeterministicRuleId == "STANDARDIZE_DATE_ISO8601")
                        {
                            foreach (var rowIdx in issue.AffectedRowIndices)
                            {
                                var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == rowIdx);
                                if (row != null && colIdx < row.Values.Count)
                                {
                                    var orig = row.Values[colIdx];
                                    if (!string.IsNullOrWhiteSpace(orig) &&
                                        DateTime.TryParse(orig.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                                    {
                                        var iso = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                                        if (iso != orig.Trim())
                                        {
                                            plans.Add(new ProposedTransformation
                                            {
                                                IssueId = issue.IssueId,
                                                RowIndex = rowIdx,
                                                ColumnIndex = colIdx,
                                                ColumnName = colName,
                                                OriginalValue = orig,
                                                ProposedValue = iso,
                                                Reason = "Standardize date to ISO 8601 (YYYY-MM-DD)",
                                                Source = TransformationSource.Deterministic,
                                                Status = TransformationStatus.Pending,
                                                Confidence = 0.9,
                                                IsSafeDeterministic = true
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        break;
                }
            }

            // 2. Process AI Suggestions if present
            if (aiResult != null)
            {
                // Normalization rules for categorical columns
                foreach (var rule in aiResult.NormalizationRules)
                {
                    var col = dataset.Columns.FirstOrDefault(c => c.Name.Equals(rule.ColumnName, StringComparison.OrdinalIgnoreCase));
                    if (col != null)
                    {
                        var variants = new HashSet<string>(rule.VariantValues, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < dataset.Rows.Count; i++)
                        {
                            var row = dataset.Rows[i];
                            var currentVal = col.Index < row.Values.Count ? row.Values[col.Index] : null;

                            if (currentVal != null && variants.Contains(currentVal.Trim()) &&
                                !currentVal.Trim().Equals(rule.CanonicalValue, StringComparison.Ordinal))
                            {
                                // Avoid duplicate proposal for same cell
                                if (!plans.Any(p => p.RowIndex == row.RowIndex && p.ColumnIndex == col.Index))
                                {
                                    plans.Add(new ProposedTransformation
                                    {
                                        IssueId = $"AI-NORM-{col.Index}",
                                        RowIndex = row.RowIndex,
                                        ColumnIndex = col.Index,
                                        ColumnName = col.Name,
                                        OriginalValue = currentVal,
                                        ProposedValue = rule.CanonicalValue,
                                        Reason = $"AI normalization: {rule.Rationale}",
                                        Source = TransformationSource.LocalAi,
                                        Status = TransformationStatus.Pending,
                                        Confidence = rule.Confidence,
                                        IsSafeDeterministic = false // AI suggestions require manual confirmation
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return plans;
        }
    }
}
