# Testing version 0.4

Download the [testing release](https://github.com/goldpattyriley/hls-style-sequencer/releases/tag/v0.4.0-testing), extract the whole ZIP and run `HlsStyleSequencer-0.4.exe`. Keep `.exe.config` and `demo.wav` with it.

## Manual test route

1. Open `display-demo.hlsr`. Inspect raw channels and work groups; edit a Level/Ramp clip and verify undo/redo. The sample contains normal editable effects.
2. Open **Preview → Create/Edit Preview**. Move a roof/arch object, edit its Group/Number/nodes and assign raw channels. Save compiles the Preview Control File. Use **Identify Preview Control File** to activate it again, then check preview-to-row lookup.
3. In **Output → Select Output Method**, inspect the sample loopback E1.31 route. Run **Prepare Channels for Output** and inspect raw-slot/controller coverage. Configuration and saved route-enabled flags do not arm physical output.
4. Open **Test Output Lights**, check raw channels and leave physical arming unchecked. Try Solid, Ramp and Chase in recording mode. **All Off** must stay off; Stop/Close should reset session arming.
5. **Compile for Show** writes FSEQ plus its manifest. Check it as current, edit a clip/controller setting, and verify the footer/freshness check requests a new compile. Live preview uses the editable sequence; scheduling is not implemented.
6. Open `custom-demo.hlsr` and `handcrafted.hlsrlib` for custom recipes, rectangular captures and independent library insertion.

Hardware tests are optional and separate: choose the real destination/interface or COM/baud/range, inspect the preparation report, then explicitly enable physical output and start. Actual Windows 7, hardware drivers and received/optical timing have not been verified by the automated checks.

## Reporting a problem

Use the bug-report template in Issues. Include Windows architecture/version, .NET version, app version, repeatable steps, expected/actual behavior, and whether the test was recording or physical. For output problems include protocol, cadence and mapping. Note whether old playback data was recompiled/identified.

## Automated checks

```powershell
.\HlsStyleSequencer-0.4.exe --self-test checks
```

The executable returns after writing `checks/validation.txt` and actual offscreen-control screenshots. The checks use recording/fake transports and an owned loopback receiver; they do not open an audio device, visible application windows, LAN controllers or COM ports. Roughly 60 MB of local fixture space is needed. The shipped executable passed 213 checks.
