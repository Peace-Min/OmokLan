using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace OmokLan.Services;

/// <summary>로컬 설정 (%LOCALAPPDATA%\OmokLan\settings.json).</summary>
public sealed class AppSettings
{
    public string Nickname { get; set; } = "";
    public int AvatarId { get; set; } = 0;
    public bool HasCompletedSetup { get; set; } = false;
    
    /// <summary>게임 시작 시 진짜 Windows 터미널(CLI 콘솔)을 띄워 플레이하는 스텔스 위장 모드 여부</summary>
    public bool UseTerminalStealthMode { get; set; } = false;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmokLan", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 손상된 설정은 무시하고 기본값 사용
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // 저장 실패는 치명적이지 않음
        }
    }
}

public static class NetInfo
{
    /// <summary>표시용 대표 IPv4 주소 (게이트웨이가 있는 NIC 우선).</summary>
    public static string PrimaryIPv4()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => (Props: n.GetIPProperties(), Nic: n))
                .OrderByDescending(t => t.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                .SelectMany(t => t.Props.UnicastAddresses)
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(u => u.Address.ToString());

            return candidates.FirstOrDefault() ?? "127.0.0.1";
        }
        catch (Exception)
        {
            return "127.0.0.1";
        }
    }
}
