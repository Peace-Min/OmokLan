using System.Windows.Media;

namespace OmokLan.Services;

public sealed record AvatarInfo(int Id, string Name, string Symbol, string GradientStart, string GradientEnd);

public static class AvatarCatalog
{
    public static readonly IReadOnlyList<AvatarInfo> Avatars = new[]
    {
        new AvatarInfo(0, "신비한 고양이", "🐱", "#8B5CF6", "#C084FC"),
        new AvatarInfo(1, "활기찬 강아지", "🐶", "#F59E0B", "#FCD34D"),
        new AvatarInfo(2, "영리한 붉은여우", "🦊", "#EF4444", "#F97316"),
        new AvatarInfo(3, "느긋한 대나무판다", "🐼", "#10B981", "#6EE7B7"),
        new AvatarInfo(4, "위풍당당 사자", "🦁", "#F59E0B", "#EF4444"),
        new AvatarInfo(5, "우주 비행사", "🚀", "#3B82F6", "#60A5FA"),
        new AvatarInfo(6, "오목 황제", "👑", "#FBBF24", "#F59E0B"),
        new AvatarInfo(7, "푸른 번개", "⚡", "#06B6D4", "#38BDF8"),
    };

    public static AvatarInfo Get(int id)
    {
        if (id >= 0 && id < Avatars.Count)
            return Avatars[id];
        return Avatars[0];
    }
}
