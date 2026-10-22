using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace OmokLan.Mvvm;

/// <summary>INotifyPropertyChanged 기본 구현.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>동기/비동기 공용 커맨드. 비동기 실행 중에는 재실행을 막습니다.</summary>
public sealed class ActionCommand : ICommand
{
    private readonly Func<object?, Task> _run;
    private readonly Func<object?, bool>? _guard;
    private bool _running;

    public ActionCommand(Action execute)
        : this(_ => { execute(); return Task.CompletedTask; }, (Func<object?, bool>?)null) { }

    public ActionCommand(Action execute, Func<bool>? canExecute)
        : this(_ => { execute(); return Task.CompletedTask; }, canExecute is null ? null : _ => canExecute()) { }

    public ActionCommand(Func<Task> execute)
        : this(_ => execute(), (Func<object?, bool>?)null) { }

    public ActionCommand(Func<Task> execute, Func<bool>? canExecute)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public ActionCommand(Func<object?, Task> execute)
        : this(execute, (Func<object?, bool>?)null) { }

    public ActionCommand(Func<object?, Task> execute, Func<bool> canExecute)
        : this(execute, _ => canExecute()) { }

    public ActionCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute)
    {
        _run = execute;
        _guard = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (_guard?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _run(parameter);
        }
        catch (Exception ex)
        {
            Ui.Toast?.Invoke($"오류: {ex.Message}");
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

/// <summary>UI 스레드 마샬링 도우미.</summary>
public static class Ui
{
    /// <summary>전역 토스트 출력 (MainViewModel 이 연결).</summary>
    public static Action<string>? Toast { get; set; }

    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public static void Refresh() => CommandManager.InvalidateRequerySuggested();
}
