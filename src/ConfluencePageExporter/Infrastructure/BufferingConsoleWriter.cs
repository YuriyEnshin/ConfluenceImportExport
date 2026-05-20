using System.Text;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// <see cref="IConsoleWriter"/> implementation that captures emitted lines
/// into an in-memory buffer instead of writing to <see cref="Console.Out"/>.
/// Used by the MCP server runner so that handler/report output can be
/// attached to the JSON tool result instead of leaking onto stdout
/// (which is reserved for the JSON-RPC protocol).
/// Registered scoped: a fresh buffer per tool invocation.
/// </summary>
public sealed class BufferingConsoleWriter : IConsoleWriter
{
    private readonly List<string> _lines = new();
    private readonly StringBuilder _pending = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lock)
            {
                if (_pending.Length == 0)
                    return _lines.ToArray();

                var snapshot = new List<string>(_lines.Count + 1);
                snapshot.AddRange(_lines);
                snapshot.Add(_pending.ToString());
                return snapshot;
            }
        }
    }

    public void WriteLine(string message = "")
    {
        lock (_lock)
        {
            if (_pending.Length > 0)
            {
                _lines.Add(_pending.ToString() + message);
                _pending.Clear();
            }
            else
            {
                _lines.Add(message);
            }
        }
    }

    public void Write(string message)
    {
        lock (_lock)
        {
            _pending.Append(message);
        }
    }
}
