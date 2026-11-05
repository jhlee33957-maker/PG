# AGENTS.md — AI 작업 지시문 (팀 공용)

> 이 파일은 이 저장소에서 일하는 모든 AI 도구(Claude Code, Cursor, Codex 등)가 읽는 공용 규칙이다.
> 사람이 매번 같은 설명을 반복하지 않도록, **AI가 실수할 때마다 여기에 규칙을 추가**하고 맨 아래 "변경 기록"에 이유를 남긴다.

## 1. 프로젝트 한 줄 요약
커피를 쏟아 일부가 지워진 기획서를 보고, 개발자(플레이어)가 빈칸을 제한된 선택지로 채운 뒤
플레이테스트해서 스테이지를 클리어하는 **Unity 2D 플랫포머**.
자세한 기획은 `docs/game-design.md`, 핵심 설계 결정은 `docs/decisions/`를 먼저 읽을 것.

## 2. 아키텍처 원칙 (반드시 지킬 것)
- **규칙은 코드가 아니라 데이터다.** 기획서 문장/빈칸/선택지는 `ScriptableObject`(StageSpec, Slot, RuleEffect)로 정의한다.
  새 규칙을 추가할 때 기존 클래스에 `if/switch` 분기를 늘리지 말고, 새 `RuleEffect` 하위 클래스를 만든다.
- **엔티티는 규칙을 모른다.** Player, Enemy, Platform은 바꿀 수 있는 값만 외부에 열어 두고, 그 값을 바꾸는 것은 RuleSystem만 한다.
  (인터페이스는 `docs/decisions/001-rule-interface.md` 기준)
- 새 StatId, 새 게임 이벤트, 새 공용 인터페이스가 필요하면 **코드를 쓰기 전에 사람에게 먼저 제안**한다. 두 사람의 작업이 만나는 지점이기 때문이다.

## 3. 폴더 구조
```
Assets/_Project/
  Scripts/
    Core/      게임 흐름, 공용 유틸, 이벤트
    Player/    플레이어 이동/점프/상태
    Entities/  적, 발판, 가시 등
    Spec/      StageSpec, Sentence, Slot (기획서 데이터)
    Rules/     RuleSystem, RuleEffect와 하위 클래스
    UI/        기획서 화면, HUD
  Data/
    Stages/    StageSpec 에셋
    Rules/     RuleEffect 에셋
  Resources/   CSV 데이터 테이블 (Stage.csv 등). 형식과 규칙은 docs/data-tables.md를 따른다
  Scenes/
  Prefabs/
  Art/
  Audio/
```
`Assets/_Project/` 밖에 새 파일을 만들지 않는다 (서드파티 패키지 제외).

## 4. 코딩 컨벤션 (C#)
- 네임스페이스: `CoffeeSpec.<폴더명>` (예: `CoffeeSpec.Rules`)  ← 프로젝트명 확정 시 수정
- 클래스/메서드/프로퍼티: PascalCase, private 필드: `_camelCase`, 인스펙터 노출은 `[SerializeField] private`
- `public` 필드 대신 `[SerializeField] private` + 필요 시 읽기 전용 프로퍼티
- `Update()`에서 `GetComponent`, `Find` 호출 금지 → `Awake`에서 캐싱
- 매직 넘버 금지 → 기획 수치는 ScriptableObject 또는 `[SerializeField]`로
- 주석은 "무엇"이 아니라 "왜"를 적는다. 한국어 주석 허용.

## 5. Unity 관련 금지 사항 (AI가 특히 자주 틀리는 부분)
- `.meta` 파일을 직접 만들거나 GUID를 지어내지 않는다. 새 스크립트/에셋의 `.meta`는 Unity 에디터가 생성하게 둔다.
- `.unity`(씬), `.prefab` 파일을 텍스트로 직접 편집하지 않는다. 씬/프리팹 수정이 필요하면 **사람이 에디터에서 할 작업 절차를 적어 준다.**
- `ProjectSettings/`, `Packages/manifest.json`은 사람의 확인 없이 수정하지 않는다. 패키지 추가는 제안만 한다.
- Unity 버전에 없는 API를 쓰지 않는다. 확실하지 않으면 사용하려는 API와 최소 버전을 명시한다.

## 6. 작업 방식
- 작업 전에 관련 Issue 번호와 완료 기준을 확인한다. 범위를 벗어나는 수정은 하지 말고 따로 제안한다.
- 큰 작업은 먼저 **계획(어떤 파일을 어떻게 바꿀지)**을 보여 주고 승인을 받은 뒤 진행한다.
- 변경은 작게. 한 번에 한 가지 목적만.
- 코드를 다 쓴 뒤에는 **사람이 Unity에서 확인할 테스트 절차**를 함께 적는다.
- 확신이 없는 부분(물리 수치, 타이밍, 재미 판단)은 정답처럼 말하지 말고 후보와 근거를 제시한다. 재미와 난이도 결정은 사람이 한다.
- `git commit`, `git push`는 사람이 직접 한다. AI는 커밋 메시지 초안만 제안한다.

## 7. 커밋 메시지 형식 (초안 제안 시)
```
<type>: <요약> (#이슈번호)

type: feat | fix | refactor | rule | stage | docs | chore
예: rule: 착지 시 중력 반전 규칙 추가 (#18)
```

---

## 변경 기록
> AI가 실수했거나 같은 설명을 반복하게 되면, 규칙을 위에 추가하고 여기에 한 줄 남긴다.
> 이 기록 자체가 "AI 협업 방식을 어떻게 개선해 왔는지"의 증거가 된다.

| 날짜 | 작성자 | 추가/변경한 규칙 | 계기 (AI가 무엇을 틀렸나) |
|---|---|---|---|
| 2026-11-05 | (이름) | 초안 작성 | 저장소 세팅 |
| 2026-11-05 | (이름) | 폴더 구조에 `Resources/`(CSV 테이블) 추가, 규칙은 docs/data-tables.md 참조 | 실수 아님. CSV 데이터 도입 (decisions/002) |
