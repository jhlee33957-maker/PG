# 커피스펙 타이틀 연출 v2 (야근 방 → 모니터 → 타이틀 화면)

## 1. 들어 있는 파일

| 종류 | 경로 |
|---|---|
| 연출 | `Assets/_Project/Scripts/UI/TitleIntroDirector.cs` |
| 타이틀 화면 (에디터 화면, 효과, 효과음) | `Assets/_Project/Scripts/UI/TitleScreenView.cs` |
| 메뉴 줄 (hover, 실행 연출) | `Assets/_Project/Scripts/UI/TitleMenuLine.cs` |
| 기획서 문장 / 빈칸 (지금 씬에서는 안 씀) | `Assets/_Project/Scripts/UI/TitleDocSentence.cs` |
| 에디터 메뉴: 마젠타 → 투명 | `Assets/_Project/Scripts/Editor/TitleMagentaConverter.cs` |
| 에디터 메뉴: 씬 자동 생성 | `Assets/_Project/Scripts/Editor/TitleSceneBuilder.cs` |
| 이미지 | `Assets/_Project/Sprite/Title/` (아래 표) |

기존 `TitleUI.cs`, `Popup.cs`, `SceneFader.cs`, `SaveData*.cs` 는 수정하지 않았습니다. 메뉴 동작은 TitleUI 가 그대로 담당합니다.

### 이미지 (전부 480x270 도트를 정수배 확대, 안티앨리어싱 없음, 글자 없음)

