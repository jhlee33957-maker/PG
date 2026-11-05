# 협업 규칙

## 1. 작업 흐름 (GitHub Flow)
```
Issue 확인/작성 → 브랜치 생성 → 작업 (AI와 함께) → PR → 상대 리뷰 → main에 merge → 브랜치 삭제
```

1. **모든 작업은 Issue에서 시작한다.** Issue가 없으면 템플릿으로 먼저 만든다. 자기에게 Assign하고 Projects 보드에서 `In Progress`로 옮긴다.
2. **main에서 새 브랜치를 만든다.**
   ```bash
   git switch main
   git pull
   git switch -c feat/3-player-move
   ```
3. **작게 커밋한다.** AI가 한 번에 많은 코드를 만들었어도, 의미 단위로 나눠 커밋한다.
4. **PR을 올린다.** PR 템플릿의 모든 칸을 채운다. 작업 중이라도 방향 확인이 필요하면 Draft PR로 일찍 올린다.
5. **상대가 리뷰하고 승인하면 merge한다.** merge는 PR 작성자가 한다 (Squash merge 권장).

## 2. 이름 규칙

### 브랜치
```
<type>/<이슈번호>-<짧은-설명>
예: feat/3-player-move, rule/14-jump-height, fix/21-respawn-bug, docs/2-rule-interface
```

### 커밋 메시지
```
<type>: <요약> (#이슈번호)
```
| type | 용도 |
|---|---|
| feat | 기능 추가 |
| fix | 버그 수정 |
| refactor | 동작 변경 없는 구조 개선 |
| rule | 규칙(RuleEffect) 추가/수정 |
| stage | 스테이지 데이터/레벨 |
| docs | 문서 |
| chore | 설정, 패키지 등 |

## 3. 리뷰 규칙
- **24시간 안에 리뷰한다.** 막히는 시간이 가장 비싸다.
- "LGTM"만 달고 승인하지 않는다. 최소 하나는 확인한 것을 적는다. (예: "테스트 절차대로 해 봤고 R1 세 가지 다 동작 확인")
- 코멘트 앞에 강도를 붙인다.
  - `[필수]` 고쳐야 merge 가능
  - `[제안]` 고치면 좋지만 작성자가 판단
  - `[질문]` 이해가 안 돼서 묻는 것
- AI가 쓴 코드도 **사람이 쓴 코드와 똑같이** 리뷰한다. "AI가 짰으니 맞겠지"는 금지.
- 리뷰어는 가능하면 **브랜치를 받아 Unity에서 직접 플레이**해 본다.

## 4. Unity 협업 규칙 (충돌 방지)
Unity의 씬과 프리팹은 두 사람이 동시에 고치면 merge하기가 매우 어렵다. 그래서 **파일마다 주인을 정한다.**

- **씬은 주인이 한 명이다.** 각자 작업용 테스트 씬을 따로 가진다. (`Scenes/Sandbox_<이름>.unity`)
- 공용 씬(메인, 스테이지)을 고쳐야 하면 **고치기 전에 메신저로 말한다.** ("Stage1 씬 지금 잡을게요")
- 레벨은 가능하면 **프리팹 단위로** 만든다. 씬에는 프리팹을 배치만 한다.
- 새 에셋을 추가하면 **`.meta` 파일을 반드시 같이 커밋**한다. 빠지면 상대 쪽에서 참조가 깨진다.
- Unity 버전은 둘이 같아야 한다. 버전을 올릴 때는 decisions 문서로 합의한다.

### (선택) Smart Merge 설정
씬/프리팹 충돌을 Unity 전용 도구로 merge하게 해 준다. 각자 한 번씩 로컬에서 설정한다.
경로의 `<버전>`은 설치한 Unity 버전으로 바꾼다.

Windows:
```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/<버전>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -h -p --force %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```
Mac:
```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/<버전>/Unity.app/Contents/Tools/UnityYAMLMerge' merge -h -p --force %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

## 5. AI 활용 규칙
- AI 도구는 자유롭게 쓴다. 대신 **무엇을 맡겼고 무엇을 직접 판단했는지** PR에 남긴다.
- 공용 규칙은 [`AGENTS.md`](AGENTS.md) 한 곳에서 관리한다. 개인 프롬프트로 같은 설명을 반복하고 있다면 AGENTS.md로 옮긴다.
- **AI가 같은 실수를 두 번 하면 AGENTS.md에 규칙을 추가한다.** 맨 아래 변경 기록에 이유를 남긴다. AGENTS.md 변경도 PR로 올린다.
- 기록할 만한 시행착오는 [`docs/ai-log/`](docs/ai-log/)에 남긴다. 한 사람당 주 1개 이상.
- 재미, 난이도, 설계 결정은 AI의 제안을 참고하되 **사람이 정하고 이유를 적는다.**
- AI가 만든 코드를 이해하지 못한 채로 merge하지 않는다. 리뷰에서 설명을 요구받으면 답할 수 있어야 한다.

## 6. 소통
- **주 1회 싱크** (30분): 지난주 merge된 것, 이번 주 할 Issue, 막힌 것, AGENTS.md에 추가할 규칙
- 작업 이야기는 가능하면 **Issue/PR 코멘트에** 남긴다. 메신저에서 정한 내용도 결정이면 Issue에 한 줄 옮긴다. (기록이 남아야 나중에 보여 줄 수 있다)
- 막히면 30분 이상 혼자 붙잡지 말고 Issue에 상황을 적고 상대를 멘션한다.

## 7. 완료의 정의 (Definition of Done)
Issue를 닫으려면:
- [ ] 완료 기준을 전부 체크했다
- [ ] PR이 리뷰 승인 후 merge됐다
- [ ] Unity에서 플레이해서 동작을 확인했다
- [ ] 콘솔에 새 에러가 없다
