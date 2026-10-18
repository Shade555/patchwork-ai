using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PatchworkAI.Core.Models;
using PatchworkAI.Core.Services;
using Xunit;

namespace PatchworkAI.Tests
{
    public class TransformationExecutorTests
    {
        private readonly TransformationExecutor _executor = new();

        [Fact]
        public void ApplyApprovedTransformations_AppliesOnlyApprovedAndModifiesWorkingCopy()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn>
                {
                    new() { Index = 0, Name = "Name" },
                    new() { Index = 1, Name = "Price" }
                },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "  Widget A  ", "$100.00" }, OriginalValues = new List<string?> { "  Widget A  ", "$100.00" } },
                    new() { RowIndex = 2, Values = new List<string?> { "Widget B", "200.00" }, OriginalValues = new List<string?> { "Widget B", "200.00" } }
                }
            };

            var transApproved = new ProposedTransformation
            {
                RowIndex = 1,
                ColumnIndex = 0,
                ColumnName = "Name",
                OriginalValue = "  Widget A  ",
                ProposedValue = "Widget A",
                Reason = "Trim whitespace",
                Status = TransformationStatus.Approved
            };

            var transRejected = new ProposedTransformation
            {
                RowIndex = 1,
                ColumnIndex = 1,
                ColumnName = "Price",
                OriginalValue = "$100.00",
                ProposedValue = "100.00",
                Reason = "Clean currency",
                Status = TransformationStatus.Rejected // REJECTED
            };

            // Act
            var result = _executor.ApplyApprovedTransformations(dataset, new[] { transApproved, transRejected });

            // Assert
            result.Success.Should().BeTrue();
            result.AffectedCellsCount.Should().Be(1);

            // Row 1 Name must be updated
            dataset.Rows[0].Values[0].Should().Be("Widget A");
            dataset.Rows[0].IsModified.Should().BeTrue();

            // Row 1 Price must REMAIN UNCHANGED because it was rejected
            dataset.Rows[0].Values[1].Should().Be("$100.00");

            // Original values must remain untouched
            dataset.Rows[0].OriginalValues[0].Should().Be("  Widget A  ");
            dataset.Rows[0].OriginalValues[1].Should().Be("$100.00");

            // Change log check
            _executor.ChangeLog.Should().HaveCount(1);
            _executor.ChangeLog[0].PreviousValue.Should().Be("  Widget A  ");
            _executor.ChangeLog[0].NewValue.Should().Be("Widget A");
        }

        [Fact]
        public void UndoLast_RestoresPreviousStateAccurately()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn> { new() { Index = 0, Name = "Category" } },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "electronics" }, OriginalValues = new List<string?> { "electronics" } }
                }
            };

            var trans = new ProposedTransformation
            {
                RowIndex = 1,
                ColumnIndex = 0,
                ColumnName = "Category",
                OriginalValue = "electronics",
                ProposedValue = "Electronics",
                Status = TransformationStatus.Approved
            };

            _executor.ApplyApprovedTransformations(dataset, new[] { trans });
            dataset.Rows[0].Values[0].Should().Be("Electronics");
            _executor.CanUndo.Should().BeTrue();

            // Act: Undo
            var undoResult = _executor.UndoLast(dataset);

            // Assert
            undoResult.Success.Should().BeTrue();
            dataset.Rows[0].Values[0].Should().Be("electronics");
            _executor.CanUndo.Should().BeFalse();
        }

        [Fact]
        public void RevertTransformation_RevertsSingleTargetCell()
        {
            // Arrange
            var dataset = new ImportedDataset
            {
                Columns = new List<DatasetColumn> { new() { Index = 0, Name = "Item" } },
                Rows = new List<DataRowModel>
                {
                    new() { RowIndex = 1, Values = new List<string?> { "Widget" }, OriginalValues = new List<string?> { "  Widget  " } }
                }
            };

            var trans = new ProposedTransformation
            {
                RowIndex = 1,
                ColumnIndex = 0,
                ColumnName = "Item",
                OriginalValue = "  Widget  ",
                ProposedValue = "Widget",
                Status = TransformationStatus.Applied
            };

            // Act: Revert
            var revertResult = _executor.RevertTransformation(dataset, trans);

            // Assert
            revertResult.Success.Should().BeTrue();
            dataset.Rows[0].Values[0].Should().Be("  Widget  ");
            trans.Status.Should().Be(TransformationStatus.Reverted);
        }
    }
}