| 파일 | 크기 | 배경 | 내용 |
|---|---|---|---|
| `title_room.png` | 3840x2160 (8배) | 불투명 | 방 벽, 블라인드 틈의 아주 약한 밤빛, 선반 실루엣. 거의 검정 #0B0E14 |
| `title_desk.png` | 3840x2160 (8배) | 마젠타 | 살짝 앞에서 본 책상. 왼쪽 기획서·커피잔, 오른쪽 모니터·키보드·에너지드링크 캔·포스트잇. 모니터 화면은 마젠타로 비움 |
| `title_monitor_glow.png` | 1920x1080 | 이미 투명 | 모니터 주변의 부드러운 파란 빛 (#5A8BD6, 코드로 만든 원형 그라데이션, 점 없음) |
| `title_monitor_glow_soft.png` | 3840x2160 | 이미 투명 | (이제 쓰지 않음) 예전 계단형 빛 번짐 |
| `title_doc_closeup.png` | 1920x1080 (4배) | 불투명 | 얼룩진 기획서 한 장 (문장·얼룩 포함, 아래 2번) |
| `title_mug.png` (지금 씬에서는 안 씀) | 256x256 (4배) | 마젠타 | 흰 머그잔, 바닥에 커피 조금 |
| `title_drop_01~04.png` (안 씀) | 128x128 (4배) | 마젠타 | 커피 방울 #6B4226, 가장자리 1픽셀 더 진하게 |
| `title_drop_mark_01~04.png` (안 씀) | 128x128 (4배) | 마젠타 | 커피 방울 자국 |
| `title_fx_scanline.png`, `title_fx_vignette.png` | | | 씬 빌더가 처음 실행할 때 만드는 효과 텍스처 (`title_fx_screen_glow.png`는 이제 쓰지 않음) |

빛 번짐: 예전의 디더링 점 방식을 없애고, 알파가 부드럽게 줄어드는 PNG(`title_monitor_glow.png`)로 바꿨습니다. 깜빡일 때 모니터 주변을 뺀 방이 함께 어두워지도록 `title_room_dim.png`(가운데가 뚫린 검은 막, 1920x1080)를 추가했습니다.
- 진하기는 Director 의 `Glow Max Alpha`(기본 0.3), 방이 어두워지는 정도는 `Flicker Room Dim`(기본 0.45)으로 조절합니다.
- 두 이미지는 마젠타가 없으므로 변환할 필요가 없습니다.

## 2. 문구

### 기획서 클로즈업 (이미지에 직접 그려 둔 문장)
`title_doc_closeup.png` 한 장에 맨 위 제목 "기획서"와 문장, 얼룩이 같이 그려져 있습니다. 연출 중에 바뀌는 것은 없습니다 (TMP 문장, 커피잔, 방울, 자국 없음).
```
1. 게임 개요
  - 장르: 2D 퍼즐 플랫포머
  - 목표: 플레이어는 [적]을(를) 피해 [출구]에 도달한다
  - 핵심 규칙: [빈칸]은(는) [규칙]이다
```
- [ ] 안의 단어(적, 출구, 빈칸, 규칙)는 실제로 쓰여 있지만 얼룩 아래에서 잉크가 번져 읽을 수 없습니다.
- 얼룩: 커피잔 바닥 링 1개(출구), 번짐 2개(적·규칙 / 빈칸). 호박색 #C8893A, 안쪽 약 34%.
- 글꼴: Galmuri11 / Galmuri14 (SIL OFL 1.1). 이미지에 그려져 있어 Unity 에 폰트를 넣을 필요는 없습니다.
- 문장을 바꾸려면 이미지를 다시 만들어야 합니다.

### 타이틀 화면 (title.cs)
```
 1  // ==========================================
 2  // Coffee Spec                 ← 제목, 크게 (TitleScreenView > Game Title)
 3  // ==========================================
 4  // 마지막 수정: 새벽 2:07
 5  // TODO: 기획서에 쏟은 커피 수습하기
 6
 7  void Title()
 8  {
 9      ▶ StartGame();
10      SelectStage();              ← 기록 없으면  // SelectStage();  // 잠김  (회색, 클릭 안 됨)
11      OpenGuide();
12      Exit();
13  }
```
주석 1, 3, 4, 5번 줄은 TitleScreenView > Comment Texts 에서 바꿀 수 있습니다.

### 팝업 (에디터 경고창 / 서브 창 스타일, UI_RoundRect_Fill / Outline 9-slice 사용)
- `처음부터 다시?` : "⚠ 저장된 진행을 덮어쓰고 처음부터 시작합니다." [예] [아니오]
- `SelectStage.cs` : 스테이지 버튼 `LoadStage(1);` … (기존 TitleUI 의 스테이지 수만큼)
- `Guide.md` : 조작법. PlatformerPlayer.cs / SpecPanel.cs 에서 확인한 키 그대로입니다 (A·D/←→ 이동, Z 점프, X 공격, C 대시(누르고 있으면 달리기), R 기획서).

▶, ⚠, × 글자는 D2Coding, NotoSansKR 폰트 파일 둘 다에 들어 있는 것을 확인했고, 두 SDF 에셋이 Dynamic 모드라 실행 중 자동으로 추가됩니다.

## 3. 연출 순서와 Inspector 항목 (TitleCanvas > TitleIntroDirector)

모든 시간은 초, `Time.timeScale` 과 무관한 실제 시간입니다. 기본값 합계 약 7.5초.

| 컷 | 항목 | 기본값 | 설명 / 후보 |
|---|---|---|---|
| 재생 조건 | Play Intro | 켬 | 끄면 항상 최종 화면 |
| | Always Play In Editor | 끔 | 켜면 에디터에서 Title 에 들어올 때마다 재생 (테스트용) |
| 1) 모니터 켜짐 (1.5초) | Dark Hold | 0.3 | 완전한 어둠 유지 (예전 0.2의 1.5배) |
| | Monitor On Duration | 1.2 | 깜빡이며 켜지는 시간 (예전 0.8의 1.5배). 깜빡임 간격도 함께 1.5배로 늘어남 |
| | Monitor Flicker | 곡선 | 가로 0~1 진행, 세로 밝기. 들쭉날쭉할수록 깜빡임 |
| | Dark Overlay End | 0.15 | 다 켜진 뒤 남는 어둠. 후보 0.1(방이 더 보임) ~ 0.3(더 어둡게) |
| | Glow Max Alpha | 0.3 | 모니터 빛 세기. 후보 0.2(은은하게) ~ 0.45(강하게) |
| | Room Dim | L3_RoomDim | 모니터 주변만 뚫린 검은 막 (빌더가 연결) |
| | Flicker Count | 3 | 깜빡임 횟수. 0 이면 Monitor Flicker 곡선을 그대로 씀. 후보 2 ~ 4 |
| | Flicker Low | 0.15 | 깜빡일 때 모니터가 꺼지는 정도 (0 = 완전히 꺼짐) |
| | Flicker Room Dim | 0.45 | 모니터가 꺼질 때 방(모니터 주변 제외)이 어두워지는 정도. 후보 0.3 ~ 0.6 |
| | Bloom Strength | 0.22 | 모니터 화면 가운데에서 퍼지는 빛(ScreenBloom) 진하기. 후보 0.15 ~ 0.3. 0 = 끔 |
| | Bloom Size | 2.2 | 빛 크기 = 모니터 화면 x 이 값. 후보 1.6(화면 근처만) ~ 3(방까지 넓게) |
| | Bloom Color | #A9C6F5 | 화면 빛 색 |
| | Bloom Fade Start / End | 0.1 / 0.75 | 4) 모니터 줌인 구간(0~1) 중 화면 빛이 줄기 시작/다 사라지는 지점. 최종 타이틀 화면·건너뛰기·재진입 때는 빛 없음 |
| | Wide Shot | (0.5, 0.5), 1 | 1)에서 보이는 전체 방 |
| 2) 기획서 (약 3초) | Closeup Fade In | 0.25 | |
| | Closeup Hold | 2.0 | 얼룩진 기획서를 보여 주는 시간. 후보 1.5(빠르게) ~ 2.5(천천히 읽게) |
| | Mug Wobble Delay / Duration | 0.2 / 0.6 | 커피잔이 없어 그냥 대기 시간으로 쓰임 (합쳐서 0.8초). 줄이려면 둘 다 낮추기 |
| 3) 이동 (1.5초) | Closeup Fade Out | 0.25 | |
| | Pan Duration | 1.5 | |
| | Pan Start | (0.215, 0.30), 2.2 | 책상 그림 속 기획서 위치. 0~1, 왼쪽 아래 (0,0) |
| | Pan End | 줌 2.2 | Pan End At Monitor 가 켜져 있으면 위치는 모니터 가운데로 자동 |
| | Pan Ease | EaseInOut | |
| | Layers | 방 0.55 / 0.8 / 1.15, 책상 1/1/1, 빛 1/1/1 | Pan Factor·Zoom Factor·Overscale. 방을 느리게 움직여 깊이감. 방 Pan Factor 후보 0.4(깊이감 큼) ~ 0.7 |
| 4) 줌인 (1.0초) | Zoom Duration | 1.0 | |
| | Zoom End Scale | 0 (자동) | 0이면 모니터 화면이 화면에 꽉 차는 배율(지금 그림 기준 약 3.1)을 계산 |
| | Zoom Fill Ratio | 0.95 | 자동 배율일 때 채우는 비율 |
| | Screen Expand Portion | 0.3 | 줌 마지막 30% 동안 타이틀 화면이 모니터 크기 → 전체 화면으로 커지고 방은 사라짐. 0.2(빠르게) ~ 0.5(천천히) |
| 이미지 | Room/Desk/Glow/Closeup Sprite (Mug, Drop 항목은 비워 둠) | Sprite/Title 이미지 | 비우면 각 Image 에 들어 있는 것을 그대로 씀 |

