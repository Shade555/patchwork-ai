using System;
using System.Collections.Generic;
using System.Linq;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public interface ITransformationExecutor
    {
        List<ChangeLogEntry> ChangeLog { get; }
        bool CanUndo { get; }
        TransformationResult ApplyApprovedTransformations(ImportedDataset dataset, IEnumerable<ProposedTransformation> transformations);
        TransformationResult RevertTransformation(ImportedDataset dataset, ProposedTransformation transformation);
        TransformationResult UndoLast(ImportedDataset dataset);
        void ResetSession(ImportedDataset dataset);
    }

    public class TransformationExecutor : ITransformationExecutor
    {
        private readonly Stack<List<ChangeLogEntry>> _undoHistory = new();
        public List<ChangeLogEntry> ChangeLog { get; } = new();

        public bool CanUndo => _undoHistory.Count > 0;

        public TransformationResult ApplyApprovedTransformations(ImportedDataset dataset, IEnumerable<ProposedTransformation> transformations)
        {
            var approvedList = transformations.Where(t => t.Status == TransformationStatus.Approved).ToList();
            if (approvedList.Count == 0)
            {
                return new TransformationResult
                {
                    Success = true,
                    AffectedCellsCount = 0,
                    AffectedRowsCount = 0,
                    Message = "No approved transformations to apply."
                };
            }

            var batchLog = new List<ChangeLogEntry>();
            var affectedRows = new HashSet<int>();
            int affectedCells = 0;

            foreach (var trans in approvedList)
            {
                var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == trans.RowIndex);
                if (row != null && trans.ColumnIndex >= 0 && trans.ColumnIndex < row.Values.Count)
                {
                    var previousVal = row.Values[trans.ColumnIndex];

                    // Apply change in-memory
                    row.Values[trans.ColumnIndex] = trans.ProposedValue;
                    row.IsModified = true;

                    var logEntry = new ChangeLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        RowIndex = trans.RowIndex,
                        ColumnIndex = trans.ColumnIndex,
                        ColumnName = trans.ColumnName,
                        PreviousValue = previousVal,
                        NewValue = trans.ProposedValue,
                        RuleApplied = trans.Reason,
                        Source = trans.Source,
                        Status = TransformationStatus.Applied
                    };

                    batchLog.Add(logEntry);
                    ChangeLog.Add(logEntry);
                    affectedRows.Add(trans.RowIndex);
                    affectedCells++;

                    trans.Status = TransformationStatus.Applied;
                }
            }

            _undoHistory.Push(batchLog);

            return new TransformationResult
            {
                Success = true,
                AffectedCellsCount = affectedCells,
                AffectedRowsCount = affectedRows.Count,
                Message = $"Successfully applied {affectedCells} transformation(s) across {affectedRows.Count} row(s)."
            };
        }

        public TransformationResult RevertTransformation(ImportedDataset dataset, ProposedTransformation transformation)
        {
            var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == transformation.RowIndex);
            if (row == null || transformation.ColumnIndex < 0 || transformation.ColumnIndex >= row.Values.Count)
            {
                return new TransformationResult
                {
                    Success = false,
                    Message = "Target cell could not be found for rollback."
                };
            }

            row.Values[transformation.ColumnIndex] = transformation.OriginalValue;
            transformation.Status = TransformationStatus.Reverted;

            var logEntry = new ChangeLogEntry
            {
                Timestamp = DateTime.UtcNow,
                RowIndex = transformation.RowIndex,
                ColumnIndex = transformation.ColumnIndex,
                ColumnName = transformation.ColumnName,
                PreviousValue = transformation.ProposedValue,
                NewValue = transformation.OriginalValue,
                RuleApplied = "Manual Revert",
                Source = TransformationSource.UserManual,
                Status = TransformationStatus.Reverted
            };

            ChangeLog.Add(logEntry);

            return new TransformationResult
            {
                Success = true,
                AffectedCellsCount = 1,
                AffectedRowsCount = 1,
                Message = $"Reverted row {transformation.RowIndex}, column '{transformation.ColumnName}'."
            };
        }

        public TransformationResult UndoLast(ImportedDataset dataset)
        {
            if (_undoHistory.Count == 0)
            {
                return new TransformationResult
                {
                    Success = false,
                    Message = "No transformations in undo history."
                };
            }

            var lastBatch = _undoHistory.Pop();
            int revertedCount = 0;
            var affectedRows = new HashSet<int>();

            foreach (var item in lastBatch)
            {
                var row = dataset.Rows.FirstOrDefault(r => r.RowIndex == item.RowIndex);
                if (row != null && item.ColumnIndex >= 0 && item.ColumnIndex < row.Values.Count)
                {
                    row.Values[item.ColumnIndex] = item.PreviousValue;
                    affectedRows.Add(item.RowIndex);
                    revertedCount++;

                    ChangeLog.Add(new ChangeLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        RowIndex = item.RowIndex,
                        ColumnIndex = item.ColumnIndex,
                        ColumnName = item.ColumnName,
                        PreviousValue = item.NewValue,
                        NewValue = item.PreviousValue,
                        RuleApplied = "Undo Batch Action",
                        Source = TransformationSource.UserManual,
                        Status = TransformationStatus.Reverted
                    });
                }
            }

            return new TransformationResult
            {
                Success = true,
                AffectedCellsCount = revertedCount,
                AffectedRowsCount = affectedRows.Count,
                Message = $"Undid {revertedCount} transformation(s) across {affectedRows.Count} row(s)."
            };
        }

        public void ResetSession(ImportedDataset dataset)
        {
            _undoHistory.Clear();
            ChangeLog.Clear();

            foreach (var row in dataset.Rows)
            {
                row.Values = new List<string?>(row.OriginalValues);
                row.IsModified = false;
            }
        }
    }
}
