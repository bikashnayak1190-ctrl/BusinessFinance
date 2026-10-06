using System.Data;
using System.IO.Compression;
using System.Xml;
namespace Finance.Core;
public static class ExcelExport
{
    // Genuine XLSX; inline strings prevent spreadsheet formula injection.
    public static void Write(string path,string title,DataTable display)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        void Text(string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(text);}
        Text("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        Text("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Text("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Text("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        using var xml=XmlWriter.Create(zip.CreateEntry("xl/worksheets/sheet1.xml").Open(),new XmlWriterSettings{CloseOutput=true});const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";xml.WriteStartElement("worksheet",ns);xml.WriteStartElement("sheetData",ns);
        void Row(IEnumerable<string> cells,bool numeric=false){xml.WriteStartElement("row",ns);int col=0;foreach(var cell in cells){xml.WriteStartElement("c",ns);if(numeric&&display.Columns[col].ColumnName.EndsWith("(INR)")&&decimal.TryParse(cell,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.GetCultureInfo("en-IN"),out var amount)){xml.WriteElementString("v",ns,amount.ToString(System.Globalization.CultureInfo.InvariantCulture));}else{xml.WriteAttributeString("t","inlineStr");xml.WriteStartElement("is",ns);xml.WriteElementString("t",ns,cell);xml.WriteEndElement();}xml.WriteEndElement();col++;}xml.WriteEndElement();}
        Row([title]);Row(display.Columns.Cast<DataColumn>().Select(c=>c.ColumnName));foreach(DataRow row in display.Rows)Row(row.ItemArray.Select(Value.Text),true);xml.WriteEndElement();xml.WriteEndElement();
    }
}
