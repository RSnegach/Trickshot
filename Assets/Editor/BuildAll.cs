using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Produces the shippable build tree: one folder per operating system under Build/, each
    /// self-contained and handed to a player as-is.
    ///
    ///   Build/Windows/Trickshot.exe
    ///   Build/Mac/Trickshot.app
    ///   Build/Linux/Trickshot.x86_64
    ///
    /// WHY A SCRIPT AND NOT THE BUILD WINDOW. Three things have to be true for every one of the
    /// three players or cross-platform multiplayer silently breaks, and none of them is the Unity
    /// default: runInBackground (an alt-tabbed host stops pumping the socket and the 5s peer
    /// timeout drops everybody), a non-empty application identifier (macOS refuses to bundle
    /// without one), and the Mono scripting backend (IL2CPP for macOS/Linux cannot be produced
    /// from a Windows editor). Doing that by hand once per platform per release is where the
    /// mismatch comes from, so it is encoded here instead.
    ///
    /// A MISSING PLATFORM MODULE IS SKIPPED, NOT FATAL. Unity only builds an OS whose build
    /// support module is installed via the Hub. Skipping with a loud warning means one missing
    /// module costs you that folder, not the whole run.
    ///
    /// Menu: Trickshot > Build. Batch: -executeMethod Trickshot.BuildAll.Batch
    /// </summary>
    public static class BuildAll
    {
        // Product identity. The bundle id must be non-empty and reverse-DNS or the macOS player
        // will not launch; Unity ships an empty one by default.
        const string BundleId = "com.defaultcompany.trickshot";
        const string Product  = "Trickshot";

        // One entry per shipped OS. Folder is what the player receives; Exe is the path Unity is
        // told to write, RELATIVE to that folder. Unix marks the two platforms whose shipped text
        // files must use LF (a shell script with CRLF fails on the shebang line, so this is not a
        // tidiness question), and Launcher is the one-command entry point those two need because a
        // zip cannot carry an executable bit.
        struct Plat
        {
            public string Folder;
            public BuildTarget Target;
            public string Exe;
            public string Readme;
            public bool Unix;
            public string Launcher;       // file name shipped in the folder, or null
            public string LauncherBody;
            public string Zip;            // archive name, written next to the platform folders
        }

        static Plat[] Platforms()
        {
            return new[]
            {
                new Plat
                {
                    Folder = "Windows", Target = BuildTarget.StandaloneWindows64,
                    Exe = Product + ".exe", Readme = WindowsReadme,
                    Unix = false, Launcher = null, LauncherBody = null,
                    Zip = Product + "-Windows.zip",
                },
                new Plat
                {
                    Folder = "Mac", Target = BuildTarget.StandaloneOSX,
                    Exe = Product + ".app", Readme = MacReadme,
                    Unix = true, Launcher = "run.command", LauncherBody = MacLauncher,
                    Zip = Product + "-Mac.zip",
                },
                new Plat
                {
                    Folder = "Linux", Target = BuildTarget.StandaloneLinux64,
                    Exe = Product + ".x86_64", Readme = LinuxReadme,
                    Unix = true, Launcher = "run.sh", LauncherBody = LinuxLauncher,
                    Zip = Product + "-Linux.zip",
                },
            };
        }

        // ---------------------------------------------------------------- menu

        [MenuItem("Trickshot/Build/All Platforms", false, 0)]
        public static void MenuAll() { Run(null); }

        [MenuItem("Trickshot/Build/Windows Only", false, 20)]
        public static void MenuWindows() { Run("Windows"); }

        [MenuItem("Trickshot/Build/Mac Only", false, 21)]
        public static void MenuMac() { Run("Mac"); }

        [MenuItem("Trickshot/Build/Linux Only", false, 22)]
        public static void MenuLinux() { Run("Linux"); }

        // Compressing three players takes minutes, which is dead time while iterating locally. On by
        // default because the packed archive is the thing you actually hand someone.
        const string ZipPrefKey = "Trickshot.BuildAll.Zip";
        const string ZipMenuPath = "Trickshot/Build/Zip Packages";

        static bool ZipEnabled
        {
            get { return EditorPrefs.GetBool(ZipPrefKey, true); }
            set { EditorPrefs.SetBool(ZipPrefKey, value); }
        }

        [MenuItem(ZipMenuPath, false, 30)]
        static void MenuToggleZip() { ZipEnabled = !ZipEnabled; }

        [MenuItem(ZipMenuPath, true, 30)]
        static bool MenuToggleZipValidate()
        {
            Menu.SetChecked(ZipMenuPath, ZipEnabled);
            return true;
        }

        [MenuItem("Trickshot/Build/Open Build Folder", false, 40)]
        public static void MenuOpen()
        {
            string root = BuildRoot();
            Directory.CreateDirectory(root);
            EditorUtility.RevealInFinder(root);
        }

        /// <summary>
        /// Batch entry point. Exits non-zero if any platform that COULD be built failed, so CI or a
        /// shell loop can tell a real failure from a skipped module.
        ///
        ///   Unity.exe -quit -batchmode -nographics -projectPath &lt;proj&gt;
        ///             -executeMethod Trickshot.BuildAll.Batch
        ///
        /// Pass -buildPlatform Windows|Mac|Linux to do just one, and -noZip to skip packaging.
        /// </summary>
        public static void Batch()
        {
            string only = null;
            bool zip = true;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-noZip") zip = false;
                else if (args[i] == "-buildPlatform" && i + 1 < args.Length) only = args[i + 1];
            }

            bool ok = Run(only, zip);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // ---------------------------------------------------------------- driver

        // Menu builds honour the Zip Packages toggle; Batch passes its own answer.
        static bool Run(string only) { return Run(only, ZipEnabled); }

        /// <summary>
        /// Builds every platform (or just <paramref name="only"/>). Returns false only when a
        /// platform whose module IS installed failed to build; a skipped module is not a failure.
        /// </summary>
        static bool Run(string only, bool zip)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("BuildAll: no enabled scenes in Build Settings. Nothing to build.");
                return false;
            }

            ApplyCommonSettings();

            string root = BuildRoot();
            Directory.CreateDirectory(root);
            WriteText(Path.Combine(root, "README.txt"), RootReadme, false);

            var built = new List<string>();
            var skipped = new List<string>();
            var failed = new List<string>();
            var zipped = new List<string>();

            foreach (var p in Platforms())
            {
                if (only != null && !string.Equals(only, p.Folder, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ModuleInstalled(p.Target))
                {
                    // Named explicitly, because the fix is a Hub install the script cannot perform.
                    Debug.LogWarning("BuildAll: SKIPPED " + p.Folder + ". Install \"" + HubModule(p.Target)
                                   + "\" for this Unity version in Unity Hub > Installs > Add modules.");
                    skipped.Add(p.Folder);
                    continue;
                }

                string dir = Path.Combine(root, p.Folder);
                // Wipe the platform folder first. Unity overwrites what it writes but leaves
                // orphans behind, and a stale managed DLL from an older build inside a player's
                // _Data folder is exactly the kind of half-updated install that desyncs a match.
                ClearFolder(dir);
                Directory.CreateDirectory(dir);

                var opts = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(dir, p.Exe),
                    target = p.Target,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };

                Debug.Log("BuildAll: building " + p.Folder + " -> " + opts.locationPathName);

                BuildResult result = BuildResult.Failed;
                try
                {
                    var report = BuildPipeline.BuildPlayer(opts);
                    if (report != null) result = report.summary.result;
                }
                catch (Exception e)
                {
                    Debug.LogError("BuildAll: " + p.Folder + " threw: " + e.Message);
                    result = BuildResult.Failed;
                }

                if (result == BuildResult.Succeeded)
                {
                    WriteText(Path.Combine(dir, "README.txt"), p.Readme, p.Unix);
                    if (p.Launcher != null)
                        WriteText(Path.Combine(dir, p.Launcher), p.LauncherBody, true);

                    built.Add(p.Folder);
                    Debug.Log("BuildAll: " + p.Folder + " OK.");

                    if (zip)
                    {
                        string zipPath = Path.Combine(root, p.Zip);
                        if (ZipFolder(dir, zipPath, Product + "-" + p.Folder))
                        {
                            zipped.Add(p.Zip);
                            Debug.Log("BuildAll: packed " + p.Zip + " (" + SizeMb(zipPath) + ").");
                        }
                    }
                }
                else
                {
                    failed.Add(p.Folder);
                    Debug.LogError("BuildAll: " + p.Folder + " FAILED (" + result + ").");
                }
            }

            Debug.Log("BuildAll: built [" + string.Join(", ", built.ToArray())
                    + "] skipped [" + string.Join(", ", skipped.ToArray())
                    + "] failed [" + string.Join(", ", failed.ToArray())
                    + "] zipped [" + string.Join(", ", zipped.ToArray()) + "]");

            return failed.Count == 0;
        }

        // Project root (the folder holding Assets/), which is where Build/ goes.
        static string BuildRoot()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Build");
        }

        static string[] EnabledScenes()
        {
            var list = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path)) list.Add(s.path);
            return list.ToArray();
        }

        // ---------------------------------------------------------------- settings

        /// <summary>
        /// The settings cross-platform play depends on. Applied on every run rather than left to
        /// whatever the project file happens to hold, so all three folders in a release agree.
        /// </summary>
        static void ApplyCommonSettings()
        {
            // An alt-tabbed player must keep running its loop. Without this the host stops polling
            // the UDP socket the moment it loses focus, keepalives stop, and every client trips the
            // transport's peer timeout a few seconds later. This is THE most common "multiplayer
            // randomly dropped" report and it is one flag.
            PlayerSettings.runInBackground = true;

            // Mono, for all three standalone targets. IL2CPP cannot cross-compile a macOS or Linux
            // player from a Windows editor, and the backend is a per-GROUP setting (NamedBuildTarget
            // .Standalone covers all three), so it cannot be varied per OS anyway. Mono builds all
            // three from one machine, which is the whole point of this script.
            TrySet(() => PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
                                                            ScriptingImplementation.Mono2x),
                   "scripting backend");

            // macOS will not produce a launchable bundle without a reverse-DNS identifier, and the
            // project ships an empty one.
            TrySet(() => PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId),
                   "application identifier");

            // Player, not dedicated server.
            TrySet(() => EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player,
                   "standalone subtarget");

            // A macOS build can be emitted either as a .app or as an Xcode project; only the former
            // is runnable by a player who was handed the folder.
            TrySet(() => EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateXcodeProject", "false"),
                   "CreateXcodeProject");

            // Universal (Intel + Apple Silicon) macOS binary where the module offers it. Reached by
            // reflection because the type lives in the macOS build support module: referencing it
            // directly would stop this script compiling on a machine without that module installed,
            // which is the exact machine that needs the rest of the script to work.
            TrySetMacUniversal();
        }

        static void TrySet(Action a, string what)
        {
            try { a(); }
            catch (Exception e) { Debug.LogWarning("BuildAll: could not set " + what + ": " + e.Message); }
        }

        // UnityEditor.OSXStandalone.UserBuildSettings.architecture = MacOSArchitecture.x64ARM64.
        // Silent no-op when the module is absent; there is nothing for the user to do about it until
        // they install Mac build support, at which point the skip warning already told them.
        static void TrySetMacUniversal()
        {
            try
            {
                Type t = FindType("UnityEditor.OSXStandalone.UserBuildSettings");
                Type e = FindType("UnityEditor.OSXStandalone.MacOSArchitecture");
                if (t == null || e == null) return;
                var prop = t.GetProperty("architecture", BindingFlags.Static | BindingFlags.Public);
                if (prop == null) return;
                string[] names = Enum.GetNames(e);
                foreach (var want in new[] { "x64ARM64", "Universal" })
                {
                    if (Array.IndexOf(names, want) < 0) continue;
                    prop.SetValue(null, Enum.Parse(e, want), null);
                    return;
                }
            }
            catch { }
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        // ---------------------------------------------------------------- module probe

        /// <summary>
        /// Is the build support module for this target installed? BuildPipeline exposes the answer
        /// but not always publicly across versions, so it is reached by reflection and an unknown
        /// answer means "try the build and let it report", never "skip silently".
        /// </summary>
        static bool ModuleInstalled(BuildTarget target)
        {
            try
            {
                var m = typeof(BuildPipeline).GetMethod(
                    "IsBuildTargetSupported",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(BuildTargetGroup), typeof(BuildTarget) },
                    null);
                if (m != null)
                    return (bool)m.Invoke(null, new object[] { BuildTargetGroup.Standalone, target });
            }
            catch { }
            return true;
        }

        static string HubModule(BuildTarget t)
        {
            if (t == BuildTarget.StandaloneOSX) return "Mac Build Support (Mono)";
            if (t == BuildTarget.StandaloneLinux64) return "Linux Build Support (Mono)";
            return "Windows Build Support (Mono)";
        }

        // ---------------------------------------------------------------- files

        static void ClearFolder(string dir)
        {
            if (!Directory.Exists(dir)) return;
            try { Directory.Delete(dir, true); }
            catch (Exception e) { Debug.LogWarning("BuildAll: could not clear " + dir + ": " + e.Message); }
        }

        /// <summary>
        /// Writes a shipped text file with the line endings that platform's tools require.
        ///
        /// This matters for exactly one reason: sh treats a trailing CR as part of the interpreter
        /// path, so a launcher script written with CRLF dies with "bad interpreter" before it runs a
        /// single line. Normalising to LF FIRST also stops a source string that already holds CRLF
        /// from becoming CRCRLF. File.WriteAllText emits no BOM, which is equally required - a BOM in
        /// front of #! breaks the shebang the same way.
        /// </summary>
        static void WriteText(string path, string body, bool unix)
        {
            try
            {
                string lf = body.Replace("\r\n", "\n").Replace("\r", "\n");
                File.WriteAllText(path, unix ? lf : lf.Replace("\n", "\r\n"));
            }
            catch (Exception e) { Debug.LogWarning("BuildAll: could not write " + path + ": " + e.Message); }
        }

        // ---------------------------------------------------------------- packaging

        /// <summary>
        /// Zips a built platform folder into <paramref name="zipPath"/>, with every entry under a
        /// single <paramref name="rootName"/> directory.
        ///
        /// Hand-rolled rather than ZipFile.CreateFromDirectory because that overload puts the
        /// contents at the archive root, so extracting spills the player's files into whatever
        /// folder they were in; the includeBaseDirectory overload roots them at "Windows", which
        /// tells the player nothing once it is sitting in their Downloads folder. Entry paths are
        /// built by hand with forward slashes because the zip spec requires them and macOS and Linux
        /// unzip tools treat a backslash as part of the file NAME, producing one file called
        /// "Trickshot_Data\resources.assets" instead of a folder.
        ///
        /// The archive is written to Build/, i.e. OUTSIDE the folder being walked, so it can never
        /// include itself.
        /// </summary>
        static bool ZipFolder(string dir, string zipPath, string rootName)
        {
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);

                int cut = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar,
                                                        Path.AltDirectorySeparatorChar).Length + 1;

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetFullPath(file).Substring(cut).Replace('\\', '/');
                        // Fully qualified: UnityEngine also has a CompressionLevel.
                        zip.CreateEntryFromFile(file, rootName + "/" + rel,
                                                System.IO.Compression.CompressionLevel.Optimal);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("BuildAll: could not zip " + dir + ": " + e.Message);
                return false;
            }
        }

        static string SizeMb(string path)
        {
            try { return (new FileInfo(path).Length / (1024f * 1024f)).ToString("0.0") + " MB"; }
            catch { return "?"; }
        }

        // ---------------------------------------------------------------- launchers
        //
        // A zip cannot carry a unix executable bit, and one built on Windows definitely does not, so
        // a macOS or Linux player who extracts the folder has a binary they cannot run and no obvious
        // reason why. These scripts fix that from inside the folder: chmod the player, clear the
        // quarantine flag on macOS, then launch.
        //
        // The launcher ARRIVES non-executable too, which is why both readmes say to run it through
        // sh rather than by double-clicking. That is still one command instead of three, and going
        // through the interpreter also means Gatekeeper never inspects the script itself.
        //
        // cd to the script's own directory first: the working directory is wherever the terminal
        // happens to be, and a relative path to the player fails from anywhere else.

        const string LinuxLauncher =
