# 001. 규칙(RuleEffect)이 게임 오브젝트를 바꾸는 방식

- 상태: **제안** (두 사람이 검토 후 `확정`으로 변경)
- 날짜: 2026-11-05
- 작성자: (이름)
- 관련 Issue: #2

## 배경
이 게임의 핵심은 "기획서 빈칸에 고른 값 → 게임 규칙 변경"이다.
플랫포머 코어(플레이어, 적)를 만드는 사람과 기획서/규칙 시스템을 만드는 사람이 다르기 때문에,
**두 코드가 만나는 지점**을 먼저 정해 두지 않으면 서로의 코드를 계속 고치게 된다.

요구 사항:
1. 규칙을 추가할 때 Player/Enemy 코드를 고치지 않아야 한다.
2. 수치형 규칙(점프 높이)과 조건+효과형 규칙(착지할 때 중력 반전)을 모두 표현할 수 있어야 한다.
3. 스테이지를 다시 시작하면 규칙이 깨끗하게 초기화되어야 한다.

## 선택지

### A. RuleEffect가 컴포넌트를 직접 참조
```csharp
public override void Apply(GameContext ctx) => ctx.Player.GetComponent<PlayerMovement>().jumpHeight = 8f;
```
- 장점: 가장 단순하고 빠르다.
- 단점: 규칙이 Player 내부 구조를 알아야 한다. 필드 이름이 바뀌면 규칙이 다 깨진다. 요구 사항 1 위반.

### B. 스탯 레지스트리 + 게임 이벤트 (추천안)
- 엔티티는 바꿀 수 있는 값을 `StatId`로 등록한다. 규칙은 `StatId`로만 값을 바꾼다.
- 조건형 규칙은 게임 이벤트(`OnJump`, `OnLand`, `OnHit`)를 구독한다.
```csharp
public enum StatId { JumpHeight, GravityScale, MoveSpeed, Size, ContactResult }

// 엔티티 쪽 (A 담당)
public interface IRuleTarget
{
    TargetKind Kind { get; }                 // Player, Enemy, Platform
    void SetStat(StatId id, float value);
    void ResetStats();                       // 스테이지 재시작 시
}

// 규칙 쪽 (B 담당)
public abstract class RuleEffect : ScriptableObject
{
    public abstract void Apply(RuleContext ctx);   // 스테이지 시작 시 호출
    public virtual void Remove(RuleContext ctx) {} // 이벤트 구독 해제 등
}

public class RuleContext
{
    public IReadOnlyList<IRuleTarget> Targets;
    public GameEvents Events;                // OnJump, OnLand, OnHit ...
}
```
- 장점: 규칙 추가 시 엔티티 코드를 건드리지 않는다. 두 사람의 접점이 `StatId`와 `GameEvents` 목록 두 개로 줄어든다.
- 단점: 초기 설계 비용. `ContactResult`처럼 수치가 아닌 값은 float로 표현하기 어색하다 (enum을 int로 변환하는 등 보완 필요).

### C. 모든 규칙을 하나의 RuleSystem 클래스의 switch로 처리
- 장점: 처음엔 한 파일이라 이해하기 쉽다.
- 단점: 규칙이 늘수록 한 파일이 비대해지고, 두 사람이 같은 파일을 동시에 고쳐 충돌이 잦아진다.

## 결정
(검토 후 작성) — 추천안은 B.

## 이유
(검토 후 작성)

## 확정 전에 같이 정할 것
- [ ] v0.1 `StatId` 목록 확정 (규칙 5종 기준으로 필요한 것만)
- [ ] `GameEvents` v0.1 목록: OnJump, OnLand, OnHit, OnDeath, OnGoal?
- [ ] 수치가 아닌 값(ContactResult 등)을 어떻게 표현할지
- [ ] 스테이지 재시작 시 초기화 순서

## AI 활용
선택지 A/B/C의 초안과 코드 스케치는 AI(Claude)가 제안했다.
(검토하면서 바꾼 부분, AI 제안 중 버린 부분을 여기에 기록)
