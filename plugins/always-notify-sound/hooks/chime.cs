// chime.cs — plays an attention chime when ZCode needs user confirmation AND the
// ZCode window is focused. When the window is NOT focused the built-in ZCode
// notification sound handles the alert instead, so this plugin only fills the
// gap the app leaves open (focused window). Always returns 0.
//
// Volume (0-100, values <= 1 are treated as fractions, e.g. 0.8 = 80%):
//   1. plugins.options.<plugin-id>.volume in ~/.zcode/cli/config.json (set from
//      Settings -> Plugin Management -> plugin -> Advanced)
//   2. env var ZCODE_NOTIFY_SOUND_VOLUME
//   3. volume.txt next to this exe
//   4. default 100
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;

class Chime
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    static string PluginDir()
    {
        try { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ""; }
        catch { return ""; }
    }

    static string PluginName()
    {
        try
        {
            string hooksDir = PluginDir();
            string pluginRoot = Directory.GetParent(hooksDir) != null ? Directory.GetParent(hooksDir).FullName : "";
            string manifest = Path.Combine(pluginRoot, ".zcode-plugin", "plugin.json");
            if (File.Exists(manifest))
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var m = ser.DeserializeObject(File.ReadAllText(manifest)) as Dictionary<string, object>;
                if (m != null && m.ContainsKey("name")) return Convert.ToString(m["name"]);
            }
        }
        catch { }
        return "always-notify-sound";
    }

    // Foreground window belongs to ZCode.exe -> the app window is focused.
    // Fail-open: if the foreground owner cannot be resolved (race), play anyway
    // rather than silently dropping the chime.
    static bool IsZcodeFocused()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        if (GetWindowThreadProcessId(h, out pid) == 0 || pid == 0) return false;
        try
        {
            using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
            {
                return p.ProcessName.Equals("ZCode", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { return true; }
    }

    // FNV-1a hash so the session id never shows up raw in a temp filename.
    static string HashId(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s) { h ^= c; h *= 16777619u; }
        return h.ToString("x8");
    }

    static int NormalizeVolume(double v)
    {
        double pct = v <= 1.0 ? v * 100.0 : v; // fraction vs percent
        if (pct <= 0) return 0;
        if (pct >= 100) return 100;
        return (int)pct;
    }

    // Volume from the plugin-management UI: plugins.options.<id>.volume in config.json. -1 when unset.
    static int ReadUiVolume(string pluginName)
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cfg = Path.Combine(home, ".zcode", "cli", "config.json");
            if (!File.Exists(cfg)) return -1;
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var root = ser.DeserializeObject(File.ReadAllText(cfg)) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("plugins")) return -1;
            var plugins = root["plugins"] as Dictionary<string, object>;
            if (plugins == null || !plugins.ContainsKey("options")) return -1;
            var options = plugins["options"] as Dictionary<string, object>;
            if (options == null) return -1;
            foreach (var kv in options)
            {
                if (kv.Key.StartsWith(pluginName + "@", StringComparison.OrdinalIgnoreCase))
                {
                    var opts = kv.Value as Dictionary<string, object>;
                    if (opts != null && opts.ContainsKey("volume"))
                    {
                        object val = opts["volume"];
                        double v;
                        if (val is int) v = (int)val;
                        else if (val is long) v = (long)val;
                        else if (val is double) v = (double)val;
                        else if (val is decimal) v = (double)(decimal)val;
                        else if (val != null)
                        {
                            double dv;
                            if (double.TryParse(val.ToString(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out dv)) v = dv;
                            else v = 0;
                        }
                        else v = 0;
                        return NormalizeVolume(v);
                    }
                }
            }
        }
        catch { }
        return -1;
    }

    // Volume from env var ZCODE_NOTIFY_SOUND_VOLUME, then volume.txt, else default 100.
    static int GetVolumePct()
    {
        try
        {
            string env = Environment.GetEnvironmentVariable("ZCODE_NOTIFY_SOUND_VOLUME");
            double ev;
            if (!string.IsNullOrEmpty(env) && double.TryParse(env, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out ev))
                return NormalizeVolume(ev);

            string file = Path.Combine(PluginDir(), "volume.txt");
            if (File.Exists(file))
            {
                string t = File.ReadAllText(file).Trim();
                double fv;
                if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out fv))
                    return NormalizeVolume(fv);
            }
        }
        catch { }
        return 100;
    }

    // Scales a 16-bit PCM mono/stereo WAV's samples by volume (0..1). Null on unsupported format.
    static byte[] ScaleWav(byte[] wav, double volume)
    {
        int dataStart = -1, dataLen = 0, channels = 1, bits = 16;
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int sz = BitConverter.ToInt32(wav, pos + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(wav, pos + 10);
                bits = BitConverter.ToInt16(wav, pos + 22);
            }
            else if (id == "data")
            {
                dataStart = pos + 8;
                dataLen = sz;
                break;
            }
            pos += 8 + sz + (sz & 1);
        }
        if (dataStart < 8 || dataLen <= 0 || bits != 16 || (channels != 1 && channels != 2)) return null;

        byte[] outp = (byte[])wav.Clone();
        int samples = dataLen / 2;
        for (int i = 0; i < samples; i++)
        {
            int o = dataStart + i * 2;
            int s = BitConverter.ToInt16(wav, o) * (int)(volume * 100) / 100;
            if (s > 32767) s = 32767;
            else if (s < -32768) s = -32768;
            byte[] b = BitConverter.GetBytes((short)s);
            outp[o] = b[0];
            outp[o + 1] = b[1];
        }
        return outp;
    }

    static int Main()
    {
        // Opt-out: env var ZCODE_NOTIFY_SOUND=0/off, disabled.flag.
        try
        {
            string env = Environment.GetEnvironmentVariable("ZCODE_NOTIFY_SOUND");
            if (!string.IsNullOrEmpty(env) && (env == "0" || env.Equals("off", StringComparison.OrdinalIgnoreCase))) return 0;
            if (File.Exists(Path.Combine(PluginDir(), "disabled.flag"))) return 0;
        }
        catch { }

        // Focus gate: focused window -> our chime; leaving the window -> the built-in
        // ZCode notification sound plays instead (no double sound, no gap).
        if (!IsZcodeFocused()) return 0;

        int volume = ReadUiVolume(PluginName());
        if (volume < 0) volume = GetVolumePct();
        if (volume <= 0) return 0; // muted

        // Dedup: several hook events can fire for the same confirmation moment.
        // The stamp is keyed per conversation (ZCODE_SESSION_ID) so a chime in
        // another conversation never swallows this one, and the window is short
        // (500ms) so quick follow-up confirmations still chime.
        try
        {
            string sessionId = Environment.GetEnvironmentVariable("ZCODE_SESSION_ID")
                ?? Environment.GetEnvironmentVariable("CLAUDE_SESSION_ID") ?? "";
            string stampName = string.IsNullOrEmpty(sessionId)
                ? "zcode-notify-sound-last.txt"
                : "zcode-notify-sound-last-" + HashId(sessionId) + ".txt";
            string stamp = Path.Combine(Path.GetTempPath(), stampName);
            long last = 0;
            if (File.Exists(stamp)) long.TryParse(File.ReadAllText(stamp).Trim(), out last);
            long now = DateTime.UtcNow.Ticks;
            if (now - last < 5000000L) return 0; // within 0.5 seconds
            File.WriteAllText(stamp, now.ToString());
        }
        catch { }

        string wavPath = Path.Combine(PluginDir(), "chime.wav");
        if (File.Exists(wavPath))
        {
            try
            {
                byte[] wav = File.ReadAllBytes(wavPath);
                byte[] scaled = ScaleWav(wav, volume / 100.0) ?? wav;
                using (var ms = new MemoryStream(scaled))
                using (var player = new SoundPlayer(ms))
                {
                    player.PlaySync();
                }
                return 0;
            }
            catch { }
        }

        // Fallback: console beep (no volume control); skip when volume is low to avoid blasting.
        if (volume >= 50)
        {
            try
            {
                Console.Beep(880, 200);
                System.Threading.Thread.Sleep(80);
                Console.Beep(1245, 320);
            }
            catch { }
        }
        return 0;
    }
}