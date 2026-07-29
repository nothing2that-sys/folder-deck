using System.Threading;

namespace FolderDeck.App.Services;

/// <summary>
/// 이름 붙은 뮤텍스로 "이 워크스페이스(또는 런처)는 내가 들고 있다"를 표시한다 (§6.2).
/// 잡히면 이 객체, 이미 살아 있는 주인이 있으면 null.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>createdNew</c> 단독으로 판정하지 않는다.</b> Stage 0 실측: 같은 이름의 핸들을 다른
/// 프로세스가 쥐고 있으면 주인을 강제 종료해도 객체가 남아 <c>createdNew=False</c> 가 되는데,
/// 그때 진짜 상태는 <see cref="Mutex.WaitOne(int)"/> 가 던지는
/// <see cref="AbandonedMutexException"/> 에 드러난다. 그 예외는 "앞 주인이 정리 없이 죽었고
/// 소유권이 나에게 넘어왔다"는 뜻이라 <b>성공 경로</b>다.
/// </para>
/// <para>
/// 뮤텍스 소유권은 스레드에 묶인다. 잡은 스레드에서 놓아야 하므로 앱은 시작(<c>OnStartup</c>)과
/// 종료(<c>OnExit</c>) 모두 UI 스레드에서 이 객체를 다룬다.
/// </para>
/// </remarks>
public sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    private InstanceLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        _owned = true;
        Name = name;
    }

    /// <summary>잡고 있는 이름 — 진단용.</summary>
    public string Name { get; }

    /// <summary>
    /// 이름을 잡아 본다. 성공하면 잠금 객체, 이미 살아 있는 주인이 있으면 null.
    /// </summary>
    public static InstanceLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // initiallyOwned: false + WaitOne 이라야 버려진 뮤텍스를 예외로 알아챌 수 있다.
        var mutex = new Mutex(initiallyOwned: false, name);

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // 앞 주인이 정리 없이 죽었다 = 소유권이 나에게 넘어왔다 = 내가 첫 인스턴스다.
            acquired = true;
        }

        if (acquired)
        {
            return new InstanceLock(mutex, name);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_owned)
        {
            _owned = false;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 잡은 스레드가 아닌 곳에서 놓으려 한 경우. 프로세스가 끝나면 커널이 정리하므로
                // 여기서 더 할 일은 없다 — 종료 경로에서 예외를 올리지 않는 것이 중요하다.
            }
        }

        _mutex.Dispose();
    }
}
