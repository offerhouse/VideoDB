# VideoDB v0.2.0

A lightweight local video scene database tool for Windows, MPC-HC, and VLC.

Supports:

* Scene tagging
* Actress tagging
* Timestamp capture from MPC-HC or VLC
* Thumbnail preview
* Auto scan / relink
* Video ID recovery
* F8 global scene capture
* F9 floating media tool for scene count and playback seeking
* Recent tag and actress shortcuts
* Startup logging
* Dark mode UI

![Main UI](screenshots/screenshot_01.png)

---

# Requirements

* Windows 10 / 11
* Media Player Classic Home Cinema (MPC-HC) or VLC media player
* ffmpeg.exe (optional, for thumbnail generation)

MPC-HC:
https://github.com/clsid2/mpc-hc

K-Lite Codec Pack, Standard edition includes MPC-HC:

https://codecguide.com/download_kl.htm

VLC:
https://www.videolan.org/vlc/

---

# First Time Setup

## 1. Configure player mode and player paths

After launching VideoDB, open Options.

Choose the player mode:

* Auto
* MPC
* VLC

Then set the executable path for MPC-HC and/or VLC.

Example MPC-HC path:

```text
C:\Program Files (x86)\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe
```

Example VLC path:

```text
C:\Program Files\VideoLAN\VLC\vlc.exe
```

---

## 2. Configure video library scan folders

In Options:

Add your video library folders.

The Scan button will recursively scan these folders and import videos into the database.

---

## 3. Enable MPC-HC Web Interface

In MPC-HC:

View > Options > Player > Web Interface

Enable:

* Listen on port: 13579

This is required for MPC-HC scene capture and seeking.

![mpc_hc_web_interface](screenshots/mpc_hc_web_interface.png)

---

## 4. Enable VLC HTTP Interface

In VLC:

Tools > Preferences > Show settings: All > Interface > Main interfaces

Enable:

* Web

Then set the Lua HTTP password:

Tools > Preferences > Show settings: All > Interface > Main interfaces > Lua > Lua HTTP > Password

VideoDB defaults:

* Host: 127.0.0.1
* Port: 8080

Set the same host, port, and password in VideoDB Options.

---

# How To Use

## Capture a Scene

1. Open a video in MPC-HC or VLC.
2. Seek to a scene.
3. Press F8, or click F8 Capture in VideoDB.
4. VideoDB popup will appear.
5. Add:

   * Tags
   * Actresses
   * Rating
   * Notes

The current playback timestamp is automatically captured.

## Floating Media Tool

Press F9, or click F9 Media Tool in VideoDB.

The floating media tool can:

* Capture the current scene
* Show the recorded scene count for the current video
* Remember and return to a playback position
* Seek backward or forward by 1, 3, 10, or 30 seconds

Hold the Scenes button to check how many scenes are already recorded for the active video.

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

Startup log:

```text
startup.log
```

Recommended publish build:

```text
win-x64
self-contained
```
