using System.Windows.Input;
using OmokLan.Engine.Net;
using OmokLan.Engine.Play;
using OmokLan.Mvvm;
using OmokLan.Services;

namespace OmokLan.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly string _selfId;
    private LanBeacon? _beacon;
    private DuelHost? _host;

    private object? _currentView;
    private LobbyViewModel? _lobbyVm;
    private GameViewModel? _gameVm;

    private bool _isProfileModalOpen;
    private ProfileViewModel? _profileEditVm;
    private bool _isFirstTimeSetup;

    private string? _toastText;
    private bool _isToastVisible;
    private System.Timers.Timer? _toastTimer;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _selfId = Guid.NewGuid().ToString("N");

        Ui.Toast = ShowToast;

        OpenProfileModalCommand = new ActionCommand(() =>
        {
            ProfileEditVm = new ProfileViewModel(_settings.Nickname, _settings.AvatarId);
            IsFirstTimeSetup = false;
            IsProfileModalOpen = true;
        });

        SaveProfileCommand = new ActionCommand(() =>
        {
            if (ProfileEditVm is null || !ProfileEditVm.Validate()) return;

            _settings.Nickname = ProfileEditVm.Nickname.Trim();
            _settings.AvatarId = ProfileEditVm.SelectedAvatarId;
            _settings.HasCompletedSetup = true;
            _settings.Save();

            Raise(nameof(CurrentNick));
            Raise(nameof(CurrentAvatar));

            // 네트워크 서비스에 프로필 갱신 알림
            if (_host is not null)
            {
                _host.SelfNick = _settings.Nickname;
                _host.SelfAvatar = _settings.AvatarId;
            }
            _beacon?.AnnounceNow();
            _gameVm?.NotifyProfileChanged();

            IsProfileModalOpen = false;

            // 최초 설정 완료 후 로비로 진입
            if (CurrentView is null || IsFirstTimeSetup)
            {
                InitNetworkingAndLobby();
            }
        });

        CancelProfileCommand = new ActionCommand(() =>
        {
            if (!IsFirstTimeSetup)
            {
                IsProfileModalOpen = false;
            }
        });

        if (!_settings.HasCompletedSetup || string.IsNullOrWhiteSpace(_settings.Nickname))
        {
            // 최초 실행: 프로필 설정 요구
            ProfileEditVm = new ProfileViewModel("", 0);
            IsFirstTimeSetup = true;
            IsProfileModalOpen = true;
        }
        else
        {
            InitNetworkingAndLobby();
        }
    }

    public string CurrentNick => _settings.Nickname;
    public int CurrentAvatar => _settings.AvatarId;
    public string LocalIp => NetInfo.PrimaryIPv4();

    public object? CurrentView
    {
        get => _currentView;
        private set => Set(ref _currentView, value);
    }

    public bool IsProfileModalOpen
    {
        get => _isProfileModalOpen;
        set => Set(ref _isProfileModalOpen, value);
    }

    public bool IsFirstTimeSetup
    {
        get => _isFirstTimeSetup;
        private set => Set(ref _isFirstTimeSetup, value);
    }

    public ProfileViewModel? ProfileEditVm
    {
        get => _profileEditVm;
        private set => Set(ref _profileEditVm, value);
    }

    public string? ToastText
    {
        get => _toastText;
        private set => Set(ref _toastText, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => Set(ref _isToastVisible, value);
    }

    public ICommand OpenProfileModalCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand CancelProfileCommand { get; }

    private void InitNetworkingAndLobby()
    {
        _host = new DuelHost(_selfId, _settings.Nickname)
        {
            SelfAvatar = _settings.AvatarId
        };
        _host.Start();

        _beacon = new LanBeacon(_selfId, () =>
        {
            var status = _host.InGame || _host.Engaged
                ? PresenceStatus.InGame
                : (_host.DoNotDisturb ? PresenceStatus.Dnd : PresenceStatus.Available);
            return (_settings.Nickname, _settings.AvatarId, status, _host.Port);
        });
        _beacon.Start();

        _lobbyVm = new LobbyViewModel(_beacon, _host, StartGame);
        CurrentView = _lobbyVm;
    }

    private void StartGame(DuelSession session)
    {
        Ui.Post(() =>
        {
            if (_host is not null) _host.Engaged = true;
            _beacon?.AnnounceNow();

            var match = new OmokMatch(session, _settings.Nickname, _settings.AvatarId);
            _gameVm = new GameViewModel(match, () =>
            {
                // 게임 종료 후 로비로 복귀
                if (_host is not null) _host.Engaged = false;
                _beacon?.AnnounceNow();
                _gameVm = null;
                CurrentView = _lobbyVm;
                _lobbyVm?.RefreshPeers();
            });

            CurrentView = _gameVm;
        });
    }

    public void ShowToast(string message)
    {
        Ui.Post(() =>
        {
            ToastText = message;
            IsToastVisible = true;

            _toastTimer?.Dispose();
            _toastTimer = new System.Timers.Timer(3200);
            _toastTimer.Elapsed += (s, e) =>
            {
                Ui.Post(() =>
                {
                    IsToastVisible = false;
                });
            };
            _toastTimer.AutoReset = false;
            _toastTimer.Start();
        });
    }

    public void Dispose()
    {
        _lobbyVm?.Dispose();
        _beacon?.Dispose();
        _host?.Dispose();
        _toastTimer?.Dispose();
    }
}
