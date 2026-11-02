HLS-STYLE SEQUENCER 0.4  -  DISPLAY PREVIEW AND CONTROLLER OUTPUT

Extract the complete ZIP and run HlsStyleSequencer-0.4.exe.
Keep its .exe.config and demo.wav alongside it. The new filename preserves
older running versions; those older programs do not acquire these changes.

Target: Windows 7 x86/x64 with FULL .NET Framework 4.0 or compatible later 4.x.
Native C#4/WinForms, built against exact full .NET4.0 reference assemblies.
No browser, Python, Node, Electron or WebView is needed to run the application.
Actual execution on Windows7 and real controller hardware remain unverified.

DISPLAY PREVIEW  -  HLS ORDER OF OPERATIONS

1. Open display-demo.hlsr for the supplied authored roof/arch sequence, or use
   your existing .hlsr project. Schema2/3 files upgrade in memory to schema4.
   Original HLS project/control formats are not supported.
2. Manage Channels > Manage Raw Channels: physical Universe/Address/Output
   order remain independent of Preview Group/Number and our Preview pixel.
3. Preview > Create/Edit Preview: draw Point, Line, Arch, Rectangle, Matrix or
   Polyline objects. Drag to establish bounds. Select moves the whole object;
   gold handles edit its bounds/vertices. Polyline clicks extend the string;
   double-click completes it. Matrix rows/columns and serpentine order are
   editable. A one-node object lights the whole drawn vector.
4. Set Group/Number/node count, select raw channels in the right-hand list,
   then Assign selected raw -> object (or Assign reverse). Pixel assignment
   follows raw Output order. For multiple nodes, select exactly one raw RGB
   channel or dimmer per node. Several raws may combine on a one-node object.
   Shapes sharing G/N intentionally display the same mapped channels.
5. Browse to a local photograph/background and set opacity. Missing, invalid
   and overly large backgrounds are diagnosed; pictures are not file-locked.
6. Save in the editor compiles a .hlsrpreview file. Use Preview > Identify
   Preview Control File to activate it for playback. Compile/Save is also
   available in the Preview menu. Geometry/mapping edits or changed control
   data mark the runtime preview stale until you compile/identify again.
7. Another sequence can Identify the same file when its raw channel names
   match. Preview assignments are reused; controller addresses are retained.
   Clicking a preview light identifies its timeline raw/Segment/PixelPlane
   row and output routes. Lights outside every work group are reported.

Controller setup and preview files never transmit merely by opening/saving.
The preview's pixel sub-index, file formats and exact geometry controls are
reconstruction choices; HLS Group/Number, editor Save/Compile and separate
Identify workflow are supported by developer/forum/video evidence.

PREPARE, TEST, COMPILE AND PLAY

1. Output > Select Output Method: create E1.31 or Renard routes. Set names,
   physical source universes/range and destination. Choose NIC/COM buttons
   list installed interfaces/ports without opening a controller. A route's
   Enabled checkbox participates in the output plan; physical arming is
   separate and is never saved in the project.
2. Output > Prepare Channels for Output lists every raw channel's actual
   physical slots and covering controllers. Fix errors; inspect unrouted,
   unused and unpatched warnings. RGB ranges cannot be split silently.
3. Output > Test Output Lights defaults to RECORDING / DRY RUN. Check raw
   channels and use Solid, Ramp or Chase with level/color. Start records
   packets without a device. Only explicitly selecting physical output and
   pressing Start physical output opens UDP/COM handles. Stop, All Off and
   closing the dialog stop/join the worker, request blackout and release
   transports. All Off stays off. Errors, skipped ticks and sent frames/s
   are shown; recording retains the latest4096 packets to bound memory.
4. E1.31: unicast IPv4 or per-universe multicast, optional local IPv4 NIC,
   priority, UDP port, full512-slot packets. Output interval defaults25ms;
   minimum23ms follows the E1.31 source limit, not a100fps hardware claim.
5. Renard: one enabled contiguous route per COM port, 8N1, configured baud
   and flattened Universe/Address/byte count. Reserved bytes are escaped.
   Unused tail slots are zero-padded; worst-case serial capacity is checked.
6. Output > Compile for Show writes .fseq plus .fseq.hlsrshow manifest.
   Hashes detect edited sequence/settings or changed rendered data. Failed
   manifest publication restores the previous FSEQ; interrupted/externally
   changed pairs fail freshness checks. Use Check compiled show data after
   reopening. The footer marks data changed by edits as needing compilation.
7. Preview plays the WAV using the audio-device clock. Send to controllers
   is unchecked by default; selecting it and pressing Preview arms output
   only for that session. Stop/end/edit/load/close requests blackout before
   releasing audio. A delayed worker stop retains resources for retry.

The output worker skips obsolete frames instead of sending late bursts.
Sent-frame statistics measure application sending, not received/optical timing.
E1.31 stop sends a black frame plus three stream-termination packets. UDP has
no delivery acknowledgement. Actual controller/serial/audio-optical timing
needs hardware checks. Playlists/scheduling and other HLS protocols are future
work. Compiled manifest data is prepared output; this milestone's live playback
renders the current editable sequence rather than being a scheduled player.

