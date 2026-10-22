using System.Net;
using OmokLan.Engine.Net;
using Xunit;

namespace OmokLan.Tests;

public class DuelIntegrationTests
{
    [Fact]
    public async Task ChallengeAndAccept_EstablishesSessions()
    {
        using var hostA = new DuelHost("player-A", "철수") { SelfAvatar = 0 };
        using var hostB = new DuelHost("player-B", "영희") { SelfAvatar = 1 };

        hostA.Start(basePort: 0); // OS 자동 배정 포트
        hostB.Start(basePort: 0);

        var targetB = new PeerInfo(hostB.SelfId, hostB.SelfNick, hostB.SelfAvatar, PresenceStatus.Available, IPAddress.Loopback, hostB.Port);

        // B 측에서 신청을 받으면 즉시 수락하도록 이벤트 등록
        hostB.ChallengeReceived += async req =>
        {
            var acceptedSession = await req.AcceptAsync();
            Assert.NotNull(acceptedSession);
            Assert.Equal("player-A", acceptedSession.OpponentId);
            Assert.False(acceptedSession.IsChallenger);
        };

        // A 가 B 에게 대결 신청
        var result = await hostA.ChallengeAsync(targetB);

        Assert.Equal(ChallengeOutcome.Accepted, result.Outcome);
        Assert.NotNull(result.Session);
        Assert.True(result.Session.IsChallenger);
        Assert.Equal("player-B", result.Session.OpponentId);

        result.Session.Dispose();
    }

    [Fact]
    public async Task ChallengeToDndHost_AutomaticallyRefused()
    {
        using var hostA = new DuelHost("player-A", "철수");
        using var hostB = new DuelHost("player-B", "영희") { DoNotDisturb = true };

        hostA.Start(basePort: 0);
        hostB.Start(basePort: 0);

        var targetB = new PeerInfo(hostB.SelfId, hostB.SelfNick, hostB.SelfAvatar, PresenceStatus.Dnd, IPAddress.Loopback, hostB.Port);

        var result = await hostA.ChallengeAsync(targetB);
        Assert.Equal(ChallengeOutcome.DoNotDisturb, result.Outcome);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task ChallengeToBusyHost_AutomaticallyRefused()
    {
        using var hostA = new DuelHost("player-A", "철수");
        using var hostB = new DuelHost("player-B", "영희") { Engaged = true }; // 게임 중

        hostA.Start(basePort: 0);
        hostB.Start(basePort: 0);

        var targetB = new PeerInfo(hostB.SelfId, hostB.SelfNick, hostB.SelfAvatar, PresenceStatus.InGame, IPAddress.Loopback, hostB.Port);

        var result = await hostA.ChallengeAsync(targetB);
        Assert.Equal(ChallengeOutcome.Busy, result.Outcome);
        Assert.Null(result.Session);
    }
}