- 연출 중 아무 키(키보드, 마우스 버튼, 게임패드 A/B/Start, 터치) → 바로 5)로.
- 앱을 켠 뒤 처음 Title 에 들어올 때만 재생 (static 플래그). 게임에서 돌아오면 바로 최종 화면.
- 연출 중에는 메뉴 CanvasGroup 이 꺼져 있어 클릭되지 않습니다.
- 타이틀 화면은 처음부터 모니터 화면 영역 안에 같은 오브젝트로 그려지고, 줌이 끝날 때 그 창이 전체 화면 크기로 커집니다. 다른 화면으로 바꿔 끼우는 순간이 없어서 끊기지 않습니다.

### TitleScreenView (ScreenViewport > TitleScreen)
| 항목 | 기본값 | 설명 |
|---|---|---|
| Game Title | Coffee Spec | 제목 |
| Title Prefix / Color | `// ` / #4EC9B0 | 제목 앞 주석 기호 |
| Comment Targets / Texts | 4줄 | 위 문구 |
| Menu Group | Menu | 연출 중 잠금 |
| Scanline Intensity | 0.06 | 후보 0.04(거의 안 보임) ~ 0.1. 0.15 넘으면 글자가 흐려짐 |
| Scanline Period | 4 | 줄 간격(캔버스 단위). 4 = 도트 1칸 |
| Glow Intensity / Color | 0.25 / 검정 | 화면 가장자리 비네트 (가운데 타원 없음, 부드러운 그라데이션). 0 = 끔. 후보 0.15 ~ 0.35 |
| Audio Source | TitleCanvas | |
| Sfx Monitor On / Coffee Drop / Keyboard / Menu Execute | 비어 있음 | 비어 있어도 동작. Coffee Drop 은 지금 쓰이지 않음. Keyboard 는 이동 시작과 메뉴 커서 이동 때 재생 |
| Sfx Volume | 0.8 | |

### TitleMenuLine (Menu > LineNN_BTN_xxx)
| 항목 | 기본값 | 설명 |
|---|---|---|
| Target Button | 자식 BTN_xxx | TitleUI 가 연결한 버튼. interactable 이 꺼지면 Locked Code 로 표시 |
| Code / Locked Code | `StartGame();` 등 / `// SelectStage();  // 잠김` | |
| Normal / Hover / Line Number / Locked | #D4D4D4 / #E8913B / #5C6370 / #5C6370 | hover 시 글자와 줄 번호가 주황 |
| Highlight Color | 흰색 6% | hover 줄 배경 |
| Execute Flash Color | 주황 22% | 실행 깜빡임 |
| Caret Move Duration | 0.12 | 커서가 줄 끝으로 가는 시간 |
| Blink Count / Interval | 2 / 0.07 | 실행 전 깜빡임. 합계 약 0.4초 뒤 기능 실행 |

