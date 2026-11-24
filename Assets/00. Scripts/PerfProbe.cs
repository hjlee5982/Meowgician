using System;
using System.Diagnostics;

/// <summary>
/// 개발 빌드/에디터 전용 구간 시간 측정. 릴리스 빌드에서는 본문이 비어 있어 비용이 없다.
/// 사용: using var _ = PerfProbe.Measure(PerfProbe.Id.ProjectileUpdate);
/// </summary>
public static class PerfProbe
{
    public enum Id
    {
        ProjectileUpdate,
        ProjectileTrigger,
        ShooterUpdate,
        ShooterTryFire,
        MonsterUpdate,
        MonsterTakeDamage,
        SpriteShadowLate,
        SpawnerUpdate,
        MonsterHpGauge,
        MonsterHitFlash,
        MonsterDeath,
        MonsterOnDestroy,
        FlashFastPath,
        FlashStart,
        FlashEnsureMat,
        FlashSwapMat,
        FlashSetColor,
        FlashSetAmount,
        Count
    }

    public readonly struct Scope : IDisposable
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly long start;
        private readonly int id;

        public Scope(int id)
        {
            this.id = id;
            start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            ticks[id] += Stopwatch.GetTimestamp() - start;
            counts[id]++;
        }
#else
        public void Dispose() { }
#endif
    }

    public static Scope Measure(Id id)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return new Scope((int)id);
#else
        return default;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly long[] ticks = new long[(int)Id.Count];
    private static readonly int[] counts = new int[(int)Id.Count];

    /// <summary>누적 값을 ms / 호출 수로 읽고 0으로 비운다.</summary>
    public static void Drain(int id, out double ms, out int count)
    {
        ms = ticks[id] * 1000.0 / Stopwatch.Frequency;
        count = counts[id];
        ticks[id] = 0;
        counts[id] = 0;
    }
#endif
}
