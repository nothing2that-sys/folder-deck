using System.Text;

namespace FolderDeck.Core.Storage;

/// <summary>
/// 임시파일 → 원자적 교체 쓰기 (§5). 쓰는 중에 죽어도 기존 JSON 이 깨지지 않는다.
/// </summary>
/// <remarks>
/// 순서가 중요하다. (1) <c>{target}.tmp</c> 에 전부 쓰고 (2) 디스크까지 flush 한 뒤
/// (3) 이름을 바꿔 교체한다. 교체는 같은 볼륨 안의 rename 이라 NTFS 에서 원자적이다.
/// 어느 지점에서 죽어도 <paramref name="targetPath"/> 는 이전 내용 그대로거나 새 내용
/// 그대로이고, 절반만 쓰인 상태가 되지 않는다. 교체 전에 죽으면 <c>.tmp</c> 가 남지만
/// 다음 쓰기가 덮어쓴다(<see cref="FileMode.Create"/>).
/// </remarks>
public static class AtomicFile
{
    public const string TempSuffix = ".tmp";

    public static void WriteAllText(string targetPath, string contents)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = targetPath + TempSuffix;

        // WriteThrough + Flush(true): OS 캐시에만 있는 상태로 rename 되지 않게 한다.
        using (var stream = new FileStream(
                   tempPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.WriteThrough))
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        // 대상이 있든 없든 한 번의 호출로 교체된다(MoveFileEx + MOVEFILE_REPLACE_EXISTING).
        File.Move(tempPath, targetPath, overwrite: true);
    }
}
