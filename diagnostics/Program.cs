using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MiralayIFSTanilama;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1200 };
    private readonly Button start = new() { Text = "KAYDI BAŞLAT", Width = 150, Height = 42 };
    private readonly Button stop = new() { Text = "KAYDI DURDUR", Width = 150, Height = 42, Enabled = false };
    private readonly Label state = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox logPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly HashSet<int> seenProcesses = new();
    private readonly HashSet<string> seenConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> relevantNames = new(StringComparer.OrdinalIgnoreCase)
        { "VeriTakip", "MiralayIFSTanilama", "Ifs.Fnd.Explorer", "Ifs.Fnd.AccessProvider", "dfsvc", "powershell", "pwsh" };
    private StreamWriter? writer;
    private string? sessionDir;
    private DateTime started;
    private int samples;

    public MainForm()
    {
        Text = "Miralay IFS Tanılama";
        Width = 680; Height = 330; MinimumSize = new Size(620, 300);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 27, 43); ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), RowCount = 6, ColumnCount = 1, BackColor = BackColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        var heading = new Label { Text = "IFS BAĞLANTI VE İŞLEM KAYDI", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = Color.FromArgb(125, 155, 255), TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(heading, 0, 0);
        var info = new Label { Text = "Kaydı başlat → IFS'yi normal şekilde aç → Veri Takip'ten bir kez veri çek → Kaydı durdur. Yalnızca IFS ile ilişkili işlem ve bağlantı bilgileri tutulur.", Dock = DockStyle.Fill, ForeColor = Color.Gainsboro };
        root.Controls.Add(info, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        StyleButton(start, Color.FromArgb(69, 104, 205)); StyleButton(stop, Color.FromArgb(160, 65, 65));
        buttons.Controls.Add(start); buttons.Controls.Add(stop); root.Controls.Add(buttons, 0, 2);
        root.Controls.Add(state, 0, 3);
        root.Controls.Add(new Label { Text = "Tanılama klasörü:", Dock = DockStyle.Fill, ForeColor = Color.Silver, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        root.Controls.Add(logPath, 0, 5);
        start.Click += (_, _) => StartCapture();
        stop.Click += (_, _) => StopCapture();
        timer.Tick += (_, _) => Sample();
        FormClosing += (_, _) => { if (writer != null) StopCapture(); };
        state.Text = "Hazır. Kayıt başlatıldığında IFS işlemleri izlenecek.";
    }

    private static void StyleButton(Button b, Color color)
    {
        b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
        b.BackColor = color; b.ForeColor = Color.White; b.Font = new Font("Segoe UI", 10, FontStyle.Bold);
    }

    private void StartCapture()
    {
        try
        {
            started = DateTime.Now; samples = 0;
            seenProcesses.Clear(); seenConnections.Clear();
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            sessionDir = Path.Combine(desktop, "Miralay-IFS-Tanilama-" + started.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(sessionDir);
            writer = new StreamWriter(Path.Combine(sessionDir, "pc-izleme.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            Write("capture_started", new { machine = Environment.MachineName, user = Environment.UserName, target = "ifs10app2.teknorot.com:48080", interval_ms = timer.Interval });
            try
            {
                foreach (var ip in Dns.GetHostAddresses("ifs10app2.teknorot.com"))
                    Write("target_dns", new { host = "ifs10app2.teknorot.com", address = ip.ToString() });
            }
            catch (Exception ex) { Write("target_dns_error", new { error = ex.Message }); }
            logPath.Text = sessionDir;
            start.Enabled = false; stop.Enabled = true;
            state.Text = "KAYIT AKTİF — IFS'yi açıp Veri Takip'ten bir kez veri çekebilirsin.";
            timer.Start();
            Sample();
        }
        catch (Exception ex) { MessageBox.Show("Kayıt başlatılamadı: " + ex.Message, "Miralay IFS Tanılama", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Sample()
    {
        if (writer == null) return;
        samples++;
        var active = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (!IsRelevantName(name)) continue;
                active.Add(p.Id);
                if (seenProcesses.Add(p.Id))
                {
                    string? startTime = null;
                    try { startTime = p.StartTime.ToString("O"); } catch { }
                    Write("process_seen", new { pid = p.Id, process = name, started = startTime });
                }
            }
            catch { }
            finally { p.Dispose(); }
        }

        foreach (var stale in seenProcesses.Where(pid => !active.Contains(pid)).ToArray())
        {
            Write("process_gone", new { pid = stale });
            seenProcesses.Remove(stale);
        }

        try
        {
            var psi = new ProcessStartInfo("netstat.exe", "-ano -p tcp")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var netstat = Process.Start(psi);
            if (netstat != null)
            {
                var output = netstat.StandardOutput.ReadToEnd();
                netstat.WaitForExit(2500);
                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                    var remote = parts[2]; var stateName = parts[3];
                    if (!int.TryParse(parts[4], out var pid)) continue;
                    var remotePort = ParsePort(remote);
                    string processName = "";
                    try { using var p = Process.GetProcessById(pid); processName = p.ProcessName; } catch { }
                    if (remotePort != 48080 && !IsRelevantName(processName)) continue;
                    var key = $"{pid}|{parts[1]}|{remote}|{stateName}";
                    if (seenConnections.Add(key))
                        Write("tcp_connection", new { pid, process = processName, local = parts[1], remote, remote_port = remotePort, state = stateName });
                }
            }
        }
        catch (Exception ex) { Write("netstat_error", new { error = ex.Message }); }

        if (samples % 5 == 0)
            state.Text = $"KAYIT AKTİF — {DateTime.Now - started:mm\\:ss} | İzlenen işlem: {seenProcesses.Count} | Yeni bağlantı: {seenConnections.Count}";
    }

    private bool IsRelevantName(string name) =>
        relevantNames.Contains(name) || name.Contains("Ifs", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VeriTakip", StringComparison.OrdinalIgnoreCase);

    private static int ParsePort(string endpoint)
    {
        var ix = endpoint.LastIndexOf(':');
        return ix >= 0 && int.TryParse(endpoint[(ix + 1)..], out var port) ? port : -1;
    }

    private void Write(string type, object data)
    {
        if (writer == null) return;
        writer.WriteLine(JsonSerializer.Serialize(new { time = DateTimeOffset.Now, type, data }));
    }

    private void StopCapture()
    {
        if (writer == null) return;
        timer.Stop();
        Write("capture_stopped", new { duration_seconds = (DateTime.Now - started).TotalSeconds, samples });
        writer.Dispose(); writer = null;
        try
        {
            var bridgeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiralayIFS", "Diagnostics");
            if (Directory.Exists(bridgeDir))
            {
                foreach (var f in Directory.GetFiles(bridgeDir))
                    File.Copy(f, Path.Combine(sessionDir!, "bridge-" + Path.GetFileName(f)), true);
            }
            var zip = sessionDir + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(sessionDir!, zip, CompressionLevel.Optimal, false);
            state.Text = "Kayıt tamamlandı. ZIP dosyası masaüstünde hazır.";
            logPath.Text = zip;
            MessageBox.Show("Tanılama tamamlandı. Bu ZIP dosyasını bana gönder:\n\n" + zip + "\n\nParola veya tuş kaydı alınmadı.", "Kayıt hazır", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { state.Text = "Kayıt durdu ancak ZIP oluşturulamadı: " + ex.Message; }
        start.Enabled = true; stop.Enabled = false;
    }
}
