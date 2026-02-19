# Changelog

## v0.0.7
- Bug Fix
  - 채팅에서 아이템 획득 메시지가 2번째부터 번역이 적용되지 않는 문제 수정
  - 보트 수리 자원 비용(예: `Fir Wood (15)`)이 번역되지 않는 문제 수정
  - AncientCore 띄어쓰기 문제로 번역이 안 되는 문제 수정
- 번역 추가/개선
  - 보트 건축물(수리) 이름 번역 추가
  - 난파선/가디언 발견 메시지 번역 추가
  - `-DAY X-` 일차 표시 번역 추가
  - 승리 UI 번역 및 원문 오타(Victory!에 느낌표 누락) 수정
  - 도전과제 일부 추가 번역
  - 파워업/불도저/스니커즈 설명 원문 오타 수정
  - 전나무를 소나무로 번역 수정
  - 일부 아이템 설명 개선

## v0.0.6
- Repository
  - CHANGELOG.md 추가
  - Thunderstore 패키지에 CHANGELOG.md 포함하도록 워크플로우 수정

## v0.0.5
- Bug Fix
  - translations.json5 대소문자 중복 항목 제거
  - missed_strings.txt 게임 시작 시 초기화되지 않고, 누적 기록되는 문제 수정
  - missed_strings.txt에 번역된 한글 텍스트가 잘못 기록되는 버그 수정
- Minor Fix
  - 디버그 로그 출력 제거
- README 개선
  - README 레이아웃 수정 (배지, 스크린샷 추가)

## v0.0.4
- README 개선
  - README 이미지 URL 브랜치명 수정 (main → master)

## v0.0.3
- MuckKorean 한글 플러그인 최초 배포 (GitHub/ThunderStore)
- README 개선
  - README 이미지를 GitHub raw URL로 변경 (Thunderstore 호환)

## v0.0.2
- MuckKorean 한글 플러그인 테스트 배포 (ThunderStore)
- Repository
  - GitHub Action에서 manifest.json 버전 자동 동기화
  - publish.yml 워크플로우 수정

## v0.0.1
- 초기 릴리스
  - 메뉴, 설정, 인벤토리, 제작, 상점 등 주요 UI 한글화
  - 아이템/무기/방어구/스킬 이름 번역 (738+ 항목)
  - 맑은 고딕 폰트 자동 주입 (TMP fallback 방식)
- Repository
  - GitHub Actions 자동 배포 추가 (GitHub Release + Thunderstore)



