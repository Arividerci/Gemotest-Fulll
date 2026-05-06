using SiMed.Laboratory;
using System;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Laboratory.Gemotest.Options
{
    public enum LabelEncoding { UTF8 = 1, Code866, Windows1251 }
    public enum LabelType { ZPL = 1, EPL, Custom }

    [Serializable]
    public class LocalOptions : BaseOptions
    {
        public bool PrintStikersAtOnce { get; set; }
        public bool PrintBlankAtOnce { get; set; }
        [XmlIgnore] public PaperSource PaperSource { get; set; }
        [XmlIgnore] public PrinterSettings PdfPrinterSettings { get; set; }
        [XmlIgnore] public PrinterSettings StickerPrinterSettings { get; set; }
        public string PdfPrinterName { get; set; } = "";
        public string StickerPrinterName { get; set; } = "";
        public int PaperSourceRawKind { get; set; } = 0;
        public string PaperSourceName { get; set; } = "";

        public LabelEncoding LabelEncoding { get; set; } = LabelEncoding.Code866;
        public LabelType LabelType { get; set; } = LabelType.EPL;
        public string CustomLabelTemplate { get; set; } = "";

        public static string GetDefaultLabelTemplate(LabelType _LabelType)
        {
            switch (_LabelType)
            {
                case LabelType.ZPL:
                    return
@"^XA^CWZ,E:TT0003M_.FNT
^PR2
~TA000
^LS0
^LT0
^LH0,0
^PW335
^LL200
^MD10
^FS^XZ
^XA
^CI28
^FO10,15^AZN,25,20^FD{1}^FS
^FO10,35^AZN,25,20^FD{2}^FS
^FO190,15^AZN,25,20^FDMSC^FS
^FO25,60^BY2^B2N,60,N,N,N^FD{0}^FS
^FO10,130^AZN,25,20^FD{0}^FS
^FO135,130^AZN,25,20^FD{3}^FS
^FO10,150^AZN,25,20^FD{4}^FS
^FO10,170^AZN,25,20^FD{5}^FS
^FO135,170^AZN,25,20^FD{6}^FS
^XZ";
                case LabelType.EPL:
                    return
@"N
S2
D5
ZB
I8,10
q335
Q200,24
A10,0,0,3,1,1,N,""{1}""
A10,20,0,3,1,1,N,""{2}""
A190,0,0,3,1,1,N,""MSC""
B35,40,0,2,2,5,60,N,""{0}""
A10,110,0,3,1,1,N,""{0}""
A170,110,0,3,1,1,N,""{3}""
A10,130,0,3,1,1,N,""{4}""
A10,150,0,3,1,1,N,""{5}""
A120,150,0,3,1,1,N,""{6}""
P1,1
";
                default:
                    return "";
            }
        }

        private void NormalizeForSave()
        {
            PdfPrinterName = PdfPrinterSettings?.PrinterName ?? PdfPrinterName ?? "";
            StickerPrinterName = StickerPrinterSettings?.PrinterName ?? StickerPrinterName ?? "";

            if (PaperSource != null)
            {
                PaperSourceRawKind = PaperSource.RawKind;
                PaperSourceName = PaperSource.SourceName ?? "";
            }
        }

        private void FixupRuntimeObjects()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(PdfPrinterName))
                    PdfPrinterSettings = new PrinterSettings { PrinterName = PdfPrinterName };

                if (!string.IsNullOrWhiteSpace(StickerPrinterName))
                    StickerPrinterSettings = new PrinterSettings { PrinterName = StickerPrinterName };

                PrinterSettings ps = (PdfPrinterSettings != null && PdfPrinterSettings.IsValid)
                    ? PdfPrinterSettings
                    : (StickerPrinterSettings != null && StickerPrinterSettings.IsValid ? StickerPrinterSettings : null);

                if (ps != null)
                {
                    if (PaperSourceRawKind != 0 || !string.IsNullOrWhiteSpace(PaperSourceName))
                    {
                        foreach (PaperSource src in ps.PaperSources)
                        {
                            if (PaperSourceRawKind != 0 && src.RawKind == PaperSourceRawKind) { PaperSource = src; break; }
                            if (!string.IsNullOrWhiteSpace(PaperSourceName) &&
                                string.Equals(src.SourceName, PaperSourceName, StringComparison.OrdinalIgnoreCase))
                            { PaperSource = src; break; }
                        }
                    }

                    if (PaperSource == null)
                        PaperSource = ps.DefaultPageSettings.PaperSource;

                    try { ps.DefaultPageSettings.PaperSource = PaperSource; } catch { }
                }
            }
            catch { }
        }

        public override string Pack()
        {
            NormalizeForSave();
            using (var ms = new MemoryStream())
            {
                new XmlSerializer(typeof(LocalOptions)).Serialize(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public override BaseOptions Unpack(string _Source)
        {
            try
            {
                string source = (_Source ?? string.Empty).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(source))
                    return new LocalOptions();

                using (var sr = new StringReader(source))
                {
                    var obj = (LocalOptions)new XmlSerializer(typeof(LocalOptions)).Deserialize(sr);
                    obj?.FixupRuntimeObjects();
                    return obj ?? new LocalOptions();
                }
            }
            catch
            {
                return new LocalOptions();
            }
        }

        public void SaveToFile(string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            File.WriteAllText(filePath, Pack(), Encoding.UTF8);
        }

        public static LocalOptions LoadFromFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                string xml = File.ReadAllText(filePath, Encoding.UTF8);
                return (LocalOptions)new LocalOptions().Unpack(xml);
            }
            return new LocalOptions();
        }
    }
}
