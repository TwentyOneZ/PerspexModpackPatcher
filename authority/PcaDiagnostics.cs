using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PerspexCharacterAuthority;

internal enum PcaDiagnosticLevel { Off, Basic, Verbose, Trace }

internal sealed class PcaDiagnostics : IDisposable
{
    private readonly object sync = new object();
    private readonly ManualLogSource logger;
    private readonly bool writeFile;
    private readonly bool mirrorToGameLog;
    private readonly string directory;
    private readonly List<string> buffered = new List<string>();
    private StreamWriter writer;
    private bool fileFailed;

    internal PcaDiagnostics(ManualLogSource logger, string level, bool legacyDebug, bool writeFile, bool mirrorToGameLog, string directory, float pendingInterval)
    {
        this.logger = logger;
        this.writeFile = writeFile;
        this.mirrorToGameLog = mirrorToGameLog;
        this.directory = directory;
        Level = Parse(level);
        if (legacyDebug && Level < PcaDiagnosticLevel.Verbose) Level = PcaDiagnosticLevel.Verbose;
        PendingInterval = Math.Max(0.25f, Math.Min(60f, pendingInterval));
    }

    internal PcaDiagnosticLevel Level { get; }
    internal float PendingInterval { get; }
    internal bool Enabled => Level != PcaDiagnosticLevel.Off;

    internal void DetectRole(string role, bool dedicated)
    {
        if (!Enabled) return;
        lock (sync)
        {
            EnsureWriter(role);
            WriteLine(Format(PcaDiagnosticLevel.Basic, role, "-", "ROLE_DETECTED", "dedicated=" + dedicated));
        }
    }

    internal void Log(PcaDiagnosticLevel required, string role, string connection, string eventName, string details = "")
    {
        if (Level < required) return;
        var line = Format(required, role, connection ?? "-", eventName, details);
        lock (sync)
        {
            if (writeFile && !fileFailed && writer == null) buffered.Add(line);
            else WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (writeFile && !fileFailed && writer == null && buffered.Count > 0) EnsureWriter("UNKNOWN");
            writer?.Dispose();
            writer = null;
        }
    }

    private void EnsureWriter(string role)
    {
        if (!writeFile || fileFailed || writer != null) return;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "PCA_" + role + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + ".log");
            writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            fileFailed = true;
            logger.LogWarning("[PCA] Could not create diagnostic file: " + ex.Message);
        }
        foreach (var line in buffered) WriteLine(line);
        buffered.Clear();
    }

    private void WriteLine(string line)
    {
        try { writer?.WriteLine(line); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ObjectDisposedException)
        {
            fileFailed = true;
            writer?.Dispose();
            writer = null;
            logger.LogWarning("[PCA] Diagnostic file write failed: " + ex.Message);
        }
        logger.LogInfo(line);
        if (mirrorToGameLog) ZLog.Log(line);
    }

    private static string Format(PcaDiagnosticLevel level, string role, string connection, string eventName, string details) =>
        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " [PCA][" + level.ToString().ToUpperInvariant() + "][" + role + "][" + connection + "][" + eventName + "]" +
        (string.IsNullOrWhiteSpace(details) ? string.Empty : " " + details);

    private static PcaDiagnosticLevel Parse(string value) =>
        Enum.TryParse(value, true, out PcaDiagnosticLevel parsed) ? parsed : PcaDiagnosticLevel.Off;
}
