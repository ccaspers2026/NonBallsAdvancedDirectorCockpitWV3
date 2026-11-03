# WV2 tweak list

Running list for Advanced Director Cockpit WV2. Each rework round starts from this file, not from memory. Add a line when Christian asks for a change. Do not mark it done until that round is in the running app.

(none yet)

1. Done. A drop on a stage slot posts adc-open-pane with that slot index, and the host navigates only the WebView2 positioned on that slot's rectangle. The left stage slot is slot 0, so it is not sent to a pane further right. The separate right-hand split is removed. The drag itself was not clicked in this round; this was checked from the code path.

2. One cockpit window per group. Four groups means four windows. Each window shows that group's layout. Not done.

3. Tools button. In edit mode, every group gets a Tools button next to its existing buttons. Clicking it opens a tool catalog pop-up, admin mode only. File pane leads the list. Picking a tool drops it into the group whose button was clicked. Queue after the drop fix and the one-window-per-group work. Not done.

4. Published. Every preset layout can add an empty pane, not only a custom layout. An empty pane shows Delete beside "Drop a card here to load its page". A pane with a card does not. Save still writes the same layout, slots, sizes, and loads. The process that was already open was not restarted, so this shows the next time that cockpit is opened.

## 2026-10-05, browser wording review

Text only. No ids, classes, urls, or behavior changed. These changes are in Source and dotnet build passes. They are not in the running app yet: the exe that is already open was not restarted, and release and publish are with DevOps.

- www\advanced-director-cockpit.js:1122. Detach toast. Was "Pop-up blocked - allow pop-ups for this site". Now "Detach did not open a window - WV2 keeps card pages in their stage panes". WV2 has no pop-up setting to allow.
- www\advanced-director-cockpit.js:1840. Open group dialog title. Was "Pop-ups blocked". Now "Windows not opened".
- www\advanced-director-cockpit.js:1841. Same dialog hint. Was "Browser blocked some windows (common with file://). Same Open-group path, use these links; drag each window to a monitor." Now "WV2 did not open these config windows. WV2 does not open new windows from the cockpit page, so these links will not open one either. Use Configs, then Load, to apply a config in this window."
- www\advanced-director-cockpit.js:1878. Open group toast. Was "... blocked, use links". Now "... not opened - see list".
- www\advanced-director-cockpit.js:2011. Save config group hint. Was "Drag each Chrome window to a monitor." Now "WV2 does not open new windows from the cockpit page, so Open group does not open a window per config here."
- www\advanced-director-cockpit.js:2303. Tray load error. Was "Couldn't load the page data. Open this page from a local web server, not by double-clicking the file." Now "Couldn't load the page data from www\data. Check that cards.json, groups.json, tabs.json, and stages.json are valid JSON, then restart the app."
- www\advanced-director-cockpit.js:3304. Group form checkbox. "this browser has no saved state" is now "this app has no saved state".
- www\advanced-director-cockpit.js:3576. Delete tab confirm. "this browser's copy" is now "this app's saved copy".
- www\advanced-director-cockpit.js:3687. Reset page data confirm. "saved in this browser" is now "saved in this app".
- www\advanced-director-cockpit.js:3877. Maintain cards hint. "saved in this browser" is now "saved in this app".
- www\advanced-director-cockpit.html:101. Tray footer. "click opens browser" is now "click loads the site in a stage pane".
- www\data\groups.json:6. _readme help for defaultCollapsed. "in the browser" is now "in this app's WebView2 profile".
- www\data\stages.json:15. _readme help for defaults. "The browser's saved stage" is now "The stage saved in this app's WebView2 profile".
## 2026-10-05, launch pad load fix

- www\advanced-director-cockpit.js render(): `meta` was used but never declared, so render threw `ReferenceError: meta is not defined` and the boot showed the couldn't-load note with no cards. Added `var meta = LAYOUTS[state.layout] || LAYOUTS.half || { cls: '' };` before the grid class line. Panes added beyond the layout count are not trimmed.

## 2026-10-05, Workshop group and Bambu Studio

- www\data\groups.json: added the Workshop group (id `workstop`, order 0 so it sits first). Nothing else in WV2's list was changed. Bills and every other group stayed.
- www\data\cards.json: added the Bambu Studio card (id `b408c42e-c0df-48b2-b1fb-38fe107b272b`) in Workshop, with url `bambustudio://open`. WV2 has no app-launch tool kind, so a click follows the link. Whether WebView2 hands that link to Windows is untested.

## 2026-10-05, live data files: fresh load, auto-refresh, stale-save guard

These changes are in Source and dotnet build passes. They are not in the running app: the exe that is open (PID 52396) was not restarted, and publish is with DevOps.

- www\advanced-director-cockpit.html. The CSS and JS tags no longer use the fixed `?v=pane` tag. A small inline script adds `?v=<current time>` to both on every load, so a fresh load always runs the current CSS and JS.
- MainWindow.xaml.cs, StartAsync. The window now opens `advanced-director-cockpit.html?v=<start time>` instead of the bare page, so WebView2 cannot hand back a cached copy of the page itself. No other host change. No file watcher was added in C#: the page watches the files itself.
- www\advanced-director-cockpit.js, boot block. Every load reads www\data\cards.json, groups.json, tabs.json, and stages.json with no cache and remembers the exact text of each file.
- www\advanced-director-cockpit.js, auto-refresh. While the window is open, the page re-reads those four files every 3 seconds with no cache. When one changed, the tray, rail, tabs, and layout buttons are redrawn from the new data without reloading the page. Open panes are only rebuilt when stages.json changed. If a dialog is open, a drag is in progress, or a text box in the page has focus, a toast says the files changed and the new data loads within a few seconds after that ends.
- www\advanced-director-cockpit.js, save guard. Before any save, the page re-reads the four files. If any file differs from the text it last loaded or saved, nothing is written: the page loads the new data and a toast says the files changed on disk, the save was not applied, and to redo the edit. After a save that worked, the remembered text is updated. Saves run one at a time. WV2 still has no save endpoint (POST /adc-data on the virtual host fails as before and shows "The file was not saved"), so in WV2 the guard only matters if a save endpoint is added later.
- README.md. "Restart the exe after a JSON edit" now says an open window picks up a JSON edit within a few seconds.
- The Workshop group and the Bambu Studio card in www\data were not touched. No data file was changed.
## 2026-10-05, Bambu click / embed / Onshape / modal / toolbars (Source only; not published)

Backups under `V:\_Data\CaspersCreations\Grok\Scrap\wv2-bambu-click-backup-20261005-004404\` (rounds 2–7). Test build: `bin\BambuModalTest`. Screenshots: `Grok\Scrap\wv2-bambu-embed-20261005-0056\`.

### Desktop app cards and click / pane drop
- www\data\cards.json: Bambu Studio card url is the exe path `C:\Program Files\Bambu Studio\bambu-studio.exe`. Added Onshape card (`https://cad.onshape.com`) in Workshop.
- www\advanced-director-cockpit.js: tray click on a desktop-app card posts `adc-launch` (own window). Pane drop posts `adc-embed-app` and shows "Starting … in this pane…". `__adcEmbedEnded(slot)` clears the pane when the host reports the app exited.
- MainWindow.xaml.cs: `ApplyLaunch` / `ApplyEmbed`; FindBambuExe; pane popups as owned WPF windows (75%×80% of owner work area).
- EmbeddedApps.cs (new): owned borderless frame over the pane; SetParent embed of the app; Place / Release / Suspend. Start wait is 150 s (two Bambus at once).

