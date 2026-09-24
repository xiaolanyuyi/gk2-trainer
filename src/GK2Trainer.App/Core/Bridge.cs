using System.Text;
using System.Text.Json;

namespace GK2Trainer.App.Core;

/// <summary>
/// Talks to the in-game plugin through <c>Trainer\command.json</c> and
/// <c>Trainer\state.json</c>. Commands are queued and sent one batch at a time,
/// so a slow frame in the game can never make the app skip an operation.
///
/// The (token, seq) pair is the acknowledgement protocol: the plugin persists
/// what it executed inside state.json, which is why reloading a save can never
/// replay the last command.
/// </summary>
public sealed class BridgeClient
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new LenientNumberConverter(),
            new LenientIntConverter(),
            new LenientBoolConverter(),
            new LenientStringConverter(),
            new LenientListConverter<string>(),
            new LenientListConverter<OpResult>(),
            new LenientListConverter<TargetInfo>(),
        },
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AppSettings _settings;
    private readonly List<Op> _queue = new();

    private long _inFlightSeq;
    private DateTime _inFlightSince = DateTime.MinValue;
    private DateTime _lastStateWrite = DateTime.MinValue;

    public string Token { get; }
    public long Sequence { get; private set; }
    public StateDoc? State { get; private set; }
    public string? LastError { get; private set; }
    public bool CommandInFlight { get; private set; }
    public long LastSentSequence { get; private set; }

    /// <summary>Op results the app has not consumed yet (eg. the id lists).</summary>
    public List<OpResult> PendingResults { get; } = new();

    public BridgeClient(AppSettings settings)
    {
        _settings = settings;
        Token = Guid.NewGuid().ToString("N")[..12];
        Sequence = Math.Max(settings.LastSequence, 0);
    }

    public int QueuedCount => _queue.Count;

    public void Enqueue(Op op) => _queue.Add(op);

    public void Enqueue(IEnumerable<Op> ops) => _queue.AddRange(ops);

    /// <summary>Reads state.json when it changed since the last call.</summary>
    public void Poll()
    {
        try
        {
            if (!File.Exists(Paths.StateFile)) return;

            var write = File.GetLastWriteTimeUtc(Paths.StateFile);
            if (write == _lastStateWrite) return;

            var text = ReadAllTextShared(Paths.StateFile);
            if (string.IsNullOrWhiteSpace(text)) return;

            var state = JsonSerializer.Deserialize<StateDoc>(text, ReadOptions);
            if (state == null) return;

            _lastStateWrite = write;

            // Hand the command results to the caller once.
            foreach (var result in state.Results)
            {
                if (result.Op == null) continue;
                PendingResults.Add(result);
            }
            state.Results.Clear();

            State = state;
            LastError = null;
        }
        catch (JsonException)
        {
            // The plugin rewrites the file in place; keep the previous snapshot.
            _lastStateWrite = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Sends the next queued batch when the previous one was acknowledged.</summary>
    public void Flush()
    {
        if (CommandInFlight)
        {
            var acknowledged = State != null && State.Seq >= _inFlightSeq;
            var timedOut = DateTime.UtcNow - _inFlightSince > TimeSpan.FromSeconds(4);
            if (!acknowledged && !timedOut) return;
            CommandInFlight = false;
        }

        if (_queue.Count == 0) return;

        Sequence++;
        var doc = new CommandDoc
        {
            Token = Token,
            Seq = Sequence,
            Ops = new List<Op>(_queue),
        };
        _queue.Clear();

        try
        {
            Directory.CreateDirectory(Paths.BridgeDir);
            var json = JsonSerializer.Serialize(doc, WriteOptions);
            var temp = Paths.CommandFile + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, Paths.CommandFile, overwrite: true);

            _settings.LastSequence = Sequence;
            _settings.Save();

            _inFlightSeq = Sequence;
            _inFlightSince = DateTime.UtcNow;
            LastSentSequence = Sequence;
            CommandInFlight = true;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            CommandInFlight = false;
        }
    }

    /// <summary>Content of the plugin's log file.</summary>
    public string ReadGameLog()
    {
        try
        {
            return File.Exists(Paths.BridgeLogFile) ? ReadAllTextShared(Paths.BridgeLogFile) : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Reads the game's BepInEx log tail (the first place to look when the plugin is missing).</summary>
    public static string ReadBepInExLog(string gameDir, int lines = 40)
    {
        try
        {
            var path = Paths.BepInExLog(gameDir);
            if (!File.Exists(path)) return "";
            var all = File.ReadAllLines(path);
            return string.Join(Environment.NewLine, all.TakeLast(lines));
        }
        catch
        {
            return "";
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
