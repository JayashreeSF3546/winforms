// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests;

public partial class DataGridViewCellTests
{
    [Collection("Sequential")]
    [UISettings(MaxAttempts = 3)] // Try up to 3 times before failing.
    public class ClipboardTests
    {
        public static TheoryData<int, bool, bool, bool, bool, string?, object?> GetClipboardContent_TheoryData() => new()
        {
            { -2, true, true, true, true, "format", null },
            { -2, true, true, true, true, null, null },
            { -1, true, true, true, true, "format", null },
            { -1, true, true, true, true, null, null },
            { 0, true, true, true, true, "format", null },
            { 0, true, true, true, true, null, null },
        };

        [WinFormsTheory]
        [MemberData(nameof(GetClipboardContent_TheoryData))]
        public void DataGridViewCell_GetClipboardContent_Invoke_ReturnsExpected(int rowIndex, bool firstCell, bool lastCell, bool inFirstRow, bool inLastRow, string? format, object? expected)
        {
            using SubDataGridViewCell cell = new();
            cell.GetClipboardContent(rowIndex, firstCell, lastCell, inFirstRow, inLastRow, format).Should().Be(expected);
        }

        [WinFormsTheory]
        [MemberData(nameof(GetClipboardContent_TheoryData))]
        public void DataGridViewCell_GetClipboardContent_InvokeWithRow_ReturnsExpected(int rowIndex, bool firstCell, bool lastCell, bool inFirstRow, bool inLastRow, string? format, object? expected)
        {
            using DataGridViewRow row = new();
            using SubDataGridViewCell cell = new();
            row.Cells.Add(cell);
            cell.GetClipboardContent(rowIndex, firstCell, lastCell, inFirstRow, inLastRow, format).Should().Be(expected);
        }

        public static TheoryData<bool, bool, bool, bool, string?, object?> GetClipboardContent_WithColumn_TheoryData() => new()
        {
            { true, true, true, true, "format", null },
            { true, true, true, true, null, null }
        };

        [WinFormsTheory]
        [MemberData(nameof(GetClipboardContent_WithColumn_TheoryData))]
        public void DataGridViewCell_GetClipboardContent_InvokeWithColumn_ReturnsExpected(bool firstCell, bool lastCell, bool inFirstRow, bool inLastRow, string? format, object? expected)
        {
            using DataGridViewColumn column = new();
            using SubDataGridViewColumnHeaderCell cell = new();
            column.HeaderCell = cell;
            cell.GetClipboardContent(-1, firstCell, lastCell, inFirstRow, inLastRow, format).Should().Be(expected);
        }

        [WinFormsTheory]
        [MemberData(nameof(GetClipboardContent_WithColumn_TheoryData))]
        public void DataGridViewCell_GetClipboardContent_InvokeWithDataGridView_ReturnsExpected(bool firstCell, bool lastCell, bool inFirstRow, bool inLastRow, string? format, object? expected)
        {
            using SubDataGridViewCell cellTemplate = new();
            using DataGridViewColumn column = new()
            {
                CellTemplate = cellTemplate
            };
            using DataGridView control = new();
            control.Columns.Add(column);
            SubDataGridViewCell cell = (SubDataGridViewCell)control.Rows[0].Cells[0];
            cell.GetClipboardContent(0, firstCell, lastCell, inFirstRow, inLastRow, format).Should().Be(expected);
        }

        [WinFormsTheory]
        [MemberData(nameof(GetClipboardContent_WithColumn_TheoryData))]
        public void DataGridViewCell_GetClipboardContent_InvokeShared_ReturnsExpected(bool firstCell, bool lastCell, bool inFirstRow, bool inLastRow, string? format, object? expected)
        {
            using SubDataGridViewCell cellTemplate = new();
            using DataGridViewColumn column = new()
            {
                CellTemplate = cellTemplate
            };
            using DataGridView control = new();
            control.Columns.Add(column);
            SubDataGridViewCell cell = (SubDataGridViewCell)control.Rows.SharedRow(0).Cells[0];
            cell.GetClipboardContent(0, firstCell, lastCell, inFirstRow, inLastRow, format).Should().Be(expected);
        }

        [WinFormsTheory]
        [InlineData(-2)]
        [InlineData(0)]
        public void DataGridViewCell_GetClipboardContent_InvalidRowIndexWithColumn_ThrowsArgumentOutOfRangeException(int rowIndex)
        {
            using DataGridViewColumn column = new();
            using SubDataGridViewColumnHeaderCell cell = new();
            column.HeaderCell = cell;
            Action action = () => cell.GetClipboardContent(rowIndex, true, true, true, true, "format");
            action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("rowIndex");
        }

        [WinFormsTheory]
        [InlineData(-2)]
        [InlineData(-1)]
        [InlineData(1)]
        public void DataGridViewCell_GetClipboardContent_InvalidRowIndexWithDataGridView_ThrowsArgumentOutOfRangeException(int rowIndex)
        {
            using SubDataGridViewCell cellTemplate = new();
            using DataGridViewColumn column = new()
            {
                CellTemplate = cellTemplate
            };
            using DataGridView control = new();
            control.Columns.Add(column);
            SubDataGridViewCell cell = (SubDataGridViewCell)control.Rows[0].Cells[0];
            Action action = () => cell.GetClipboardContent(rowIndex, true, true, true, true, "format");
            action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("rowIndex");
        }

