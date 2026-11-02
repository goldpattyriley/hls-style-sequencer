using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HlsRecreation
{
    public class Controller
    {
        public string Id = ""; public string Name = ""; public string Protocol = "E131"; public bool Enabled;
        public int FirstUniverse = 1, UniverseCount = 1, FirstAddress = 1, ChannelCount = 24;
        public string Address = "127.0.0.1", LocalAddress = "", ComPort = "COM1"; public bool Multicast;
        public int Port = 5568, Priority = 100, BaudRate = 57600;
    }
    public class OutputSettings { public int IntervalMs = 25; public List<Controller> Controllers = new List<Controller>(); }
    public class OutputPlan { public List<string> Errors = new List<string>(); public List<string> Warnings = new List<string>(); public string Summary = ""; }
    public class OutputPacket
    {
        public string ControllerId = "", Protocol = "", Address = "", LocalAddress = "", ComPort = ""; public int Port, Universe, BaudRate; public byte[] Data = new byte[0];
    }

    public static class ControllerOutput
    {
        public static OutputSettings Clone(OutputSettings source)
        {
            OutputSettings copy = new OutputSettings(); if (source == null) return copy; copy.IntervalMs = source.IntervalMs; if (source.Controllers == null) { copy.Controllers = null; return copy; }
            int i; for (i = 0; i < source.Controllers.Count; i++) { Controller c = source.Controllers[i]; copy.Controllers.Add(c == null ? null : new Controller { Id = c.Id, Name = c.Name, Protocol = c.Protocol, Enabled = c.Enabled, FirstUniverse = c.FirstUniverse, UniverseCount = c.UniverseCount, FirstAddress = c.FirstAddress, ChannelCount = c.ChannelCount, Address = c.Address, LocalAddress = c.LocalAddress, ComPort = c.ComPort, Multicast = c.Multicast, Port = c.Port, Priority = c.Priority, BaudRate = c.BaudRate }); } return copy;
        }
        public static List<string> Validate(OutputSettings settings)
        {
            List<string> errors = new List<string>(); if (settings == null) { errors.Add("Output settings are required."); return errors; } if (settings.IntervalMs < 23 || settings.IntervalMs > 60000) errors.Add("Output interval must be 23..60000 ms."); if (settings.Controllers == null) { errors.Add("Controllers list is required."); return errors; } if (settings.Controllers.Count > 256) { errors.Add("At most 256 controllers are supported."); return errors; }
            Dictionary<string, bool> ids = new Dictionary<string, bool>(StringComparer.Ordinal); Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); Dictionary<string, string> activeRenardPorts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); int i; for (i = 0; i < settings.Controllers.Count; i++) { Controller c = settings.Controllers[i]; ControllerErrors(errors, c, i, ids, names); if (c != null && c.Enabled && c.Protocol == "Renard" && ValidCom(c.ComPort)) { string first; if (activeRenardPorts.TryGetValue(c.ComPort, out first)) errors.Add("Enabled Renard controllers " + first + " and " + Label(c) + " both use " + c.ComPort + "; a COM port supports one contiguous route."); else activeRenardPorts.Add(c.ComPort, Label(c)); } } return errors;
        }
        public static OutputPlan Prepare(Project project)
        {
            OutputPlan plan = new OutputPlan(); if (project == null) { plan.Errors.Add("Project is required."); plan.Summary = "Output cannot be prepared."; return plan; }
            plan.Errors.AddRange(Validate(project.Output)); if (project.Channels == null) plan.Errors.Add("Raw channels list is required."); if (plan.Errors.Count != 0) { plan.Summary = "Output cannot be prepared: " + plan.Errors.Count.ToString(CultureInfo.InvariantCulture) + " error(s)."; return plan; }
            Dictionary<int, bool> physicalUniverses = new Dictionary<int, bool>(); int i, j; for (i = 0; i < project.Channels.Count; i++) { RawChannel raw = project.Channels[i]; if (raw == null) { plan.Errors.Add("A raw channel is null."); continue; } if (raw.Universe < 0 || raw.Universe > 64 || raw.Address < 1 || raw.Address > 512 || (raw.Kind != "Dimmer" && raw.Kind != "RGB") || (raw.Kind == "RGB" && raw.Address > 510)) { plan.Errors.Add("Raw channel " + (String.IsNullOrEmpty(raw.Name) ? i.ToString(CultureInfo.InvariantCulture) : raw.Name) + " has an invalid physical mapping."); continue; } if (raw.Universe > 0) physicalUniverses[raw.Universe] = true; else plan.Warnings.Add("Raw channel " + raw.Name + " is unpatched (universe 0); excluded from physical output."); }
            for (i = 0; i < project.Output.Controllers.Count; i++) { Controller c = project.Output.Controllers[i]; if (!c.Enabled) continue; if (c.Protocol == "E131") { for (j = c.FirstUniverse; j < c.FirstUniverse + c.UniverseCount; j++) if (!physicalUniverses.ContainsKey(j)) plan.Warnings.Add("E1.31 controller " + Label(c) + " routes unused universe " + j.ToString(CultureInfo.InvariantCulture) + "."); } else { int slots = RenardWorstBytes(c.ChannelCount); long capacity = ((long)c.BaudRate * project.Output.IntervalMs) / 10000L; if (slots > capacity) plan.Errors.Add("Renard controller " + Label(c) + " cannot send " + c.ChannelCount.ToString(CultureInfo.InvariantCulture) + " worst-case escaped slots in the output interval; lower count, slow interval, or use higher baud."); }
            }
            for (i = 0; i < project.Channels.Count; i++) { RawChannel raw = project.Channels[i]; if (raw == null || raw.Universe == 0) continue; bool routed = false; for (j = 0; j < project.Output.Controllers.Count; j++) if (project.Output.Controllers[j].Enabled && Covers(project.Output.Controllers[j], raw)) { routed = true; break; } if (!routed) plan.Warnings.Add("Raw channel " + (String.IsNullOrEmpty(raw.Name) ? i.ToString(CultureInfo.InvariantCulture) : raw.Name) + " (universe " + raw.Universe.ToString(CultureInfo.InvariantCulture) + ", address " + raw.Address.ToString(CultureInfo.InvariantCulture) + ") has no enabled route."); }
            StringBuilder summary = new StringBuilder(); summary.Append(plan.Errors.Count == 0 ? "Prepared " : "Output cannot be prepared: ").Append(ActiveCount(project.Output).ToString(CultureInfo.InvariantCulture)).Append(" enabled controller route(s); ").Append(plan.Errors.Count.ToString(CultureInfo.InvariantCulture)).Append(" error(s), ").Append(plan.Warnings.Count.ToString(CultureInfo.InvariantCulture)).Append(" warning(s)."); for (i = 0; i < project.Output.Controllers.Count; i++) { Controller c = project.Output.Controllers[i]; if (c == null || !c.Enabled) continue; summary.Append("\r\n").Append(c.Protocol).Append(" ").Append(Label(c)).Append(": "); if (c.Protocol == "E131") summary.Append("universes ").Append(c.FirstUniverse.ToString(CultureInfo.InvariantCulture)).Append("..").Append((c.FirstUniverse + c.UniverseCount - 1).ToString(CultureInfo.InvariantCulture)).Append(" -> ").Append(c.Multicast ? MulticastAddress(c.FirstUniverse) + " multicast" : c.Address + ":" + c.Port.ToString(CultureInfo.InvariantCulture)); else summary.Append("universe ").Append(c.FirstUniverse.ToString(CultureInfo.InvariantCulture)).Append(" address ").Append(c.FirstAddress.ToString(CultureInfo.InvariantCulture)).Append(" slots ").Append(c.ChannelCount.ToString(CultureInfo.InvariantCulture)).Append(" -> ").Append(c.ComPort).Append(" @ ").Append(c.BaudRate.ToString(CultureInfo.InvariantCulture)); }
            for(i=0;i<project.Channels.Count;i++)
            {
                RawChannel raw=project.Channels[i];if(raw==null)continue;List<string> routes=new List<string>();
                if(raw.Universe>0)for(j=0;j<project.Output.Controllers.Count;j++){Controller route=project.Output.Controllers[j];if(route.Enabled&&Covers(route,raw))routes.Add(Label(route));}
                summary.Append("\r\n").Append(raw.OutputOrder).Append("  ").Append(raw.Name).Append("  U").Append(raw.Universe).Append(":").Append(raw.Address).Append(" (").Append(raw.Kind).Append(") -> ").Append(routes.Count==0?"unrouted":String.Join(", ",routes.ToArray()));
            }
            plan.Summary = summary.ToString(); return plan;
        }
        public static byte[] EncodeE131(Guid cid, string source, int universe, byte sequence, int priority, byte[] slots, bool terminated)
        {
            if (universe < 1 || universe > 63999) throw new ArgumentOutOfRangeException("universe"); if (priority < 0 || priority > 200) throw new ArgumentOutOfRangeException("priority"); if (slots == null || slots.Length != 512) throw new ArgumentException("E1.31 requires exactly 512 slots.", "slots");
            byte[] data = new byte[638]; data[0] = 0; data[1] = 16; byte[] acn = Encoding.ASCII.GetBytes("ASC-E1.17\0\0\0"); Buffer.BlockCopy(acn, 0, data, 4, acn.Length); PutFlagsLength(data, 16, 622); Put32(data, 18, 4); byte[] uuid = CanonicalUuid(cid); Buffer.BlockCopy(uuid, 0, data, 22, 16); PutFlagsLength(data, 38, 600); Put32(data, 40, 2); byte[] name = SourceName(source); Buffer.BlockCopy(name, 0, data, 44, name.Length); data[108] = (byte)priority; data[111] = sequence; data[112] = terminated ? (byte)0x40 : (byte)0; data[113] = (byte)(universe >> 8); data[114] = (byte)universe; PutFlagsLength(data, 115, 523); data[117] = 2; data[118] = 0xa1; data[122] = 1; data[123] = 2; data[124] = 1; Buffer.BlockCopy(slots, 0, data, 126, 512); return data;
        }
        public static byte[] EncodeRenard(byte[] slots)
        {
            if (slots == null) throw new ArgumentNullException("slots"); List<byte> data = new List<byte>(slots.Length * 2 + 2); data.Add(0x7e); data.Add(0x80); int i; for (i = 0; i < slots.Length; i++) { byte value = slots[i]; if (value == 0x7d) { data.Add(0x7f); data.Add(0x2f); } else if (value == 0x7e) { data.Add(0x7f); data.Add(0x30); } else if (value == 0x7f) { data.Add(0x7f); data.Add(0x31); } else data.Add(value); } return data.ToArray();
        }
        public static List<OutputPacket> BuildPackets(Project project, byte[] frame, Guid cid, byte sequence, bool terminated)
        {
            if (project == null || frame == null) throw new ArgumentNullException(project == null ? "project" : "frame"); Model.RequireValid(project); OutputPlan plan = Prepare(project); if (plan.Errors.Count != 0) throw new ArgumentException(String.Join(" ", plan.Errors.ToArray()), "project"); int required = RequiredFrameLength(project); if (frame.Length < required) throw new ArgumentException("Frame is shorter than the project's physical output map.", "frame");
            List<OutputPacket> packets = new List<OutputPacket>(); int i, u; for (i = 0; i < project.Output.Controllers.Count; i++) { Controller c = project.Output.Controllers[i]; if (!c.Enabled) continue; if (c.Protocol == "E131") for (u = c.FirstUniverse; u < c.FirstUniverse + c.UniverseCount; u++) { byte[] slots = SliceUniverse(frame, u); string destination = c.Multicast ? MulticastAddress(u) : c.Address; packets.Add(new OutputPacket { ControllerId = c.Id, Protocol = "E131", Address = destination, LocalAddress = c.LocalAddress, Port = c.Port, Universe = u, Data = EncodeE131(cid, project.Name, u, sequence, c.Priority, slots, terminated) }); } else { byte[] slots = SliceRange(frame, c.FirstUniverse, c.FirstAddress, c.ChannelCount); packets.Add(new OutputPacket { ControllerId = c.Id, Protocol = "Renard", ComPort = c.ComPort, BaudRate = c.BaudRate, Universe = c.FirstUniverse, Data = EncodeRenard(slots) }); } }
            return packets;
        }
        public static byte[] TestFrame(Project project, IList<string> rawIds, string mode, int elapsedMs, int levelPercent, string colorHtml)
        {
            if (project == null || rawIds == null) throw new ArgumentNullException(project == null ? "project" : "rawIds"); Model.RequireValid(project); if (mode != "Solid" && mode != "Ramp" && mode != "Chase") throw new ArgumentException("mode must be Solid, Ramp, or Chase.", "mode"); if (levelPercent < 0 || levelPercent > 100) throw new ArgumentOutOfRangeException("levelPercent"); ColorValue color = ParseColor(colorHtml); byte[] frame = new byte[RequiredFrameLength(project)]; int selected = rawIds.Count, i; if (selected == 0) return frame; int active = mode == "Chase" ? ((elapsedMs < 0 ? 0 : elapsedMs / Math.Max(1, project.Output == null ? 25 : project.Output.IntervalMs)) % selected) : -1; double level = levelPercent / 100.0; if (mode == "Ramp") level *= ((elapsedMs < 0 ? 0 : elapsedMs % 1000) / 999.0);
            for (i = 0; i < rawIds.Count; i++) if (mode != "Chase" || i == active) { RawChannel raw = FindRaw(project, rawIds[i]); if (raw == null) throw new ArgumentException("Unknown raw channel.", "rawIds"); ApplyRaw(frame, raw, color, level); } return frame;
        }
        private static void ControllerErrors(List<string> errors, Controller c, int index, Dictionary<string, bool> ids, Dictionary<string, bool> names)
        {
            string prefix = "Controller " + index.ToString(CultureInfo.InvariantCulture); if (c == null) { errors.Add(prefix + " is null."); return; } if (String.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 128 || ids.ContainsKey(c.Id)) errors.Add(prefix + " Id is missing, too long, or duplicate."); else ids.Add(c.Id, true); if (String.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 256 || names.ContainsKey(c.Name)) errors.Add(prefix + " Name is missing, too long, or duplicate."); else names.Add(c.Name, true);
            if (c.Protocol != "E131" && c.Protocol != "Renard") errors.Add(prefix + " protocol must be E131 or Renard."); if (c.FirstUniverse < 1 || c.FirstUniverse > 64 || c.UniverseCount < 1 || c.UniverseCount > 64 || c.FirstUniverse + c.UniverseCount - 1 > 64) errors.Add(prefix + " universe range must stay in 1..64."); if (c.FirstAddress < 1 || c.FirstAddress > 512 || c.ChannelCount < 1 || c.ChannelCount > 32768) errors.Add(prefix + " first address/channel count is invalid."); if (c.Protocol == "Renard" && ((long)c.FirstAddress + c.ChannelCount - 1 > (long)c.UniverseCount * 512L)) errors.Add(prefix + " Renard range exceeds its configured universes.");
            if (c.Protocol == "E131") { if (c.Port < 1 || c.Port > 65535) errors.Add(prefix + " UDP port must be 1..65535."); if (c.Priority < 0 || c.Priority > 200) errors.Add(prefix + " priority must be 0..200."); if (!c.Multicast && !UnicastIpv4(c.Address)) errors.Add(prefix + " unicast address must be a non-multicast IPv4 literal."); if (!String.IsNullOrEmpty(c.LocalAddress) && !UnicastIpv4(c.LocalAddress)) errors.Add(prefix + " local address must be a blank or unicast IPv4 literal."); } else { if (!ValidCom(c.ComPort)) errors.Add(prefix + " COM port is invalid."); if (!CommonBaud(c.BaudRate)) errors.Add(prefix + " baud rate is not a supported common value."); }
        }
        private static int RequiredFrameLength(Project project) { int length = 1, i; if (project.Channels == null) return length; for (i = 0; i < project.Channels.Count; i++) { RawChannel r = project.Channels[i]; if (r != null && r.Universe > 0) length = Math.Max(length, ((r.Universe - 1) * 512) + r.Address - 1 + (r.Kind == "RGB" ? 3 : 1)); } return length; }
        private static byte[] SliceUniverse(byte[] frame, int universe) { byte[] result = new byte[512]; int start = (universe - 1) * 512; if (start < frame.Length) Buffer.BlockCopy(frame, start, result, 0, Math.Min(512, frame.Length - start)); return result; }
        private static byte[] SliceRange(byte[] frame, int universe, int address, int count) { byte[] result = new byte[count]; int start = ((universe - 1) * 512) + address - 1; if (start < 0) throw new ArgumentException("Renard range is invalid.", "address"); if (start < frame.Length) Buffer.BlockCopy(frame, start, result, 0, Math.Min(count, frame.Length - start)); return result; }
        private static string MulticastAddress(int universe) { return "239.255." + ((universe >> 8) & 255).ToString(CultureInfo.InvariantCulture) + "." + (universe & 255).ToString(CultureInfo.InvariantCulture); }
        private static int RenardWorstBytes(int channels) { return 2 + (channels * 2); }
        private static int ActiveCount(OutputSettings output) { int count = 0, i; if (output == null || output.Controllers == null) return 0; for (i = 0; i < output.Controllers.Count; i++) if (output.Controllers[i] != null && output.Controllers[i].Enabled) count++; return count; }
        private static string Label(Controller c) { return String.IsNullOrEmpty(c.Name) ? c.Id : c.Name; }
        private static bool UnicastIpv4(string value) { IPAddress address; if (!IPAddress.TryParse(value, out address) || address.AddressFamily != AddressFamily.InterNetwork) return false; byte[] bytes = address.GetAddressBytes(); return !(bytes[0] == 0 || bytes[0] >= 224 || (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255)); }
        private static bool ValidCom(string value) { if (String.IsNullOrEmpty(value) || !value.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return false; int n; return Int32.TryParse(value.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 256; }
        private static bool CommonBaud(int value) { return value == 1200 || value == 2400 || value == 4800 || value == 9600 || value == 19200 || value == 38400 || value == 57600 || value == 115200 || value == 230400; }
        private static byte[] CanonicalUuid(Guid id) { string text = id.ToString("N"); byte[] result = new byte[16]; int i; for (i = 0; i < 16; i++) result[i] = Byte.Parse(text.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture); return result; }
        private static byte[] SourceName(string source) { string text = String.IsNullOrWhiteSpace(source) ? "HlsStyleSequencer" : source; List<byte> bytes = new List<byte>(); int i; for (i = 0; i < text.Length; ) { int units = Char.IsHighSurrogate(text[i]) && i + 1 < text.Length && Char.IsLowSurrogate(text[i + 1]) ? 2 : 1; byte[] next = Encoding.UTF8.GetBytes(text.Substring(i, units)); if (bytes.Count + next.Length > 63) break; bytes.AddRange(next); i += units; } return bytes.ToArray(); }
        private static void PutFlagsLength(byte[] data, int index, int length) { data[index] = (byte)(0x70 | ((length >> 8) & 0x0f)); data[index + 1] = (byte)length; }
        private static void Put32(byte[] data, int index, int value) { data[index] = (byte)(value >> 24); data[index + 1] = (byte)(value >> 16); data[index + 2] = (byte)(value >> 8); data[index + 3] = (byte)value; }
        private class ColorValue { public int R, G, B; }
        private static ColorValue ParseColor(string value) { if (value == null || value.Length != 7 || value[0] != '#') throw new ArgumentException("Use #RRGGBB.", "colorHtml"); int rgb; if (!Int32.TryParse(value.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) throw new ArgumentException("Use #RRGGBB.", "colorHtml"); return new ColorValue { R = (rgb >> 16) & 255, G = (rgb >> 8) & 255, B = rgb & 255 }; }
        private static RawChannel FindRaw(Project p, string id) { if (p.Channels == null) return null; int i; for (i = 0; i < p.Channels.Count; i++) if (p.Channels[i] != null && p.Channels[i].Id == id) return p.Channels[i]; return null; }
        private static bool Covers(Controller c, RawChannel raw) { if (c.Protocol == "E131") return raw.Universe >= c.FirstUniverse && raw.Universe < c.FirstUniverse + c.UniverseCount; int start = ((raw.Universe - c.FirstUniverse) * 512) + raw.Address - c.FirstAddress; int width = raw.Kind == "RGB" ? 3 : 1; return start >= 0 && start + width <= c.ChannelCount; }
        private static void ApplyRaw(byte[] frame, RawChannel raw, ColorValue color, double level) { if (raw == null || raw.Universe == 0) return; int offset = ((raw.Universe - 1) * 512) + raw.Address - 1; if (offset < 0 || offset >= frame.Length) return; if (raw.Kind == "RGB") { Write(frame, offset, color.R, level); Write(frame, offset + 1, color.G, level); Write(frame, offset + 2, color.B, level); } else { int maximum = Math.Max(color.R, Math.Max(color.G, color.B)); Write(frame, offset, maximum, level); } }
        private static void Write(byte[] frame, int offset, int color, double level) { if (offset >= 0 && offset < frame.Length) frame[offset] = Math.Max(frame[offset], (byte)Math.Max(0, Math.Min(255, (int)Math.Round(color * level)))); }
    }

    public interface IOutputTransport : IDisposable { bool IsPhysical { get; } void Write(OutputPacket packet); }
    public sealed class RecordingOutputTransport : IOutputTransport
    {
        private readonly object gate = new object();
        private readonly Queue<OutputPacket> records = new Queue<OutputPacket>();
        public bool IsPhysical { get { return false; } }
        public long DroppedPackets { get; private set; }
        public List<OutputPacket> Packets { get { lock(gate) return new List<OutputPacket>(records); } }
        public void Write(OutputPacket packet)
        {
            if(packet==null||packet.Data==null)throw new ArgumentNullException("packet");
            OutputPacket copy=new OutputPacket{ControllerId=packet.ControllerId,Protocol=packet.Protocol,Address=packet.Address,LocalAddress=packet.LocalAddress,ComPort=packet.ComPort,Port=packet.Port,Universe=packet.Universe,BaudRate=packet.BaudRate,Data=(byte[])packet.Data.Clone()};
            lock(gate){if(records.Count==4096){records.Dequeue();DroppedPackets++;}records.Enqueue(copy);}
        }
        public void Dispose() { }
    }
    public sealed class NativeOutputTransport : IOutputTransport
    {
        private readonly Dictionary<string, UdpClient> udp = new Dictionary<string, UdpClient>(StringComparer.Ordinal); private readonly Dictionary<string, SerialPort> serial = new Dictionary<string, SerialPort>(StringComparer.OrdinalIgnoreCase); private bool disposed;
        public bool IsPhysical { get { return true; } }
        public void Write(OutputPacket packet)
        {
            if (disposed) throw new ObjectDisposedException("NativeOutputTransport"); if (packet == null || packet.Data == null) throw new ArgumentNullException("packet"); if (packet.Protocol == "E131") { UdpClient client; string key = packet.LocalAddress ?? ""; if (!udp.TryGetValue(key, out client)) { client = String.IsNullOrEmpty(packet.LocalAddress) ? new UdpClient(AddressFamily.InterNetwork) : new UdpClient(new IPEndPoint(IPAddress.Parse(packet.LocalAddress), 0)); client.Client.SendTimeout = 250; client.Ttl = 1; client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1); if (!String.IsNullOrEmpty(packet.LocalAddress)) client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Parse(packet.LocalAddress).GetAddressBytes()); udp.Add(key, client); } client.Send(packet.Data, packet.Data.Length, new IPEndPoint(IPAddress.Parse(packet.Address), packet.Port)); } else if (packet.Protocol == "Renard") { SerialPort port; if (!serial.TryGetValue(packet.ComPort, out port)) { port = new SerialPort(packet.ComPort, packet.BaudRate, Parity.None, 8, StopBits.One); port.WriteTimeout = 250; try { port.Open(); serial.Add(packet.ComPort, port); } catch { port.Dispose(); throw; } } port.Write(packet.Data, 0, packet.Data.Length); } else throw new ArgumentException("Unknown output protocol.", "packet");
        }
        public void Dispose() { if (disposed) return; disposed = true; Exception failure = null; foreach (UdpClient client in udp.Values) { try { client.Close(); } catch (Exception ex) { if (failure == null) failure = ex; } } udp.Clear(); foreach (SerialPort port in serial.Values) { try { if (port.IsOpen) port.Close(); } catch (Exception ex) { if (failure == null) failure = ex; } finally { try { port.Dispose(); } catch (Exception ex) { if (failure == null) failure = ex; } } } serial.Clear(); if (failure != null) throw failure; }
    }

    public sealed class OutputSession : IDisposable
    {
        private readonly Project project;
        private readonly IOutputTransport transport;
        private readonly Guid cid=Guid.NewGuid();
        private readonly object gate=new object();
        private bool running,disposed,terminal,transportDisposed;
        private byte sequence;
        private long framesSent,packetsSent;
        private string lastError="";
        public bool IsRunning{get{lock(gate)return running;}}
        public long FramesSent{get{lock(gate)return framesSent;}}
        public long PacketsSent{get{lock(gate)return packetsSent;}}
        public string LastError{get{lock(gate)return lastError;}}
        public OutputSession(Project source,IOutputTransport output)
        {
            if(source==null||output==null)throw new ArgumentNullException(source==null?"source":"output");
            Model.RequireValid(source);project=Model.Clone(source);transport=output;
        }
        public void Start(bool physicallyArmed)
        {
            lock(gate)
            {
                ThrowDisposed();if(terminal)throw new InvalidOperationException("Output sessions are one-shot; create a new session for another run.");if(running)return;
                if(transport.IsPhysical&&!physicallyArmed)throw new InvalidOperationException("Physical output requires explicit arming.");
                OutputPlan plan=ControllerOutput.Prepare(project);
                if(plan.Errors.Count!=0)throw new InvalidOperationException(String.Join(" ",plan.Errors.ToArray()));
                if(!project.Output.Controllers.Exists(delegate(Controller c){return c.Enabled;}))throw new InvalidOperationException("Configure and enable an output route first; physical arming is separate.");
                running=true;
            }
        }
        public void SendFrame(byte[] frame)
        {
            lock(gate)
            {
                ThrowDisposed();if(!running)throw new InvalidOperationException("Output session is not running.");
                try{List<OutputPacket> packets=ControllerOutput.BuildPackets(project,frame,cid,sequence++,false);foreach(OutputPacket packet in packets){transport.Write(packet);packetsSent++;}framesSent++;}
                catch(Exception error){lastError=error.Message;throw;}
            }
        }
        public void Stop()
        {
            lock(gate)
            {
                if(!running)return;running=false;terminal=true;Exception failure=null;
                try
                {
                    byte[] black=new byte[Model.OutputSize(project)];
                    Attempt(ControllerOutput.BuildPackets(project,black,cid,sequence++,false),null,ref failure);
                    for(int i=0;i<3;i++)Attempt(ControllerOutput.BuildPackets(project,black,cid,sequence++,true),"E131",ref failure);
                }
                catch(Exception error){if(failure==null)failure=error;}
                finally{DisposeTransport(ref failure);}
                if(failure!=null){lastError=failure.Message;throw new InvalidOperationException("Output stopped with a blackout/transport cleanup error: "+failure.Message,failure);}
            }
        }
        private void Attempt(List<OutputPacket> packets,string protocol,ref Exception failure)
        {
            foreach(OutputPacket packet in packets)if(protocol==null||packet.Protocol==protocol)
                try{transport.Write(packet);packetsSent++;}catch(Exception error){if(failure==null)failure=error;}
        }
        public void Dispose()
        {
            lock(gate)
            {
                if(disposed)return;Exception failure=null;
                try { if(running) { try { Stop(); } catch(Exception error) { failure=error; } } else { terminal=true; DisposeTransport(ref failure); } }
                finally { terminal=true; disposed=true; DisposeTransport(ref failure); }
                if(failure!=null){if(lastError.Length==0)lastError=failure.Message;throw failure;}
            }
        }
        private void DisposeTransport(ref Exception failure)
        {
            if(transportDisposed)return;transportDisposed=true;try{transport.Dispose();}catch(Exception error){if(failure==null)failure=error;}
        }
        private void ThrowDisposed(){if(disposed)throw new ObjectDisposedException("OutputSession");}
    }
    public sealed class OutputPump : IDisposable
    {
        private readonly OutputSession session;
        private readonly Func<int> clockMs;
        private readonly Func<int,byte[]> frameAt;
        private readonly int intervalMs;
        private readonly ManualResetEvent stop=new ManualResetEvent(false);
        private Thread thread;
        private bool disposed;
        private volatile bool running;
        private long skipped;
        private string lastError="";
        public bool IsRunning{get{return running;}}
        public long SkippedFrames{get{return Interlocked.Read(ref skipped);}}
        public string LastError{get{return lastError;}}
        public OutputPump(OutputSession session,Func<int> clockMs,Func<int,byte[]> frameAt,int intervalMs)
        {
            if(session==null||clockMs==null||frameAt==null)throw new ArgumentNullException("session");
            if(intervalMs<23||intervalMs>60000)throw new ArgumentOutOfRangeException("intervalMs");
            this.session=session;this.clockMs=clockMs;this.frameAt=frameAt;this.intervalMs=intervalMs;
        }
        public void Start(bool physicallyArmed)
        {
            if(disposed)throw new ObjectDisposedException("OutputPump");
            if(thread!=null)throw new InvalidOperationException("Create a new output pump for each test/playback session.");
            stop.Reset();session.Start(physicallyArmed);running=true;
            thread=new Thread(Run);thread.IsBackground=true;thread.Name="HLS output pump";
            try{thread.Start();}catch{running=false;session.Stop();throw;}
        }
        public void Stop()
        {
            stop.Set();Thread current=thread;
            if(current!=null&&current!=Thread.CurrentThread&&current.IsAlive&&!current.Join(3000))
                throw new TimeoutException("The output worker is still stopping. Its resources were retained for a safe retry.");
            if(current==null)session.Stop();
        }
        public void Dispose(){if(disposed)return;Stop();session.Dispose();stop.Close();disposed=true;}
        private void Run()
        {
            Stopwatch watch=Stopwatch.StartNew();long next=0;
            try
            {
                while(!stop.WaitOne(0))
                {
                    long now=watch.ElapsedMilliseconds;
                    if(now<next){stop.WaitOne((int)Math.Min(1000,next-now));continue;}
                    if(now>=next+intervalMs){long missed=(now-next)/intervalMs;Interlocked.Add(ref skipped,missed);next+=missed*intervalMs;}
                    session.SendFrame(frameAt(clockMs()));next+=intervalMs;
                }
            }
            catch(Exception error){lastError=error.Message;}
            finally{try{session.Stop();}catch(Exception error){if(lastError.Length==0)lastError=error.Message;}running=false;}
        }
    }
}
