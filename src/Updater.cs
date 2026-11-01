using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace VanguardSwitch
{
    /// <summary>Checks GitHub Releases for a newer version and swaps the exe in place.</summary>
    static class Updater
    {
        public const string Repo = "shinishiz/VanguardSwitch";
        const string ExeName = "VanguardSwitch.exe";

#pragma warning disable CS0649   // filled by the JSON serializer
        [DataContract]
        class GhRelease
        {
            [DataMember(Name = "tag_name")] public string Tag;
            [DataMember(Name = "html_url")] public string Page;
            [DataMember(Name = "draft")] public bool Draft;
            [DataMember(Name = "prerelease")] public bool Prerelease;
            [DataMember(Name = "assets")] public List<GhAsset> Assets;
        }

        [DataContract]
        class GhAsset
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "browser_download_url")] public string Url;
        }
#pragma warning restore CS0649

        public class Release
        {
            public Version Version;
            public string Page, ExeUrl, HashUrl;
        }

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new WebClient { Encoding = Encoding.UTF8 };
            c.Headers[HttpRequestHeader.UserAgent] = "VanguardSwitch/" + Current;
            return c;
        }

        /// <summary>The latest release if it's newer than this exe, otherwise null. Never throws.</summary>
        public static Release Check()
        {
            try
            {
                string json;
                using (var c = Client())
                {
                    c.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = c.DownloadString($"https://api.github.com/repos/{Repo}/releases/latest");
                }

                GhRelease r;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    r = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);

                if (r == null || r.Draft || r.Prerelease || string.IsNullOrEmpty(r.Tag)) return null;
                if (!Version.TryParse(r.Tag.TrimStart('v', 'V'), out var v)) return null;
                v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
                if (v <= Current) return null;

                var exe = r.Assets?.FirstOrDefault(a => a.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase));
                var hash = r.Assets?.FirstOrDefault(a => a.Name.Equals(ExeName + ".sha256", StringComparison.OrdinalIgnoreCase));
                if (exe == null) return null;

                return new Release { Version = v, Page = r.Page, ExeUrl = exe.Url, HashUrl = hash?.Url };
            }
            catch { return null; }
        }

        /// <summary>
        /// Downloads the new exe, checks its SHA-256, puts it in place of the running one and starts it.
        /// The old file is renamed to .old (Windows allows renaming a running exe) and deleted on the next start.
        /// </summary>
        public static void Install(Release rel)
        {
            string exe = ExePath;
            string fresh = exe + ".new", old = exe + ".old";

            // The app runs as admin, so never install a file that wasn't checked.
            if (rel.HashUrl == null)
                throw new Exception(L.T("A release não tem o arquivo .sha256, então a atualização não pode ser conferida.",
                                        "The release has no .sha256 file, so the update can't be checked."));

            try
            {
                using (var c = Client())
                {
                    string expected = c.DownloadString(rel.HashUrl).Trim().Split(' ', '\t')[0].ToLowerInvariant();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(expected, "^[0-9a-f]{64}$"))
                        throw new Exception(L.T("O arquivo .sha256 da release é inválido.", "The release's .sha256 file is invalid."));

                    c.DownloadFile(rel.ExeUrl, fresh);
                    string actual;
                    using (var sha = SHA256.Create())
                    using (var f = File.OpenRead(fresh))
                        actual = string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
                    if (actual != expected)
                        throw new Exception(L.T("O arquivo baixado não bate com o SHA-256 da release.", "The downloaded file doesn't match the release's SHA-256."));
                }
            }
            catch
            {
                try { File.Delete(fresh); } catch { }
                throw;
            }

            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }

            Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true });
        }

        /// <summary>Removes the .old file left by the previous update.</summary>
        public static void Cleanup()
        {
            try
            {
                foreach (var leftover in new[] { ExePath + ".old", ExePath + ".new" })
                    for (int i = 0; i < 10 && File.Exists(leftover); i++)
                    {
                        try { File.Delete(leftover); }
                        catch { System.Threading.Thread.Sleep(500); }
                    }
            }
            catch { }
        }
    }
}