        [WinFormsTheory]
        [InlineData(-2)]
        [InlineData(-1)]
        [InlineData(1)]
        public void DataGridViewCell_GetClipboardContent_InvalidRowIndexShared_ThrowsArgumentOutOfRangeException(int rowIndex)
        {
            using SubDataGridViewCell cellTemplate = new();
            using DataGridViewColumn column = new()
            {
                CellTemplate = cellTemplate
            };
            using DataGridView control = new();
            control.Columns.Add(column);
            SubDataGridViewCell cell = (SubDataGridViewCell)control.Rows.SharedRow(0).Cells[0];
            Action action = () => cell.GetClipboardContent(rowIndex, true, true, true, true, "format");
            action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("rowIndex");
        }

        [WinFormsFact]
        public void DataGridViewColumnHeaderCell_GetClipboardContent_Html_NullValue_ReturnsNbsp()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add("Column1", "HeaderText");

            DataGridViewColumnHeaderCell cell = dataGridView.Columns[0].HeaderCell;
            cell.Value = null;

            object? result = cell.GetClipboardContentInternal(
                rowIndex: -1,
                firstCell: true,
                lastCell: true,
                inFirstRow: true,
                inLastRow: true,
                format: DataFormats.Html);

            string html = Assert.IsType<string>(result);

            Assert.Contains("&nbsp;", html);
        }

        [WinFormsFact]
        public void DataGridViewColumnHeaderCell_GetClipboardContent_Html_WithValue_ReturnsHeaderText()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add("Column1", "HeaderText");

            DataGridViewColumnHeaderCell cell = dataGridView.Columns[0].HeaderCell;

            object? result = cell.GetClipboardContentInternal(
                -1,
                true,
                true,
                true,
                true,
                DataFormats.Html);

            string html = Assert.IsType<string>(result);

