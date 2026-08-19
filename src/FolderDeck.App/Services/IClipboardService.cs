using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Services;

/// <summary>
/// 클립보드 쓰기. 테스트가 STA 스레드 없이 돌 수 있게 인터페이스로 뺀다.
/// </summary>
public interface IClipboardService
{
    /// <summary>성공하면 null, 실패하면 사용자에게 보일 메시지.</summary>
    string? SetText(string text);

    /// <summary>
    /// 고른 것을 <b>셸 파일 클립보드</b>에 올린다 (§4.6 310행 · A군 ③ C-1).
    /// </summary>
    /// <remarks>
    /// 계약은 <see cref="SetText"/> 와 같다 — 성공하면 null, 실패하면 사용자에게 보일 메시지.
    /// 싣는 것은 경로 <b>글자</b>가 아니라 파일 포맷이다(「경로 복사」와 다른 일이다).
    /// </remarks>
    string? SetFiles(IReadOnlyList<OperationItem> items);

    /// <summary>
    /// 지금 클립보드에 있는 데이터 개체. <b>비었으면 null</b> (§4.6 310행 · A군 ③ C-2).
    /// </summary>
    /// <remarks>
    /// 푸는 일은 여기서 하지 않는다 — 셸 드롭이 쓰는 파서(<see cref="DropRouter.ShellItems"/>)가
    /// 이 개체를 그대로 받는다. 클립보드도 드롭도 같은 <c>CF_HDROP</c> 이라 변환 계층이 없다(§4.9).
    /// </remarks>
    System.Windows.IDataObject? GetData();
}

/// <inheritdoc/>
public sealed class ClipboardService : IClipboardService
{
    public string? SetText(string text)
    {
        try
        {
            // 다른 프로세스가 클립보드를 잡고 있으면 실패할 수 있다(COM 경쟁).
            // 조용히 삼키지 않고 호출부가 알리게 한다.
            System.Windows.Clipboard.SetText(text);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 클립보드에 올릴 데이터 개체를 만든다 — <b>클립보드를 건드리지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 포맷을 싣는 일은 드래그 내보내기가 쓰는 <see cref="ShellExport.Attach"/> 그대로다
    /// (§4.9 · Stage 2) — 여기서 새로 정하는 것은 하나도 없다. <c>Preferred DropEffect</c> 가
    /// 복사로 못박히는 것도 그 함수의 값이다.
    /// <para>
    /// <b>클립보드에 올리는 줄과 갈라 둔 이유</b>: 진짜 클립보드는 STA 스레드를 요구해 테스트가
    /// 부를 수 없다. <b>무엇이 실리는가</b>는 여기서 확인하고, 올리는 줄만 손 확인으로 남는다.
    /// </para>
    /// </remarks>
    public static System.Windows.DataObject FileDataObject(IReadOnlyList<OperationItem> items)
    {
        var data = new System.Windows.DataObject();
        ShellExport.Attach(data, items);
        return data;
    }

    /// <inheritdoc/>
    public string? SetFiles(IReadOnlyList<OperationItem> items)
    {
        try
        {
            // OLE 클립보드는 포맷을 셸·타 앱 코드로 렌더링할 수 있다 — 셸 메뉴와 같은 위험이다(F-1).
            using var fpu = FolderDeck.App.Interop.FpuGuard.Enter("Clipboard.SetDataObject");

            // copy: true — 앱이 닫혀도 클립보드에 남는다(OLE 플러시). 탐색기의 복사와 같은 결이다.
            System.Windows.Clipboard.SetDataObject(FileDataObject(items), copy: true);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 읽기는 <see cref="SetText"/>·<see cref="SetFiles"/> 와 달리 <b>메시지를 돌려줄 자리가
    /// 없다</b> — 계약이 "비었으면 null" 하나다. 다른 프로세스가 클립보드를 잡고 있어 읽기가
    /// 실패해도 null 이 되고, 부르는 쪽은 "붙여넣을 파일이 없다"로 거절한다. <b>둘을 가리지
    /// 못한다는 것을 여기 적어 둔다.</b>
    /// </remarks>
    public System.Windows.IDataObject? GetData()
    {
        try
        {
            // 남이 올린 데이터 개체를 받는다 — 렌더링이 그쪽 코드다(F-1).
            using var fpu = FolderDeck.App.Interop.FpuGuard.Enter("Clipboard.GetDataObject");

            return System.Windows.Clipboard.GetDataObject();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