16MB FIX RETAINED

Project Open/Save/recovery and standalone libraries retain streamed XML and
structural/string quotas without the old16MB byte-size gate. Real40000-effect
project and library fixtures above16MB round-trip in the release checks.
Actual memory/model limits still apply. Keep an older running application
open until any unsaved work has been saved separately.

CUSTOM EFFECT AUTHORING

1. Choose a work group and click Custom, or Sequence > Create/modify custom
   effects. Definition management works without a rectangle; placement needs one.
2. Use New, name the effect, then add Level/RampUp/RampDown primitives.
   Each row has a cell length, start/end percentages and stored color. Level is
   flat; its end percentage follows the start. Ramps have both endpoints.
3. Duplicate/remove/reorder rows to create the serial recipe. Total duration is
   the sum of cell lengths. The sampled preview retains colors and endpoints.
   A saved recipe can be renamed/duplicated/deleted; deleting the last recipe
   leaves an empty manager rather than injecting another preset.
4. Select a rectangle in the time grid and choose the recipe through Custom.
   Chase uses row staggering; Random chooses valid candidate cells. Repeat,
   period, coverage, randomized line order/intensity and seed are independent
   placement inputs. Period0 means recipe width in Chase / any valid cell in
   Random. The exact original HLS RNG algorithm is unknown.
5. Color modes: Selected uses placement color; Embedded uses each stored line
   color; Random realizes one hue per primitive. Preview, then Place.

A complete recipe must fit. New results are normal timeline effects with fresh
IDs, saved values and one undoable edit. Playback never rerandomizes them.
Editing a definition later does not change existing placed effects. Right-click
individual effects to edit their timing/intensity/endpoints/color. PixelPlanes
retain their separate spatial painter and do not accept serial time recipes.

EFFECT LIBRARIES

Select a rectangle of complete authored effects, then Library > Add Effect Group
to a Library. Make/open a library, name the item, Capture selection and Save.
Blank rows and gaps are preserved. Partial edge clips are rejected explicitly;
expand the rectangle instead of losing data.

To reuse: choose a saved Group, Insert group, confirm, then click the destination
upper-left grid cell. Esc cancels. Mapping is positional from that anchor; wiring
and channels are not imported. The destination must have enough compatible rows,
time, the same frame interval, and matching dimensions for PixelPlane rows.
The whole insertion is validated before one commit and can be undone at once.

Inserted effects are independent. Modify/delete channel by channel, not through
an implicit live-linked whole-group instance. Edit a captured group on the
normal timeline and recapture it. Stored Custom recipes use the same serial
editor. Import Custom makes an independent project definition; Close applies
imports and Cancel discards them. Library Save/SaveAs are separate from project
Save. Failed saves preserve the prior file/path. Replacing/closing a dirty
library offers Save/Discard/Cancel.

FORMATS / COMPATIBILITY

Own project: .hlsr schema4. Schema2/3 opens with an in-memory upgrade; reading does
not rewrite the source. Legacy ramp rendering is retained unless you explicitly
author its endpoints. Own library: .hlsrlib version1. Atomic saves retain .bak.
Original HLS .hls/.hlsIdata/libraries and browser-prototype JSON are unsupported.

The program is an independently implemented HLS-style recreation, not original
HLS. No HLS/competitor code or graphics are bundled. Historical manual/video
sources support serial cell recipes, color/placement options and rectangular
upper-left library insertion. Definition-manager CRUD, exact newer labels/RNG
and conservative bounds are reconstruction choices. Complete final-version HLS
parity and original native formats are unverified.

Native channel manager, overlapping work groups, raw-member Segments, sparse
PixelPlane mapping/frame painting, waveform editing, undo/recovery and audio-
device-clock preview from the prior milestone remain included. Sequence > Time
resolution selects10/20/25/50/100ms (50ms default). These are data intervals,
not measured physical-controller rates. Controller output is optional and requires an explicitly armed session.
Audio uses local16-bit PCM WAV. Controller/optical timing remains unverified.

REPRODUCE CHECKS

HlsStyleSequencer-0.4.exe --self-test checks

Writes validation logs and actual offscreen WinForms bitmaps; opens no visible
window, audio device, LAN controller or COM port. Native UDP is checked only
against a receiver it owns on127.0.0.1 and an ephemeral port. Fake/recording
transports verify errors, arming, blackout and stop-timeout recovery. Allow
roughly60MB for large-file fixtures. The213-check release gate passed. See validation.txt/json and manifest.json.

REBUILD

powershell -File build.ps1 -ReferenceDirectory "C:\path\to\full-net40-references"

Framework C# compiler: /langversion:4 /platform:x86 /nostdlib with explicit full
.NET4.0 reference assemblies. Official reference pack:
https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net40/1.0.3
Use build/.NETFramework/v4.0 (not Profile/Client). References are build-only,
not bundled. See display-output.html for pictures and primary HLS evidence;
custom-effects.html and channel-workflow.html cover previous milestones.
