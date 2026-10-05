using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIVitals.App;

public sealed record PausedThreadRowViewModel
{
    public required string ThreadId { get; init; }
    public required string BlockedTurnId { get; init; }
    public required string Name { get; init; }
    public required string Project { get; init; }
    public required string StatusText { get; init; }
    public string? PermissionsText { get; init; }
    public bool CanContinue { get; init; }
    public bool IsArmed { get; init; }
    public bool CanArm { get; init; }
}

/// <summary>Backs the "paused Codex threads" card on the connections page.</summary>
public sealed class CodexResumeViewModel : INotifyPropertyChanged
{
    private bool _autoResumeEnabled;
    private bool _showThreadNames;
    private string? _message;
    private string? _scanStatus;

    public ObservableCollection<PausedThreadRowViewModel> Threads { get; } = [];

    public bool HasThreads => Threads.Count > 0;

    public string? ScanStatus
    {
        get => _scanStatus;
        set => Set(ref _scanStatus, value);
    }

    public bool AutoResumeEnabled
    {
        get => _autoResumeEnabled;
        set => Set(ref _autoResumeEnabled, value);
    }

    public bool ShowThreadNames
    {
        get => _showThreadNames;
        set => Set(ref _showThreadNames, value);
    }

    /// <summary>Outcome of the last action, such as a thread still open in another app.</summary>
    public string? Message
    {
        get => _message;
        set
        {
            if (!Set(ref _message, value)) return;
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(_message);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReplaceThreads(IEnumerable<PausedThreadRowViewModel> rows)
    {
        Threads.Clear();
        foreach (var row in rows) Threads.Add(row);
        OnPropertyChanged(nameof(HasThreads));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
