using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;
using FolderDeck.Core.Storage;

namespace FolderDeck.App.Services;

/// <summary>
/// 1회용 창을 여닫는 자리 (2026-10-14 2차 A2). 상주 프로그램은 한 프로세스에 1회용 창을 여럿 연다 —
/// <c>App.MainWindow</c> 같은 「프로세스에 창 하나」 전제를 건드리지 않고 여기서 세고 닫는다.
/// </summary>
/// <remarks>
/// 창을 실제로 만드는 일(뷰모델 배선)은 <paramref name="createWindow"/> 가 한다 — <c>App</c> 이 넘기고, 시험은
/// 가짜 의존성으로 만든 것을 넘긴다. 이 클래스가 하는 일은 작업 관리를 메모리에서 만들고, 저장을 막는 래퍼로
/// 감싸고, 열린 창을 세는 것뿐이다.
/// </remarks>
public sealed class ScratchWindowHost(
    IWorkspaceStore store,
    Func<LayoutPreset> layout,
    Func<Workspace, IWorkspaceStore, Window> createWindow)
{
    private readonly List<Window> _open = [];

    /// <summary>지금 열려 있는 1회용 창. 데우기 창(<see cref="WarmUp"/>)은 세지 않는다.</summary>
    public IReadOnlyList<Window> OpenWindows => _open;

    /// <summary>열린 1회용 창이 하나도 안 남게 됐다 — 상주가 「창이 다 닫히면 끝내기」에 쓴다.</summary>
    public event EventHandler? LastWindowClosed;

    /// <summary>새 1회용 창을 열고 앞으로 가져온다.</summary>
    public Window Open()
    {
        var window = Create();
        _open.Add(window);
        window.Closed += (_, _) =>
        {
            _open.Remove(window);
            if (_open.Count == 0)
            {
                LastWindowClosed?.Invoke(this, EventArgs.Empty);
            }
        };

        window.Show();
        WindowActivation.BringToFront(window);
        return window;
    }

    /// <summary>
    /// 데우기 (2026-10-14 2차 · JH 결정). 상주가 시작할 때 1회용 창 하나를 <b>화면 밖에서, 작업 표시줄에 안 보이게,
    /// 포커스를 안 뺏고</b> 만들어 첫 렌더까지 돌린 뒤 닫는다. 실측(보고문 A5)으로 프로세스의 첫 창만 600 ms 쯤 더
    /// 걸렸다 — 그 비용(형식 적재 · JIT · 리소스 해석)을 사용자가 단축키를 누르기 전에 치른다.
    /// <see cref="OpenWindows"/> 에 들어가지 않고 <see cref="LastWindowClosed"/> 도 쏘지 않는다.
    /// </summary>
    /// <param name="done">창이 닫힌 뒤 부른다(시험·측정용). 없어도 된다.</param>
    public void WarmUp(Action? done = null)
    {
        var window = Create();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(window.Close);
        window.Closed += (_, _) => done?.Invoke();
        window.Show();
    }

    /// <summary>열린 1회용 창을 모두 닫는다(저장할 것이 없다).</summary>
    public void CloseAll()
    {
        foreach (var window in _open.ToList())
        {
            window.Close();
        }
    }

    private Window Create()
    {
        var workspace = new Workspace
        {
            Title = "1회용",
            Folders = [],
            Tiles = LayoutPresets.Build(layout()),
        };

        return createWindow(workspace, new ScratchWorkspaceStore(store, workspace.Id));
    }
}
