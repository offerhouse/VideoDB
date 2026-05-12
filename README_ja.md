# VideoDB

VideoDB は、Windows 用のローカル動画 Scene Database ツールです。

MPC-HC と連携して：

* Scene Bookmark
* Tag
* Actress
* Rating
* Timestamp
* Thumbnail Preview

を簡単に管理できます。

さらに：

* 動画ライブラリ自動 Scan
* Rename / Move File 自動復元
* Video ID Metadata Recovery
* Dark Mode UI

にも対応しています。

![Main UI](screenshots/screenshot_01.png)

---

# 主な機能

## F8 Scene Capture

動画再生中：

1. MPC-HC で動画を再生
2. 保存したいシーンへ移動
3. F8 を押す
4. VideoDB の popup window が表示
5. 以下を登録可能：

   * Tag
   * Actress
   * Rating
   * Note

現在の再生時間は自動保存されます。

---

## 自動 Thumbnail 生成

```text id="vx12pu"
ffmpeg.exe
```

を VideoDB.exe の隣に配置すると：

* シーン画像を自動キャプチャ
* WebP Thumbnail を生成
* Scene List に表示

します。

---

## Video ID Metadata System

VideoDB は動画 metadata stream に Video ID を保存します。

そのため：

* rename file
* move file
* folder change

を行っても、

動画が Scan Folder 内に存在すれば：

* Scene
* Tag
* Actress
* Rating
* Event

を自動復元できます。

---

## Scan System

Scan Button は：

* 動画フォルダを scan
* 新しい動画を import
* rename file を検出
* moved file を検出
* existing database records を reconnect

します。

対応フォーマット：

* MKV
* MP4
* AVI
* TS

---

# 動作環境

* Windows 10 / 11
* MPC-HC
* ffmpeg.exe（optional）

MPC-HC：

https://github.com/mpc-hc/mpc-hc

または：

K-Lite Codec Pack（Standard edition includes MPC-HC）

https://codecguide.com/download_kl.htm

FFmpeg：

https://ffmpeg.org/download.html

---

# 初回セットアップ

## 1. MPC-HC Path 設定

```text id="7mpsxt"
Options
```

から：

```text id="i9q6xl"
mpc-hc64.exe
```

を指定してください。

例：

```text id="4wbxaz"
C:\Program Files (x86)\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe
```

---

## 2. 動画ライブラリ設定

```text id="jjlwmr"
Options
```

で動画フォルダを追加します。

Scan Button はこれらのフォルダを scan します。

---

## 3. MPC-HC Web Interface を有効化

MPC-HC：

```text id="0dnmhn"
View → Options → Player → Web Interface
```

以下を有効化：

```text id="ygh9ah"
Listen on port: 13579

![mpc_hc_web_interface](screenshots/mpc_hc_web_interface.png)
```

これは F8 Scene Capture に必要です。

---

# 使い方

## Scene 登録

1. MPC-HC で動画を再生
2. 保存したい位置へ移動
3. F8 を押す
4. 以下を入力：

   * Tag
   * Actress
   * Rating
   * Note

Scene が database に保存されます。

---

## Scene Filter

以下で Scene を filter 可能：

* Tag
* Actress
* Rating

---

## Double Click Scene

Scene を Double Click すると：

* MPC-HC が自動起動
* 対応 timestamp へ自動 seek

します。

---

# データ保存場所

Database：

```text id="gqujlwm"
video.db
```

Thumbnail：

```text id="5rt2vp"
thumbnails/
```

---

# 推奨 Publish 設定

推奨：

```text id="7m7c8v"
win-x64
self-contained
```

.NET Runtime の別途インストール不要になります。

---

# 注意

現在は early alpha version です。

推奨：

* 定期的に video.db を backup
* database を直接編集しない
* Scan 前に動画フォルダ状態を確認

---

# GitHub

Project Repository：

https://github.com/offerhouse/VideoDB
