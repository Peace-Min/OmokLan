using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OmokLan.Engine.Net;

/// <summary>
/// 탐색된 사용자 목록. 마지막 신호 이후 TTL 이 지나면 제거됩니다.
/// 시간은 외부에서 주입받아 테스트가 쉽도록 했습니다.
/// </summary>
public sealed class PeerDirectory
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (PeerInfo Info, DateTime SeenAt)> _entries = new();

    public PeerDirectory(TimeSpan? ttl = null)
    {
        Ttl = ttl ?? TimeSpan.FromSeconds(6);
    }

    public TimeSpan Ttl { get; }

    /// <summary>추가 또는 갱신. 새 사용자이거나 정보가 바뀐 경우 true.</summary>
    public bool Upsert(PeerInfo info, DateTime now)
    {
        lock (_sync)
        {
            bool changed = !_entries.TryGetValue(info.Id, out var prev) || prev.Info != info;
            _entries[info.Id] = (info, now);
            return changed;
        }
    }

    public bool Remove(string id)
    {
        lock (_sync) return _entries.Remove(id);
    }

    /// <summary>만료된 항목을 제거합니다. 하나라도 제거되면 true.</summary>
    public bool Sweep(DateTime now)
    {
        lock (_sync)
        {
            var expired = _entries
                .Where(kv => now - kv.Value.SeenAt > Ttl)
                .Select(kv => kv.Key)
                .ToList();
            expired.ForEach(id => _entries.Remove(id));
            return expired.Count > 0;
        }
    }

    public PeerInfo? Find(string id)
    {
        lock (_sync) return _entries.TryGetValue(id, out var e) ? e.Info : null;
    }

    public IReadOnlyList<PeerInfo> Snapshot()
    {
        lock (_sync)
        {
            return _entries.Values
                .Select(e => e.Info)
                .OrderBy(p => p.Nick, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Id, StringComparer.Ordinal)
                .ToList();
        }
    }
}

/// <summary>
/// UDP 브로드캐스트로 자신의 존재를 알리고, 다른 사용자의 신호를 수집합니다.
/// </summary>
public sealed class LanBeacon : IDisposable
{
    public const int DefaultPort = 47800;

    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TargetCacheLife = TimeSpan.FromSeconds(15);

    private readonly int _port;
    private readonly string _selfId;
    private readonly Func<(string Nick, int Avatar, PresenceStatus Status, int TcpPort)> _identity;
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _udp;
    private IPAddress[] _targets = Array.Empty<IPAddress>();
    private DateTime _targetsBuiltAt = DateTime.MinValue;
    private int _disposed;

    public LanBeacon(string selfId, Func<(string Nick, int Avatar, PresenceStatus Status, int TcpPort)> identity, int port = DefaultPort)
    {
        _selfId = selfId;
        _identity = identity;
        _port = port;
    }

    public PeerDirectory Directory { get; } = new();

    /// <summary>목록이 바뀌면 발생합니다. (백그라운드 스레드)</summary>
    public event Action? PeersChanged;

    public void Start()
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        // 같은 PC 에서 여러 인스턴스가 같은 포트로 브로드캐스트를 받을 수 있도록 재사용 허용
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.EnableBroadcast = true;
        if (OperatingSystem.IsWindows())
        {
            // ICMP Port Unreachable 로 인한 WSAECONNRESET(10054) 수신 오류 방지 (SIO_UDP_CONNRESET = false)
            const int SioUdpConnReset = -1744830452;
            try { udp.Client.IOControl(SioUdpConnReset, new byte[] { 0 }, null); } catch { /* 미지원 시 무시 */ }
        }
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
        _udp = udp;

        _ = ListenLoopAsync(_stop.Token);
        _ = AnnounceLoopAsync(_stop.Token);
    }

    /// <summary>상태 변경 등을 즉시 알립니다.</summary>
    public void AnnounceNow() => _ = BroadcastAsync(BeaconPacket.KindHello);

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await BroadcastAsync(BeaconPacket.KindHello).ConfigureAwait(false);
            if (Directory.Sweep(DateTime.UtcNow)) PeersChanged?.Invoke();

            try { await Task.Delay(AnnounceInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        var udp = _udp!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult datagram;
            try
            {
                datagram = await udp.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            if (!BeaconPacket.TryParse(datagram.Buffer, out var packet) || packet.Id == _selfId)
                continue;

            bool changed = packet.Type == BeaconPacket.KindBye
                ? Directory.Remove(packet.Id)
                : Directory.Upsert(
                    new PeerInfo(packet.Id, packet.Nick.Trim(), AvatarRange.Clamp(packet.Avatar), packet.Status,
                        datagram.RemoteEndPoint.Address, packet.Tcp),
                    DateTime.UtcNow);

            if (changed) PeersChanged?.Invoke();
        }
    }

    private BeaconPacket ComposePacket(string kind)
    {
        var (nick, avatar, status, tcpPort) = _identity();
        return new BeaconPacket { Type = kind, Id = _selfId, Nick = nick, Avatar = avatar, Status = status, Tcp = tcpPort };
    }

    private async Task BroadcastAsync(string kind)
    {
        var udp = _udp;
        if (udp is null || Volatile.Read(ref _disposed) != 0) return;

        byte[] payload = ComposePacket(kind).ToBytes();
        foreach (var target in ResolveTargets())
        {
            try
            {
                await udp.SendAsync(payload, new IPEndPoint(target, _port)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 특정 NIC 전송 실패는 무시하고 다음 대상으로 진행
            }
        }
    }

    /// <summary>
    /// 전송 대상: 제한 브로드캐스트(255.255.255.255) + 활성 NIC 별 서브넷 브로드캐스트 주소.
    /// 다중 NIC 환경에서 제한 브로드캐스트가 한 NIC 로만 나가는 문제를 보완합니다.
    /// </summary>
    private IPAddress[] ResolveTargets()
    {
        if (DateTime.UtcNow - _targetsBuiltAt < TargetCacheLife && _targets.Length > 0)
            return _targets;

        var found = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                        continue;

                    byte[] ip = unicast.Address.GetAddressBytes();
                    byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                    var directed = new byte[4];
                    for (int i = 0; i < 4; i++)
                        directed[i] = (byte)(ip[i] | ~mask[i]);
                    found.Add(new IPAddress(directed));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // NIC 조회 실패 시 제한 브로드캐스트만 사용
        }

        _targets = found.ToArray();
        _targetsBuiltAt = DateTime.UtcNow;
        return _targets;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();

        var udp = _udp;
        if (udp is null) return;
        try
        {
            // 종료 알림(BYE)은 동기로 보내 프로세스 종료 전에 나가도록 함
            byte[] bye = ComposePacket(BeaconPacket.KindBye).ToBytes();
            foreach (var target in ResolveTargets())
            {
                try { udp.Send(bye, bye.Length, new IPEndPoint(target, _port)); } catch { /* 무시 */ }
            }
        }
        finally
        {
            udp.Dispose();
        }
    }
}
