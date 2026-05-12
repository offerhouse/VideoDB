# VideoDB v0.1

A lightweight local video scene database tool for Windows + MPC-HC.

Supports:

* Scene tagging
* Actress tagging
* Timestamp capture
* Thumbnail preview
* Auto scan / relink
* Video ID recovery
* Dark mode UI

![Main UI](screenshots/screenshot_01.png)
---

# Requirements

* Windows 10 / 11
* Media Player Classic Home Cinema (MPC-HC)
* ffmpeg.exe (optional, for thumbnail generation)

MPC-HC:
https://github.com/mpc-hc/mpc-hc

Or install K-Lite Codec Pack (Standard edition includes MPC-HC):

https://codecguide.com/download_kl.htm

---

# First Time Setup

## 1. Configure MPC-HC path

After launching VideoDB:

Options → Select MPC-HC executable path

Example:

```text
C:\Program Files (x86)\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe
```

---

## 2. Configure video library scan folders

In Options:

Add your video library folders.

The Scan button will recursively scan these folders and import videos into the database.

---

## 3. Enable MPC-HC Web Interface

In MPC-HC:

View → Options → Player → Web Interface

Enable:

* Listen on port: 13579

This is required for F8 scene capture.

![mpc_hc_web_interface](screenshots/mpc_hc_web_interface.png)
---

# How To Use

## Capture a Scene

1. Open a video in MPC-HC
2. Seek to a scene
3. Press F8
4. VideoDB popup will appear
5. Add:

   * Tags
   * Actresses
   * Rating
   * Notes

The current playback timestamp is automatically captured.

---

# Scan Function

The Scan button:

* scans all configured video folders
* imports new videos
* detects moved files
* detects renamed files
* restores missing paths
* reconnects existing events/tags to recovered videos

Your scene database is preserved even if files are renamed or moved inside your library folders.

---

# Video ID System

VideoDB writes a persistent internal video ID into the video metadata stream.

This allows the application to:

* recover renamed videos
* recover moved videos
* reconnect scenes/tags automatically
* avoid duplicate entries

Supported formats:

* MKV
* MP4
* AVI
* TS

---

# Optional: Thumbnail Preview

Place:

```text
ffmpeg.exe
```

beside:

```text
VideoDB.exe
```

VideoDB will automatically generate scene thumbnail previews.

FFmpeg:
https://ffmpeg.org/download.html

---

# Notes

Database file:

```text
video.db
```

Thumbnail folder:

```text
thumbnails/
```

Recommended publish build:

```text
win-x64
self-contained
```
