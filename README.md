# Advanced Director Cockpit WV2

This is a full shell over a copy of the Advanced Director Cockpit. It is not a replacement for the original app, and it does not edit that folder.

The original page (HTML, CSS, JS, and the data JSON) is copied into `www` in this project. The window loads that copy. The tray, groups, cards, and tabs are the cockpit UI. Dropping or opening a card does not send the address through the frame proxy. The copied page tells this shell the card name and the real url, and a WebView2 pane opens that url.

Page data lives here:

`V:\_Data\CaspersCreations\Applications\Source\NonBalls\Advanced Director Cockpit WV2\www\data`

Edit `cards.json`, `groups.json`, `tabs.json`, and `stages.json` there. This app reads that copy, not a second list and not the original cockpit folder. An open window picks up a JSON edit within a few seconds; no restart is needed.

The window is the cockpit page. Dropping a card on a stage slot opens that card in a WebView2 laid over that same slot. The left slot is the left pane. The right slot is the right pane. There is no separate split to the right of the stage. Urls come from `www\data\cards.json`.

## Run

Start the built exe:

`V:\_Data\CaspersCreations\Applications\Source\NonBalls\Advanced Director Cockpit WV2\bin\Debug\net10.0-windows\AdvancedDirectorCockpitWV2.exe`

From this folder, `dotnet build` then run that exe. `dotnet run` also works.

## Sign-in

Logins stay in this app's own WebView2 data folder:

`%LOCALAPPDATA%\AdvancedDirectorCockpitWV2\WebView2`

That profile is not Chrome. The app does not read the Chrome profile, and it does not inject credentials or log cookies or passwords.
