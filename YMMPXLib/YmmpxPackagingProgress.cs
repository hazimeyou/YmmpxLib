namespace YmmpxLib;

public sealed record YmmpxPackagingProgress(int CompletedCount, int TotalCount, string Message)
{
    public double Percentage => TotalCount <= 0 ? 0 : (double)CompletedCount / TotalCount * 100;
}
