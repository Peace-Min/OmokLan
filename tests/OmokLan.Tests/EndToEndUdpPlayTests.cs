using System.Net;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;
using OmokLan.Engine.Play;
using Xunit;

namespace OmokLan.Tests;

public class EndToEndUdpPlayTests
{
    [Fact]
    public async Task FullUdpDiscovery_And_MatchPlay_To_Win()
    {
        string idA = "p-A-" + Guid.NewGuid().ToString("N")[..6];
        string idB = "p-B-" + Guid.NewGuid().ToString("N")[..6];

        // 1. A와 B의 TCP 호스트 시작
        using var hostA = new DuelHost(idA, "플레이어A") { SelfAvatar = 0 };
        using var hostB = new DuelHost(idB, "플레이어B") { SelfAvatar = 1 };
        hostA.Start(basePort: 0);
        hostB.Start(basePort: 0);

        // 2. A와 B의 UDP 비컨 시작
        using var beaconA = new LanBeacon(idA, () => ("플레이어A", 0, PresenceStatus.Available, hostA.Port));
        using var beaconB = new LanBeacon(idB, () => ("플레이어B", 1, PresenceStatus.Available, hostB.Port));

        var bDiscoveredTcs = new TaskCompletionSource<PeerInfo>();
        beaconA.PeersChanged += () =>
        {
            var found = beaconA.Directory.Find(idB);
            if (found is not null) bDiscoveredTcs.TrySetResult(found);
        };

        beaconA.Start();
        beaconB.Start();

        // 즉시 공지
        beaconB.AnnounceNow();

        // UDP 브로드캐스트를 통해 A가 B를 발견할 때까지 대기 (최대 3초)
        var completed = await Task.WhenAny(bDiscoveredTcs.Task, Task.Delay(3000));
        Assert.True(completed == bDiscoveredTcs.Task, "UDP 브로드캐스트를 통해 상대방을 발견하지 못했습니다.");

        var peerB = await bDiscoveredTcs.Task;
        Assert.Equal("플레이어B", peerB.Nick);
        Assert.Equal(hostB.Port, peerB.TcpPort);

        // 3. A가 B에게 대결 신청 및 B의 자동 수락
        var sessionBTcs = new TaskCompletionSource<DuelSession>();
        hostB.ChallengeReceived += async req =>
        {
            var session = await req.AcceptAsync();
            sessionBTcs.SetResult(session!);
        };

        var chalResult = await hostA.ChallengeAsync(peerB);
        Assert.Equal(ChallengeOutcome.Accepted, chalResult.Outcome);

        var sessionA = chalResult.Session!;
        var sessionB = await sessionBTcs.Task;

        // 4. 대국 컨트롤러 생성 및 게임 시작
        using var matchA = new OmokMatch(sessionA, "플레이어A", 0);
        using var matchB = new OmokMatch(sessionB, "플레이어B", 1);

        var tossDoneA = new TaskCompletionSource<bool>();
        var tossDoneB = new TaskCompletionSource<bool>();
        matchA.CoinTossStarted += _ => tossDoneA.TrySetResult(true);
        matchB.CoinTossStarted += _ => tossDoneB.TrySetResult(true);

        matchB.Begin();
        matchA.Begin();

        await Task.WhenAll(tossDoneA.Task, tossDoneB.Task);

        // 코인 토스 애니메이션 기간(약 3.5초) 대기
        await Task.Delay(3600);

        // 5. 실제 5목 착수 플레이 (선공인 쪽이 가로 5목 완성하여 승리)
        var blackMatch = matchA.MyColor == Stone.Black ? matchA : matchB;
        var whiteMatch = matchA.MyColor == Stone.White ? matchA : matchB;

        var winTcs = new TaskCompletionSource<MatchResult>();
        blackMatch.Finished += res => winTcs.TrySetResult(res);
        whiteMatch.Finished += res => winTcs.TrySetResult(res);

        for (int i = 0; i < 4; i++)
        {
            Assert.True(await blackMatch.PlaceAsync(i, 7));
            await Task.Delay(50); // 패킷 송수신 동기화 딜레이
            Assert.True(await whiteMatch.PlaceAsync(i, 8));
            await Task.Delay(50);
        }

        // 흑의 마지막 5번째 수 -> 5목 완성으로 승리!
        Assert.True(await blackMatch.PlaceAsync(4, 7));

        var result = await winTcs.Task;
        Assert.Equal(Stone.Black, result.Winner);
        Assert.Equal(MatchEndReason.FiveInRow, result.Reason);
    }
}
