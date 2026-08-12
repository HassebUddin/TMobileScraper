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
