using System.Text;
using System.Xml.Linq;

namespace LogEase.Desktop;

internal static class ExcelExport
{
    /// <summary>Writes an Excel-compatible SpreadsheetML (.xls) file that opens in Excel / LibreOffice.</summary>
    public static void SaveWorklogs(string path, IEnumerable<ParsedRow> rows)
    {
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        XNamespace o = "urn:schemas-microsoft-com:office:office";
        XNamespace x = "urn:schemas-microsoft-com:office:excel";

        static XElement Cell(XNamespace ns, string? value, string type = "String") =>
            new(ns + "Cell",
                new XElement(ns + "Data",
                    new XAttribute(ns + "Type", type),
                    value ?? ""));

        var header = new XElement(ss + "Row",
            Cell(ss, "Date"),
            Cell(ss, "Project"),
            Cell(ss, "Activity"),
            Cell(ss, "Hours"),
            Cell(ss, "Description"),
            Cell(ss, "Trust"));

        var dataRows = rows.Select(r => new XElement(ss + "Row",
            Cell(ss, r.WorkDate),
            Cell(ss, r.ProjectName),
            Cell(ss, r.ActivityName),
            Cell(ss, r.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture), "Number"),
            Cell(ss, r.Description),
            Cell(ss, r.Confidence)));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ss + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", ss),
                new XAttribute(XNamespace.Xmlns + "o", o),
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(ss + "Worksheet",
                    new XAttribute(ss + "Name", "Worklogs"),
                    new XElement(ss + "Table", header, dataRows))));

        var xml = "<?xml version=\"1.0\"?>\r\n"
                  + "<?mso-application progid=\"Excel.Sheet\"?>\r\n"
                  + doc.ToString(SaveOptions.DisableFormatting);
        File.WriteAllText(path, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