## 4. Unity 에서 확인하는 순서
1. Unity 를 열고 컴파일 에러가 없는지 Console 확인.
2. Project 창에서 `Sprite/Title` 의 마젠타 이미지(`title_desk`, `title_mug`, `title_drop_*`, `title_drop_mark_*`)를 선택 → 메뉴 `CoffeeSpec > Title > 선택한 이미지 마젠타 → 투명 PNG`. (원본을 덮어씁니다. 마젠타 원본이 필요하면 먼저 복사해 두세요.)
   - 나머지(room, closeup, monitor_glow, room_dim)는 마젠타가 없어 그대로 두면 됩니다. 아래 3번이 임포트 설정(Sprite, Point, 압축 없음, 최대 4096)을 전부 맞춥니다.
3. 메뉴 `CoffeeSpec > Title > 타이틀 씬 자동 생성`. 마젠타가 남은 이미지가 있으면 바꿀지 묻습니다.
   - Title 씬이 열리고 `[TitleV2]` 가 만들어진 뒤 저장됩니다.
   - 기존 Title 오브젝트(TitleUI 가 있던 것)와 옛 EventSystem 은 지우지 않고 `(old)` 를 붙여 꺼 둡니다. 확인 후 직접 지우면 됩니다.
   - 기존 TitleUI 의 스테이지 씬 이름, Intro 씬 이름, 테스트용 가짜 세이브 값을 그대로 가져옵니다. 지금 Title 씬의 스테이지 5개는 씬 이름이 비어 있으니 `TitleCanvas > TitleUI > 스테이지 목록` 에서 입력하세요.
4. Play. 어둠 → 모니터 깜빡임 → 얼룩진 기획서 → 모니터로 이동·줌 → 타이틀 화면.
5. Play 중 아무 키 → 바로 타이틀 화면이 되는지.
6. 메뉴 hover(줄 배경, 주황 글자·줄 번호), 클릭(커서 이동 → 깜빡임 → 실행) 확인.
7. TitleUI 의 `Use Debug Save` 로 세 가지 상태 확인:
   - 기록 없음 → SelectStage 가 회색 주석 줄, 클릭 안 됨. StartGame → Intro.
   - 진행 중(Last Played 2) → StartGame → 스테이지 2.
   - 전부 클리어(Last Played 0, Highest Cleared 5) → StartGame → "처음부터 다시?" 팝업. 예 → Intro, 아니오 → 닫힘.
8. 다른 씬에서 Title 로 돌아왔을 때 연출 없이 최종 화면인지 (Build Profiles 에 Intro 와 스테이지 씬이 등록되어 있어야 이동됩니다. 지금은 Title 만 등록되어 있습니다).
9. 1920x1080 외 해상도(예: 16:10)에서 Game 뷰를 바꿔 가장자리 확인. 1) 컷 전체 화면은 16:9 기준이라 더 넓은 화면에서는 가장자리가 보일 수 있습니다. 그러면 방 Overscale 을 올리세요.

## 5. 알려진 제한
- Unity 에서 실제 실행은 해 보지 못했습니다. 스크립트는 이 프로젝트의 Unity 6000.6.8f1 / TextMeshPro / Input System DLL 을 참조해 컴파일 에러 0개, 경고 0개인 것만 확인했습니다.
- 이미지는 이미지 생성 후 480x270 격자로 줄이고 색을 12색 정도로 줄여 만든 것(책상, 머그)과 코드로 직접 찍은 것(방, 빛, 기획서, 방울, 자국)입니다.
- 책상 이미지를 바꾸면 모니터 화면 위치(`MonitorScreenRect` 의 Anchor)도 맞춰야 합니다. 지금 값: X 0.558~0.815, Y 0.485~0.796.

## 내가 직접 해야 하는 것
- [ ] 화풍 확정, 생성된 이미지 중 마음에 드는 것 고르기
- [ ] 폰트 고르기와 다운로드 (라이선스 확인)
- [ ] 효과음 고르기 (라이선스 확인)
- [ ] 게임 제목 확정
- [ ] Unity에서 에디터 메뉴 실행, Play 확인
- [ ] 연출 타이밍과 강도 조정
- [ ] 커밋, 푸시
