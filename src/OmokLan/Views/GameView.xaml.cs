using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using OmokLan.ViewModels;

namespace OmokLan.Views;

public partial class GameView : UserControl
{
    public GameView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is GameViewModel oldVm)
        {
            oldVm.ChatMessages.CollectionChanged -= OnChatMessagesChanged;
        }

        if (e.NewValue is GameViewModel newVm)
        {
            newVm.ChatMessages.CollectionChanged += OnChatMessagesChanged;
        }
    }

    private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ChatScrollViewer.ScrollToEnd();
        }
    }

    private void OnChatInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is GameViewModel vm && vm.SendChatCommand.CanExecute(null))
            {
                vm.SendChatCommand.Execute(null);
            }
        }
    }
}
