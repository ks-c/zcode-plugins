// chime.cs — plays an attention chime whenever ZCode needs the user's action.
// v1.1: the sound is ALWAYS played regardless of window focus or which
// conversation is in front (full takeover of the alert), so a confirmation in
// any window/conversation is never missed. Hooks pass --event <name> so the
// optional "turn end" chime can be turned off from the plugin management UI.
//
// Events: permission (tool permission request), question (AskUserQuestion),
//         stop (agent finished its turn and waits for input).
// Always returns 0 so it never blocks or errors the session.
//
// Volume (0-100; values <= 1 are treated as fractions, e.g. 0.8 = 80%):
//   1. plugins.options.<plugin-id>.volume in ~/.zcode/cli/config.json (UI)
//   2. env var ZCODE_NOTIFY_SOUND_VOLUME
//   3. volume.txt next to this exe
//   4. default 100
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;

class Chime
{
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

    // Returns the plugin's own options from ~/.zcode/cli/config.json (plugins.options.<id>.*).
    static Dictionary<string, object> ReadUiOptions(string pluginName)
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cfg = Path.Combine(home, ".zcode", "cli", "config.json");
            if (!File.Exists(cfg)) return null;
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var root = ser.DeserializeObject(File.ReadAllText(cfg)) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("plugins")) return null;
            var plugins = root["plugins"] as Dictionary<string, object>;
            if (plugins == null || !plugins.ContainsKey("options")) return null;
            var options = plugins["options"] as Dictionary<string, object>;
            if (options == null) return null;
            foreach (var kv in options)
            {
                if (kv.Key.StartsWith(pluginName + "@", StringComparison.OrdinalIgnoreCase))
                    return kv.Value as Dictionary<string, object>;
            }
        }
        catch { }
        return null;
    }

    static bool TryGetDouble(object val, out double result)
    {
        result = 0;
        if (val == null) return false;
        if (val is int) { result = (int)val; return true; }
        if (val is long) { result = (long)val; return true; }
        if (val is double) { result = (double)val; return true; }
        if (val is decimal) { result = (double)(decimal)val; return true; }
        return double.TryParse(val.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    static bool TryGetBool(object val, out bool result)
    {
        result = false;
        if (val == null) return false;
        if (val is bool) { result = (bool)val; return true; }
        string s = val.ToString().Trim();
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
        return false;
    }

    static int NormalizeVolume(double v)
    {
        double pct = v <= 1.0 ? v * 100.0 : v; // fraction vs percent
        if (pct <= 0) return 0;
        if (pct >= 100) return 100;
        return (int)pct;
    }

    // FNV-1a hash so the session id never shows up raw in a temp filename.
    static string HashId(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s) { h ^= c; h *= 16777619u; }
        return h.ToString("x8");
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

    static int Main(string[] args)
    {
        // Opt-out: env var ZCODE_NOTIFY_SOUND=0/off, disabled.flag.
        try
        {
            string env = Environment.GetEnvironmentVariable("ZCODE_NOTIFY_SOUND");
            if (!string.IsNullOrEmpty(env) && (env == "0" || env.Equals("off", StringComparison.OrdinalIgnoreCase))) return 0;
            if (File.Exists(Path.Combine(PluginDir(), "disabled.flag"))) return 0;
        }
        catch { }

        // --event <name>: permission | question | stop (default: generic prompt)
        string eventName = "";
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--event") eventName = args[i + 1];
        eventName = eventName.Trim().ToLowerInvariant();

        Dictionary<string, object> ui = ReadUiOptions(PluginName());

        // Optional: skip the "agent finished, waiting for you" chime when disabled in the UI.
        if (eventName == "stop" && ui != null && ui.ContainsKey("turnEndChime"))
        {
            bool turnEnd;
            if (TryGetBool(ui["turnEndChime"], out turnEnd) && !turnEnd) return 0;
        }

        // Volume: UI option > env var > volume.txt > 100
        int volume = -1;
        if (ui != null && ui.ContainsKey("volume"))
        {
            double uv;
            if (TryGetDouble(ui["volume"], out uv)) volume = NormalizeVolume(uv);
        }
        if (volume < 0)
        {
            volume = 100;
            try
            {
                string env = Environment.GetEnvironmentVariable("ZCODE_NOTIFY_SOUND_VOLUME");
                double ev;
                if (!string.IsNullOrEmpty(env) && double.TryParse(env, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ev))
                    volume = NormalizeVolume(ev);
                else
                {
                    string file = Path.Combine(PluginDir(), "volume.txt");
                    if (File.Exists(file))
                    {
                        string t = File.ReadAllText(file).Trim();
                        double fv;
                        if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out fv))
                            volume = NormalizeVolume(fv);
                    }
                }
            }
            catch { }
        }
        if (volume <= 0) return 0; // muted

        // Dedup: several hook events can fire for the same confirmation moment.
        // Keyed per conversation (ZCODE_SESSION_ID) so a chime in another
        // conversation never swallows this one; 500ms window so quick follow-ups
        // still chime.
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