@"#!/bin/sh
# Trickshot launcher. Run:  sh run.sh
cd ""$(dirname ""$0"")"" || exit 1
chmod +x ./Trickshot.x86_64 2>/dev/null
exec ./Trickshot.x86_64 ""$@""
";

        const string MacLauncher =
@"#!/bin/sh
# Trickshot launcher. Run:  sh run.command
cd ""$(dirname ""$0"")"" || exit 1
# The app is unsigned, so macOS quarantines anything downloaded. Clearing the flag here saves the
# player the ""damaged and cannot be opened"" dialog, which is the wrong diagnosis for an unsigned app.
xattr -dr com.apple.quarantine ./Trickshot.app 2>/dev/null
chmod -R +x ./Trickshot.app/Contents/MacOS 2>/dev/null
open ./Trickshot.app
";

        // ---------------------------------------------------------------- readmes
        //
        // Shipped next to each player. They exist because three of the failure modes are on the
        // PLAYER's machine and invisible from here: an unsigned macOS app is quarantined, a Linux
        // binary arrives without the executable bit, and a host's firewall drops inbound UDP 7777
        // so joining fails with no error the joiner can act on.

        const string RootReadme =
@"TRICKSHOT BUILDS

One folder per operating system, plus a zip of each. Send a player one zip.

  Trickshot-Windows.zip   Windows/   Trickshot.exe
  Trickshot-Mac.zip       Mac/       Trickshot.app   + run.command
  Trickshot-Linux.zip     Linux/     Trickshot.x86_64 + run.sh