            Assert.Contains("HeaderText", html);
            Assert.Contains("<TH>", html);
            Assert.Contains("</TABLE>", html);
        }

        [WinFormsFact]
        public void DataGridViewColumnHeaderCell_GetClipboardContent_Text_NotLastCell_AppendsTab()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add("Column1", "HeaderText");

            DataGridViewColumnHeaderCell cell = dataGridView.Columns[0].HeaderCell;

            object? result = cell.GetClipboardContentInternal(
                -1,
                true,
                false,
                true,
                true,
                DataFormats.Text);

            Assert.Equal("HeaderText\t", result);
        }

        [WinFormsFact]
        public void DataGridViewColumnHeaderCell_GetClipboardContent_Text_NotLastRow_AppendsNewLine()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add("Column1", "HeaderText");

            DataGridViewColumnHeaderCell cell = dataGridView.Columns[0].HeaderCell;

            object? result = cell.GetClipboardContentInternal(
                -1,
                true,
                true,
                true,
                false,
                DataFormats.Text);

            Assert.Equal("HeaderText\r\n", result);
        }

        [WinFormsFact]
        public void DataGridViewColumnHeaderCell_GetClipboardContent_Csv_EscapesValue()
        {
            using DataGridView dataGridView = new();

            DataGridViewColumn column = new(new DataGridViewTextBoxCell())
            {
                HeaderText = "A,B"
            };

            dataGridView.Columns.Add(column);

            DataGridViewColumnHeaderCell cell = column.HeaderCell;

            object? result = cell.GetClipboardContentInternal(
                -1,
                true,
                false,
                true,
                true,
                DataFormats.CommaSeparatedValue);

            Assert.NotNull(result);
        }

        [WinFormsFact]
        public void DataGridViewRowHeaderCell_GetClipboardContent_Html_NullValue_ReturnsNbsp()
        {
            using DataGridView dataGridView = new();

            dataGridView.RowHeadersVisible = true;
            dataGridView.Columns.Add("Column1", "Column1");

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewRowHeaderCell cell = dataGridView.Rows[rowIndex].HeaderCell;
            cell.Value = null;

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                true,
                true,
                true,
                true,
                DataFormats.Html);

            string html = Assert.IsType<string>(result);

            Assert.Contains("&nbsp;", html);
        }

        [WinFormsFact]
        public void DataGridViewRowHeaderCell_GetClipboardContent_Html_WithValue_ReturnsBoldText()
        {
            using DataGridView dataGridView = new();

            dataGridView.RowHeadersVisible = true;
            dataGridView.Columns.Add("Column1", "Column1");

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewRowHeaderCell cell = dataGridView.Rows[rowIndex].HeaderCell;
            cell.Value = "RowHeader";

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                true,
                true,
                true,
                true,
                DataFormats.Html);

            string html = Assert.IsType<string>(result);

            Assert.Contains("RowHeader", html);
            Assert.Contains("<B>", html);
        }

        [WinFormsFact]
        public void DataGridViewRowHeaderCell_GetClipboardContent_Text_NotLastCell_AppendsTab()
        {
            using DataGridView dataGridView = new();

            dataGridView.RowHeadersVisible = true;
            dataGridView.Columns.Add("Column1", "Column1");

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewRowHeaderCell cell = dataGridView.Rows[rowIndex].HeaderCell;
            cell.Value = "RowHeader";

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                true,
                false,
                true,
                true,
                DataFormats.Text);

            Assert.Equal("RowHeader\t", result);
        }

        [WinFormsFact]
        public void DataGridViewRowHeaderCell_GetClipboardContent_Text_NotLastRow_AppendsNewLine()
        {
            using DataGridView dataGridView = new();

            dataGridView.RowHeadersVisible = true;
            dataGridView.Columns.Add("Column1", "Column1");

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewRowHeaderCell cell = dataGridView.Rows[rowIndex].HeaderCell;
            cell.Value = "RowHeader";

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                true,
                true,
                true,
                false,
                DataFormats.Text);

            Assert.Equal("RowHeader\r\n", result);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_SelectedCell_Html_ReturnsFormattedValue()
        {
            using DataGridView dataGridView = new()
            {
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };

            dataGridView.Columns.Add(
                new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex].Cells[0];

            cell.Value = "Test";

            dataGridView.CurrentCell = cell;
            cell.Selected = true;

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                firstCell: true,
                lastCell: true,
                inFirstRow: true,
                inLastRow: true,
                format: DataFormats.Html);

            string html = Assert.IsType<string>(result);

            Assert.Contains("Test", html);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_SelectedCell_Text_ReturnsValue()
        {
            using DataGridView dataGridView = new()
            {
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };

            dataGridView.Columns.Add(
                new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex].Cells[0];

            cell.Value = "Test";

            dataGridView.CurrentCell = cell;
            cell.Selected = true;

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                true,
                true,
                true,
                true,
                DataFormats.Text);

            Assert.Equal("Test", result);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_SelectedRow_AssignsFormattedValue()
        {
            using DataGridView dataGridView = new()
            {
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            dataGridView.Columns.Add(
                new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell =
                dataGridView.Rows[rowIndex].Cells[0];

            cell.Value = "Test";

            dataGridView.Rows[rowIndex].Selected = true;

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                true,
                true,
                true,
                true,
                DataFormats.Text);

            Assert.Equal("Test", result);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_Html_UnselectedCell_ReturnsNbsp()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex].Cells[0];
            cell.Value = "Test";

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                firstCell: true,
                lastCell: true,
                inFirstRow: true,
                inLastRow: true,
                format: DataFormats.Html);

            string html = Assert.IsType<string>(result);

            Assert.Contains("&nbsp;", html);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_Csv_ValueWithComma_EscapesText()
        {
            using DataGridView dataGridView = new()
            {
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex].Cells[0];

            cell.Value = "A,B";

            dataGridView.Rows[rowIndex].Selected = true;

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                firstCell: false,
                lastCell: false,
                inFirstRow: false,
                inLastRow: true,
                format: DataFormats.CommaSeparatedValue);

            string text = Assert.IsType<string>(result);

            Assert.StartsWith("\"", text);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_Text_NotLastCell_AppendsTab()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex].Cells[0];

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                firstCell: true,
                lastCell: false,
                inFirstRow: true,
                inLastRow: true,
                format: DataFormats.Text);

            Assert.EndsWith("\t", Assert.IsType<string>(result));
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_Csv_NotLastCell_AppendsComma()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn());

            int rowIndex = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex].Cells[0];

            object? result = cell.GetClipboardContentInternal(
                rowIndex,
                firstCell: true,
                lastCell: false,
                inFirstRow: true,
                inLastRow: true,
                format: DataFormats.CommaSeparatedValue);

            Assert.EndsWith(",", Assert.IsType<string>(result));
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_Text_LastCell_NotLastRow_AppendsNewLine()
        {
            using DataGridView dataGridView = new()
            {
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn());

            int rowIndex1 = dataGridView.Rows.Add();
            int rowIndex2 = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex1].Cells[0];

            cell.Value = "Test";

            dataGridView.Rows[rowIndex1].Selected = true;

            object? result = cell.GetClipboardContentInternal(
                rowIndex1,
                firstCell: true,
                lastCell: true,
                inFirstRow: true,
                inLastRow: false,
                format: DataFormats.Text);

            string text = Assert.IsType<string>(result);

            Assert.EndsWith("\r\n", text);
        }

        [WinFormsFact]
        public void DataGridViewCell_GetClipboardContent_Csv_LastCell_NotLastRow_AppendsNewLine()
        {
            using DataGridView dataGridView = new();

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn());

            int rowIndex1 = dataGridView.Rows.Add();
            int rowIndex2 = dataGridView.Rows.Add();

            DataGridViewCell cell = dataGridView.Rows[rowIndex1].Cells[0];

            object? result = cell.GetClipboardContentInternal(
                rowIndex1,
                firstCell: true,
                lastCell: true,
                inFirstRow: true,
                inLastRow: false,
                format: DataFormats.CommaSeparatedValue);

            Assert.EndsWith("\r\n", Assert.IsType<string>(result));
        }
    }
}
