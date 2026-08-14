using ClosedXML.Excel;

namespace TMobileScraper.Helpers;

public static class ExcelExportHelper
{
    public static MemoryStream BuildMultiSheetWorkbook(
        IReadOnlyDictionary<string, List<Dictionary<string, object?>>> sheets,
        IReadOnlyList<string>? columns = null)
    {
        using var wb = new XLWorkbook();

        foreach (var sheet in sheets)
        {
            var sheetName = sheet.Key.Length > 31 ? sheet.Key[..31] : sheet.Key;
            var ws = wb.Worksheets.Add(sheetName);
            var rows = sheet.Value;

            if (rows.Count == 0)
                continue;

            var headers = columns?.ToList() ?? rows[0].Keys.ToList();

            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (int c = 0; c < headers.Count; c++)
                    SetCellValue(ws.Cell(r + 2, c + 1), row.GetValueOrDefault(headers[c]));

                if (r % 2 == 1)
                    ws.Row(r + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }

            ws.Range(1, 1, 1, headers.Count).SetAutoFilter();
            ws.Columns().AdjustToContents();
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    public static void AppendToWorkbook( string filePath, IReadOnlyDictionary<string, List<Dictionary<string, object?>>> sheets, IReadOnlyList<string>? columns = null,int? retentionDays = null)
    {
        if (sheets.Count == 0)
            return;

        var outputSheets = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(filePath))
        {
            using var existingWb = new XLWorkbook(filePath);
            foreach (var existingWs in existingWb.Worksheets)
            {
                var sheetName = existingWs.Name;
                var headers = columns?.ToList() ?? [];
                var existingRows = ReadWorksheetRows(existingWs, headers);
                if (existingRows.Count > 0)
                    outputSheets[sheetName] = existingRows;
            }
        }

        foreach (var sheet in sheets)
        {
            var sheetName = sheet.Key.Length > 31 ? sheet.Key[..31] : sheet.Key;
            var newRows = sheet.Value;
            if (newRows.Count == 0)
                continue;

            var headers = columns?.ToList() ?? newRows[0].Keys.ToList();
            var dateColumnName = FindDateColumn(headers, newRows);
            var applyRetention = dateColumnName is not null && retentionDays is > 0;
            var cutoff = applyRetention ? DateTime.Now.AddDays(-retentionDays!.Value) : DateTime.MinValue;

            if (!outputSheets.TryGetValue(sheetName, out var keptRows))
                keptRows = outputSheets[sheetName] = [];

            if (applyRetention)
            {
                keptRows = keptRows.Where(row =>
                {
                    if (!row.TryGetValue(dateColumnName!, out var dateValue) || dateValue is null)
                        return false;

                    var timestamp = dateValue is DateTime dt
                        ? dt
                        : DateTime.TryParse(dateValue.ToString(), out var parsed) ? parsed : DateTime.MinValue;

                    return timestamp >= cutoff;
                }).ToList();

                outputSheets[sheetName] = keptRows;
            }

            keptRows.AddRange(newRows);
        }

        using var wb = new XLWorkbook();

        foreach (var sheet in outputSheets)
        {
            if (sheet.Value.Count == 0)
                continue;

            var headers = columns?.ToList() ?? sheet.Value[0].Keys.ToList();
            var ws = wb.Worksheets.Add(sheet.Key);

            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            for (int r = 0; r < sheet.Value.Count; r++)
            {
                var row = sheet.Value[r];
                for (int c = 0; c < headers.Count; c++)
                    SetCellValue(ws.Cell(r + 2, c + 1), row.GetValueOrDefault(headers[c]));

                if (r % 2 == 1)
                    ws.Row(r + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }

            ws.Range(1, 1, 1, headers.Count).SetAutoFilter();
            ws.Columns().AdjustToContents();
        }

        wb.SaveAs(filePath);
    }

    private static List<Dictionary<string, object?>> ReadWorksheetRows(IXLWorksheet ws, IReadOnlyList<string> columns)
    {
        var rows = new List<Dictionary<string, object?>>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            return rows;

        var headerCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= (ws.LastColumnUsed()?.ColumnNumber() ?? columns.Count); c++)
        {
            var header = ws.Cell(1, c).GetString().Trim();
            if (header.Length > 0)
                headerCols[header] = c;
        }

        for (int r = 2; r <= lastRow; r++)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (header, col) in headerCols)
            {
                var cell = ws.Cell(r, col);
                if (cell.IsEmpty())
                    row[header] = null;
                else if (cell.DataType == XLDataType.DateTime)
                    row[header] = cell.GetDateTime();
                else
                    row[header] = cell.GetString();
            }

            if (row.Values.Any(static v => v is not null and not ""))
                rows.Add(row);
        }

        return rows;
    }

    private static string? FindDateColumn(IReadOnlyList<string> headers, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        foreach (var header in headers)
        {
            foreach (var row in rows)
            {
                if (row.TryGetValue(header, out var value) && value is DateTime or DateTimeOffset)
                    return header;
            }
        }

        foreach (var header in headers)
        {
            if (header.Contains("date", StringComparison.OrdinalIgnoreCase)
                || header.Contains("time", StringComparison.OrdinalIgnoreCase))
                return header;
        }

        return null;
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
            case DBNull: cell.Value = Blank.Value; break;
            case bool b: cell.Value = b; break;
            case DateTime dt: cell.Value = dt; break;
            case TimeSpan ts: cell.Value = ts; break;
            case decimal dec: cell.Value = (double)dec; break;
            case double dbl: cell.Value = dbl; break;
            case float flt: cell.Value = (double)flt; break;
            case int i: cell.Value = (double)i; break;
            case long l: cell.Value = (double)l; break;
            case short s: cell.Value = (double)s; break;
            default: cell.Value = value.ToString() ?? ""; break;
        }
    }
}
