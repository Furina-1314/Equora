namespace Equora.App.Services;

/// <summary>
/// 简单撤销栈(M1):记录操作的逆动作,UI 在 Ctrl+Z 时调用。
/// 重做(P6)与跨会话撤销不在本阶段范围。
/// </summary>
public interface IUndoService
{
    bool CanUndo { get; }
    string? UndoDescription { get; }
    int Count { get; }
    event EventHandler? Changed;

    /// <summary>压入一条可撤销操作(已执行完成后的逆动作)。</summary>
    void Push(string description, Action undo);

    /// <summary>执行最近一条撤销;无条目返回 null。</summary>
    string? Undo();

    void Clear();
}

public sealed class UndoService : IUndoService
{
    private const int MaxEntries = 100;
    private readonly Stack<Entry> _entries = new();

    private sealed record Entry(string Description, Action Undo);

    public event EventHandler? Changed;

    public bool CanUndo => _entries.Count > 0;
    public string? UndoDescription => _entries.Count > 0 ? _entries.Peek().Description : null;
    public int Count => _entries.Count;

    public void Push(string description, Action undo)
    {
        _entries.Push(new Entry(description, undo));
        while (_entries.Count > MaxEntries)
        {
            var drained = new List<Entry>();
            while (_entries.Count > MaxEntries) drained.Add(_entries.Pop());
            // 丢弃最老的一条,保留其余顺序。
            drained.RemoveAt(drained.Count - 1);
            for (var i = drained.Count - 1; i >= 0; i--) _entries.Push(drained[i]);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? Undo()
    {
        if (_entries.Count == 0) return null;
        var entry = _entries.Pop();
        entry.Undo();
        Changed?.Invoke(this, EventArgs.Empty);
        return entry.Description;
    }

    public void Clear()
    {
        _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
