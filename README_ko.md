# VideoDB

VideoDB는 Windows용 로컬 비디오 Scene Database 도구입니다.

MPC-HC와 함께 사용하여：

* Scene Bookmark
* Tag
* Actress
* Rating
* Timestamp
* Thumbnail Preview

를 빠르게 관리할 수 있습니다.

또한：

* 비디오 라이브러리 자동 Scan
* Rename / Move File 자동 복구
* Video ID Metadata Recovery
* Dark Mode UI

를 지원합니다.

![Main UI](screenshots/screenshot_01.png)
---

# 주요 기능

## F8 Scene Capture

비디오 재생 중：

1. MPC-HC로 비디오 재생
2. 저장하고 싶은 장면으로 이동
3. F8 키 입력
4. VideoDB popup window 표시
5. 다음 정보 입력 가능：

   * Tag
   * Actress
   * Rating
   * Note

현재 재생 시간이 자동 저장됩니다.

---

## 자동 Thumbnail 생성

```text id="r9ztwu"
ffmpeg.exe
```

를 VideoDB.exe 옆에 넣으면：

* 장면 이미지 자동 캡처
* WebP Thumbnail 생성
* Scene List 표시

가 자동으로 수행됩니다.

---

## Video ID Metadata System

VideoDB는 비디오 metadata stream에 Video ID를 저장합니다.

따라서：

* rename file
* move file
* folder change

가 발생해도，

비디오가 Scan Folder 안에 존재하면：

* Scene
* Tag
* Actress
* Rating
* Event

를 자동으로 복구할 수 있습니다.

---

## Scan System

Scan Button 기능：

* 비디오 폴더 scan
* 새 비디오 import
* rename file 감지
* moved file 감지
* existing database records reconnect

지원 포맷：

* MKV
* MP4
* AVI
* TS

---

# 시스템 요구 사항

* Windows 10 / 11
* MPC-HC
* ffmpeg.exe（optional）

MPC-HC：

https://github.com/mpc-hc/mpc-hc

또는：

K-Lite Codec Pack（Standard edition includes MPC-HC）

https://codecguide.com/download_kl.htm

FFmpeg：

https://ffmpeg.org/download.html

---

# 초기 설정

## 1. MPC-HC Path 설정

```text id="q9q2lc"
Options
```

에서：

```text id="72kvzi"
mpc-hc64.exe
```

를 선택합니다.

예시：

```text id="muv6hk"
C:\Program Files (x86)\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe
```

---

## 2. 비디오 라이브러리 설정

```text id="w6r5hv"
Options
```

에서 비디오 폴더를 추가합니다.

Scan Button은 이 폴더들을 scan 합니다.

---

## 3. MPC-HC Web Interface 활성화

MPC-HC：

```text id="89lqzn"
View → Options → Player → Web Interface
```

다음을 활성화：

```text id="0l1dvp"
Listen on port: 13579
```

![mpc_hc_web_interface](screenshots/mpc_hc_web_interface.png)

F8 Scene Capture 기능에 필요합니다.

---

# 사용 방법

## Scene 추가

1. MPC-HC로 비디오 재생
2. 저장할 위치로 이동
3. F8 키 입력
4. 다음 정보 입력：

   * Tag
   * Actress
   * Rating
   * Note

Scene가 database에 저장됩니다.

---

## Scene Filter

다음 기준으로 Scene filter 가능：

* Tag
* Actress
* Rating

---

## Double Click Scene

Scene를 Double Click 하면：

* MPC-HC 자동 실행
* 해당 timestamp로 자동 seek

됩니다.

---

# 데이터 저장 위치

Database：

```text id="s9x5uq"
video.db
```

Thumbnail：

```text id="ntfvl9"
thumbnails/
```

---

# 권장 Publish 설정

권장：

```text id="5l3xek"
win-x64
self-contained
```

별도의 .NET Runtime 설치가 필요하지 않습니다.

---

# 주의 사항

현재 early alpha version 입니다.

권장 사항：

* 정기적으로 video.db backup
* database 직접 수정 금지
* Scan 전에 비디오 폴더 상태 확인

---

# GitHub

Project Repository：

https://github.com/offerhouse/VideoDB