Each zip extracts to a single folder. Mac and Linux players run the launcher script
inside it; the readme in every folder says how.

All three play together. Ship the same build to everyone: a mismatched version is
refused at join with a version message.

Rebuild in the editor:  Trickshot > Build > All Platforms
Toggle packaging:       Trickshot > Build > Zip Packages
Rebuild from a shell:
  Unity.exe -quit -batchmode -nographics -projectPath <project> -executeMethod Trickshot.BuildAll.Batch
Add -noZip to skip packaging, -buildPlatform Windows|Mac|Linux to do one.
";

        const string WindowsReadme =
@"TRICKSHOT - WINDOWS

Run Trickshot.exe. Keep Trickshot_Data next to it.

MULTIPLAYER
Hosting: allow inbound UDP 7777 when Windows Firewall asks, on Private networks.
Joining: paste the host's invite code, or their IP.

Same network works with no setup. Over the internet, both players install Tailscale
and sign in to the same tailnet, then the host shows up in the browser.
";

        const string MacReadme =
@"TRICKSHOT - MACOS

In Terminal, from this folder:

  sh run.command

That is it. The app is unsigned, so the script clears the quarantine flag for you.

If you would rather do it by hand:

  xattr -dr com.apple.quarantine Trickshot.app
  chmod +x Trickshot.app/Contents/MacOS/*
  open Trickshot.app

MULTIPLAYER
Allow Local Network the first time it asks. Denying it hides all LAN games.
Turn it back on in System Settings > Privacy & Security > Local Network.

Hosting: allow incoming connections when the firewall asks. UDP 7777.
Joining: paste the host's invite code, or their IP.

Over the internet, both players install Tailscale and sign in to the same tailnet.
";

        const string LinuxReadme =
@"TRICKSHOT - LINUX

From a terminal in this folder:

  sh run.sh

By hand instead:

  chmod +x Trickshot.x86_64
  ./Trickshot.x86_64

Keep Trickshot_Data next to the binary.

MULTIPLAYER
Hosting: open UDP 7777.
  sudo ufw allow 7777/udp
Joining: paste the host's invite code, or their IP.

Over the internet, both players install Tailscale and sign in to the same tailnet.
";
    }
}
