using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace Hearth;

public sealed partial class ToolBox
{
    string ScopedFile(string relative)
    {
        if (folder == null) throw new IOException("Choose a folder first.");
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveScopedPath(folder, relative);
        if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw new IOException("Choose an ordinary file.");
        return path;
    }
    public string ReadDocument(string relativePath, int startPage = 1, int pageCount = 10)
    {
        var path = ScopedFile(relativePath);
        if (new FileInfo(path).Length > 20_000_000) return "Choose a document smaller than 20 MB.";
        activity("Reading document " + Path.GetFileName(path));
        const int limit = 50000;
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (startPage < 1 || pageCount < 1 || pageCount > 20) return "Use a positive startPage and pageCount between 1 and 20.";
            using var pdf = PdfDocument.Open(path);
            if (startPage > pdf.NumberOfPages) return "The requested page is beyond this PDF.";
            var text = new StringBuilder(); var last = Math.Min(pdf.NumberOfPages, startPage + pageCount - 1);
            for (var page = startPage; page <= last && text.Length < limit; page++) { cancellationToken.ThrowIfCancellationRequested(); text.AppendLine($"\n[Page {page}]"); text.AppendLine(pdf.GetPage(page).Text); }
            var body = text.ToString();
            return $"PDF text extraction; {pdf.NumberOfPages} pages total. Requested pages {startPage}-{last}. Layout/tables may be imperfect; scanned pages need OCR, which is not included.\n" + body[..Math.Min(body.Length, limit)] + (body.Length > limit ? "\n[Text truncated at 50,000 characters.]" : "");
        }
        if (!Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)) return "Use a PDF or Word .docx file. Legacy .doc files are not supported.";
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml") ?? throw new IOException("This is not a readable Word document.");
        if (entry.Length > 5_000_000) return "This Word document's text is too large; use a smaller export.";
        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5_000_000 });
        var xml = XDocument.Load(reader); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var bodyText = string.Join("\n", xml.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))));
        return "Word body text only; formatting, images, comments, headers and footnotes are not included.\n" + bodyText[..Math.Min(bodyText.Length, limit)] + (bodyText.Length > limit ? "\n[Text truncated at 50,000 characters.]" : "");
    }
    public string EditText(string relativePath, string oldText, string newText)
    {
        var path = ScopedFile(relativePath);
        if (oldText.Length == 0 || oldText.Length > 100000 || newText.Length > 100000 || new FileInfo(path).Length > 100000) return "Use a nonempty exact passage in a text file no larger than 100 KB.";
        if (!new[] { ".txt", ".md", ".csv" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return "Only .txt, .md and .csv files can be edited here.";
        var original = File.ReadAllText(path, new UTF8Encoding(false, true));
        var at = original.IndexOf(oldText, StringComparison.Ordinal);
        if (at < 0 || original.IndexOf(oldText, at + oldText.Length, StringComparison.Ordinal) >= 0) return "The passage must match exactly once. Read the current file and provide a unique passage.";
        var result = original[..at] + newText + original[(at + oldText.Length)..];
        if (Encoding.UTF8.GetByteCount(result) > 100000) return "The resulting text exceeds 100 KB.";
        activity("Waiting for approval to edit " + Path.GetFileName(path));
        if (!confirm("Edit this file?", path + "\n\nComplete proposed contents:\n\n" + result)) return "User declined; nothing was changed.";
        cancellationToken.ThrowIfCancellationRequested(); ScopedFile(relativePath);
        if (File.ReadAllText(path, new UTF8Encoding(false, true)) != original) return "The file changed during review. Read it again; nothing was written.";
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".new";
        File.WriteAllText(temp, result, new UTF8Encoding(false));
        File.Replace(temp, path, path + "." + Guid.NewGuid().ToString("N") + ".bak", true);
        activity("Edited " + Path.GetFileName(path)); return "Edited the exact passage. The previous file was preserved beside it as a unique .bak file.";
    }
    public string CreateFolder(string relativePath)
    {
        if (folder == null) throw new IOException("Choose a folder first.");
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveScopedPath(folder, relativePath, false);
        if (File.Exists(path) || Directory.Exists(path)) return "That destination already exists; nothing changed.";
        if (!Directory.Exists(Path.GetDirectoryName(path))) return "Create the parent folder first.";
        if (!confirm("Create folder?", path)) return "User declined; nothing changed.";
        cancellationToken.ThrowIfCancellationRequested(); ResolveScopedPath(folder, relativePath, false);
        Directory.CreateDirectory(path); activity("Created folder " + Path.GetFileName(path)); return "Created " + relativePath;
    }
    public string TransferFile(string sourcePath, string destinationPath, bool copy = true)
    {
        var source = ScopedFile(sourcePath); var destination = ResolveScopedPath(folder!, destinationPath, false);
        if (new FileInfo(source).Length > 500_000_000) return "This tool handles individual files up to 500 MB.";
        if (File.Exists(destination) || Directory.Exists(destination)) return "The destination exists. No file was overwritten.";
        if (!Directory.Exists(Path.GetDirectoryName(destination))) return "Create the destination folder first.";
        if (!Path.GetExtension(source).Equals(Path.GetExtension(destination), StringComparison.OrdinalIgnoreCase)) return "Keep the same extension when copying or renaming a file.";
        var action = copy ? "Copy" : "Move / rename";
        if (!confirm(action + " file?", "From: " + source + "\n\nTo: " + destination + (copy ? "\n\nThe original will remain." : "\n\nThe file will leave its current location. References to the old path may stop working."))) return "User declined; nothing changed.";
        cancellationToken.ThrowIfCancellationRequested(); ScopedFile(sourcePath); ResolveScopedPath(folder!, destinationPath, false);
        if (copy) File.Copy(source, destination, false); else File.Move(source, destination, false);
        activity(action + " completed: " + Path.GetFileName(destination)); return action + " completed. Destination: " + destinationPath;
    }
}
