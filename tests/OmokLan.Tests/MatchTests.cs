using System.Net;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;
using OmokLan.Engine.Play;
using Xunit;

namespace OmokLan.Tests;

public class MatchTests
{
    [Fact]
    public async Task TwoPlayers_CoinTossAndMoves_SyncProperly()
    {
        using var hostA = new DuelHost("p-A", "철수") { SelfAvatar = 0 };
        using var hostB = new DuelHost("p-B", "영희") { SelfAvatar = 1 };

        hostA.Start(basePort: 0);
        hostB.Start(basePort: 0);

        var targetB = new PeerInfo(hostB.SelfId, hostB.SelfNick, hostB.SelfAvatar, PresenceStatus.Available, IPAddress.Loopback, hostB.Port);

        var tcsSessionB = new TaskCompletionSource<DuelSession>();
        hostB.ChallengeReceived += async req =>
        {
            var session = await req.AcceptAsync();
            tcsSessionB.SetResult(session!);
        };

        var chalResult = await hostA.ChallengeAsync(targetB);
        Assert.Equal(ChallengeOutcome.Accepted, chalResult.Outcome);

        var sessionA = chalResult.Session!;
        var sessionB = await tcsSessionB.Task;

        using var matchA = new OmokMatch(sessionA, "철수", 0);
        using var matchB = new OmokMatch(sessionB, "영희", 1);

        var coinTossDoneA = new TaskCompletionSource<bool>();
        var coinTossDoneB = new TaskCompletionSource<bool>();

        matchA.CoinTossStarted += winner => coinTossDoneA.TrySetResult(true);
        matchB.CoinTossStarted += winner => coinTossDoneB.TrySetResult(true);

        matchB.Begin();
        matchA.Begin();

        await Task.WhenAll(coinTossDoneA.Task, coinTossDoneB.Task);

        // 양측의 색상은 서로 반대여야 함
        Assert.NotEqual(Stone.None, matchA.MyColor);
        Assert.NotEqual(Stone.None, matchB.MyColor);
        Assert.Equal(matchA.MyColor.Opponent(), matchB.MyColor);

        // 채팅 송수신 테스트
        var chatTcs = new TaskCompletionSource<ChatEntry>();
        matchB.ChatReceived += entry => chatTcs.TrySetResult(entry);

        await matchA.SendChatAsync("안녕 반가워!");
        var receivedChat = await chatTcs.Task;
        Assert.Equal("안녕 반가워!", receivedChat.Text);
        Assert.Equal("철수", receivedChat.Sender);
    }

    [Fact]
    public async Task Match_PauseAndResume_TogglesStateCorrectly()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clientTask = Task.Run(() => new System.Net.Sockets.TcpClient("127.0.0.1", port));
        using var serverClient = await listener.AcceptTcpClientAsync();
        using var client = await clientTask;
        listener.Stop();

        using var channel = new FramedChannel(client);
        using var session = new DuelSession(channel, true, "bot", "상대", 0);
        using var match = new OmokMatch(session, "나", 1);

        Assert.False(match.IsPaused);

        match.Pause();
        Assert.True(match.IsPaused);
        Assert.True(match.Snapshot().IsPaused);

        match.Resume();
        Assert.False(match.IsPaused);
        Assert.False(match.Snapshot().IsPaused);
    }
}
