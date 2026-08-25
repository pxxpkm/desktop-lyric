# desktop-lyric

Desktop lyrics overlay for Windows. Shows synced lyrics with Chinese translation for whatever's playing in Tidal (or any SMTC-compatible player).

## what it does

- Detects currently playing song via Windows SMTC
- Fetches lyrics from Netease, QQ Music, Kugou, LRCLIB
- Syncs lyrics to playback position (interpolates when SMTC position freezes)
- Traditional Chinese by default (toggle **繁體**; OpenCC phrase-level 簡→繁, Hong Kong variants)
- Floating overlay — opens on startup; drag / resize; type size follows window height
- Karaoke word-by-word highlight (Netease YRC); Japanese + Chinese packed into one line is split
- **選歌** when several tracks share a name (choice is remembered)
- Romaji for Japanese lyrics
- Export as .lrc file

## install

Download from [Releases](https://github.com/Epi-1120/desktop-lyric/releases) — single .exe, no install needed.

Or build from source:

```
dotnet publish src/DesktopLyric/DesktopLyric.csproj -c Release
```

## fonts

Optional but recommended: put [Chiron GoRound TC](https://fonts.google.com/specimen/Chiron+GoRound+TC) Regular and Medium `.ttf` in a `Fonts/` folder next to the exe (or `src/DesktopLyric/Fonts/` when building). Overlay Chinese defaults to that family. Japanese lyrics use Yu Gothic UI. Font files are gitignored and not shipped in the repo.

## usage

1. Play music in Tidal (or Spotify, or anything that shows in Windows media controls)
2. Run DesktopLyric.exe
3. Overlay opens automatically; lyrics follow playback
4. Use **選歌** if the wrong same-name track was matched
5. **昭源圓體** cycles Chinese fonts; overlay A+/A− grows the window (and the type)

## known limits

- Translation quality depends on Google Translate
- Karaoke timing only works when Netease has YRC data for the song
- Windows only for now

## settings

Saved to `%AppData%/DesktopLyric/settings.json`. You can edit it manually or through the app.

## lyrics sources

Searches these in parallel, picks the one whose timing best matches track duration:

1. Netease Music (网易云)
2. QQ Music
3. Kugou (酷狗)
4. LRCLIB

---

made by [Epi-1120](https://github.com/Epi-1120)
