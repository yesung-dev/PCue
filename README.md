# PCue (피큐)

현장용 음원·영상 재생기입니다. 관제 창은 주 모니터에 두고, 영상만 선택한 디스플레이로 보냅니다.

Windows 10/11 x64 · WPF · LibVLCSharp · Velopack

## 기능

- 재생 목록: 파일 추가, 드래그 앤 드롭, 번호·길이, 위아래 정렬, Delete로 제거
- 자동 다음 재생 ON/OFF
- 관제 UI / 영상 출력(선택 디스플레이) 분리
- 재생·일시정지·정지·이전·다음·시크·볼륨·음소거
- 설치본에서 GitHub Releases 기반 업데이트 안내

## 단축키

| 키 | 동작 |
| --- | --- |
| `Space` | 일시정지 |
| `Enter` | 선택 항목 재생 |
| `↑` / `↓` | 목록 선택 이동 |
| `←` / `→` | 시크 |
| `Shift` + `←` / `→` | 이전 / 다음 |
| `Delete` | 선택 항목 제거 |
| `Esc` | 출력 창에서 영상 출력 끄기 |

## 개발 실행

```bash
cd PCue
dotnet run
```

## 폴더 배포

```bash
cd PCue
dotnet publish -p:PublishProfile=FolderProfile
```

출력: `bin/Release/net10.0-windows/win-x64/publish/`

폴더 전체를 복사해 `PCue.exe`를 실행합니다. LibVLC가 포함되어 있습니다.  
`dotnet run` / 미설치 빌드에서는 업데이트 UI가 뜨지 않습니다.

## 설치본 · 릴리스

- 랜딩: [`PCue-web/`](PCue-web/) (GitHub Pages)
- 설치 프로그램: `PCue.Installer/` → `PCue-Setup.exe`
- 태그 `v*` 푸시 시 Release 워크플로가 Velopack 패키지와 `PCue-Setup.exe`를 업로드합니다
- 다운로드: https://github.com/yesung-dev/PCue/releases

## 사용

1. 파일을 추가하거나 재생 목록에 끌어다 놓습니다.
2. 출력 디스플레이를 고른 뒤 **영상 출력**을 켭니다.
3. 영상은 선택한 디스플레이에만 전체화면으로 나갑니다. 관제 창에는 영상이 뜨지 않습니다.
4. **자동 다음**이 켜져 있으면 한 항목이 끝나면 다음을 재생합니다.

## 요구 사항

- Windows 10/11 x64
- 배포본은 self-contained라 .NET 런타임 별도 설치가 필요 없습니다.
