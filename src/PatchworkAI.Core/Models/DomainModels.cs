using System;
using System.Collections.Generic;

namespace PatchworkAI.Core.Models
{
    public enum IssueCategory
    {
        MissingValues,
        DuplicateRows,
        InconsistentDateFormats,
        NumbersStoredAsText,
        LeadingTrailingWhitespace,
        InconsistentCapitalization,
        CategoricalInconsistencies,
        MixedDataTypes,
        InvalidEmails,
        SuspiciousOutliers,
        InconsistentCurrencyFormatting,
        AmbiguousColumnNames
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum TransformationSource
    {
        Deterministic,
        LocalAi,
        UserManual
    }

    public enum TransformationStatus
    {
        Pending,
        Approved,
        Rejected,
        Applied,
        Reverted
    }

    public enum DatasetFormat
    {
        Csv,
        Xlsx,
        Unknown
    }

    public class DatasetColumn
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string InferredType { get; set; } = "String";
        public int NullCount { get; set; }
        public int UniqueCount { get; set; }
        public List<string> SampleValues { get; set; } = new();
        public bool HasPotentialIssues { get; set; }

        public override string ToString() => $"{Name} ({InferredType})";
    }

    public class DataRowModel
    {
        public int RowIndex { get; set; }
        public List<string?> Values { get; set; } = new();
        public List<string?> OriginalValues { get; set; } = new();
        public bool IsModified { get; set; }

        public DataRowModel Clone()
        {
            return new DataRowModel
            {
                RowIndex = RowIndex,
                Values = new List<string?>(Values),
                OriginalValues = new List<string?>(OriginalValues),
                IsModified = IsModified
            };
        }
    }

    public class ImportedDataset
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DatasetFormat Format { get; set; }
        public List<string> SheetNames { get; set; } = new();
        public string? SelectedSheet { get; set; }
        public List<DatasetColumn> Columns { get; set; } = new();
        public List<DataRowModel> Rows { get; set; } = new();
        public DateTime LoadedAt { get; set; } = DateTime.UtcNow;

        public int TotalRows { get => Rows.Count; set { } }
        public int TotalColumns { get => Columns.Count; set { } }
    }

    public class DataIssue
    {
        public string IssueId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        public IssueCategory Category { get; set; }
        public string Column { get; set; } = string.Empty;
        public int ColumnIndex { get; set; } = -1;
        public int AffectedRowCount { get; set; }
        public List<int> AffectedRowIndices { get; set; } = new();
        public List<string> ExampleValues { get; set; } = new();
        public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
        public string Explanation { get; set; } = string.Empty;
        public string ProposedFixDescription { get; set; } = string.Empty;
        public bool IsAiSuggested { get; set; }
        public double Confidence { get; set; } = 1.0;
        public string? AmbiguityReason { get; set; }
        public bool CanAutoApply { get; set; }
        public string? DeterministicRuleId { get; set; }
    }

    public class ProposedTransformation
    {
        public string TransformationId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        public string IssueId { get; set; } = string.Empty;
        public int RowIndex { get; set; }
        public int ColumnIndex { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string? OriginalValue { get; set; }
        public string? ProposedValue { get; set; }
        public string Reason { get; set; } = string.Empty;
        public TransformationSource Source { get; set; } = TransformationSource.Deterministic;
        public TransformationStatus Status { get; set; } = TransformationStatus.Pending;
        public double Confidence { get; set; } = 1.0;
        public bool IsSafeDeterministic { get; set; }
    }

    public class TransformationResult
    {
        public bool Success { get; set; }
        public int AffectedCellsCount { get; set; }
        public int AffectedRowsCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ChangeLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int RowIndex { get; set; }
        public int ColumnIndex { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string? PreviousValue { get; set; }
        public string? NewValue { get; set; }
        public string RuleApplied { get; set; } = string.Empty;
        public TransformationSource Source { get; set; }
        public TransformationStatus Status { get; set; }
    }

    public class ExportReport
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string ExportPath { get; set; } = string.Empty;
        public int TotalIssuesDetected { get; set; }
        public int ApprovedFixesCount { get; set; }
        public int RejectedSuggestionsCount { get; set; }
        public int UnresolvedWarningsCount { get; set; }
        public int AffectedRowsCount { get; set; }
        public int AffectedCellsCount { get; set; }
        public long ProcessingTimeMs { get; set; }
        public DateTime ExportTimestamp { get; set; } = DateTime.UtcNow;
        public DatasetFormat ExportFormat { get; set; }
        public List<ChangeLogEntry> AppliedChanges { get; set; } = new();
    }

    public class LocalModelStatus
    {
        public bool IsAvailable { get; set; }
        public string EndpointUrl { get; set; } = "http://localhost:11434";
        public string ConfiguredModel { get; set; } = "llama3.1:latest";
        public List<string> AvailableModels { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public long ResponseTimeMs { get; set; }
    }

    public class UserSettings
    {
        public string OllamaEndpoint { get; set; } = "http://localhost:11434";
        public string ModelName { get; set; } = "llama3.1:latest";
        public string Theme { get; set; } = "Dark";
        public string? DefaultExportDirectory { get; set; }
        public bool AutoDetectOnLoad { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
    }
}
