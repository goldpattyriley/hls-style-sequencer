using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace HlsRecreation
{
    // WaveOut position is the preview clock. The UI timer only chooses which frame to draw.
    // No network or physical-light timing is inferred from this clock.
    public sealed class AudioClock : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            public ushort FormatTag, Channels;
            public uint SamplesPerSecond, AverageBytesPerSecond;
            public ushort BlockAlign, BitsPerSample, ExtraSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr Data;
            public uint Length, BytesRecorded;
            public UIntPtr User;
            public uint Flags, Loops;
            public IntPtr Next;
            public UIntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MultimediaTime { public uint Type, Value, Reserved; }
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(IntPtr handle, ref MultimediaTime time, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
        private IntPtr handle, header;
        private GCHandle pinned;
        private WaveFormat format;
        private bool prepared, submitted;
        private int durationMs;
        public bool IsPlaying { get { return handle != IntPtr.Zero && submitted && PositionMs < durationMs && !Done; } }
        public int DurationMs { get { return durationMs; } }
        private bool Done
        {
            get { return header != IntPtr.Zero && (((WaveHeader)Marshal.PtrToStructure(header, typeof(WaveHeader))).Flags & 1) != 0; }
        }
        public int PositionMs
        {
            get
            {
                if (handle == IntPtr.Zero || !submitted) return 0;
                MultimediaTime time = new MultimediaTime(); time.Type = 2;
                Check(waveOutGetPosition(handle, ref time, (uint)Marshal.SizeOf(typeof(MultimediaTime))), "read audio position");
                long value;
                if (time.Type == 2) value = (long)time.Value * 1000 / format.SamplesPerSecond;
                else if (time.Type == 4) value = (long)time.Value * 1000 / format.AverageBytesPerSecond;
                else if (time.Type == 1) value = time.Value;
                else throw new InvalidOperationException("The audio device returned an unsupported position unit.");
                return (int)Math.Min(durationMs, value);
            }
        }
        public void Play(string path)
        {
            Stop();
            byte[] data = ReadPcm(path, out format);
            durationMs = (int)((long)data.Length * 1000 / format.AverageBytesPerSecond);
            Check(waveOutOpen(out handle, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0), "open audio device");
            try
            {
                pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
                WaveHeader value = new WaveHeader(); value.Data = pinned.AddrOfPinnedObject(); value.Length = (uint)data.Length;
                header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                Marshal.StructureToPtr(value, header, false);
                Check(waveOutPrepareHeader(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))), "prepare audio");
                prepared = true;
                Check(waveOutWrite(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))), "start audio");
                submitted = true;
            }
            catch (Exception primary)
            {
                try { Stop(); }
                catch (Exception cleanup) { throw new InvalidOperationException(primary.Message + " Audio cleanup: " + cleanup.Message, primary); }
                throw;
            }
        }
        public void Stop()
        {
            List<string> failures = new List<string>();
            if (handle != IntPtr.Zero)
            {
                uint reset = Attempt(delegate { return waveOutReset(handle); }, "stop audio", failures);
                if (reset == 0) submitted = false;
                if (prepared)
                {
                    uint unprepare = Attempt(delegate { return waveOutUnprepareHeader(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))); }, "release audio buffer", failures);
                    if (unprepare == 0) { prepared = false; submitted = false; }
                }
                uint close = Attempt(delegate { return waveOutClose(handle); }, "close audio device", failures);
                if (close == 0) { handle = IntPtr.Zero; prepared = false; submitted = false; }
            }
            // A driver that still owns a prepared buffer may still read it. Retain the
            // pinned memory for a later Stop retry rather than freeing live native data.
            if (!prepared)
            {
                if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
                if (pinned.IsAllocated) pinned.Free();
            }
            if (failures.Count != 0) throw new InvalidOperationException(String.Join(" ", failures.ToArray()));
        }
        private static uint Attempt(Func<uint> operation, string name, List<string> failures)
        {
            try { uint code = operation(); if (code != 0) failures.Add("Could not " + name + " (WinMM code " + code + ")."); return code; }
            catch (Exception error) { failures.Add("Could not " + name + ": " + error.Message); return uint.MaxValue; }
        }
        public void Dispose() { Stop(); }
        private static void Check(uint code, string operation)
        { if (code != 0) throw new InvalidOperationException("Could not " + operation + " (WinMM code " + code + ")."); }

        private static byte[] ReadPcm(string path, out WaveFormat wave)
        {
            wave = new WaveFormat(); byte[] data = null; bool found = false;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.BaseStream.Length < 44 || reader.BaseStream.Length > 120L * 1024 * 1024)
                    throw new InvalidDataException("Use a PCM WAV file up to 120 MB.");
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Audio is not a RIFF WAV file.");
                reader.ReadUInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Audio is not a WAV file.");
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunk = new string(reader.ReadChars(4)); uint length = reader.ReadUInt32(); long start = reader.BaseStream.Position;
                    if (length > reader.BaseStream.Length - start) throw new InvalidDataException("WAV chunk is truncated.");
                    if (chunk == "fmt " && length >= 16)
                    {
                        wave.FormatTag = reader.ReadUInt16(); wave.Channels = reader.ReadUInt16();
                        wave.SamplesPerSecond = reader.ReadUInt32(); wave.AverageBytesPerSecond = reader.ReadUInt32();
                        wave.BlockAlign = reader.ReadUInt16(); wave.BitsPerSample = reader.ReadUInt16(); found = true;
                    }
                    else if (chunk == "data") data = reader.ReadBytes(checked((int)length));
                    reader.BaseStream.Position = start + length + (length % 2);
                }
            }
            if (!found || data == null || data.Length == 0 || wave.FormatTag != 1 || wave.BitsPerSample != 16 ||
                (wave.Channels != 1 && wave.Channels != 2) || wave.SamplesPerSecond < 8000 || wave.SamplesPerSecond > 96000 ||
                wave.BlockAlign != wave.Channels * 2 || wave.AverageBytesPerSecond != wave.SamplesPerSecond * wave.BlockAlign || data.Length % wave.BlockAlign != 0)
                throw new InvalidDataException("Use 16-bit mono/stereo PCM WAV audio (8–96 kHz).");
            return data;
        }
        public static float[] ReadWaveform(string path, int bins)
        {
            if (bins < 1 || bins > 8192) throw new ArgumentOutOfRangeException("bins");
            WaveFormat wave; byte[] data = ReadPcm(path, out wave); float[] result = new float[bins];
            int samples = data.Length / 2;
            for (int i = 0; i < samples; i++)
            {
                int sample = (short)(data[i * 2] | data[i * 2 + 1] << 8);
                int bin = (int)((long)i * bins / samples);
                result[bin] = Math.Max(result[bin], Math.Abs(sample / 32768f));
            }
            return result;
        }
        public static int ReadDurationMs(string path)
        {
            WaveFormat wave; byte[] data = ReadPcm(path, out wave);
            return checked((int)((long)data.Length * 1000 / wave.AverageBytesPerSecond));
        }
    }
}
