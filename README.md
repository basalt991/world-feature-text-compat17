# World Feature Text Compatibility 1.7

RimWorld **1.7.4976 rev1043**에서 한글 월드 지형 이름을 생성할 때 발생하는 `WorldFeatureTextMesh_Legacy.Init()` 예외를 우회하는 독립 모드입니다. Harmony가 필요합니다. Windows에서 독립 엔진 검사를 완료했고 **macOS용 처리도 추가했지만 실제 Mac 실행은 아직 검증하지 않았습니다.**

**설치 파일: [GitHub Releases](https://github.com/basalt991/world-feature-text-compat17/releases)** — v0.2.0부터 Windows/macOS가 같은 ZIP과 모드 DLL을 사용합니다. 시험판이며 실제 플레이 화면 검증은 아직 남아 있습니다.

English: RimWorld 1.7 compatibility patch for Korean world feature names. Uses an installed Korean font and the native TextMeshPro renderer to avoid the broken legacy TextMesh path. Windows uses Malgun Gothic; experimental macOS handling uses Apple SD Gothic Neo or AppleGothic. Requires Harmony; Readable Fonts 1.7 is optional. Game binaries, Harmony and font files are not bundled. Windows isolated engine checks passed; actual macOS execution and in-game visual verification are pending.

## 설치

압축파일 안의 `World Feature Text Compatibility 1.7` 폴더를 림월드의 `Mods` 폴더에 넣고, 모드 목록에서 Harmony 다음에 활성화합니다. 언어를 한국어로 설정한 뒤 게임을 재시작하고 월드를 다시 불러오거나 생성합니다.

설치 후 `RimWorld/Mods/World Feature Text Compatibility 1.7/About/About.xml` 경로가 존재해야 합니다.

`Readable Fonts 1.7`과 함께 사용할 수 있으며 해당 모드는 필수 의존성이 아닙니다. 일반 UI 폰트는 바꾸지 않고 월드 지형 이름에만 다음 글꼴을 사용합니다. 글꼴 파일은 배포하지 않습니다.

| 플랫폼 | 월드 이름 글꼴 | 검증 상태 |
| --- | --- | --- |
| Windows | 설치된 맑은 고딕(`malgun.ttf`) | 실제 Windows 게임 어셈블리/엔진으로 독립 검증 |
| macOS | Apple SD Gothic Neo, 이후 AppleGothic / Arial Unicode 순으로 탐색 | 코드와 모의 경로 탐색 검증, Mac 실행 미검증 |

Mac에서는 게임의 엔진 코드에도 명시된 `/System/Library/Fonts/AppleSDGothicNeo.ttc`를 먼저 확인합니다. 없거나 필요한 글자가 빠지면 `/System/Library/Fonts/Supplemental/AppleGothic.ttf`, `/System/Library/Fonts/AppleGothic.ttf`, `/System/Library/Fonts/Supplemental/Arial Unicode.ttf` 순으로 확인합니다. TTC 모음에서는 필요한 글자가 있는 Regular(굵기 400)에 가까운 글꼴을 골라 독립 SFNT 데이터로 추출합니다. 첫 번째 글꼴이 가장 가는 굵기이거나 한글을 지원하지 않아도 첫 번째를 무조건 선택하지 않습니다.

Apple SD Gothic Neo와 AppleGothic은 [Apple의 macOS 글꼴 목록](https://support.apple.com/en-us/120414)에 포함되어 있습니다. 글꼴이 없으면 서체 관리자에서 설치 상태를 확인하세요. Mac에서도 설치 방법은 같습니다. 게임의 로컬 파일 위치에서 `Mods` 폴더에 모드 폴더를 넣고 Harmony 다음에 활성화합니다. 이 환경에는 Mac 게임 파일이나 실행 장치가 없으므로 macOS 버전별 실제 설치 경로와 Apple Silicon/Intel에서의 실행은 확인하지 못했습니다.

## 동작

현재 엔진의 TextMeshPro 글자 준비 목록은 라틴 문자 범위에 한정되어 있습니다. 한글 이름은 지원하지 않는 문자로 판정되어 구형 TextMesh 경로에 들어가고, 해당 경로에서 MeshRenderer를 참조하다 실패합니다. 텍스트 생성이 중단되면 다음 Update에서 목록 개수가 맞지 않아 추가 예외가 발생할 수 있습니다.

이 모드는 비영어 환경에서 한글이 포함된 월드 이름이 있을 때만 작동합니다.

- 월드 이름에 실제로 쓰인 문자를 별도 한글 글꼴 슬롯에 준비합니다. 한글 전체를 매번 생성하지 않습니다.
- 글꼴 이미지와 문자 좌표를 엔진의 TextMeshPro에 연결하고, 구형 TextMesh 대신 기존 TextMeshPro 이름 객체를 생성합니다.
- 각 이름 객체의 글꼴과 월드 표시용 렌더 순서(3610)를 설정합니다.
- 지형 이름, 월드 데이터, 언어 설정은 바꾸지 않습니다. 위치, 줄바꿈, 구면 배치, 크기와 표시 여부 계산에는 원래 게임 코드를 사용합니다.
- 이름 생성 중에만 공유 템플릿을 바꾸며, 다른 예외가 발생해도 원래 글꼴과 구형 렌더링 플래그를 복구합니다. 관련 없는 예외를 숨기지 않습니다.
- 영어 환경은 원래 월드 글꼴을 사용합니다. 일반 UI 글꼴은 이 모드의 적용 대상이 아닙니다.

## 검증

빌드 오류 0개, 경고 0개. 배포 DLL을 Windows 게임에 포함된 실제 **Waylith CLR 10.0.10**, Harmony 1.7, FreeType, ImGui/TextMeshPro와 게임 어셈블리로 불러온 독립 프로세스에서 **54개 검사**를 통과했습니다. 게임의 Windows 참조 어셈블리가 일반 .NET API에도 Windows 속성을 부여하므로 빌드 시 CA1416 경고를 제외합니다. 배포 DLL에는 플랫폼 전용 속성, P/Invoke 또는 Windows 네이티브 글꼴 API가 없으며 각 플랫폼의 런타임은 게임이 제공합니다.

검사는 `푸른 산맥`, `붉은 강`, `ABC 123 해안`, `오래된\n숲`, `끝없는 협곡`을 사용해 실제 게임의 `CreateOrDestroyTexts()` 실행, 정상 TMP 객체 수, 한글 글자의 메시 생성, 글꼴 이미지 연결, 렌더 순서, 새 글자와 추가 글꼴 등록 후 재생성, 영어 전환, 실제 예외 발생 후 템플릿 복구를 확인합니다.

추가 검사는 Windows 기본 글꼴의 한글 완성형 11,172자, Mac 기본/대체 경로, 글꼴 누락/손상/글자 누락 처리, 한글을 지원하지 않는 첫 글꼴이 있는 TTC의 선택, 추출된 글꼴의 문자 매핑·체크섬과 실제 Waylith/FreeType 등록을 확인합니다. Mac 경로 검사에는 Windows 글꼴 데이터를 대입했고 실제 TTC 검사는 Windows의 바탕 글꼴 모음을 사용했습니다. **Apple 글꼴 파일 자체를 시험한 것은 아닙니다.**

기록: `Validation/engine_probe.log`, `Validation/qa_report.json`. 검증 프로그램의 월드 이름 템플릿과 복제 팩토리는 독립 프로세스에서 구성했습니다. **실제 macOS 엔진 실행, 저장 파일을 로드한 월드 화면, 줌/회전, 지형 위 배치와 표시 품질은 아직 검증하지 않았습니다.**

## 빌드와 재검증

`build.ps1`로 공통 모드 DLL을 빌드합니다. `Validation/run.ps1`은 Windows 전용 검증 실행기이며 검증 DLL 빌드 성공과 어셈블리 이름을 확인한 뒤, 작업 폴더의 임시 런타임 복사본에서 창을 숨긴 독립 검사를 실행합니다. 설치된 게임 파일을 덮어쓰지 않습니다. 게임 DLL, 런타임, Harmony DLL과 폰트는 배포 압축파일에 포함되지 않습니다.

기본 게임 경로는 `G:\Steam\steamapps\common\RimWorld`입니다. 다른 경로에서는 `build.ps1` 또는 `Validation/run.ps1`에 `-RimWorldDir`와 `-HarmonyPath`를 지정하세요. .NET 9 SDK 컴파일러로 빌드하고 게임에 포함된 .NET 10 어셈블리를 참조합니다.

이 모드는 1.7의 내부 글꼴 API를 사용하므로 게임 업데이트로 API가 변경되면 재검증이 필요합니다. 한국어 외 언어 전체를 지원하는 범용 문자 패치는 아닙니다.
