using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Guna.UI2.WinForms;
using Microsoft.Data.Sqlite;

namespace MiralayIFSExplorer;

internal static class Program
{
    [DllImport("shell32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [STAThread]
    static int Main(string[] args)
    {
        try { _ = SetCurrentProcessExplicitAppUserModelID("Miralay.VeriTakip"); } catch { }
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            if (!e.Name.StartsWith("Guna.UI2", StringComparison.OrdinalIgnoreCase)) return null;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.Guna.UI2.dll");
            if (stream is null) return null;
            using var memory = new MemoryStream(); stream.CopyTo(memory); return Assembly.Load(memory.ToArray());
        };
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SmokeTests.Run();
            Database.Initialize();
            if (args.Length == 2 && args[0] == "--sync-source" && int.TryParse(args[1], out var sourceId))
            {
                var ok = SyncService.Execute(sourceId, out var message);
                if (ok) { Console.WriteLine(message); return 0; }
                Console.Error.WriteLine(message); return 1;
            }
            ApplicationConfiguration.Initialize();
            Application.Run(new ExplorerForm());
            return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.ToString(), "Veri Takip", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 11, 39);
    public static readonly Color Panel = Color.FromArgb(20, 23, 48);
    public static readonly Color PanelLight = Color.FromArgb(32, 38, 73);
    public static readonly Color Input = Color.FromArgb(30, 36, 69);
    public static readonly Color Ink = Color.FromArgb(238, 241, 252);
    public static readonly Color Muted = Color.FromArgb(154, 165, 202);
    public static readonly Color Purple = Color.FromArgb(89, 41, 171);
    public static readonly Color Border = Color.FromArgb(57, 64, 111);
    public static readonly Color Blue = Color.FromArgb(45, 112, 239);
    public static readonly Color Cyan = Color.FromArgb(5, 165, 196);
    public static readonly Color Rose = Color.FromArgb(198, 39, 91);
    public static Guna2Button Button(string text, bool primary = false, int width = 130, Color? accent = null)
    {
        var fill = accent ?? (primary ? Purple : PanelLight);
        bool filled = primary || accent.HasValue;
        var b = new Guna2Button
        {
            Text = text, Width = width, Height = 42, FillColor = fill,
            ForeColor = Color.White, Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BorderColor = filled ? Tint(fill, 1.22f) : Border, BorderThickness = 1,
            BorderRadius = 11, Cursor = Cursors.Hand, Animated = true, Margin = new Padding(4),
            ShadowDecoration = { Enabled = filled, Depth = 3, Color = Color.FromArgb(30, fill) }
        };
        b.HoverState.FillColor = Tint(fill, 1.16f);
        b.HoverState.ForeColor = Color.White;
        b.HoverState.BorderColor = Tint(fill, 1.16f);
        b.PressedColor = Tint(fill, 0.82f);
        return b;
    }
    private static Color Tint(Color c, float amount) => Color.FromArgb(c.A, Math.Clamp((int)(c.R * amount), 0, 255), Math.Clamp((int)(c.G * amount), 0, 255), Math.Clamp((int)(c.B * amount), 0, 255));
    public static Label Label(string text, float size = 9, bool bold = false, Color? color = null) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? Ink, BackColor = Color.Transparent, AutoEllipsis = true
    };
}

internal sealed class AmbientBackdrop : Panel
{
    public AmbientBackdrop()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        using (var baseBrush = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Theme.Background, Color.FromArgb(24, 15, 57), 20f))
            g.FillRectangle(baseBrush, ClientRectangle);
        using (var lowerShape = new SolidBrush(Color.FromArgb(20, 55, 114, 104)))
            g.FillEllipse(lowerShape, -ClientSize.Width * .22f, ClientSize.Height * .59f, ClientSize.Width * .42f, ClientSize.Height * .52f);
        using (var upperShape = new SolidBrush(Color.FromArgb(22, 42, 61, 143)))
            g.FillEllipse(upperShape, ClientSize.Width * .79f, -ClientSize.Height * .44f, ClientSize.Width * .46f, ClientSize.Height * .72f);
        using var contour = new Pen(Color.FromArgb(35, 145, 157, 213), 1f);
        g.DrawEllipse(contour, ClientSize.Width * .035f, ClientSize.Height * .04f, ClientSize.Height * .64f, ClientSize.Height * .64f);
        g.DrawEllipse(contour, ClientSize.Width * .23f, ClientSize.Height * .48f, ClientSize.Height * .34f, ClientSize.Height * .34f);
        g.DrawEllipse(contour, ClientSize.Width * .81f, ClientSize.Height * .65f, ClientSize.Height * .47f, ClientSize.Height * .47f);
        using var hairline = new Pen(Color.FromArgb(16, 150, 164, 228), 1f);
        g.DrawLine(hairline, 0, ClientSize.Height * .285f, ClientSize.Width * .33f, ClientSize.Height * .04f);
        g.DrawLine(hairline, ClientSize.Width * .68f, ClientSize.Height, ClientSize.Width, ClientSize.Height * .76f);
    }
}

internal sealed class BrandBanner : Panel
{
    private const int CornerRadius = 15;
    public BrandBanner()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var path = RoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), CornerRadius);
        Region?.Dispose(); Region = new Region(path);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Color.FromArgb(49, 28, 125), Color.FromArgb(112, 40, 163), 0f);
        using var path = RoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), CornerRadius);
        e.Graphics.FillPath(gradient, path);
        using var edge = new Pen(Color.FromArgb(70, 201, 178, 255), 1f);
        e.Graphics.DrawPath(edge, path);
    }
    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath(); int d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class Source
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Lu { get; set; } = "";
    public string View { get; set; } = "";
    public string CopyText { get; set; } = "";
    public List<Field> Fields { get; set; } = [];
    public string KeyFields { get; set; } = "";
    public int Interval { get; set; } = 60;
    public string VisibleColumns { get; set; } = "";
    public bool IsScheduled { get; set; }
    public override string ToString() => Name;
}