### Web pane z-order (BringPaneToFront)
- MainWindow.xaml.cs: after a pane WebView2 is shown, `SetWindowPos(..., HWND_TOP, NOSIZE|NOMOVE|NOACTIVATE)` so the pane HWND is above the full-window shell. Verified: Onshape pane centre hits the pane's Chrome_RenderWidgetHostHWND, not the shell. Same class of bug as Pub.

### Page popups truly modal (approach b)
- Preferred approach (a) WPF ShowDialog was not used: every in-page dialog and dropdown is built in page HTML via `openModal` / `.lp-dd`; (b) fits without rewriting them.
- www\advanced-director-cockpit.js: MutationObserver reports `{type:'adc-modal', open}` for `.lp-modal-backdrop.show`, `.lp-dd.open`, and other dialog/menu roles.
- MainWindow.xaml.cs `ApplyModal`: while open, every wanted pane is `Visibility.Hidden` (not Collapsed/closed) and embeds are `_embeds.Suspend(true)` (frames hidden, process and window kept). On close, panes are shown and BringPaneToFront, embeds Suspend(false).
- Dialog size stays `min(80vw,1600)×min(80vh,1100)` with `resize:both`.

### Hide toolbars (title row)
- www html/js/css: "Hide toolbar" moved from the tabs row into `header .adc-head-tools`, renamed "Hide toolbars". `body.toolbar-collapsed` hides both `#tabs` and `#lpToolbar`. Toggle stays reachable in the title row. localStorage key `lp-toolbar-collapsed` unchanged. Toggle calls `scheduleSlotReport()` so native panes and embeds get new rects.

### Admin / Edit mode: no red
- www\advanced-director-cockpit.css: removed `body.adc-editing` red page tint (`#32181c` and the `::before` overlay). Banner keeps the class and the Edit toggle; banner colors use normal panel tokens. Edit mode still adds the crud controls.

### Save group validation (P2M Launch patch, folded in)
- www\advanced-director-cockpit.js `promptSaveGroup`: `saveGroupRuleOk()` requires 1+ checked config and `/\p{L}/u` in the trimmed name. `validateSaveGroup()` disables the primary button; bound to `#lpGrpName` input and `#lpGrpChecks` change; run once after `openModal`. Click refuses and toasts if the rule fails.
- www\advanced-director-cockpit.css: `.lp-modal-actions button:disabled` dimmed. No Ctrl+Shift+Space binding added anywhere (1Password Quick Access).

