# 커피 쏟은 기획서 (가제)

> 커피를 쏟아 일부가 지워진 기획서. 빈칸은 개발자 마음대로, 근데 빌드는 돌아가야 한다.

<!-- 완성되면 여기에 플레이 GIF -->

2명이 AI와 함께 만드는 Unity 2D 퍼즐 플랫포머입니다.
플레이어는 개발자가 되어 기획서의 지워진 빈칸을 제한된 선택지로 채우고, 플레이테스트해서 스테이지를 클리어합니다.

- 상태: v0.1 개발 중
- 엔진: Unity `____` (버전 확정 후 기입)
- 팀: `____`, `김강석`

---

## 이 저장소에서 볼 수 있는 것

이 프로젝트는 결과물만큼 **AI와 함께 문제를 풀어 간 과정**을 중요하게 기록합니다.

| 보고 싶은 것 | 위치 |
|---|---|
| 문제를 어떻게 정의하고 쪼갰나 | [Issues](../../issues), [Milestones](../../milestones) |
| AI에게 무엇을 맡기고 무엇을 직접 판단했나 | [Pull Requests](../../pulls?q=is%3Apr) (각 PR의 🤖 / ✍️ 항목) |
| 두 사람이 어떻게 리뷰하고 협업했나 | PR의 리뷰 코멘트 |
| 왜 이렇게 설계했나 | [`docs/decisions/`](docs/decisions/) |
| AI와 일하는 방식이 어떻게 나아졌나 | [`docs/ai-log/`](docs/ai-log/), [`AGENTS.md`](AGENTS.md)의 변경 기록 |
| 게임 기획 | [`docs/game-design.md`](docs/game-design.md) |

---

## 처음 세팅하기

### 필요한 것
- Unity Hub + Unity `____` (팀 전원 같은 버전)
- Git, [Git LFS](https://git-lfs.com)
- (선택) [GitHub CLI](https://cli.github.com) — Issue 일괄 생성 스크립트용

### 저장소를 처음 만드는 사람 (1명만)
1. Unity Hub에서 **2D (URP 또는 Built-in)** 템플릿으로 새 프로젝트 생성
2. 이 저장소의 파일들(`.gitignore`, `.gitattributes`, `.github/`, `docs/`, `scripts/`, `*.md`)을 Unity 프로젝트 폴더 최상위(`Assets/`와 같은 위치)에 복사
3. Unity 에디터 설정 확인
   - `Edit > Project Settings > Editor > Version Control > Mode` = **Visible Meta Files**
   - `Edit > Project Settings > Editor > Asset Serialization > Mode` = **Force Text**
4. Git 초기화와 첫 push
   ```bash
   git init
   git lfs install
   git add .
   git commit -m "chore: 프로젝트 초기 세팅"
   git branch -M main
   git remote add origin https://github.com/<계정>/<저장소>.git
   git push -u origin main
   ```
____
5. GitHub 저장소 설정
   - `Settings > Collaborators`에서 팀원 초대
   - `Settings > Branches > Add rule` (`main`): **Require a pull request before merging**, **Require approvals: 1** 체크
6. (선택) Issue, 라벨, 마일스톤 일괄 생성
   ```powershell
   gh auth login
   powershell -ExecutionPolicy Bypass -File scripts/setup-github.ps1 -DryRun   # 무엇이 만들어질지 미리 보기
   powershell -ExecutionPolicy Bypass -File scripts/setup-github.ps1
   ```
   빈 저장소에서 **한 번만** 실행합니다. Issue가 이미 있으면 번호가 문서와 어긋나므로 스크립트가 스스로 중단합니다.
   Mac에서는 PowerShell 7(`pwsh`)을 설치하면 `pwsh -File scripts/setup-github.ps1`로 실행할 수 있습니다.

### 팀원 (clone 하는 사람)
```bash
git lfs install
git clone https://github.com/<계정>/<저장소>.git
```
Unity Hub에서 `Add > 폴더 선택`으로 열기. 그다음 [`CONTRIBUTING.md`](CONTRIBUTING.md)를 읽어 주세요.

---

## 폴더 구조
```
Assets/_Project/     게임 코드와 에셋 (구조는 AGENTS.md 참고)
docs/
  game-design.md     기획 (살아 있는 문서)
  decisions/         설계 결정 기록
  ai-log/            AI 실험 로그
  milestones/        마일스톤별 Issue 목록
scripts/             GitHub 세팅 스크립트
AGENTS.md            AI 도구 공용 지시문
CLAUDE.md            Claude Code용 (AGENTS.md를 불러옴)
CONTRIBUTING.md      협업 규칙
```
