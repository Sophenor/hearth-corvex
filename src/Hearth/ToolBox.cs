using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Hearth;

public sealed partial class ToolBox(bool computerAccess, string? folder, Func<string, string, bool> confirm, Action<string> activity, CancellationToken cancellationToken)
{
    private readonly Dictionary<int, long> observed = [];
    public IList<AITool> Tools()
    {
        List<AITool> tools = [];
        tools.Add(AIFunctionFactory.Create(WebSearch, "web_search", "Search the internet for current information and source links. The user approves the query before it is sent to Exa. Never include private file contents or credentials."));
        if (computerAccess)
        {
            tools.Add(AIFunctionFactory.Create(SystemSummary, "computer_health", "Read current Windows CPU, memory, disk space and top memory-using apps. Does not change anything."));
            tools.Add(AIFunctionFactory.Create(CloseApp, "request_close_app", "Ask the user to approve closing a normal app from the most recent computer_health report. Never force-kills; preserves unsaved-work dialogs."));
        }
        if (folder != null)
        {
            tools.Add(AIFunctionFactory.Create(ListFolder, "list_folder", "List up to 150 entries inside the user-selected folder. Paths are relative. Does not follow junctions or symlinks."));
            tools.Add(AIFunctionFactory.Create(ReadText, "read_text", "Read a text file in the selected folder, up to 100 KB. No images, PDFs, Office files, secrets or binaries."));
            tools.Add(AIFunctionFactory.Create(SaveText, "save_text", "Save a .txt, .md or .csv file within the selected folder only after the user approves the exact path and contents. Can create a draft or report, not executable code."));
            tools.Add(AIFunctionFactory.Create(ReadDocument, "read_document", "Extract text from a PDF or Word .docx in the chosen folder. Page/character limits apply. Does not OCR images or preserve layout."));
            tools.Add(AIFunctionFactory.Create(EditText, "edit_text", "Replace one exact unique text passage in a small .txt, .md or .csv file. Shows the complete resulting file for approval and backs up the original."));
            tools.Add(AIFunctionFactory.Create(CreateFolder, "create_folder", "Create one named subfolder inside an existing folder after approval."));
            tools.Add(AIFunctionFactory.Create(TransferFile, "transfer_file", "Copy or move/rename one ordinary file inside the chosen folder after approval. Never overwrites a destination or moves a folder."));
        }
        return tools;
    }
    public static string ResolveScopedPath(string root, string relative, bool mustExist = true)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('\\', '/').Any(p => p == "..")) throw new IOException("Use a relative path inside the chosen folder.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Outside the chosen folder.");
        for (var check = path; check != null; check = Path.GetDirectoryName(check))
        {
            if (File.Exists(check) || Directory.Exists(check))
                if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders and files are not accessible.");
            if (check.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        if (mustExist && !File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("File or folder not found.");
        if (path.Split(Path.DirectorySeparatorChar).Any(p => p.Equals(".ssh", StringComparison.OrdinalIgnoreCase) || p.Equals(".aws", StringComparison.OrdinalIgnoreCase) || p.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || p.Equals("connection.bin", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Credential files are not accessible.");
        return path;
    }
    public string ListFolder([Description("Relative subfolder, or . for the selected folder")] string relativePath = ".")
    {
        cancellationToken.ThrowIfCancellationRequested(); activity("Listing the selected folder");
        var path = ResolveScopedPath(folder!, relativePath);
        var entries = Directory.EnumerateFileSystemEntries(path).Take(151).ToArray();
        return JsonSerializer.Serialize(new { entries = entries.Take(150).Select(p => new { name = Path.GetFileName(p), isFolder = Directory.Exists(p), linked = (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0 }), truncated = entries.Length > 150 });
    }
    public string ReadText(string relativePath)
    {
        cancellationToken.ThrowIfCancellationRequested(); var path = ResolveScopedPath(folder!, relativePath);
        var allowed = new[] { ".txt", ".md", ".csv", ".json", ".log", ".cs", ".py", ".js", ".html", ".css", ".xml", ".yaml", ".yml" };
        if (!allowed.Contains(Path.GetExtension(path).ToLowerInvariant()) || new FileInfo(path).Length > 100000) return "Only text files up to 100 KB can be read. Select a smaller text export.";
        activity("Reading " + Path.GetFileName(path)); return File.ReadAllText(path);
    }
    public string SaveText(string relativePath, string content)
    {
        cancellationToken.ThrowIfCancellationRequested(); var path = ResolveScopedPath(folder!, relativePath, false);
        if (!new[] { ".txt", ".md", ".csv" }.Contains(Path.GetExtension(path).ToLowerInvariant()) || content.Length > 100000) return "Only .txt, .md and .csv drafts up to 100,000 characters are supported.";
        if (!Directory.Exists(Path.GetDirectoryName(path))) return "The parent folder does not exist. Choose an existing folder.";
        if (Path.GetFileName(path).TrimEnd(' ', '.').Length != Path.GetFileName(path).Length) return "Trailing dots or spaces are not supported.";
        activity("Waiting for approval to save " + Path.GetFileName(path));
        if (!confirm((File.Exists(path) ? "Replace file?" : "Save file?"), path + "\n\n" + content)) return "User declined; nothing was written.";
        cancellationToken.ThrowIfCancellationRequested(); ResolveScopedPath(folder!, relativePath, false);
        var unique = Guid.NewGuid().ToString("N");
        var temp = path + "." + unique + ".new";
        using (var output = new StreamWriter(new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None), new System.Text.UTF8Encoding(false))) output.Write(content);
        ResolveScopedPath(folder!, relativePath, false);
        var backup = path + "." + unique + ".bak";
        if (File.Exists(path)) File.Replace(temp, path, backup, true); else File.Move(temp, path);
        activity("Saved " + Path.GetFileName(path)); return "Saved successfully: " + relativePath + ". If replaced, the previous version was retained beside it with a unique .bak name.";
    }
    public string SystemSummary()
    {
        cancellationToken.ThrowIfCancellationRequested(); activity("Checking CPU, memory, disk space and open apps");
        var result = new Dictionary<string, object?>();
        try
        {
            using var search = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory,LastBootUpTime,Caption FROM Win32_OperatingSystem");
            using var objects = search.Get();
            foreach (ManagementObject os in objects) using (os) { result["memoryTotalMB"] = Convert.ToInt64(os["TotalVisibleMemorySize"]) / 1024; result["memoryFreeMB"] = Convert.ToInt64(os["FreePhysicalMemory"]) / 1024; result["os"] = os["Caption"]; }
            using var cpu = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor"); using var cpus = cpu.Get();
            result["cpuLoadPercentSample"] = cpus.Cast<ManagementObject>().Select(p => Convert.ToInt32(p["LoadPercentage"])).ToArray();
        }
        catch { result["note"] = "Windows performance details are unavailable; no elevated access was requested."; }
        result["disks"] = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select(d => new { drive = d.Name, freeGB = Math.Round(d.AvailableFreeSpace / 1e9, 1), totalGB = Math.Round(d.TotalSize / 1e9, 1) }).ToArray();
        var rows = new List<(int Id, string App, double MemoryMB)>(); observed.Clear();
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try
            {
                if (process.SessionId != Process.GetCurrentProcess().SessionId || process.MainWindowHandle == IntPtr.Zero || process.Id == Environment.ProcessId) continue;
                observed[process.Id] = process.StartTime.ToUniversalTime().Ticks;
                rows.Add((process.Id, process.ProcessName, Math.Round(process.WorkingSet64 / 1048576.0)));
            }
            catch { }
        }
        result["openApps"] = rows.OrderByDescending(r => r.MemoryMB).Take(60).Select(r => new { id = r.Id, app = r.App, memoryMB = r.MemoryMB }); result["uptimeHours"] = Math.Round(Environment.TickCount64 / 3600000.0, 1);
        return JsonSerializer.Serialize(result);
    }
    public string CloseApp(int processId)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!observed.TryGetValue(processId, out var start)) return "Run computer_health first and select an app from that report.";
        using var process = Process.GetProcessById(processId);
        if (process.StartTime.ToUniversalTime().Ticks != start || process.MainWindowHandle == IntPtr.Zero || process.SessionId != Process.GetCurrentProcess().SessionId || process.Id == Environment.ProcessId) return "The app changed; refresh the report.";
        var name = process.ProcessName;
        if (new[] { "explorer", "dwm", "winlogon", "taskmgr", "securityhealthsystray" }.Contains(name.ToLowerInvariant())) return "This Windows component cannot be closed by Hearth.";
        activity("Waiting for approval to close " + name);
        if (!confirm("Close " + name + "?", "Hearth would like to ask this app to close. Save your work first.\n\nApp: " + name + "\nProcess ID: " + processId + "\n\nThis will not force-quit the app. Windows may ask you to save unsaved work.")) return "User declined. The app was not changed.";
        cancellationToken.ThrowIfCancellationRequested();
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != start) return "The app already exited or changed.";
        var requested = process.CloseMainWindow(); activity("Sent a normal close request to " + name);
        return requested ? "Close request sent. This is not proof the app exited; it may be waiting for the user to save work." : "The app did not accept the close request. No force-quit attempted.";
    }
}
