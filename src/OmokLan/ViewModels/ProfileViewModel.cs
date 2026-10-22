using System.Collections.ObjectModel;
using System.Windows.Input;
using OmokLan.Mvvm;
using OmokLan.Services;

namespace OmokLan.ViewModels;

public sealed class ProfileViewModel : ObservableObject
{
    private string _nickname;
    private int _selectedAvatarId;
    private string? _errorMessage;

    public ProfileViewModel(string currentNick, int currentAvatarId)
    {
        _nickname = currentNick;
        _selectedAvatarId = currentAvatarId;

        Avatars = new ObservableCollection<AvatarInfo>(AvatarCatalog.Avatars);

        SelectAvatarCommand = new ActionCommand(param =>
        {
            if (param is int id)
            {
                SelectedAvatarId = id;
            }
            return Task.CompletedTask;
        });
    }

    public ObservableCollection<AvatarInfo> Avatars { get; }

    public string Nickname
    {
        get => _nickname;
        set
        {
            if (Set(ref _nickname, value))
            {
                ErrorMessage = null;
            }
        }
    }

    public int SelectedAvatarId
    {
        get => _selectedAvatarId;
        set => Set(ref _selectedAvatarId, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => Set(ref _errorMessage, value);
    }

    public ICommand SelectAvatarCommand { get; }

    public bool Validate()
    {
        var trimmed = Nickname?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ErrorMessage = "사용자 명(닉네임)을 입력해 주세요.";
            return false;
        }

        if (trimmed.Length < 2 || trimmed.Length > 12)
        {
            ErrorMessage = "닉네임은 2자 이상 12자 이하로 입력해 주세요.";
            return false;
        }

        ErrorMessage = null;
        return true;
    }
}
