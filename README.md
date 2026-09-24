# PCue

현장용 음원/영상 재생기 (WPF + LibVLCSharp).

## 기능

- 재생 목록, 자동 다음 재생 ON/OFF
- 관제 UI(주 모니터) / 영상 출력(선택 디스플레이) 분리
- 재생·일시정지·정지·이전·다음·시크·볼륨

## 실행 (개발)

```bash
cd PCue/PCue
dotnet run
```

## 배포 (폴더)

```bash
cd PCue/PCue
dotnet publish -p:PublishProfile=FolderProfile
```

출력: `bin/Release/net10.0-windows/win-x64/publish/`

이 폴더 전체를 복사해 `PCue.exe`를 실행합니다. LibVLC 네이티브 라이브러리가 함께 포함됩니다.

## 사용 팁

1. **파일 추가**로 음원/영상 파일을 넣습니다.
2. 출력 디스플레이에서 **보조 모니터**를 고른 뒤 **영상 출력 ON**을 켭니다.
3. 영상은 선택한 디스플레이에만 전체화면으로 나갑니다. 관제 창에는 영상이 뜨지 않습니다.
4. **자동 다음 재생**이 켜져 있으면 한 항목이 끝나면 다음 항목을 재생합니다.
5. 출력 창에 포커스가 있을 때 `Esc`로 출력을 끌 수 있습니다.

## 요구 사항

- Windows 10/11 x64
- 배포본은 self-contained라 .NET 런타임 별도 설치가 필요 없습니다.
