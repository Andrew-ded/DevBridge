using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

record ManagedProject(string Id, string Name, string Path, string Arguments, bool AutoStart);
record ProcessLine(long Seq, DateTimeOffset At, string Level, string Text);
sealed class ManagedProcess
{
    public required ManagedProject Project { get; init; }
    public Process? Process;
    public readonly ConcurrentQueue<ProcessLine> Lines = new();
    public long Sequence;
    public string? Url;
    public string? Error;
    public bool Running => Process is { HasExited: false };
    public void Append(string line, string stream = "stdout")
    {
        var level = stream == "stderr" || Regex.IsMatch(line, @"\b(error|fail|exception|critical)\b", RegexOptions.IgnoreCase) ? "error" : Regex.IsMatch(line, @"\b(warn|warning)\b", RegexOptions.IgnoreCase) ? "warning" : "info";
        Lines.Enqueue(new ProcessLine(Interlocked.Increment(ref Sequence), DateTimeOffset.UtcNow, level, line));
        while (Lines.Count > 3000) Lines.TryDequeue(out _);
        var m = Regex.Match(line, @"Now listening on:\s*(https?://\S+)");
        if (m.Success) Url = m.Groups[1].Value;
    }
}
sealed class ProcessManager : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, ManagedProcess> processes = new();
    private readonly string config = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevBridge", "processes.json");
    public ProcessManager()
    {
        try { foreach (var p in JsonSerializer.Deserialize<List<ManagedProject>>(File.ReadAllText(config)) ?? []) processes[p.Id] = new ManagedProcess { Project = p }; } catch { }
    }
    public void StartAuto() { string[] ids; lock (gate) ids = processes.Values.Where(x => x.Project.AutoStart).Select(x => x.Project.Id).ToArray(); foreach (var id in ids) Start(id); }
    public object List() { lock (gate) return processes.Values.Select(p => new { p.Project.Id, p.Project.Name, p.Project.Path, p.Project.Arguments, p.Project.AutoStart, running = p.Running, pid = p.Running ? p.Process?.Id : null, p.Url, p.Error }).ToArray(); }
    public object Logs(string id, long after) { lock (gate) return processes.TryGetValue(id, out var p) ? p.Lines.Where(x => x.Seq > after).Take(1000).ToArray() : []; }
    public bool Save(ManagedProject project, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(project.Id) || string.IsNullOrWhiteSpace(project.Name) || !File.Exists(project.Path) || !(project.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || project.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) { error = "Select an existing .csproj or .exe file"; return false; }
        lock (gate) { if (processes.TryGetValue(project.Id, out var old) && old.Running) { error = "Stop the process before editing"; return false; } processes[project.Id] = new ManagedProcess { Project = project }; Persist(); }
        return true;
    }
    public bool Remove(string id) { lock (gate) { if (!processes.TryGetValue(id, out var p) || p.Running) return false; processes.Remove(id); Persist(); return true; } }
    private void Persist() { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(config)!); File.WriteAllText(config, JsonSerializer.Serialize(processes.Values.Select(x => x.Project))); }
    public (bool ok, string message) Start(string id)
    {
        ManagedProcess p;
        lock (gate) { if (!processes.TryGetValue(id, out p!)) return (false, "Project not found"); if (p.Running) return (false, "Already running"); }
        if (!File.Exists(p.Project.Path)) return (false, "File not found: " + p.Project.Path);
        try
        {
            var csproj = p.Project.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
            var info = new ProcessStartInfo(csproj ? "dotnet" : p.Project.Path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Path.GetDirectoryName(p.Project.Path)! };
            if (csproj) { info.ArgumentList.Add("run"); info.ArgumentList.Add("--project"); info.ArgumentList.Add(p.Project.Path); }
            foreach (var arg in SplitArgs(p.Project.Arguments)) info.ArgumentList.Add(arg);
            var proc = new Process { StartInfo = info, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) p.Append(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) p.Append(e.Data, "stderr"); };
            proc.Exited += (_, _) => { p.Append($"Process exited with code {proc.ExitCode}"); };
            if (!proc.Start()) return (false, "Failed to start");
            lock (gate) { p.Process = proc; p.Error = null; p.Url = null; }
            p.Append($"Started PID {proc.Id}: {info.FileName}"); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            return (true, "Started");
        }
        catch (Exception e) { p.Error = e.Message; p.Append(e.Message, "stderr"); return (false, e.Message); }
    }
    private static IEnumerable<string> SplitArgs(string args) => Regex.Matches(args ?? "", "\"[^\"]*\"|\\S+").Select(m => m.Value.Trim('"'));
    public async Task<(bool ok, string message)> Stop(string id)
    {
        Process? proc; lock (gate) { if (!processes.TryGetValue(id, out var p)) return (false, "Project not found"); proc = p.Process; }
        if (proc == null || proc.HasExited) return (true, "Already stopped");
        try
        {
            // On Windows, closing the process tree is reliable for dotnet run child processes.
            // A console Ctrl+C requires a dedicated console; do not pretend CloseMainWindow does it.
            proc.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await proc.WaitForExitAsync(timeout.Token);
            return (true, "Stopped");
        }
        catch (Exception e) { return (false, e.Message); }
    }
    public async ValueTask DisposeAsync()
    {
        string[] ids; lock (gate) ids = processes.Where(x => x.Value.Running).Select(x => x.Key).ToArray();
        foreach (var id in ids) await Stop(id);
    }
}