### Workshop layout target
Onshape left; two Bambu Studio embeds stacked right (Device tab is Bambu's own UI; no CLI to preselect a printer).
## 2026-10-05, black Onshape during dialog + POST /adc-data (Source only)

### Black Onshape pane
- Cause: approach (b) hides pane WebView2s while an in-page dialog is open, so the stage shows the shell empty dark pane body. Not a bad Onshape URL, not GPU, not missing BringPaneToFront.
- Fix this round: on dialog close, restore Visible + BringPaneToFront + size nudge + re-post bounds; JS adc-modal detector limited to `.lp-modal-backdrop.show, .lp-dd.open`. Live Onshape during a dialog needs approach (a) WPF ShowDialog later.
- Shots: `Grok\Scrap\wv2-bambu-embed-20261005-0056\23-modal-onshape-parked.png`, `24-onshape-after-modal.png`.

### POST /adc-data
- MainWindow.xaml.cs: ServeLocal replaces SetVirtualHostNameToFolderMapping. GET from www / data folder with no-store. POST /adc-data merge-writes the four JSON files (atomic replace, keeps _readme). `ConfiguredDataDir` is the single data-folder setting (null = www\data); point it at the canonical folder when Director locks the path. No backup/versioning yet. Stale-save guard and 3s poll unchanged.
- Verified in bin\BambuModalTest: save returned saved, disk updated, reopen loaded the new text.

### Bambu sign-out (lookup)
- Force-kill of test Bambus ~1:23:56 truncated BambuNetworkEngine.conf. Propose graceful close only; not applied here.
## 2026-10-05, Open group + canonical data + Bambu graceful close (Source only)

### Open group (one full cockpit window per config)
- Page posts `{type:adc-open-config-windows, ids:[...]}`. Host opens a new MainWindow per id on `?config=id` with the shared WebView2 profile, places windows across monitors (else cascades). Child close does not exit the app (`ShutdownMode.OnLastWindowClose`).
- "Windows not opened" dialog only on real host failure. Save Group hint updated.
- Files: MainWindow.xaml.cs, MainWindow.HostExtras.cs (new), www\advanced-director-cockpit.js, App.xaml.cs.
- Test: Desk dual (CAD Session + Single TradingView) opened three windows (main + 2). Shots: `wv2-bambu-embed-20261005-0056\30-open-group-windows.png`, `31-open-group-three-windows.png`.

### Canonical live data
- Folder created and seeded by copy from Pub www\data: `V:\_Data\CaspersCreations\Applications\Data\Advanced Director Cockpit\data`.
- `ConfiguredDataDir` points there. ServeLocal reads/writes that folder (no-store). Missing folder is a hard error.
- Freshness every 10 s: TEST COPY / OLDER BUILD in the title; auto-reload page code when idle (`__adcLive.busy()` false). Origin stays adc-wv2.local.
- Verified POST /adc-data updated canonical cards.json (not Source www\data).

### Bambu graceful close
- EmbeddedApps: Release/Fail use WM_CLOSE under a serialize lock, WaitForExit(60s), never Process.Kill.

### DevOps publish
- Exclude www\data from Source-to-Pub. Do not publish while Pub exe is running. Include MainWindow.HostExtras.cs.
- Phoenix: add `Applications\Data\Advanced Director Cockpit\data` to fleet backups.

2026-10-07 ~12:54 AM ET — Director: in-window 1Password / Auto-Type still not present in WV2 (never finished). Custom login inside WV2 is possible later; parked until a future Balls build with built-in security. Do not start either now.
- 2026-10-09 8:54 PM ET (voice): Added a Chrome card (launches C:\Program Files\Google\Chrome\Application\chrome.exe) to Director's new 'Internet' group in the live cockpit data (Applications\Data\Advanced Director Cockpit\data\cards.json; backup cards.json.bak-20261009-2054-chrome).
- 2026-10-09 8:58 PM ET (voice): Added Chase card (https://chaseonline.chase.com/Logon.aspx?fromLoc=ALL&LOB=COLLogon) to the Bills group in live cockpit data (backup cards.json.bak-20261009-2058-chase).
- 2026-10-09 8:59 PM ET: Bills group: Chase card renamed 'Chase Freedom'; added 'Aven (25k Loan)' card (https://my.aven.com/passwordLogin).
- 2026-10-09 9:00 PM ET: Bills group: added 'American Education Services' card (https://login.aessuccess.org/accountAccess/index.cfm?event=common.logout).
- 2026-10-09 9:01 PM ET: Bills group: added 'Capital One (2700)' card (https://capitalone.com).

## 2026-10-09 ~9:10 PM ET - group reorder, card iconImage, exe pane drop fix, replace-on-drop, equal card widths (voice)
- Group drag reorder (www js/css, also HTML cockpit): drag a group header (summary) to move it. Top-level groups move among top-level groups; subgroups only within their parent. A blue line above or below the target shows where it lands; the dragged group dims. Drop rewrites order 1..n on that level and saves through /adc-data (groups.json, _readme kept). Click still collapses/expands. Card drag between groups unchanged. Fix inside: a drop redraws the tray, so the drag state is cleared on drop (otherwise later pane drops were ignored and the page stayed "busy").
- Card iconImage (optional): relative path under the page folder (assets/icons/chrome.png) or http(s) URL; shown as an image in place of the emoji, emoji stays the fallback. Saved by pagePayload. cards.json _readme updated (live data, HTML data, www\data). Chrome card c87a1422 set to assets/icons/chrome.png (icon extracted from chrome.exe, 32x32, in www\assets\icons and HTML assets\icons). Live backup cards.json.bak-20261009-20261009-2108-iconimage.
- Exe card pane drop (EmbeddedApps.cs): cause was Chrome's single-instance hand-off. chrome.exe started by the pane exits ~250 ms later after passing the request to the running Chrome, and the embed treated that exit as "program exited; pane cleared" (embed.log 20:54-20:55). Now, if the started exe exits before showing a window, the embed looks for a NEW window (not open before the drop, not in another pane) of any process with the same exe name and embeds that. Shared-process windows are released with WM_CLOSE to that window only; a 1 s check clears the pane when the window is gone. Generic for any exe; Bambu path unchanged. Needs the rebuilt exe: staged build is swapped into Pub automatically when the cockpit closes (V:\_Scripts\_Logs\adc-wv2-swap-20261009.log).
- Replace on drop: a card or pane dragged onto a filled pane replaces it. WV2: while a drag is in progress the page reports it like a popup, so the host hides pane views and embedded app windows and the drop reaches the page; the old pane is released (web: new URL; exe: window detached and WM_CLOSE). Pane-header drags now also set body.adc-dragging (HTML cockpit iframes are covered during the drag).
- Equal card widths: root cause was one width per group, so a group with one short card (Banking: Joint / reserves) drew a narrow card, and cards in a collapsed group measured 0 and were skipped. Now one width for every tray card, measured from icon + title (description wraps), re-run when a group opens; cards in the same group also share one height.
- Backups: *.bak-20261009 next to each changed Source and Pub file.
- 2026-10-09 9:13 PM ET: Bills group: added 'SoFi (33k)' card (https://login.sofi.com).
- 2026-10-09 9:14 PM ET: Workshop group: added 'Grok Bot' exe card (C:\Program Files\Grok Bot\Grok Bot.exe) with extracted icon assets/icons/grok-bot.png.
- 2026-10-09 9:17 PM ET: Trading group: added 'Tasty Trading' card (https://my.tastytrade.com/login.html).
- 2026-10-09 9:18 PM ET: Tasty Trading card switched from website to desktop app (C:\Program Files\tastytrade\tastytrade.exe), icon assets/icons/tastytrade.png.

## 2026-10-09 ~9:25 PM ET, per-card password vault (voice request) - Source built, NOT published

Backups: `V:\_Data\CaspersCreations\Grok\Scrap\adc-vault-backup-20261009-211304\` and `...-212028\` (plus `*.bak-20261009-*-vault` beside each edited file).

- Vault service: `V:\_Data\CaspersCreations\Grok\Scrap\frame-proxy\adc_vault.py` (new), hooked into `frame_proxy.py` (`/adc-vault/*` route + guarded import). Proxy restarted (127.0.0.1:8791 only).
- Vault file: `V:\_Data\CaspersCreations\Applications\Data\Advanced Director Cockpit\vault\vault.dat` (folder ACL: Christian + SYSTEM only). Not under any served root. AES-256-GCM; data key wrapped by a PBKDF2-SHA256 (600k, random salt) PIN key and then by DPAPI CurrentUser. Key is in proxy memory only; wiped on Lock or when no cockpit session has pinged for 2 min. cards.json is never written by the vault.
- Page: `www\adc-vault.js` (new, also in the HTML cockpit). Login button before Detach on every pane header; Vault / Lock vault button in the title row; PIN dialog (first use = set twice, min 6 chars); per-card dialog (username, password, optional extra field, extra allowed sites, auto-submit off by default, Delete login).
- `www\advanced-director-cockpit.js`: added `window.__adcPaneInfo(slot)` before `discardFrame` (card id/url/name for a pane, no secrets). `www\advanced-director-cockpit.html`: script tag for adc-vault.js before `</body>`.
- `MainWindow.Vault.cs` (new): same-origin forwarder https://adc-wv2.local/adc-vault/* -> 127.0.0.1:8791 (Origin https://adc-wv2.local); on pane NavigationCompleted asks the page for that card's login; injects the fill only if the pane is still on the exact https origin the vault approved.
- `MainWindow.xaml.cs`: three one-line hooks (ServeLocal -> ServeVault, ApplyShellMessage `adc-vault-fill`, InitPaneAsync -> AttachVault).
- `dotnet build` passes (test output `C:\Users\ccasperslocal\grokwork\vault\wv2build`). Running Pub exe not touched. Needs the usual DevOps Source-to-Pub publish (close the Pub exe first); include MainWindow.Vault.cs and www\adc-vault.js.
- HTML cockpit: published via publish.ps1 (adc-vault.js, advanced-director-cockpit.js/.html). Caddy: added loopback-only `/adc-vault/*` route (reloaded, not restarted).

## 2026-10-09 ~9:40 PM ET - round 2: group tabs + detach, pane move, embed fit, Chrome profile, renames, group menu, app icon (voice)
Build: one combined Release build from current Source (incl. vault: MainWindow.Vault.cs + 3 hooks, www\adc-vault.js, html script tag) staged in %USERPROFILE%\adc-work-20261009\staged2; swap armed (swap-when-closed-r2.ps1, log V:\_Scripts\_Logs\adc-wv2-swap-20261009.log) installs it into Pub when the cockpit closes, then publishes www\advanced-director-cockpit.html + www\adc-vault.js. Page js/css already in Pub. Backups: *.bak-20261009-tabs / -chrome / -p2..-p5 beside Source files; Pub exe/dll/pdb kept as .bak-20261009-r2 at swap.
- Favicons: GET /adc-favicon?url= (MainWindow.Favicons.cs) finds the site's best icon (apple-touch, largest link icon, manifest, /favicon.ico, Google s2), caches it as PNG; cards without an icon use it. Live after the reopen. HTML cockpit: auto-fetch off (needs a proxy route).
- Group as tab: tab button on a group header (or middle-click) opens the group as its own top tab; layout picked by card count. Inactive tabs stay alive (WV2: panes/embeds hidden, not released; HTML: iframes kept unless the grid changed). Tabs persist. Detach (button on the tab or drag the tab out): WV2 moves the tab's panes and embedded windows into a new native window (no reload, exes not restarted); closing that window hands the tab back (re-attach). HTML: opens the tab in a new browser window (?solo=). Old host without detach support: toast, tab stays.
- Pane move: drag a pane title bar onto another pane to swap them (web panes and embedded exes, no reload or relaunch), blue "Drop here" highlight on the target. A drag cannot start on the embedded app's own title bar (use the cockpit pane header).
- Embed fit: every embedded exe fills its pane exactly (DPI-aware, compensates invisible window borders, e.g. Electron/Grok Bot ~8 px), re-fits on resize and every 0.5 s if the app moves itself; if the app's minimum size is larger than the pane it is anchored top-left and clipped, never offset.
- Chrome/Edge embed: launched with --profile-directory="<last used profile from Local State>" --new-window, so the profile picker no longer gets embedded. Optional card fields: args (full override), profile. Generic follow: if the embedded window closes within 3 min and a new window of the same program appears (45 s watch), the pane embeds that one. Other Chrome windows are never touched.
- Group title rename: click the title text (text cursor + pencil only over the text); Enter or click away saves to groups.json, Esc cancels. Chevron/rest of header still collapse with the pointer cursor; dragging still reorders. No edit mode needed; both cockpits.
- Tab rename: click the ACTIVE tab's label to rename it (group tabs and the existing tabs); label only, id and routing unchanged. Clicking an inactive tab's label switches to it.
- Group right-click menu (registry: label, icon, enabled(group), action(group)): Edit group, Add card, Add subgroup, Delete group. Delete is enabled only when the group is empty (no cards, no subgroups; grayed with a tooltip otherwise), confirms, re-reads groups.json before saving. Closes on Esc or outside click; stays on screen near edges. Works without edit mode. The edit-mode "Add subgroup" button is removed (action lives in the menu); edit mode otherwise unchanged. Known: the Add card dialog title reads "Edit card" (existing function).
- App icon: red triplane on navy (assets\icons\adc-icon.ico 16-256 + adc-icon-16..512.png). Exe ApplicationIcon (title bar, taskbar, shortcuts via exe,0), www and HTML favicons. No other UI changed. Windows icon cache may need ie4uinit -show (the swap runs it).
- Notes: Edge resolves advanceddirectorcockpit.local to 192.168.100.55 (403) - noted, not fixed. Pub .cs copies overwritten by publish without .bak (Source backups exist).- 2026-10-09 9:40 PM ET: Trading group: added 'TraderSync' card (https://my.tradersync.com/login).
- 2026-10-09 9:43 PM ET: Bills group: added 'Splash (10k)' card (https://my.splashfinancial.com/account/continue/login).
- 2026-10-09 9:44 PM ET: Bills group: added 'Upgrade' card (https://www.upgrade.com).
- 2026-10-09 9:45 PM ET: Bills group: added 'Amazon Prime (USBank MC)' card (https://auth.usbank.com).
- 2026-10-09 9:46 PM ET: site logos set for Splash (10k), Upgrade, Amazon Prime (USBank MC).
- 2026-10-09 9:47 PM ET: Bills group: added 'Wells Fargo (Tesla)' card with Wells Fargo logo.
- 2026-10-09 9:48 PM ET: Wells Fargo (Tesla) URL cleaned to https://connect.secure.wellsfargo.com/auth/login/present?origin=cob&LOB=CONS (dropped error=yes and destination=brokerage).

## 2026-10-09 ~9:50 PM ET - embedded apps: call drops on tab switch, keyboard, frozen drops; delete empty-only; Upgrade icon (voice)
In the same staged build (staged2 9:47 PM; the armed swap installs it when the cockpit closes). Backups *.bak-20261009-r6.
- Call drops (Grok Bot voice): cause found in embed.log. The running build has no per-tab slots, so switching tabs reused slot 0 for another card and the cockpit RELEASED Grok Bot (WM_CLOSE to its process) and restarted it on the way back: 21:23:30, 21:26:16 (cockpit restart), 21:29:01, 21:37:13, 21:39:41, 21:40:23, matching the new call ids. Fix: tabs keep their own programs (space keys); a program on a tab that is not shown, or under a page popup, is PARKED off-screen: frame still shown, same size, same parent, no SetParent, no hide, no minimize, no resize, no reload. Closing the cockpit now hands every program window back running (no WM_CLOSE) and remembers it (%LOCALAPPDATA%\AdvancedDirectorCockpitWV2\handback.txt); the reopened cockpit re-embeds that same window instead of starting a new copy. Pane replace/close still closes the program on purpose.
- Embed fill: part of the same fix (FitChild + drift re-fit + park keeps the fitted size).
- Keyboard in embedded apps: cause: a click activated the cockpit's frame window and keyboard focus stayed on the frame (cross-process child never got focus). Fix: the frame passes focus to the program window (WM_SETFOCUS -> SetFocus, with AttachThreadInput only if needed, detached right after). Cockpit accelerators do not see keys while the app has focus (different top-level window). IME / Ctrl+C / Ctrl+V are handled by the app itself once it has real focus.
- Dropped card "did nothing" (CFD, 9:41-9:42): cause: closing a program waited up to 60 s on the cockpit's UI thread (tastytrade embed failed at 21:41:18, still running at 21:42:18), so the whole cockpit froze for a minute; the CFD drop ran after the freeze (CFD is loaded in slot 0 now). Fix: close waits run off the UI thread. Not related to the vault, proxy, Caddy or favicons (cfd.local:8080 returns 200 via hosts 127.0.0.1 and via 192.168.100.55; the cert is trusted). tastytrade.exe showed no main window within 150 s (separate issue).
- Delete group: deleteGroup itself now refuses unless the group is empty (no cards, no subgroups), so the edit-mode Delete button and the menu obey the same rule; it never deletes cards or subgroups (both cockpits; live via Pub www).
- Upgrade card icon: the 392-byte icon was the real Upgrade "U" (not the globe) but blurry; replaced with a 128 px version of the site's own 180 px icon (Source + Pub, both cockpits).- 2026-10-09 9:49 PM ET: Bills group: added 'Best Egg (6.5k)' card (https://www.bestegg.com/) with Best Egg logo.

## 2026-10-09 ~10:00 PM ET - multi-monitor: one cockpit process, more windows (voice)
Staged as r3 (%USERPROFILE%\adc-work-20261009\staged3, 9:57 PM; includes everything from r2). Waiter swap-when-closed-r3.ps1 installs it when the cockpit closes, then publishes the WV2 page js (New window button) and creates the Start-menu shortcut. Backups *.bak-20261009-r7 (Source), *.bak-20261009-r3 (Pub exe/dll/pdb at swap).
- Guard (before): there was NO single-instance code in the exe. "Launching again focuses the existing one" is the pinned taskbar button (a click on a running pinned app switches to it); Shift+click or the Start-menu entry started a second process, which restored the same panes and could fight over embedded apps.
- Now (App.xaml.cs): one process per Windows user (per-user mutex + named pipe). Running the exe again brings the cockpit to the front; "AdvancedDirectorCockpitWV2.exe --new-window" asks the running cockpit to open another window; --multi-instance keeps the old separate-process behaviour (testing only). If the running cockpit does not answer in 3 s, the exe starts normally.
- New window: "New window" button in the title row, Ctrl+Shift+N, exe --new-window, taskbar jump list task and Start-menu shortcut "Advanced Director Cockpit - New Window". A new window opens on the next monitor.
- Every window (main, Window 2/3..., detached tabs) is created by one function (MainWindow.Windows.cs OpenCockpitWindow) and remembers monitor, position, size and maximized state (%LOCALAPPDATA%\AdvancedDirectorCockpitWV2\windows.json); it reopens on that monitor, or centered on the primary monitor if that monitor is gone. New windows keep their own active tab, pane layouts and detached tabs (page storage prefixed per window); windows open when the cockpit closed come back next start; a window closed on its own does not.
- Closing the main window closes the whole cockpit (all windows), so the process ends and staged updates install.
- One app in one window: an exe card already running in another cockpit window is not started or grabbed again (log: "already runs in another cockpit window").
- Same process: the proxy session and the WebView2 profile are shared. The vault token is per page by design (memory only), so a new window asks for the PIN once.
- A copy of the exe without www beside it now falls back to the Pub page instead of an error.
- Tested with a test copy of the exe: second launch handed off in 221 ms and exited; --new-window opened Window 2 on DISPLAY2; a plain relaunch kept one process; closing main ended the process; placements restored; missing monitor -> primary; a window closed alone was not reopened. Page: button + Ctrl+Shift+N post the request; Window 2 keeps its own tab.
- AES pane (9:55 report): at 9:56 the pane shows the AES Sign In page (redirected from the card's logout URL), visible and on top; no host fault found. The card url is a LOGOUT url (login.aessuccess.org/...event=common.logout), so every open logs out first and takes extra redirects; suggest https://access.aessuccess.org/sign-in.- 2026-10-09 10:01 PM ET: Internet group: added '1Password' exe card (C:\Users\ccasperslocal\AppData\Local\1Password\app\8\1Password.exe) with app icon.
- Delete group dialog (adc-gmenu-close): the confirm now shows 'Deleting...' and closes when the delete finished; on failure it closes and a toast names the error ('Nothing was deleted'). Cancel closes too (both used to return true, which keeps the page's modal open, and an open modal also blocked hot reload). The right-click menu closes as soon as an item is picked. Both cockpits, live in Pub.

## 2026-10-09 22:10 - Vault fill fixes (Grok)
- Fill login: a toast for every outcome (Filled / No saved login for <host> / Vault locked / No login fields found / Blocked: origin mismatch).
- AES: access.aessuccess.org (where the logout URL lands) added to the AES card's allowed sites via pending-hosts merge in adc_vault.py (merge is now kept until a save succeeds).
- Auto-advance: real input events, waits up to 3 s for Next/Continue to enable, prefers the form's submit button, falls back to text; in-page notice 'Auto-advance: clicked ...' / 'no button found'. Never clicks on the password screen unless auto-submit is on.
- C# (staged, swaps on next cockpit close): host answers Fill login requests with the real pane URL, Ctrl+Shift+L inside panes, passes autoAdvance, reports fill result back to the page.

## 2026-10-09 10:16 PM ET - Redesign requirement (not yet built)
- Real build: delete all static/hardwired agent tab code (Launch Pad, Main / Conductor, Caddy, Runway, Sterling, Phoenix, DevOps, BallsLeader, Full Linear) from the html/js/host; the tab row is generated only from saved setups.

## 2026-10-09 r4 (10:08-10:20 PM)
- Build version (adc-version): the csproj sets VersionPrefix 1.4.0 and InformationalVersion = 1.4.0+yyyyMMdd.HHmm, stamped automatically on every build, plus an optional release tag (dotnet publish -p:AdcRelease=r4). AdcVersion.cs reads it at runtime. Every cockpit window title shows it: main 'Advanced Director Cockpit WV2 v1.4.0+20261009.2208 (r4)', New window '... (r4) - Window 2', detached tab 'ADC - <tab> - v...'. The host passes it to the page (window.__adcHostVersion), and the title row shows a small label. The label turns amber with a warning if the page's PAGE_VERSION (1.4.0 in advanced-director-cockpit.js) differs from the host's 1.4.0, or if the host is older than r4. To release, bump VersionPrefix and PAGE_VERSION together.
- r4 install: the first r4 waiter installed v1.4.0+20261009.2208 at 10:10:18 PM. That build was missing the vault worker's latest MainWindow.Vault.cs (auto-advance, real pane address, Ctrl+Shift+L, fill results). Rebuilt from current Source as v1.4.0+20261009.2210 (r4), which includes them (checked in the dll), and staged it in adc-work-20261009\staged4. Stopped the vault worker's own waiter (swap4.ps1, pid 47180, which had already exited, 'another instance running'). Exactly ONE waiter is armed: swap-when-closed-r4.ps1. It copies the whole staged folder, backs up Pub to publish.bak-20261009-r4, re-checks that no cockpit is running right before copying (a 10:17 PM reopen raced the copy; nothing was changed), then publishes the page js and adc-vault.js.
- Drag trace v2: v1's /adc-vault/dbg-* names never reached the proxy log because the host forwards only lowercase-letter vault actions (405 otherwise). v2 uses letter codes (dbgloaded, dbgpd..., dbgds..., dbgde..., dbgdrop..., dbgend...). It is TEMPORARY and turns itself off after 30 minutes.
- Pane title-bar drag fixed (adc-panedrag), live 10:23 PM. Root cause, from the live trace at 10:21 PM: in the real WebView2 the pane header's HTML5 drag started (pointerdown, then dragstart, not prevented) and was cancelled at once (dragend with dropEffect none, no dragenter, panes never hidden), so a pane title drag did nothing. The header was not covered by a native window. Card drags from the tray completed normally. Fix: in WV2, pane headers use a pointer drag with pointer capture (6 px threshold, blue drop highlight, Esc cancels). A drop on another pane swaps them through the same handleDrop and adc-move-pane path, so nothing reloads. The HTML cockpit keeps HTML5 drag.
- Version label (10:21 PM change): the title row shows nothing when page and host versions match; the version is only in the OS window title. An amber chip appears only on a mismatch (or a host older than r4).

## 2026-10-09 10:30-10:41 PM: title-row cluster, pane drag v2, v1.5.0 staged

- **Title-row cluster (live 10:31 PM, both cockpits, css only, `adc-head-cluster`).** Every control in `header .adc-head-tools`
  (Vault, New window, Hide toolbars, status pill, Edit) shares one pill: height 26px, padding 0 11px, radius 999px,
  1px solid var(--border), transparent background, color var(--muted), font inherit 11px weight 500, line-height 1,
  inline-flex centered with 5px icon gap; cluster gap 8px, items centered on one line. Hover: border var(--accent),
  color var(--text), background rgba(255,255,255,.04). Focus-visible: 2px var(--accent) outline, 1px offset. Pressed Edit
  = hover look. Status pill keeps var(--accent) text and a default cursor. Edit hint: 10px, weight 400, muted, opacity .75,
  no border. Unlocked vault pill: green text/border. Nothing outside the corner changed.
- **Pane title-bar drag v2 (live 10:41 PM, WV2, `adc-panedrag2`).** The trace showed 3 pointerdowns on pane A's title bar at
  10:28:21-23 PM, with no drag start and no move after them. The header got no pointer moves after the press. Moves
  and the release are now read at window level (pointer and mouse events), a lost pointer capture no longer ends the drag,
  and a mouse move with no button down ends it as a drop. Trace codes: pxdown, pxmove, pxstart, pxdrop/pxnone/pxcancel,
  pxclick, pxlost. The trace now runs 90 min from page load. v1.5.0 removes the trace and keeps the drag.
- **v1.5.0 staged** = `Advanced Director Cockpit WV2 v1.5.0+20261009.2236 (r5)` (staged5). This is the redesign MVP. Host: r4 (version
  titles) plus the vault worker's later Source changes (auto-advance fill script etc.), plus `adc-reload-pane` (pane menu >
  Reload reloads that WebView2 pane), plus no special 'launch' tab (adc-space-close accepts it; 'launch' is only the first
  setup's storage id). VersionPrefix 1.5.0.
  Page: built at swap time by r15\build15.py from CURRENT Source (live fixes are kept). Setups tab row from tabs.json,
  one-time migration (agent tabs dropped, saved configs become setups, backup in localStorage adc-backup-v150), right-click
  menus (tab, tab row, card, pane, group, layout, tray header 'Cards' with New > New card group / New setup tab, Reload from
  files, vault), admin mode/Edit/bar removed, slim layout row + pane headers, dialogs top, toast top-right, vault pill
  merged with the status ('Vault locked'/'Vault unlocked'). Fix found in testing: migration now removes the agent tabs in
  place (another module keeps the tabs array, which brought them back when the old tab was active).
- **Waiters:** r4 waiter pid 19916 stopped at 10:38 PM (r4 ...2210 never installed; Pub was ...2208). One waiter armed:
  pid 20124, `swap-when-closed-v150.ps1`. On close it rebuilds the page, backs up the data folder to `data.bak-20261009-v150`
  and Pub to `publish.bak-20261009-v150`, installs staged5, writes the page to Source (.bak-20261009-v150) and publishes
  both cockpits. Log: V:\_Scripts\_Logs\adc-wv2-swap-20261009.log.
- **Tests:** headless full v1.5 flows in both cockpits (migration with live data, menus, new setup, autosave to tabs.json,
  close/reopen, delete 'Main', reload persistence, Reload message), delete-group via menu (dialog and menu close, non-empty
  disabled), pane title drag, CFD/exe drop-replace, tab rename, group actions, new window. No errors. Native: dll version
  and host strings verified; NO native window was launched (it would share his live data folder and window-state file).

## 2026-10-10 13:05 - v1.5.1 (r6): vault fills login boxes inside frames (Grok)
- New VaultFrames.cs: tracks every frame in a pane (FrameCreated, nested too) and runs the fill only in frames whose current https origin equals the origin the vault approved (card host or its allowed sites, e.g. *.chase.com covers secure.chase.com). The injected script re-checks location.origin inside the frame.
- MainWindow.Vault.cs: frame load -> page asks the vault for that frame URL; 'frame' fill messages go to matching frames only; manual Fill login also covers current frames; results reported back. PIN, auto-submit off by default, multi-step/auto-advance unchanged.
- www/adc-vault.js: __adcVaultFrameLoaded + frame fills (unapproved frames skipped quietly and remembered until the next save); 'No login fields found' waits briefly for a frame result.
- VersionPrefix 1.5.1, PAGE_VERSION 1.5.1. Built as v1.5.1+20261010.1303 (r6); installed by grokwork\vault\r6\swap-when-closed-r6.ps1 on the next close (also updates Pub www adc-vault.js and PAGE_VERSION).
- Vault data: *.aessuccess.org added to the AES card's allowed sites (pending-hosts merge, applied 13:00:42).
- Tested hidden with a two-origin WebView2 harness (parent.test + cross-origin login.test iframe + decoy ads.test): phone filled, auto-advanced, password filled, not submitted, decoy untouched, in-frame origin guard refused a mismatched origin.

- 2026-10-10 1:06 PM ET (voice): Bills group: added 'Truist (Joint)' card (https://dias.bank.truist.com/ui/login), order 12, id 143d6fe7-67c6-4d33-96b8-751f81240904, Truist logo assets/icons/truist-com.png (site favicon, Source + Pub, both cockpits). Live data cards.json; backup cards.json.bak-20261010-1305-truist.

- 2026-10-10 1:24 PM ET (voice): Internet group: added 'Outlook' app card (local desktop Outlook, C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE, classic 16.0.20430.20092), order 3, id 9715f5d2-acfb-4465-88ea-c9c7c3482459, icon assets/icons/outlook.png (extracted from the exe, 32x32). Workshop group: added 'DigitalOcean' web card (https://cloud.digitalocean.com/), order 4, id 1f1b7836-efe6-4fac-883a-a3f8e3d00651, icon assets/icons/digitalocean-com.png (digitalocean.com apple-touch-icon, 180x180). Icons in Source + Pub, both cockpits. Live data cards.json; backup cards.json.bak-20261010-outlook. No code change; no rebuild.

## 2026-10-10 - Copy card in the card right-click menu (adc-copycard, after r6) (Grok)
- www\advanced-director-cockpit.js: card right-click menu gets 'Copy card' (after 'Edit card...', before 'Move to group'). It makes a new card with a new id (mintUuid, same as Add card) and the same name, app, description, icon, icon image, color, URL and any other card settings, in the same group right after the original; nothing else moves except later cards in that group shifting down one place. Saved like every other card edit (commit -> /adc-data -> cards.json). Toast 'Copied <name>'. Vault logins are kept by card id, so the copy has no saved login; any vault or login-named field is also left off the copy. Drag the copy wherever you want it. Page only; no host change.
- r7 build: VersionPrefix 1.5.2 + PAGE_VERSION 1.5.2 (Copy card patch folded in by Grok 2026-10-10 ~2:30 PM ET; vault own-site allowlist + copy-login fix shipped separately in adc_vault.py at 2:22 PM, no host change).

## 2026-10-10 15:00 - v1.5.3 (r7.1): per-card browser identity for DigitalOcean (Grok)
- New BrowserIdentity.cs; MainWindow.xaml.cs NavigateWhenReady calls ApplyPaneIdentityAsync (new helper) right before the pane navigates to a card URL. A card with "browserIdentity": "edge" in cards.json gets the Edge stable UA for the WebView2 runtime version (Edg/154) and client hints without the 'Microsoft Edge WebView2' brand (CDP Emulation.setUserAgentOverride); other cards keep the default; a pane sent to an unflagged card is reset.
- Page: pagePayload keeps browserIdentity on save (otherwise any card edit would drop it). PAGE_VERSION + VersionPrefix 1.5.3.
- Data: DORMANT. r7.1 was never armed or installed on its own; its code ships inside v1.5.4 (r7.2) but no card is flagged, so every pane keeps the default identity. To use it later, add "browserIdentity": "edge" to a card in cards.json while the cockpit is closed (back up cards.json first).
- Why: DO sign-in failed in the pane with correct values after a DO-only cookie clear; values reach DO intact. Tested off-screen with fake credentials: 12/12 (identity applied/cleared, sec-ch-ua on DO's login POST has no WebView2 brand, DO answers normally).

## 2026-10-10 3:03 PM ET - OPEN ITEM: detach from a secondary window (not yet built)
- Detaching a tab from a secondary (New Window) cockpit window removes it from that window; the main instance keeps all tabs. Revisit later: either detach leaves a copy behind, or add Send tab back to source window.

## 2026-10-10 3:15 PM ET - v1.5.4 (r7.2): vault fill watcher never overwrites a typed password (Grok)
- VaultFillJs (MainWindow.Vault.cs) and the adcVaultFill copy in www/adc-vault.js: a new fill call (every pane NavigationCompleted, frame fill) used to reset the WeakSets and re-set the password field even when the user had typed one, and with Auto-submit on it then clicked Log in with the vault value.
- Now the password is set only when the field is empty or the fill is manual (Fill login / Ctrl+Shift+L, d.force from the 'report' flag); Auto-submit only presses when the field still holds the value the vault itself put there.
- Verified off-screen on cloud.digitalocean.com/login with fake credentials (do-watch harness): old = restart overwrote typed value + 2nd login POST with vault value; r7.2 = typed value kept, 1 POST, manual Fill still overwrites.
- Build: VersionPrefix 1.5.4 + PAGE_VERSION 1.5.4, AdcRelease r7.2; includes r7.1 code (dormant, no card flagged). Installed by adc-work-20261010\r72\swap-when-closed-r72.ps1 at the next full cockpit close (Pub binaries, www advanced-director-cockpit.js and www adc-vault.js). No vault data or proxy change.

## 2026-10-11 ~2:10 PM ET - Edit card form UX (voice, Director approved 2:03 PM)
- Finding first (2:02 PM ask): nothing restricts the Edit card form to AI. No role/token/agent check in page JS or host C# (ApplyAdcSave accepts any well-formed POST). Users open it by right-clicking a card > Edit card..., or Maintain > Edit. A save is refused only when the data files changed on disk since the page loaded (guardedSave "blocked"), which can feel like a lock.
- editCard rewritten (page JS + CSS only, tag adc-editux). Same fields written (name, app, description, icon, color, url, groupId, order); iconImage is never touched; same commit()/saveOverlay() path.
- Labelled 2-column grid with a short hint under each field; single column under 560 px; dialog height fits content and scrolls when tall; Save/Cancel bar stays visible.
- Title, Group, Order marked * required; inline red messages plus a banner instead of silent failure. Order must be a whole number >= 1.
- Icon (emoji): exactly one emoji (grapheme-checked), live preview of what the card shows; hint says when the card's iconImage overrides the emoji, or that web cards get their favicon automatically.
- URL: accepts https://..., a Windows program path (C:\...\app.exe) or other scheme links; a bare domain is normalised to https:// (hint shows it, applied on blur/save); spaces or junk are flagged.
- Save feedback: Saving... (Save disabled), Saved (closes after 0.7 s), or Not saved with the reason (files changed on disk: latest data loaded, press Save again; or cards.json write failed). Edits stay in the form on failure.
- Cancel/Esc/X/backdrop on an edited form asks Keep editing / Discard changes; clean form closes at once. Enter in a field or Ctrl+S saves; focus starts on Title.
- App id and Color class moved under an "Advanced" fold (color has fin/env-dev/env-prod suggestions).
- Applied to Source WV2 www, ADC DB Source www, and live Pub WV2 www (backups *.bak-20261011-editux). Tested headless on the box with a copy of the data (validation, URL normalise, Enter save, discard check, failed save, blocked save + retry, narrow layout, Maintain and right-click paths): all pass. Live on next cockpit reload.

## 2026-10-11 - Card icon picker in the Edit card form (adc-iconpick, on r7.2 + adc-editux) (Grok)
- Edit card form (adc-editux, used by Add card, Add card in group and Edit card): the Icon row (label now 'Icon') keeps DevOps's one-emoji box, live preview, hint and error line, and gets picture buttons under it: 'Emoji' (built-in grid of 48 plus None; Esc closes the grid first, not the form), 'Image file...' (PNG, JPG, ICO, WebP or GIF up to 1 MB; the host converts it to a PNG of at most 64 px and saves it in the Data folder's data\icons as upload_<name>.png, then iconImage = data/icons/upload_<name>.png), 'Use site icon' (web cards only; uses the existing GET /adc-favicon, which saves data/icons/<host>.png), 'Remove picture' and 'Reset' (back to what the card had when the form opened). The preview shows the chosen picture. The picture path is a hidden input in the form's fields, so the form's Unsaved changes status, discard prompt, Enter / Ctrl+S save and Saving / Saved / Not saved line all cover it. Save sets or removes iconImage only when the picker changed it; Save waits (Not saved + banner) while a picture upload is still running. No new card fields.
- Host: new MainWindow.Icons.cs (ServeIconUpload + IconStore.Save): POST /adc-icon {name, data(base64)}. Checks the extension and the file's first bytes, max 1 MB, SVG refused (it can carry script on the cockpit's own origin), decodes with ToPng (MainWindow.Favicons.cs), writes only inside <data>\icons (created if missing; the same folder MainWindow.Favicons.cs uses, served as data/icons/ by ServeLocal). Upload names always start with upload_ and site icons never contain '_', so they cannot collide; never overwrites: an identical earlier upload (SHA-256) is reused (site icons and hand-placed files are never reused or touched), a taken name gets -2, -3... One hook in MainWindow.xaml.cs ServeLocal. Nothing is written into www or Pub at runtime. MainWindow.Vault.cs not touched.
- Note: on a web card, 'Remove picture' shows the emoji, but the existing automatic site-icon fill (ensureFavicons) puts the site icon back after the next start (the form's hint says so).

## 2026-10-11 ~2:22 PM ET - v1.5.5 build + swap armed (r7.2 + adc-editux + adc-iconpick) (Grok)
- r7.2 swap waiter (pid 28048, armed 3:17 PM Oct 10, never installed: cockpit never fully closed) DISARMED 2:17 PM per Conductor; superseded by this build.
- P2M Launch icon-picker patch (SHA-256 4FCFC2E9...408D) applied to Source (base hashes verified); git apply converted line endings, so js/css/Icons.cs/TWEAKS.md were replaced with P2M's byte-exact patched/ files. PAGE_VERSION and VersionPrefix 1.5.4 -> 1.5.5.
- Built in adc-work-20261011\r75\build (dotnet publish -c Release, no warnings): 1.5.5+20261011.1419. Tests: icon-picker 51/51, editux 32/32 + 8 path checks (headless, box).
- Staged r75\staged (binaries) + r75\stagedwww (advanced-director-cockpit.js/.css, adc-vault.js r7.2). Waiter r75\swap-when-closed-r75.ps1 (same as r7.2 incl. race re-check), launched hidden (WMI, SW_HIDE); log r75\swap-r75.log. Guards: Pub dll = r7 1.5.2+20261010.1425 and www MD5s as of 2:20 PM.

## 2026-10-11 adc-v156 (WV2 1.5.6): three fixes

1. New tabs stay in the cockpit (Splash "Manage my loan"). MainWindow.xaml.cs OnPaneNewWindow: a link the user
   clicked that asks for a new tab or window (target=_blank, or window.open with no size or position) to an http(s)
   address now opens in the same pane (Back returns). Sized pop-ups, pop-ups without a click, about:blank and the
   cockpit's own pages keep the existing pop-up window. All cards.
2. 1Password card hand-off (EmbeddedApps.cs): an app that hands off to a copy that is already running may show a
   window that already existed but was hidden. Such windows are no longer skipped (only windows that were visible
   before the drop are). When no window qualifies, embed.log lists up to 12 candidate windows (class, style, size,
   owner, title length; never the title text). This does not prove 1Password can be embedded; no fallback added.
3. Right-click menus no longer blank the panes. Page popup module sends menu rectangles; MainWindow.xaml.cs
   ApplyModal hides only panes a menu covers (dialogs and drags still hide all) and first sends a
   CapturePreviewAsync snapshot that the page shows in the pane's place (.adc-pane-snap). EmbeddedApps.Suspend takes
   a hit test so only covered embedded apps are parked. Backups: *.bak-20261011-v156.

## 2026-10-11 adc-v157 (WV2 1.5.7): 1Password card hand-off

Finding: 1Password 8 (Electron) closes its main window to the tray by destroying it; a second 1Password.exe hands
off to the running copy and exits, and the running copy opened no window (6 drops; also one test with --toggle).
So there was no window to embed. Not elevation (both Medium), not a reparent refusal (never reached SetParent).
EmbeddedApps.cs:
- after a hand-off with no NEW window for 5 s, the program's already-visible main window is taken if no pane in
  any cockpit window holds it (the real window; logged "taking the running ... window");
- at 5 s the pane shows a note (page window.__adcEmbedNote): open the app from its tray icon and it moves into the
  pane; the wait is extended by 120 s. If still nothing, the pane keeps a final note instead of going blank;
- SetParent is checked (GetAncestor); a refusal is logged with the error and both DPI awarenesses, the window is
  given back unchanged, and the pane says so. Pane frames are created with mixed DPI hosting
  (SetThreadDpiHostingBehavior MIXED) so a per-monitor-aware window can sit in the system-aware cockpit;
- one log line per hand-off with the program's hidden top-level windows (class, size; never titles);
- ShowArgsByImage (args that make a running copy show its window) is empty: nothing proven for 1Password.
No fallback view. Backups *.bak-20261011-v157.

## 2026-10-11 adc-taskbar: embedded app windows no longer take over the taskbar, Start or Alt+Tab (Grok)
Finding: a program window moved into a pane (SetParent, WS_CHILD of the pane frame) kept its taskbar button; the shell is
not told about a reparent. A click on Chrome's taskbar button then activated the hidden pane window, and a Chrome start
from Start or a shortcut opened its new window next to Chrome's last active window, which was the pane window parked
off-screen (Chrome keeps only ~30 px of a new window on screen), so "nothing opened". Embedded Chrome windows use the
user's normal Chrome profile (--profile-directory, --new-window), so they are in the same taskbar group.
EmbeddedApps.cs (partial now) + new EmbeddedApps.Shell.cs + new EmbedShellLogic.cs:
- while embedded, the program window has WS_EX_TOOLWINDOW and no taskbar tab (ITaskbarList::DeleteTab, checked again
  every 5 s), so it is not on the taskbar or in Alt+Tab; a taskbar click or Start launch goes to the program's own
  normal windows or opens a new normal window;
- on release, cockpit close (hand-back), process exit and an unhandled exception, the exact original style and ex style
  come back and the taskbar tab is added again; %LOCALAPPDATA%\<app>\embedded-windows.txt records each embedded window's
  original styles so the next start repairs a window left behind by a crash or kill (styles, taskbar tab, moved on screen);
- a pane program activated from outside while its pane is not shown (a link opened in the embedded Chrome, a toast)
  brings the cockpit forward on the tab that holds it (MainWindow.xaml.cs: _embeds.Reveal = RevealEmbed, page
  window.__activateView);
- a new top-level window of an embedded program that opens mostly off-screen is moved onto the screen under the mouse;
- the hand-off never takes the user's already-running browser window (chrome, msedge, brave, vivaldi, opera, firefox);
  1Password and other apps still can (adc-v157).
Not changed: page, csproj, versions, Vault, Icons, Favicons. Source patch only; not built.

## adc-v159 (2026-10-11) tab drag-to-reorder
- Cause: bindTabStripDrag() only let a tab drag in Edit mode or for group tabs (draggable=false + dragstart preventDefault), even though saved-setup tabs show the grip and the 'drag to reorder' hint; HTML5 drag is also unreliable in WebView2.
- Fix (JS module 'adc-v159' + moveTabTo()/window.__adcMoveTab): pointer-based tab drag in any mode, 6 px threshold, blue drop bar (.adc-tab-drop-ind), Esc cancels, click suppressed after a drag. Drop calls moveTabTo -> saveOverlay (tabs.json). Dragging more than 60 px below the strip (or out of the window) detaches via __adcDetachTab (same rule as the old tear-off). HTML5 dragstart is blocked only on #tabs .tab chips; card tray drags onto panes are untouched. CSS: drop bar, .tab-dragging, .adc-tab-tear.
- Backups: *.bak-20261011-v159.

## 2026-10-11 data: Amazon web card in Workshop (6:20 PM ask)
- Live data ...\Data\Advanced Director Cockpit\data\cards.json (backup cards.json.bak-20261011-amazon): new card id 60d0816b-bac5-4995-8bee-a780953edff7, groupId workstop, order 5 (after DigitalOcean), app/name Amazon, description Amazon.com, icon U+1F6D2, color fin, url https://www.amazon.com, iconImage assets/icons/www-amazon-com.png (existing PNG, present in all six assets\icons folders). Shaped like the DigitalOcean card. No other card changed.
- The running cockpit picked it up within seconds and its favicon auto-fetch first set iconImage to data/icons/www-amazon-com.png; switched to the existing assets path as asked.
- Cockpit cards have no light/dark/none tile option (CFD-only). ADC DB not changed: its data is a cache written only by the NonBalls Local API (Postgres).

## adc-icontile (2026-10-11, WV2 1.5.10 / DB 1.5.8) card icon tiles
- Every card icon (tray tiles, Maintain rows, Edit card preview; all via setCardIcon) sits on a small tile: light = #F4F4F5, 3px padding, faint 1px border, 6px corners (default); dark = #1F2023 with a faint light border; none = no tile (old look). Emoji-only cards get the same tile, same size as picture tiles, so the tray stays aligned.
- Per card: "iconTile": "dark" | "none" in cards.json; light is the default and is never stored (missing or unknown value = light). Save payload keeps it like iconImage; Copy card copies it.
- Edit card form: 'Icon tile' segmented control Light | Dark | None next to the emoji box and the live preview (radiogroup, arrow keys). Picture buttons / Use site icon unchanged.
- Page-only (JS + CSS); host and NonBalls Local API store card objects whole, so no C#/API change. CFD untouched. Backups *.bak-20261011-v1510.

## 2026-10-12 data: TastyFX (MT5) app card in Trading (11:34 AM ask)
- Live cards.json (backup cards.json.bak-20261012-mt5): id 36be6362-4607-47aa-9723-bdaa849a2951, groupId trading, order 4 (after Tasty Trading 2 / TraderSync 3), app/name TastyFX (MT5), url C:\Program Files\tastyfx MetaTrader 5\terminal64.exe (raw path with spaces, same as Bambu Studio; MetaTrader 5 Client Terminal 5.0.0.6256), icon U+1F4C8, color fin, iconImage assets/icons/tastyfx-mt5.png (32x32 PNG extracted from terminal64.exe, in all six assets\icons folders). No iconTile (renders Light). No other card changed.

## adc-keyfocus (2026-10-12, WV2 1.5.11 / DB 1.5.9) typing in embedded Chrome
- Symptom: Chrome in a pane took a paste in the address bar but typed keys and Enter did nothing.
- Cause: the program window is a WS_CHILD of the pane frame, so Windows never sends it WM_ACTIVATE/WM_NCACTIVATE (the frame is the active window). Chromium only gives its input method/text input keyboard focus on activation; menu paste needs no keyboard focus.
- Fix (EmbeddedApps.cs FrameProc/ForwardActivation): the frame passes its activation on to the embedded window (WM_NCACTIVATE + WM_ACTIVATE, active on frame activate/focus, inactive on frame deactivate), via SendMessageTimeout SMTO_ABORTIFHUNG 300 ms so a hung app cannot freeze the cockpit. Logged once per slot. Backups *.bak-20261012-v1511.

## adc-topcol (2026-10-12, WV2 1.5.12 / DB 1.5.10) collapse the top per group (approved mockup v2)
- 'Collapse top' button next to 'Hide toolbars' (both kept). Collapsed: title bar, group tabs and layout toolbar fold into a 6 px strip; panes gain the height (111 -> 6 px at 1920x1040).
- Hover the strip ~250 ms = peek; the top PUSHES the panes down (chosen over hide+snapshot: native panes cannot be drawn over, and reflow keeps embedded programs live); folds again ~750 ms after the mouse leaves the top (incl. leaving the page into a native pane). Click the strip (or Enter) = expand.
- Ctrl+Alt+T toggles: page keydown, plus host hotkey (MainWindow.TopCollapse.cs) registered only while this cockpit window or a window it owns (pane frames) is the foreground window, so it works while an embedded program or web pane has focus and is free for other apps otherwise. Host posts {type:'adc-topcol-toggle'}.
- Saved per group as "topCollapsed": true on the tab in tabs.json (omitted when expanded); collapsed groups show a small marker on their tab. Pane bounds re-reported via the slot ResizeObserver, so web panes and embedded programs resize in both states.
- Backups *.bak-20261012-v1512 (Source), publish.bak-20261012-r82 (Pub).