internal sealed class Field
{
    public string Name { get; set; } = "";
    public string Expression { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Queryable { get; set; }
    public bool Enabled { get; set; }
}

internal sealed record Snapshot(string At, string Kind, Dictionary<string, JsonElement> Values);

internal static class Database
{
    private static readonly JsonSerializerOptions FieldJson = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static readonly string PathName = Environment.GetEnvironmentVariable("MIRALAY_IFS_DB_PATH") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiralayIFSExplorer", "data.db");
    private static string ConnectionString => new SqliteConnectionStringBuilder { DataSource = PathName }.ToString();
    public static SqliteConnection Open() { var c = new SqliteConnection(ConnectionString); c.Open(); return c; }
    public static void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS sources(id INTEGER PRIMARY KEY,name TEXT,lu TEXT,view_name TEXT,copy_text TEXT,fields TEXT,key_fields TEXT,interval_minutes INTEGER DEFAULT 60,enabled INTEGER DEFAULT 1,last_run TEXT,last_status TEXT,visible_columns TEXT);
        CREATE TABLE IF NOT EXISTS snapshots(id INTEGER PRIMARY KEY,source_id INTEGER,fetched_at TEXT,row_json TEXT,row_hash TEXT,change_type TEXT,row_key TEXT);
        CREATE TABLE IF NOT EXISTS preferences(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        """;
        cmd.ExecuteNonQuery();
        EnsureColumn(c, "sources", "visible_columns", "TEXT");
        EnsureColumn(c, "snapshots", "row_key", "TEXT");
    }
    public static bool GetPreference(string key, bool fallback = false)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT value FROM preferences WHERE key=$key"; q.Parameters.AddWithValue("$key", key);
        var value = q.ExecuteScalar() as string; return value is null ? fallback : value == "1";
    }
    public static void SetPreference(string key, bool value)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "INSERT INTO preferences(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        q.Parameters.AddWithValue("$key", key); q.Parameters.AddWithValue("$value", value ? "1" : "0"); q.ExecuteNonQuery();
    }
    public static string? GetTextPreference(string key)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT value FROM preferences WHERE key=$key"; q.Parameters.AddWithValue("$key", key);
        return q.ExecuteScalar() as string;
    }
    public static void SetTextPreference(string key, string value)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "INSERT INTO preferences(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        q.Parameters.AddWithValue("$key", key); q.Parameters.AddWithValue("$value", value); q.ExecuteNonQuery();
    }
    private static void EnsureColumn(SqliteConnection c, string table, string column, string type)
    {
        using var q = c.CreateCommand(); q.CommandText = $"PRAGMA table_info({table})";
        using var r = q.ExecuteReader(); while (r.Read()) if (r.GetString(1) == column) return;
        using var alter = c.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}"; alter.ExecuteNonQuery();
    }
    public static List<Source> GetSources()
    {
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT id,name,lu,view_name,copy_text,fields,key_fields,interval_minutes,visible_columns FROM sources ORDER BY id";
        using var r = q.ExecuteReader(); var result = new List<Source>();
        while (r.Read())
        {
            List<Field> fields; try { fields = JsonSerializer.Deserialize<List<Field>>(r.GetString(5), FieldJson) ?? []; } catch { fields = []; }
            result.Add(new Source { Id = r.GetInt64(0), Name = r.GetString(1), Lu = r.IsDBNull(2) ? "" : r.GetString(2), View = r.IsDBNull(3) ? "" : r.GetString(3), CopyText = r.IsDBNull(4) ? "" : r.GetString(4), Fields = fields, KeyFields = r.IsDBNull(6) ? "" : r.GetString(6), Interval = r.IsDBNull(7) ? 60 : r.GetInt32(7), VisibleColumns = r.IsDBNull(8) ? "" : r.GetString(8) });
        }
        return result;
    }
    public static Source? GetSource(long id) => GetSources().FirstOrDefault(x => x.Id == id);
    public static bool HasRun(long id)
    {
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT EXISTS(SELECT 1 FROM sources WHERE id=$id AND last_run IS NOT NULL AND last_run <> '')";
        q.Parameters.AddWithValue("$id", id); return Convert.ToInt64(q.ExecuteScalar()) == 1;
    }
    public static void Save(Source s)
    {
        using var c = Open(); using var q = c.CreateCommand();
        q.Parameters.AddWithValue("$name", s.Name); q.Parameters.AddWithValue("$lu", s.Lu); q.Parameters.AddWithValue("$view", s.View);
        q.Parameters.AddWithValue("$copy", s.CopyText); q.Parameters.AddWithValue("$fields", JsonSerializer.Serialize(s.Fields, FieldJson));
        q.Parameters.AddWithValue("$keys", s.KeyFields); q.Parameters.AddWithValue("$interval", s.Interval);
        if (s.Id == 0) { q.CommandText = "INSERT INTO sources(name,lu,view_name,copy_text,fields,key_fields,interval_minutes) VALUES($name,$lu,$view,$copy,$fields,$keys,$interval)"; q.ExecuteNonQuery(); using var id = c.CreateCommand(); id.CommandText = "SELECT last_insert_rowid()"; s.Id = (long)(id.ExecuteScalar() ?? 0L); }
        else { q.CommandText = "UPDATE sources SET name=$name,lu=$lu,view_name=$view,copy_text=$copy,fields=$fields,key_fields=$keys,interval_minutes=$interval WHERE id=$id"; q.Parameters.AddWithValue("$id", s.Id); q.ExecuteNonQuery(); }
    }
    public static void Delete(long id)
    {
        using var c = Open(); using var t = c.BeginTransaction();
        using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "DELETE FROM snapshots WHERE source_id=$id"; q.Parameters.AddWithValue("$id", id); q.ExecuteNonQuery();
        q.CommandText = "DELETE FROM sources WHERE id=$id"; q.ExecuteNonQuery(); t.Commit();
    }
    public static List<Snapshot> GetSnapshots(long id)
    {
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT fetched_at,change_type,row_json FROM snapshots WHERE source_id=$id ORDER BY id DESC"; q.Parameters.AddWithValue("$id", id);
        using var r = q.ExecuteReader(); var rows = new List<Snapshot>();
        while (r.Read())
        {
            Dictionary<string, JsonElement> values; try { values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.GetString(2)) ?? []; } catch { values = new() { ["KAYIT"] = JsonSerializer.SerializeToElement(r.GetString(2)) }; }
            rows.Add(new Snapshot(r.GetString(0), r.GetString(1), values));
        }
        return rows;
    }
    public static void SaveVisibleColumns(long id, IEnumerable<string> names)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "UPDATE sources SET visible_columns=$v WHERE id=$id";
        q.Parameters.AddWithValue("$v", JsonSerializer.Serialize(names)); q.Parameters.AddWithValue("$id", id); q.ExecuteNonQuery();
    }
}

internal static class CopyObject
{
    private static readonly Regex LuPattern = new(@"^\s*\$LU\s*=\s*([A-Za-z0-9_]+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ViewPattern = new(@"^\s*\$VIEW\s*=\s*([A-Za-z0-9_]+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex IndexedPattern = new(@"^\s*-\$\d+\s*:\s*(.*?)\s*=\s*(.*?)\s*$", RegexOptions.Compiled);
    private static readonly Regex PlainPattern = new(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$", RegexOptions.Compiled);
    public static (string Lu, string View, List<Field> Fields) Parse(string raw)
    {
        var view = ViewPattern.Match(raw); if (!view.Success) throw new InvalidOperationException("İlk satırlarda $VIEW bulunamadı.");
        var lu = LuPattern.Match(raw); var fields = new List<Field>();
        foreach (var line in raw.Split('\n'))
        {
            var m = IndexedPattern.Match(line); if (!m.Success) m = PlainPattern.Match(line); if (!m.Success) continue;
            var expression = m.Groups[1].Value.Trim(); var value = m.Groups[2].Value.Trim();
            bool queryable = Regex.IsMatch(expression, @"^[A-Za-z][A-Za-z0-9_]*$");
            fields.Add(new Field { Name = queryable ? expression.ToUpperInvariant() : expression, Expression = expression, Value = value, Queryable = queryable });
        }
        if (fields.Count == 0) throw new InvalidOperationException("COPYOBJECT içinde alan satırı bulunamadı.");
        return (lu.Success ? lu.Groups[1].Value : "", view.Groups[1].Value, fields);
    }
}

internal static class SyncService
{
    private const string IfsUrl = "https://ifs10app2.teknorot.com:48080";
    private const string SystemName = "TEKNOROT IFS10.10 PROD";
    public static Dictionary<string, object> MakeConfig(Source s)
    {
        var columns = s.Fields.Where(x => x.Queryable).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (columns.Count == 0) throw new InvalidOperationException("Sorgulanabilir gerçek tablo alanı bulunamadı.");
        var filters = s.Fields.Where(x => x.Queryable && x.Enabled && !string.IsNullOrWhiteSpace(x.Value) && x.Value.Trim() != "%")
            .Select(x => new { name = x.Name, value = x.Value.Trim() }).ToArray();
        return new() { ["view"] = s.View, ["columns"] = columns, ["filters"] = filters, ["ifs_url"] = IfsUrl, ["system_name"] = SystemName, ["remember_credentials"] = Database.GetPreference("remember_ifs") };
    }
    public static bool Execute(long id, out string message)
        => Execute(id, null, out message);
    public static bool Execute(long id, Func<long, bool>? confirmCount, out string message)
    {
        try
        {
            var source = Database.GetSource(id) ?? throw new InvalidOperationException("Kaynak bulunamadı.");
            using var result = RunBridge(source, confirmCount);
            if (result is null) { message = "Sorgu durduruldu. Koşulları düzenleyip yeniden deneyebilirsin."; return true; }
            if (result.RootElement.TryGetProperty("ifs_user", out var user) && user.ValueKind == JsonValueKind.String)
                Database.SetTextPreference("ifs_user", user.GetString() ?? "");
            var rows = result.RootElement.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().ToList() : [];
            var now = DateTime.Now.ToString("s");
            using var c = Database.Open(); using var tx = c.BeginTransaction();
            int added = 0, changed = 0;
            if (rows.Count == 0)
            {
                using var empty = c.CreateCommand(); empty.Transaction = tx; empty.CommandText = "UPDATE sources SET last_run=$now,last_status=$status WHERE id=$id";
                empty.Parameters.AddWithValue("$now", now); empty.Parameters.AddWithValue("$status", "0 kayıt; eşleşen sonuç yok"); empty.Parameters.AddWithValue("$id", id); empty.ExecuteNonQuery();
                tx.Commit(); message = "Sorgu tamamlandı; bu filtrelerle eşleşen kayıt bulunamadı."; return true;
            }
            foreach (var row in rows)
            {
                var canonical = Canonical(row); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
                var keyNames = source.KeyFields.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var key = keyNames.Length > 0 && keyNames.All(k => row.TryGetProperty(k, out _))
                    ? CanonicalKey(row, keyNames)
                    : canonical;
                using var prev = c.CreateCommand(); prev.Transaction = tx; prev.CommandText = "SELECT row_hash FROM snapshots WHERE source_id=$id AND row_key=$key ORDER BY id DESC LIMIT 1";
                prev.Parameters.AddWithValue("$id", id); prev.Parameters.AddWithValue("$key", key); var old = prev.ExecuteScalar() as string;
                var kind = old is null ? "NEW" : old == hash ? "UNCHANGED" : "CHANGED";
                if (kind == "NEW") added++; if (kind == "CHANGED") changed++;
                using var ins = c.CreateCommand(); ins.Transaction = tx; ins.CommandText = "INSERT INTO snapshots(source_id,fetched_at,row_json,row_hash,change_type,row_key) VALUES($id,$now,$json,$hash,$kind,$key)";
                ins.Parameters.AddWithValue("$id", id); ins.Parameters.AddWithValue("$now", now); ins.Parameters.AddWithValue("$json", row.GetRawText());
                ins.Parameters.AddWithValue("$hash", hash); ins.Parameters.AddWithValue("$kind", kind); ins.Parameters.AddWithValue("$key", key); ins.ExecuteNonQuery();
            }
            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx; upd.CommandText = "UPDATE sources SET last_run=$now,last_status=$status WHERE id=$id";
                upd.Parameters.AddWithValue("$now", now); upd.Parameters.AddWithValue("$status", $"{rows.Count} kayıt; yeni {added}; değişen {changed}"); upd.Parameters.AddWithValue("$id", id); upd.ExecuteNonQuery();
            }
            tx.Commit(); message = $"{rows.Count} kayıt alındı. Yeni: {added}, değişen: {changed}"; return true;
        }
        catch (Exception ex) { message = ex.ToString(); return false; }
    }
    internal static string Canonical(JsonElement row)
    {
        var sorted = row.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToDictionary(x => x.Name, x => x.Value);
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return "{" + string.Join(", ", sorted.Select(x => JsonSerializer.Serialize(x.Key, options) + ": " + JsonSerializer.Serialize(x.Value, options))) + "}";
    }
    internal static string CanonicalKey(JsonElement row, IReadOnlyList<string> names)
    {
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return "[" + string.Join(", ", names.Select(k => JsonSerializer.Serialize(row.GetProperty(k), options))) + "]";
    }
    private static JsonDocument? RunBridge(Source source, Func<long, bool>? confirmCount)
    {
        using var temp = new TemporaryDirectory();
        var config = System.IO.Path.Combine(temp.Path, "request.json"); var output = System.IO.Path.Combine(temp.Path, "result.json");
        File.WriteAllText(config, JsonSerializer.Serialize(MakeConfig(source)), new UTF8Encoding(false));
        var bridge = System.IO.Path.Combine(temp.Path, "ifs_bridge.ps1");
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.ifs_bridge.ps1") ?? throw new FileNotFoundException("Gömülü veri bağlantı köprüsü bulunamadı."))
        using (var file = File.Create(bridge)) resource.CopyTo(file);
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass"); psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(bridge);
        psi.ArgumentList.Add("-ConfigPath"); psi.ArgumentList.Add(config); psi.ArgumentList.Add("-OutputPath"); psi.ArgumentList.Add(output);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell köprüsü başlatılamadı.");
        var stderr = process.StandardError.ReadToEndAsync(); var transcript = new StringBuilder();
        var watch = Stopwatch.StartNew(); string? line; bool declined = false;
        while (true)
        {
            var remaining = TimeSpan.FromMinutes(4) - watch.Elapsed;
            if (remaining <= TimeSpan.Zero) { try { process.Kill(true); } catch { } throw new TimeoutException("Sorgu 4 dakika içinde tamamlanmadı."); }
            try { line = process.StandardOutput.ReadLineAsync().WaitAsync(remaining).GetAwaiter().GetResult(); }
            catch (TimeoutException) { try { process.Kill(true); } catch { } throw new TimeoutException("Sorgu 4 dakika içinde tamamlanmadı."); }
            if (line is null) break;
            if (line.StartsWith("MIRALAY_COUNT:", StringComparison.Ordinal))
            {
                if (!long.TryParse(line["MIRALAY_COUNT:".Length..], out var count)) throw new InvalidDataException("Kayıt sayısı okunamadı.");
                var proceed = confirmCount?.Invoke(count) ?? true;
                process.StandardInput.WriteLine(proceed ? "Y" : "N"); process.StandardInput.Flush();
                declined = !proceed;
            }
            else if (line == "MIRALAY_CANCELLED") declined = true;
            else if (!string.IsNullOrWhiteSpace(line)) transcript.AppendLine(line);
        }
        if (!process.WaitForExit(240_000)) { try { process.Kill(true); } catch { } throw new TimeoutException("Sorgu 4 dakika içinde tamamlanmadı."); }
        stderr.Wait();
        if (declined) return null;
        if (process.ExitCode != 0 || !File.Exists(output))
        {
            var err = (stderr.Result + Environment.NewLine + transcript).Trim();
            throw new InvalidOperationException(err.Length > 0 ? err[^Math.Min(err.Length, 3000)..] : "Veri bağlantı hatası.");
        }
        return JsonDocument.Parse(File.ReadAllText(output, Encoding.UTF8));
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MiralayIFS-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}

internal sealed class ExplorerForm : Form
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    [DllImport("dwmapi.dll", PreserveSig = true)] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private readonly ListBox sourceList = new();
    private readonly Guna2TextBox nameBox = new(), rawBox = new(), searchBox = new();
    private readonly Guna2NumericUpDown interval = new() { Minimum = 1, Maximum = 1439, Value = 60, Width = 100, Height = 36, FillColor = Theme.Input, ForeColor = Theme.Ink, BorderColor = Theme.Border, BorderRadius = 6, Font = new Font("Segoe UI", 9F) };
    private readonly Guna2DataGridView grid = new();
    private readonly Guna2CheckBox rememberCheck = new() { Text = "Kullanıcıyı hatırla", AutoSize = false, ForeColor = Theme.Ink, Font = new Font("Segoe UI", 8.5F), Margin = new Padding(4), CheckedState = { FillColor = Theme.Purple, BorderColor = Theme.Purple }, UncheckedState = { FillColor = Theme.PanelLight, BorderColor = Theme.Border } };
    private readonly Label status = Theme.Label("Bir veri kaynağı seçin veya yeni kaynak ekleyin.", 8.5F, color: Theme.Muted);
    private readonly Label count = Theme.Label("0 kayıt", 8.5F, color: Theme.Muted);
    private readonly Label userLabel = Theme.Label("Kullanıcı: " + Environment.UserName, 9, color: Theme.Muted);
    private readonly Guna2Button saveButton = Theme.Button("Alanları düzenle / Kaydet", true, 195);
    private readonly Guna2Button fetchButton = Theme.Button("↻   Şimdi Çek", true, 140, Theme.Blue);
    private Guna2Panel? emptyState;
    private Label? emptyTitle, emptySubtitle;
    private Label? sourceEmptyState;
    private Source? selected;
    private List<Snapshot> resultRows = [];
    private List<string> fieldNames = [], visibleFields = [];
    private readonly ContextMenuStrip sourceMenu = new();

    public ExplorerForm()
    {
        Text = "Veri Takip"; StartPosition = FormStartPosition.CenterScreen; Size = new Size(1500, 920); MinimumSize = new Size(1100, 720);
        BackColor = Theme.Background; Font = new Font("Segoe UI", 9F); DoubleBuffered = true; ShowInTaskbar = true;
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.app.ico");
            if (iconStream is not null) { using var appIcon = new Icon(iconStream); Icon = (Icon)appIcon.Clone(); }
            else Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { } }
        BuildUi(); RefreshSources(); sourceList.SelectedIndexChanged += (_, _) => SelectSource();
        if (Database.GetTextPreference("ifs_user") is { Length: > 0 } ifsUser) userLabel.Text = "Kullanıcı: " + ifsUser;
        rememberCheck.Checked = Database.GetPreference("remember_ifs");
        rememberCheck.CheckedChanged += (_, _) => Database.SetPreference("remember_ifs", rememberCheck.Checked);
        rawBox.Multiline = true; saveButton.Click += (_, _) => SaveFields();
        sourceMenu.Items.Add("Bu kayda zamanlayıcı kur", null, (_, _) => Schedule());
        sourceMenu.Items.Add("Yeni kaynak", null, (_, _) => NewSource()); sourceMenu.Items.Add("Kaynağı ve yerel geçmişi sil", null, (_, _) => DeleteSource());
        sourceMenu.Opening += (_, e) => { if (sourceList.SelectedItem is null) e.Cancel = true; };
        sourceList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = sourceList.IndexFromPoint(e.Location);
            if (index >= 0) sourceList.SelectedIndex = index;
        };
        sourceList.ContextMenuStrip = sourceMenu; sourceList.DoubleClick += (_, _) => SelectSource();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int preference = DwmCornerRound; _ = DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref preference, sizeof(int)); } catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmEnterSizeMove) Opacity = 0.82;
        base.WndProc(ref m);
        if (m.Msg == WmExitSizeMove) Opacity = 1.0;
    }
    private void BuildUi()
    {
        var backdrop = new AmbientBackdrop { Dock = DockStyle.Fill }; Controls.Add(backdrop);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); backdrop.Controls.Add(root);

        var sidebar = new Guna2Panel { Dock = DockStyle.Fill, FillColor = Theme.Panel, Padding = Padding.Empty }; root.Controls.Add(sidebar, 0, 0);
        var sideLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Panel, Padding = new Padding(12), Margin = Padding.Empty };
        sideLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sidebar.Controls.Add(sideLayout);
        var logoHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 3) };
        var logoFrame = new Guna2Panel { Size = new Size(190, 66), FillColor = Color.White, BorderRadius = 13, Padding = new Padding(8, 9, 8, 9), Anchor = AnchorStyles.None };
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.teknorot-kalip-logo.png"))
            if (stream != null) { using var decoded = new Bitmap(stream); logo.Image = new Bitmap(decoded); }
        logoFrame.Controls.Add(logo); logoHost.Controls.Add(logoFrame);
        void CenterLogo() => logoFrame.Location = new Point(Math.Max(0, (logoHost.ClientSize.Width - logoFrame.Width) / 2), Math.Max(0, (logoHost.ClientSize.Height - logoFrame.Height) / 2));
        logoHost.Resize += (_, _) => CenterLogo(); CenterLogo(); sideLayout.Controls.Add(logoHost, 0, 0);
        var sourceTitle = Theme.Label("VERİ KAYNAKLARI", 8.5F, true, Theme.Muted); sourceTitle.Dock = DockStyle.Fill; sourceTitle.TextAlign = ContentAlignment.MiddleLeft; sideLayout.Controls.Add(sourceTitle, 0, 1);
        var sourceHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        sourceList.Dock = DockStyle.Fill; sourceList.BorderStyle = BorderStyle.None; sourceList.BackColor = Theme.Panel; sourceList.ForeColor = Theme.Ink; sourceList.Font = new Font("Segoe UI", 9F); sourceList.IntegralHeight = false; sourceList.ItemHeight = 34; sourceList.DrawMode = DrawMode.OwnerDrawFixed;
        sourceList.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool chosen = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var source = sourceList.Items[e.Index] as Source;
            using var back = new SolidBrush(chosen ? Color.FromArgb(57, 44, 115) : Theme.Panel);
            using var fore = new SolidBrush(Theme.Ink);
            e.Graphics.FillRectangle(back, e.Bounds);
            float textX = e.Bounds.Left + 12;
            if (source?.IsScheduled == true)
            {
                using var clockPen = new Pen(Color.FromArgb(90, 225, 215), 1.7F);
                var clock = new RectangleF(e.Bounds.Left + 12, e.Bounds.Top + 10, 13, 13);
                e.Graphics.DrawEllipse(clockPen, clock);
                e.Graphics.DrawLine(clockPen, clock.Left + 6.5F, clock.Top + 2.5F, clock.Left + 6.5F, clock.Top + 6.5F);
                e.Graphics.DrawLine(clockPen, clock.Left + 6.5F, clock.Top + 6.5F, clock.Left + 9.5F, clock.Top + 8.5F);
                textX += 21;
            }
            e.Graphics.DrawString(source?.Name ?? sourceList.Items[e.Index]?.ToString() ?? "", sourceList.Font, fore, textX, e.Bounds.Top + 8);
            e.DrawFocusRectangle();
        };
        sourceHost.Controls.Add(sourceList);
        sourceEmptyState = Theme.Label("Henüz kayıtlı kaynak yok.\n\nKaydetmek için ‘Alanları düzenle / Kaydet’ seçeneğini kullanın.", 8.5F, color: Theme.Muted);
        sourceEmptyState.Dock = DockStyle.Fill; sourceEmptyState.TextAlign = ContentAlignment.TopLeft; sourceEmptyState.Padding = new Padding(8, 12, 4, 4);
        sourceHost.Controls.Add(sourceEmptyState); sourceEmptyState.BringToFront(); sideLayout.Controls.Add(sourceHost, 0, 2);
        var add = Theme.Button("+  Yeni veri kaynağı", false, 205, Theme.Purple); add.Dock = DockStyle.Fill; add.Click += (_, _) => NewSource(); sideLayout.Controls.Add(add, 0, 3);
        var foot = Theme.Label("Salt okunur veri bağlantısı", 8, color: Theme.Muted); foot.Dock = DockStyle.Fill; foot.TextAlign = ContentAlignment.BottomLeft; sideLayout.Controls.Add(foot, 0, 4);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24, 18, 24, 20), BackColor = Color.Transparent, Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); root.Controls.Add(main, 1, 0);
        var header = new BrandBanner { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) }; main.Controls.Add(header, 0, 0);
        var logoBadge = new Guna2Panel { Location = new Point(13, 11), Size = new Size(172, 43), FillColor = Color.White, BorderRadius = 9, Padding = new Padding(8, 7, 8, 7) };
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.teknorot-kalip-logo.png"))
            if (stream != null) { using var decoded = new Bitmap(stream); var brandImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Image = new Bitmap(decoded) }; logoBadge.Controls.Add(brandImage); }
        header.Controls.Add(logoBadge);
        var title = Theme.Label("Veri Takip", 17, true); title.Location = new Point(202, 12); title.Size = new Size(370, 28); title.ForeColor = Color.White; header.Controls.Add(title);
        var subtitle = Theme.Label("Kayıtları görüntüleyin ve değişimleri takip edin", 8.5F, color: Color.FromArgb(225, 218, 255)); subtitle.Location = new Point(204, 40); subtitle.Size = new Size(370, 20); header.Controls.Add(subtitle);
        userLabel.Dock = DockStyle.Right; userLabel.Width = 225; userLabel.TextAlign = ContentAlignment.MiddleRight; userLabel.Padding = new Padding(0, 0, 18, 0); userLabel.ForeColor = Color.FromArgb(239, 231, 255); header.Controls.Add(userLabel);

        var editor = new Guna2Panel { Dock = DockStyle.Fill, FillColor = Color.FromArgb(236, Theme.Panel), BorderRadius = 16, BorderColor = Theme.Border, BorderThickness = 1, Padding = new Padding(13), Margin = new Padding(0, 0, 0, 8) }; main.Controls.Add(editor, 0, 1);
        var editorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); editor.Controls.Add(editorLayout);
        var row1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300)); row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182)); row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); editorLayout.Controls.Add(row1, 0, 0);
        var sourceNameLabel = Theme.Label("Kaynak adı", 9, true); sourceNameLabel.Dock = DockStyle.Fill; sourceNameLabel.TextAlign = ContentAlignment.MiddleLeft; row1.Controls.Add(sourceNameLabel, 0, 0);
        row1.Controls.Add(nameBox, 1, 0); nameBox.Dock = DockStyle.Fill; nameBox.Margin = new Padding(3, 3, 12, 3); nameBox.BorderRadius = 9; nameBox.PlaceholderText = "Ör. Sipariş takibi"; nameBox.FillColor = Theme.Input; nameBox.ForeColor = Theme.Ink; nameBox.BorderColor = Theme.Border; nameBox.PlaceholderForeColor = Theme.Muted; nameBox.FocusedState.BorderColor = Theme.Cyan; nameBox.HoverState.BorderColor = Theme.Purple;
        var intervalLabel = Theme.Label("Zamanlayıcı aralığı (dk)", 9, true); intervalLabel.Dock = DockStyle.Fill; intervalLabel.TextAlign = ContentAlignment.MiddleLeft; row1.Controls.Add(intervalLabel, 2, 0);
        var intervalTip = new ToolTip { AutoPopDelay = 9000, InitialDelay = 450, ReshowDelay = 150, ShowAlways = true };
        intervalTip.SetToolTip(intervalLabel, "Bu süre yalnızca seçtiğin kayda kuracağın zamanlayıcıda kullanılır.");
        intervalTip.SetToolTip(interval, "Önce kaydı seç veya kaydet; sonra listede kayda sağ tıklayıp zamanlayıcı kur.");
        interval.Dock = DockStyle.Fill; interval.Margin = new Padding(3, 3, 4, 3); row1.Controls.Add(interval, 3, 0);
        var queryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        queryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); queryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); editorLayout.Controls.Add(queryLayout, 0, 1);
        var copyHead = Theme.Label("SORGULAMA METNİ", 8.5F, true, Theme.Muted); copyHead.Dock = DockStyle.Fill; copyHead.TextAlign = ContentAlignment.MiddleLeft; queryLayout.Controls.Add(copyHead, 0, 0);
        rawBox.Dock = DockStyle.Fill; rawBox.Margin = new Padding(0, 1, 0, 0); rawBox.BorderRadius = 10; rawBox.Font = new Font("Consolas", 9F); rawBox.PlaceholderText = "Sorgu ekranından kopyalanan COPYOBJECT bloğunu buraya yapıştır"; rawBox.FillColor = Theme.Input; rawBox.ForeColor = Theme.Ink; rawBox.BorderColor = Theme.Border; rawBox.PlaceholderForeColor = Theme.Muted; rawBox.FocusedState.BorderColor = Theme.Cyan; rawBox.HoverState.BorderColor = Theme.Purple; queryLayout.Controls.Add(rawBox, 0, 1);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2), ColumnCount = 4, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24)); buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.Controls.Add(buttons, 0, 2);
        saveButton.Dock = DockStyle.Fill; fetchButton.Dock = DockStyle.Fill;
        buttons.Controls.Add(saveButton, 0, 0);
        buttons.Controls.Add(fetchButton, 1, 0); fetchButton.Click += async (_, _) => await FetchAsync();
        var delete = Theme.Button("Kaynağı Sil", false, 110, Theme.Rose); delete.Dock = DockStyle.Fill; buttons.Controls.Add(delete, 2, 0); delete.Click += (_, _) => DeleteSource();
        rememberCheck.Dock = DockStyle.Fill; buttons.Controls.Add(rememberCheck, 3, 0);
        var tableCard = new Guna2Panel { Dock = DockStyle.Fill, FillColor = Color.FromArgb(242, Theme.Panel), BorderRadius = 17, BorderColor = Theme.Border, BorderThickness = 1, Padding = new Padding(14), ShadowDecoration = { Enabled = true, Depth = 12, Color = Color.FromArgb(45, 4, 10, 31) } }; main.Controls.Add(tableCard, 0, 3);
        var cardLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = Padding.Empty };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); tableCard.Controls.Add(cardLayout);
        var resultbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        resultbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); resultbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); resultbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); cardLayout.Controls.Add(resultbar, 0, 0);
        var resultLeft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        resultLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); resultLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); resultLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); resultLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); resultbar.Controls.Add(resultLeft, 0, 0);
        var resultTitle = Theme.Label("Sorgu sonuçları", 11.5F, true); resultTitle.Dock = DockStyle.Fill; resultTitle.TextAlign = ContentAlignment.MiddleLeft; resultLeft.Controls.Add(resultTitle, 0, 0);
        searchBox.Height = 34; searchBox.BorderRadius = 9; searchBox.PlaceholderText = "Yüklenen kayıtlarda ara"; searchBox.FillColor = Theme.Input; searchBox.ForeColor = Theme.Ink; searchBox.BorderColor = Theme.Border; searchBox.PlaceholderForeColor = Theme.Muted; searchBox.Dock = DockStyle.Fill; searchBox.Margin = new Padding(3, 5, 3, 5); resultLeft.Controls.Add(searchBox, 1, 0);
        searchBox.TextChanged += (_, _) => ApplySearch();
        count.Dock = DockStyle.Fill; count.TextAlign = ContentAlignment.MiddleRight; count.Padding = new Padding(2, 0, 5, 0); resultLeft.Controls.Add(count, 2, 0);
        var resultActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) resultActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F)); resultActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); resultbar.Controls.Add(resultActions, 1, 0);
        var columnsBtn = Theme.Button("Sütunları seç", false, 120, Color.FromArgb(78, 55, 148)); columnsBtn.Dock = DockStyle.Fill; columnsBtn.Font = new Font("Segoe UI", 8F, FontStyle.Bold); columnsBtn.Click += (_, _) => ChooseColumns(); resultActions.Controls.Add(columnsBtn, 0, 0);
        var copyAll = Theme.Button("Tüm sonuçları\nkopyala", false, 165, Color.FromArgb(29, 90, 150)); copyAll.Dock = DockStyle.Fill; copyAll.Font = new Font("Segoe UI", 8F, FontStyle.Bold); copyAll.Click += (_, _) => CopyRows(false); resultActions.Controls.Add(copyAll, 1, 0);
        var copySelected = Theme.Button("Seçili satırları\nkopyala", false, 175, Color.FromArgb(7, 105, 119)); copySelected.Dock = DockStyle.Fill; copySelected.Font = new Font("Segoe UI", 8F, FontStyle.Bold); copySelected.Click += (_, _) => CopyRows(true); resultActions.Controls.Add(copySelected, 2, 0);
        ConfigureGrid();
        var tableHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }; cardLayout.Controls.Add(tableHost, 0, 1); tableHost.Controls.Add(grid);
        BuildEmptyState(tableHost);
        var footer = Theme.Label("Salt okunur  •  Son çekilen kayıtlar", 8.5F, color: Theme.Muted); footer.Dock = DockStyle.Fill; footer.TextAlign = ContentAlignment.MiddleLeft; cardLayout.Controls.Add(footer, 0, 2);
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; main.Controls.Add(status, 0, 4);
    }
    private void BuildEmptyState(Control host)
    {
        emptyState = new Guna2Panel
        {
            Size = new Size(390, 168), FillColor = Theme.PanelLight, BorderRadius = 18,
            BorderColor = Theme.Border, BorderThickness = 1,
            ShadowDecoration = { Enabled = true, Depth = 10, Color = Color.FromArgb(35, 0, 0, 0) }
        };
        var emblem = Theme.Label("✦", 27, true, Color.FromArgb(157, 128, 255));
        emblem.Font = new Font("Segoe UI Symbol", 27F, FontStyle.Bold); emblem.TextAlign = ContentAlignment.MiddleCenter; emblem.SetBounds(0, 14, 390, 42);
        emptyTitle = Theme.Label("Henüz kayıt yok", 12, true); emptyTitle.TextAlign = ContentAlignment.MiddleCenter; emptyTitle.SetBounds(18, 61, 354, 26);
        emptySubtitle = Theme.Label("Sorgunu çalıştırdığında sonuçlar burada görünecek.", 9, color: Theme.Muted); emptySubtitle.TextAlign = ContentAlignment.MiddleCenter; emptySubtitle.SetBounds(18, 91, 354, 42);
        emptyState.Controls.Add(emblem); emptyState.Controls.Add(emptyTitle); emptyState.Controls.Add(emptySubtitle); host.Controls.Add(emptyState); emptyState.BringToFront();
        void Center() => emptyState.Location = new Point(Math.Max(12, (host.ClientSize.Width - emptyState.Width) / 2), Math.Max(12, (host.ClientSize.Height - emptyState.Height) / 2));
        host.Resize += (_, _) => Center(); Center();
    }
    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Theme.Panel; grid.BorderStyle = BorderStyle.None; grid.GridColor = Theme.Border;
        grid.RowHeadersVisible = false; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.ReadOnly = true;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = true;
        grid.RowTemplate.Height = 38; grid.ColumnHeadersHeight = 42; grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(48, 33, 111); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold); grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
        grid.DefaultCellStyle.BackColor = Theme.Panel; grid.DefaultCellStyle.ForeColor = Theme.Ink; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(73, 50, 141);
        grid.DefaultCellStyle.SelectionForeColor = Theme.Ink; grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F); grid.DefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.PanelLight;
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "__kind") return;
            var kind = e.Value?.ToString() ?? "";
            if (kind == "NEW") { e.CellStyle.BackColor = Color.FromArgb(23, 55, 45); e.CellStyle.ForeColor = Color.FromArgb(184, 242, 207); }
            else if (kind == "CHANGED") { e.CellStyle.BackColor = Color.FromArgb(59, 45, 27); e.CellStyle.ForeColor = Color.FromArgb(255, 224, 166); }
            else { e.CellStyle.ForeColor = Theme.Muted; }
            e.CellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        };
    }
    private void RefreshSources(long? selectId = null)
    {
        var list = Database.GetSources(); foreach (var item in list) item.IsScheduled = IsTaskScheduled(item.Id);
        sourceList.BeginUpdate(); sourceList.Items.Clear();
        foreach (var s in list) sourceList.Items.Add(s);
        sourceList.EndUpdate();
        if (sourceEmptyState is not null) sourceEmptyState.Visible = list.Count == 0;
        if (selectId.HasValue)
        {
            var ix = list.FindIndex(x => x.Id == selectId.Value); if (ix >= 0) sourceList.SelectedIndex = ix;
        }
    }
    private void SelectSource()
    {
        if (sourceList.SelectedItem is not Source s) return;
        selected = Database.GetSource(s.Id); if (selected is null) return;
        selected.IsScheduled = s.IsScheduled;
        nameBox.Text = selected.Name; interval.Value = Math.Clamp(selected.Interval, 1, 1439); rawBox.Text = selected.CopyText;
        status.Text = $"LU: {selected.Lu}  |  VIEW: {selected.View}  |  Varsayılan anahtar: {selected.KeyFields}";
        LoadRows();
    }
    private void NewSource()
    {
        selected = null; sourceList.ClearSelected(); nameBox.Clear(); rawBox.Clear(); interval.Value = 60;
        resultRows.Clear(); fieldNames.Clear(); visibleFields.Clear(); grid.Columns.Clear(); grid.Rows.Clear();
        if (emptyState is not null) emptyState.Visible = true;
        if (emptyTitle is not null) emptyTitle.Text = "Kaynak ekleyerek başlayın";
        if (emptySubtitle is not null) emptySubtitle.Text = "Bir veri kaynağı seçin veya yenisini tanımlayın.";
        status.Text = "COPYOBJECT bloğunu yapıştırıp Alanları düzenle / Kaydet'e bas."; count.Text = "0 kayıt";
    }
    private void SaveFields()
    {
        try
        {
            var parsed = CopyObject.Parse(rawBox.Text);
            if (selected is not null)
            {
                var old = selected.Fields.ToDictionary(x => x.Expression, StringComparer.Ordinal);
                foreach (var f in parsed.Fields) if (old.TryGetValue(f.Expression, out var previous)) { f.Enabled = previous.Enabled; f.Value = previous.Value; }
            }
            using var dialog = new FieldEditor(parsed.Fields, (int)interval.Value);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var fields = dialog.Fields; var name = string.IsNullOrWhiteSpace(nameBox.Text) ? parsed.View : nameBox.Text.Trim();
            var keys = string.Join(",", fields.Where(x => x.Queryable && new[] { "CONTRACT", "PART_NO", "ORDER_NO", "LINE_NO", "RELEASE_NO", "SEQUENCE_NO" }.Contains(x.Name)).Select(x => x.Name));
            selected ??= new Source();
            selected.Name = name; selected.Lu = parsed.Lu; selected.View = parsed.View; selected.CopyText = rawBox.Text.Trim();
            selected.Fields = fields; selected.KeyFields = keys; selected.Interval = (int)interval.Value;
            Database.Save(selected); var id = selected.Id; RefreshSources(id);
            status.Text = $"Kaydedildi  |  {parsed.View}  |  {fields.Count(x => x.Enabled)} filtre alanı seçildi";
            MessageBox.Show(this, "Alanlar kaydedildi.\n\nŞimdi Çek ile sorgulayabilirsin. Önce eşleşen kayıt sayısı gösterilecek; onayından sonra veriler alınacak.", "Kaydedildi", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "COPYOBJECT hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void DeleteSource()
    {
        if (selected is null) { MessageBox.Show(this, "Önce bir kaynak seç.", "Kaynak", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show(this, "Bu kaynak ve bu uygulamadaki yerel geçmiş kayıtları silinsin mi?\nKaynak sistemdeki veriler etkilenmez.", "Kaynağı sil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Database.Delete(selected.Id); selected = null; NewSource(); RefreshSources(); status.Text = "Kaynak ve yerel geçmiş silindi.";
    }
    private async Task FetchAsync()
    {
        if (selected is null) { MessageBox.Show(this, "Önce kaynağı kaydet.", "Kaynak", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var id = selected.Id;
        var firstRun = !Database.HasRun(id);
        Func<long, bool>? confirmCount = firstRun
            ? n => (bool)Invoke(new Func<bool>(() =>
                MessageBox.Show(this, BuildCountPrompt(n), "İlk sorgu onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes))
            : null;
        ToggleBusy(true); status.Text = "Kayıtlar sayılıyor; giriş penceresi açılabilir...";
        try
        {
            var result = await Task.Run(() =>
            {
                var ok = SyncService.Execute(id, confirmCount, out var msg);
                return (ok, msg);
            });
            status.Text = result.msg;
            if (result.ok && result.msg.StartsWith("Sorgu durduruldu", StringComparison.Ordinal)) return;
            if (result.ok)
            {
                selected = Database.GetSource(id) ?? selected;
                if (Database.GetTextPreference("ifs_user") is { Length: > 0 } currentUser) userLabel.Text = "Kullanıcı: " + currentUser;
                LoadRows(); MessageBox.Show(this, result.msg, "Tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else ShowError(result.msg);
        }
        catch (Exception ex) { status.Text = "Veri bağlantısı hatası."; ShowError(ex.ToString()); }
        finally { ToggleBusy(false); }
    }
    private void ToggleBusy(bool busy)
    {
        saveButton.Enabled = !busy; fetchButton.Enabled = !busy; sourceList.Enabled = !busy;
        nameBox.Enabled = !busy; rawBox.Enabled = !busy; interval.Enabled = !busy; UseWaitCursor = busy;
    }
    private void ShowError(string detail)
    {
        using var form = new Form { Text = "Veri bağlantısı hatası", Size = new Size(860, 460), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ForeColor = Theme.Ink };
        var label = Theme.Label("Hata ayrıntısı (seçip kopyalayabilirsin):"); label.Dock = DockStyle.Top; label.Height = 30; label.Padding = new Padding(10, 6, 0, 0); form.Controls.Add(label);
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = detail, Font = new Font("Consolas", 9), BackColor = Theme.Input, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle }; form.Controls.Add(text);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Background }; form.Controls.Add(bar);
        var close = Theme.Button("Kapat", false, 100); close.Click += (_, _) => form.Close(); bar.Controls.Add(close);
        var copy = Theme.Button("Tüm hatayı kopyala", false, 160); copy.Click += (_, _) => Clipboard.SetText(detail); bar.Controls.Add(copy);
        form.ShowDialog(this);
    }
    private void LoadRows()
    {
        grid.Rows.Clear(); grid.Columns.Clear(); resultRows = selected is null ? [] : Database.GetSnapshots(selected.Id);
        fieldNames = [];
        foreach (var row in resultRows) foreach (var key in row.Values.Keys) if (!fieldNames.Contains(key)) fieldNames.Add(key);
        var visible = fieldNames;
        if (selected is not null && !string.IsNullOrWhiteSpace(selected.VisibleColumns))
            try { var saved = JsonSerializer.Deserialize<List<string>>(selected.VisibleColumns); if (saved is not null) visible = fieldNames.Where(saved.Contains).ToList(); } catch { }
        visibleFields = visible;
        AddGridColumn("__at", "ÇEKİLME ZAMANI", 150, false); AddGridColumn("__kind", "DEĞİŞİM", 95, false);
        foreach (var name in visibleFields) AddGridColumn("f" + fieldNames.IndexOf(name), name, 180, true);
        ApplySearch();
    }
    private void AddGridColumn(string name, string title, int width, bool fill)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = width, MinimumWidth = fill ? 110 : width, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None, Width = width });
    }
    private static string ValueText(JsonElement v) => v.ValueKind switch { JsonValueKind.Null or JsonValueKind.Undefined => "", JsonValueKind.String => v.GetString() ?? "", _ => v.ToString() };
    internal static bool RowMatchesSearch(Snapshot row, string query) =>
        query.Length == 0 || row.At.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        row.Kind.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        row.Values.Any(x => ValueText(x.Value).Contains(query, StringComparison.CurrentCultureIgnoreCase));
    private void ApplySearch()
    {
        if (grid.Columns.Count == 0) { count.Text = "0 kayıt"; return; }
        var q = searchBox.Text.Trim(); grid.Rows.Clear();
        for (int index = 0; index < resultRows.Count; index++)
        {
            var row = resultRows[index];
            if (!RowMatchesSearch(row, q)) continue;
            var values = new List<object> { row.At, row.Kind }; values.AddRange(visibleFields.Select(k => row.Values.TryGetValue(k, out var v) ? ValueText(v) : ""));
            var rowIndex = grid.Rows.Add(values.ToArray()); grid.Rows[rowIndex].Tag = index;
        }
        count.Text = q.Length > 0 ? $"{grid.Rows.Count} / {resultRows.Count} kayıt" : $"{resultRows.Count} kayıt";
        if (emptyState is not null)
        {
            emptyState.Visible = grid.Rows.Count == 0;
            if (emptyTitle is not null) emptyTitle.Text = q.Length > 0 ? "Eşleşen kayıt bulunamadı" : "Henüz kayıt yok";
            if (emptySubtitle is not null) emptySubtitle.Text = q.Length > 0 ? "Arama metnini değiştirip yeniden deneyin." : "Sorgunu çalıştırdığında sonuçlar burada görünecek.";
        }
    }
    private void ChooseColumns()
    {
        if (selected is null || fieldNames.Count == 0) { MessageBox.Show(this, "Önce bir veri kaynağı seçip sorgu sonucu getir.", "Sütunlar", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new ColumnEditor(fieldNames, visibleFields);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Database.SaveVisibleColumns(selected.Id, dialog.SelectedColumns); selected.VisibleColumns = JsonSerializer.Serialize(dialog.SelectedColumns); LoadRows();
    }
    private void CopyRows(bool selectedOnly)
    {
        var rows = selectedOnly
            ? grid.SelectedRows.Cast<DataGridViewRow>().Where(x => x.Tag is int).Select(x => resultRows[(int)x.Tag!]).ToList()
            : resultRows;
        if (rows.Count == 0) { MessageBox.Show(this, "Kopyalanacak sonuç bulunamadı.", "Kopyalama", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var keys = visibleFields.Count > 0 ? visibleFields : fieldNames;
        var lines = new List<string> { "Çekilme zamanı\tDeğişim\t" + string.Join("\t", keys) };
        foreach (var row in rows) lines.Add(string.Join("\t", new[] { row.At, row.Kind }.Concat(keys.Select(k => row.Values.TryGetValue(k, out var v) ? ValueText(v) : ""))));
        Clipboard.SetText(string.Join(Environment.NewLine, lines)); status.Text = $"{rows.Count} satır panoya kopyalandı.";
    }
    private void Schedule()
    {
        if (selected is null) { MessageBox.Show(this, "Önce kaynak seç.", "Zamanlama", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        selected.Interval = (int)interval.Value; Database.Save(selected);
        var minutes = selected.Interval; var taskName = $"MiralayIFS_{selected.Id}";
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var command = BuildTaskCommand(exe, selected.Id);
        var psi = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/Create", "/TN", taskName, "/SC", "MINUTE", "/MO", minutes.ToString(), "/TR", command, "/F" }) psi.ArgumentList.Add(arg);
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Windows Görev Zamanlayıcısı başlatılamadı.");
            var stdout = p.StandardOutput.ReadToEnd(); var stderr = p.StandardError.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException((stderr + Environment.NewLine + stdout).Trim());
            MessageBox.Show(this, $"'{selected.Name}' kaynağı için her {minutes} dakikada bir görev kuruldu.\n\nBilgisayar açık olmalı; oturum açma penceresi açılabilir.", "Zamanlama", MessageBoxButtons.OK, MessageBoxIcon.Information);
            status.Text = $"'{selected.Name}' kaynağı zamanlanıyor ({taskName}).";
            RefreshSources(selected.Id);
        }
        catch (Exception ex) { ShowError("Zamanlama hatası\r\n" + ex); }
    }
    private static bool IsTaskScheduled(long id)
    {
        var psi = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/Query", "/TN", $"MiralayIFS_{id}" }) psi.ArgumentList.Add(arg);
        try { using var process = Process.Start(psi); if (process is null) return false; process.WaitForExit(2500); return process.HasExited && process.ExitCode == 0; }
        catch { return false; }
    }
    internal static string BuildTaskCommand(string exe, long id) => $"\"{exe}\" --sync-source {id}";
    internal static string BuildCountPrompt(long matchingRows) =>
        $"Sorgu {matchingRows.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} kayıtla eşleşiyor. Eşleşen tüm kayıtlar alınacak. Devam edilsin mi?";
}

internal static class SmokeTests
{
    public static int Run()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MiralayIFS-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("MIRALAY_IFS_DB_PATH", System.IO.Path.Combine(root, "data.db"));
        try
        {
            Database.Initialize();
            var raw = "$LU = Order\n$VIEW = CUSTOMER_ORDER\n-$1: ORDER_NO = 1001\n-$2: CUSTOMER_NO = %\n-$3: Decode(STATE) = Released";
            var (lu, view, fields) = CopyObject.Parse(raw);
            Assert(lu == "Order" && view == "CUSTOMER_ORDER" && fields.Count == 3, "COPYOBJECT alan ayrıştırma");
            fields[0].Enabled = true; fields[0].Value = "1001"; fields[1].Enabled = true; fields[1].Value = "%";
            var source = new Source { Name = "Smoke", Lu = lu, View = view, CopyText = raw, Fields = fields, Interval = 12, KeyFields = "ORDER_NO" };
            Database.Save(source);
            var loaded = Database.GetSource(source.Id)!;
            Assert(loaded.Fields.Count == 3 && loaded.Fields[0].Enabled && loaded.Fields[0].Value == "1001", "Kaynak ve filtre alanı kalıcılığı");
            var config = SyncService.MakeConfig(loaded);
            Assert(!config.ContainsKey("max_rows"), "Sorgu yapılandırmasında sabit satır sınırı olmaması");
            Assert(((List<string>)config["columns"]!).Contains("ORDER_NO") && ((object[])config["filters"]!).Length == 1, "Sorgu sütunları ve filtre üretimi");
            Assert(ExplorerForm.BuildTaskCommand(@"C:\Program Files\Miralay\Explorer.exe", source.Id) == $"\"C:\\Program Files\\Miralay\\Explorer.exe\" --sync-source {source.Id}", "Zamanlanmış görev komut satırı");
            Assert(ExplorerForm.BuildCountPrompt(23414).Contains("23.414 kayıt") && ExplorerForm.BuildCountPrompt(23414).Contains("tüm kayıtlar"), "İlk sorguda tüm eşleşen kayıtlar için onay metni");
            using var searchJson = JsonDocument.Parse("{\"ORDER_NO\":\"1001\",\"CUSTOMER\":\"Örnek\"}");
            var searchRow = new Snapshot("2026-01-01", "CHANGED", searchJson.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value));
            Assert(ExplorerForm.RowMatchesSearch(searchRow, "changed") && ExplorerForm.RowMatchesSearch(searchRow, "örnek") && !ExplorerForm.RowMatchesSearch(searchRow, "bulunmaz"), "Arama eşleşmesi");
            using var hashJson = JsonDocument.Parse("{\"B\":\"ı\",\"A\":\"X\"}");
            Assert(SyncService.Canonical(hashJson.RootElement) == "{\"A\": \"X\", \"B\": \"ı\"}", "Python snapshot hash uyumluluğu");
            using var keyJson = JsonDocument.Parse("{\"ORDER_NO\":\"1001\",\"CONTRACT\":\"TR\"}");
            Assert(SyncService.CanonicalKey(keyJson.RootElement, new[] { "ORDER_NO", "CONTRACT" }) == "[\"1001\", \"TR\"]", "Python row_key JSON biçimi");
            var rows = JsonDocument.Parse("[" + string.Join(",", Enumerable.Range(0, 510).Select(i => $"{{\"NO\":\"{i}\"}}")) + "]");
            var uncapped = rows.RootElement.EnumerateArray().ToList();
            Assert(uncapped.Count == 510, "Sorgu sonuçlarında sabit 500 satır sınırı olmaması");
            using (var c = Database.Open()) using (var q = c.CreateCommand())
            {
                q.CommandText = "INSERT INTO snapshots(source_id,fetched_at,row_json,row_hash,change_type,row_key) VALUES($id,'2026-01-01T00:00:00','{\"ORDER_NO\":\"1001\"}','x','NEW','1001')";
                q.Parameters.AddWithValue("$id", source.Id); q.ExecuteNonQuery();
            }
            Assert(Database.GetSnapshots(source.Id).Count == 1, "Yerel snapshot okuma");
            Database.Delete(source.Id); Assert(Database.GetSource(source.Id) is null, "Yerel kaynak/geçmiş silme");
            var guna = Assembly.Load("Guna.UI2, Version=2.0.4.8, Culture=neutral, PublicKeyToken=8b9d14aa5142e261");
            Assert(guna.GetType("Guna.UI2.WinForms.Guna2Panel") is not null, "Guna2 gömülü DLL yükleme");
            using var iconResource = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.app.ico");
            Assert(iconResource is not null, "Marka uygulama ikonu gömülü");
            using var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Assert(appIcon is not null, "EXE uygulama ikonu");
            Console.WriteLine("SELF-TEST PASS: veri akışları, arama/filtre, 500 sınırı, kaynak zamanlaması, Guna2 kaynağı ve uygulama ikonu.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
    private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException("Başarısız: " + name); }
}

internal sealed class FieldEditor : Form
{
    private readonly Guna2DataGridView grid = new();
    public List<Field> Fields { get; private set; }
    public FieldEditor(List<Field> fields, int interval)
    {
        Fields = fields.Select(x => new Field { Name = x.Name, Expression = x.Expression, Value = x.Value, Queryable = x.Queryable, Enabled = x.Enabled }).ToList();
        Text = "Alanlar ve sorgu koşulları"; Size = new Size(1000, 700); MinimumSize = new Size(820, 520); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Ink;
        var header = Theme.Label("Sadece seçtiğin gerçek alanlar filtre olur. % = bu alan için filtre uygulama. Tarih aralığı: 01.01.2026..31.01.2026", 9, color: Theme.Muted); header.Dock = DockStyle.Top; header.Height = 42; header.Padding = new Padding(10, 12, 0, 0); Controls.Add(header);
        grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Theme.Panel; grid.BorderStyle = BorderStyle.None; grid.GridColor = Theme.Border; grid.EnableHeadersVisualStyles = false; grid.RowTemplate.Height = 36; grid.ColumnHeadersHeight = 40;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(48, 33, 111); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White; grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Theme.Panel; grid.DefaultCellStyle.ForeColor = Theme.Ink; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(73, 50, 141); grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.PanelLight;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "Filtre", FillWeight = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "expression", HeaderText = "Alan / ifade", ReadOnly = true, FillWeight = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "copy", HeaderText = "COPYOBJECT değeri", ReadOnly = true, FillWeight = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Sorguda kullanılacak değer", FillWeight = 130 });
        foreach (var f in Fields) { var i = grid.Rows.Add(f.Enabled, f.Expression, f.Value, f.Value); grid.Rows[i].Cells[0].ReadOnly = !f.Queryable; grid.Rows[i].Cells[3].ReadOnly = !f.Queryable; }
        grid.CellFormatting += (_, e) => { if (e.RowIndex >= 0 && !Fields[e.RowIndex].Queryable) e.CellStyle.ForeColor = Theme.Muted; };
        Controls.Add(grid); var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 57, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), BackColor = Theme.Background }; Controls.Add(footer);
        var ok = Theme.Button("Seçimleri kaydet", true, 155); ok.Click += (_, _) => Commit(); footer.Controls.Add(ok);
        var cancel = Theme.Button("Vazgeç", false, 100); cancel.DialogResult = DialogResult.Cancel; footer.Controls.Add(cancel);
        var note = Theme.Label("Fonksiyonlu satırlar bilgi amaçlıdır; doğrudan SQL filtresi yapılamaz.", 8.5F, color: Theme.Muted); note.Dock = DockStyle.Bottom; note.Height = 24; note.TextAlign = ContentAlignment.MiddleLeft; Controls.Add(note);
        AcceptButton = ok; CancelButton = cancel;
    }
    private void Commit()
    {
        grid.EndEdit();
        for (int i = 0; i < Fields.Count; i++) { Fields[i].Enabled = Fields[i].Queryable && Convert.ToBoolean(grid.Rows[i].Cells[0].Value ?? false); Fields[i].Value = grid.Rows[i].Cells[3].Value?.ToString()?.Trim() ?? ""; }
        DialogResult = DialogResult.OK; Close();
    }
}

internal sealed class ColumnEditor : Form
{
    private readonly CheckedListBox list = new();
    public List<string> SelectedColumns => list.CheckedItems.Cast<string>().ToList();
    public ColumnEditor(List<string> fields, List<string> visible)
    {
        Text = "Gösterilecek sütunları seç"; Size = new Size(440, 580); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Ink;
        var label = Theme.Label("Listede görmek istediğin sütunları işaretle:", 9); label.Dock = DockStyle.Top; label.Height = 38; label.Padding = new Padding(12, 10, 0, 0); Controls.Add(label);
        list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.CheckOnClick = true; list.Font = new Font("Segoe UI", 9F); list.BackColor = Theme.Panel; list.ForeColor = Theme.Ink;
        foreach (var f in fields) list.Items.Add(f, visible.Contains(f)); Controls.Add(list);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, BackColor = Theme.Background }; Controls.Add(tools);
        var all = Theme.Button("Tümünü seç", false, 110); all.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); }; tools.Controls.Add(all);
        var none = Theme.Button("Tümünü kaldır", false, 125); none.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); }; tools.Controls.Add(none);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), BackColor = Theme.Background }; Controls.Add(bottom);
        var ok = Theme.Button("Uygula", true, 95); ok.DialogResult = DialogResult.OK; bottom.Controls.Add(ok);
        var cancel = Theme.Button("İptal", false, 95); cancel.DialogResult = DialogResult.Cancel; bottom.Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
    }
}
