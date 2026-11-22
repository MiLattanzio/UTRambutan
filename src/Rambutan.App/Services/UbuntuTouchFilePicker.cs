using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Rambutan.App.ViewModels;
using Rambutan.Core.Diagnostics;

namespace Rambutan.App.Services;

/// <summary>Uses the system ContentPeerPicker and the chosen native provider.
/// Content Hub grants access only to files explicitly selected by the user.</summary>
internal static class UbuntuTouchFilePicker
{
    internal static async Task<PickedFile?> PickAsync(Window owner, CancellationToken ct)
    {
        var tools = Environment.GetEnvironmentVariable(AppDataLocation.ToolsOverrideVariable)
            ?? Path.Combine(AppContext.BaseDirectory, "tools");
        var executable = Path.Combine(tools, "browser", "rambutan-file-picker");
        if (!File.Exists(executable) || owner.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle)
            throw new IOException("The Ubuntu Touch file chooser is unavailable.");
        var root = AppDataLocation.FromEnvironment(false).DataDir;
        var temporary = Path.Combine(root, "cache", "imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var keep = false;
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var name in new[] { "LD_LIBRARY_PATH", "QT_PLUGIN_PATH", "QML2_IMPORT_PATH" }) start.Environment.Remove(name);
            start.Environment["RAMBUTAN_PICKER_PARENT_XID"] = handle.Handle.ToString(CultureInfo.InvariantCulture);
            start.Environment["RAMBUTAN_PICKER_DIRECTORY"] = temporary;
            using var process = Process.Start(start) ?? throw new IOException("The Ubuntu Touch file chooser could not start.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            var errors = DrainAsync(process.StandardError, deadline.Token);
            AppLog.Write("content-hub", "Native file chooser opened.");
            try
            {
                var line = await ReadMessageAsync(process.StandardOutput, deadline.Token);
                await process.WaitForExitAsync(deadline.Token);
                if (process.ExitCode != 0) throw new IOException("The Ubuntu Touch file chooser could not import the file. Please try again.");
                if (line is null) return null;
                var path = ValidateResult(line, temporary);
                AppLog.Write("content-hub", "Selected file received through Content Hub.");
                keep = true;
                return new PickedFile(path) { Release = () => DeleteTemporary(temporary) };
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        await process.StandardInput.WriteLineAsync("cancel");
                        await process.StandardInput.FlushAsync();
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
                    { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                }
                await deadline.CancelAsync();
                try { await errors; } catch (OperationCanceledException) { }
            }
        }
        finally { if (!keep) DeleteTemporary(temporary); }
    }

    internal static string ValidateResult(string json, string directory)
    {
        using var message = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        var path = message.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) ||
            Path.GetDirectoryName(Path.GetFullPath(path)) != Path.GetFullPath(directory) ||
            !File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new InvalidDataException("The file chooser returned an invalid file.");
        return path;
    }

    private static async Task<string?> ReadMessageAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new System.Text.StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character, ct) != 0)
        {
            if (character[0] == '\n') return text.ToString();
            if (text.Length >= 8192) throw new InvalidDataException("File chooser response is too long.");
            text.Append(character[0]);
        }
        if (text.Length != 0) throw new InvalidDataException("Incomplete file chooser response.");
        return null;
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer, ct) != 0) { } }
        catch (IOException) { }
    }
    private static void DeleteTemporary(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write("content-hub", "Could not remove an import scratch directory."); }
    }
}
