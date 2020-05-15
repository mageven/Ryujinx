using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.FsSystem.RomFs;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.Loaders.Mods;
using Ryujinx.HLE.Loaders.Executables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Ryujinx.HLE.HOS
{
    public class ModLoader
    {
        private readonly VirtualFileSystem _vfs;

        private const string RootModsDir = "mods";
        private const string RomfsDir = "romfs";
        private const string ExefsDir = "exefs";
        private const string ExefsPatchesDir = "exefs_patches";
        private const string NroPatchesDir = "nro_patches";

        public struct ModEntry
        {
            public readonly DirectoryInfo ModDir;
            public readonly DirectoryInfo Romfs;
            public readonly DirectoryInfo Exefs;

            public bool Enabled;

            public ModEntry(DirectoryInfo modDir, bool enabled) // TODO: input DirectoryInfo
            {
                ModDir = modDir;

                Romfs = new DirectoryInfo(Path.Combine(modDir.FullName, RomfsDir));
                Exefs = new DirectoryInfo(Path.Combine(modDir.FullName, ExefsDir));

                Enabled = enabled;
            }

            public string ModName => ModDir.Name;
            public string ModPath => ModDir.FullName;
        }

        public List<string> ModRootDirs { get; private set; }
        public Dictionary<ulong, List<ModEntry>> Mods { get; private set; }
        private List<DirectoryInfo> _unconditionalMods;

        public ModLoader(VirtualFileSystem vfs)
        {
            _vfs = vfs;

            // By default, mods are collected from RyujinxBasePath/{RootModsDir}
            ModRootDirs = new List<string> { Path.Combine(_vfs.GetBasePath(), RootModsDir) };
        }

        public void InitModsList()
        {
            Mods = new Dictionary<ulong, List<ModEntry>>();
            _unconditionalMods = new List<DirectoryInfo>();

            var modNames = new HashSet<string>();

            foreach (string modRootPath in ModRootDirs)
            {
                var modRootDir = new DirectoryInfo(modRootPath);
                if (!modRootDir.Exists) continue;

                Logger.PrintDebug(LogClass.Application, $"Loading mods from `{modRootPath}`");

                foreach (var titleDir in modRootDir.EnumerateDirectories())
                {
                    switch (titleDir.Name)
                    {
                        case ExefsPatchesDir:
                            // case NroPatchesDir:
                            foreach (var modDir in titleDir.EnumerateDirectories())
                            {
                                _unconditionalMods.Add(modDir);
                                Logger.PrintInfo(LogClass.Application, $"Found Unconditional Mod {modDir.Name}");

                                if (!modNames.Add(modDir.Name))
                                {
                                    Logger.PrintWarning(LogClass.Application, $"Duplicate mod name '{modDir.Name}'");
                                }
                            }
                            break;

                        default:
                            if (UInt64.TryParse(titleDir.Name, System.Globalization.NumberStyles.HexNumber, null, out ulong titleId))
                            {
                                foreach (var modDir in titleDir.EnumerateDirectories())
                                {
                                    var modEntry = new ModEntry(modDir, true);

                                    Logger.PrintInfo(LogClass.Application, $"Found Mod [{titleId:X16}] '{modEntry.ModName}'{(modEntry.Exefs.Exists ? " Exefs" : "")}{(modEntry.Romfs.Exists ? " Romfs" : "")}");

                                    if (!(modEntry.Romfs.Exists || modEntry.Exefs.Exists))
                                    {
                                        Logger.PrintWarning(LogClass.Application, $"{modEntry.ModName} is empty");
                                    }

                                    if (!modNames.Add(modDir.Name))
                                    {
                                        Logger.PrintWarning(LogClass.Application, $"Duplicate mod name '{modDir.Name}'");
                                    }

                                    if (Mods.TryGetValue(titleId, out List<ModEntry> modEntries))
                                    {
                                        modEntries.Add(modEntry);
                                    }
                                    else
                                    {
                                        Mods.Add(titleId, new List<ModEntry> { modEntry });
                                    }
                                }
                            }
                            break;
                    }
                }
            }
        }

        internal IStorage ApplyLayeredFs(ulong titleId, IStorage baseStorage)
        {
            if (Mods.TryGetValue(titleId, out var titleMods))
            {
                var romfsDirs = titleMods
                                .Where(mod => mod.Enabled && mod.Romfs.Exists)
                                .Select(mod => mod.Romfs);

                var layers = new List<IFileSystem>();

                foreach (var romfsDir in romfsDirs)
                {
                    LocalFileSystem fs = new LocalFileSystem(romfsDir.FullName);
                    layers.Add(fs);
                }

                if (layers.Count > 0)
                {
                    layers.Add(new RomFsFileSystem(baseStorage));

                    LayeredFileSystem lfs = new LayeredFileSystem(layers);

                    Logger.PrintInfo(LogClass.Loader, $"Applying {layers.Count - 1} layers to RomFS");
                    IStorage lfsStorage = new RomFsBuilder(lfs).Build();
                    Logger.PrintInfo(LogClass.Loader, "Finished building modded RomFS");

                    return lfsStorage;
                }
            }

            return baseStorage;
        }

        internal void ApplyProgramPatches(ulong titleId, int protectedOffset, params IExecutable[] programs)
        {
            var exefsDirs = (Mods.TryGetValue(titleId, out var titleMods) ? titleMods : Enumerable.Empty<ModEntry>())
                            .Where(mod => mod.Enabled && mod.Exefs.Exists)
                            .Select(mod => mod.Exefs)
                            .Concat(_unconditionalMods);

            ApplyProgramPatches(exefsDirs, protectedOffset, programs);
        }

        private void ApplyProgramPatches(IEnumerable<DirectoryInfo> dirs, int protectedOffset, params IExecutable[] programs)
        {
            MemPatch[] patches = new MemPatch[programs.Length];

            for (int i = 0; i < patches.Length; ++i)
            {
                patches[i] = new MemPatch();
            }

            var buildIds = programs.Select(p => p switch
            {
                NsoExecutable nso => BitConverter.ToString(nso.BuildId).Replace("-", "").TrimEnd('0'),
                NroExecutable nro => BitConverter.ToString(nro.Header.BuildId).Replace("-", "").TrimEnd('0'),
                _ => string.Empty
            }).ToList();

            int GetIndex(string buildId) => buildIds.FindIndex(id => id == buildId); // O(n) but list is small

            foreach (var patchDir in dirs)
            {
                foreach (var patchFile in patchDir.EnumerateFiles())
                {
                    switch (patchFile.Extension)
                    {
                        case ".ips":
                            {
                                string filename = Path.GetFileNameWithoutExtension(patchFile.FullName).Split('.')[0];
                                string buildId = filename.TrimEnd('0');

                                int index = GetIndex(buildId);
                                if (index == -1)
                                {
                                    continue;
                                }

                                Logger.PrintInfo(LogClass.Loader, $"Found matching IPS patch for bid={buildId} - '{patchFile.Name}'");

                                using var fs = patchFile.OpenRead();
                                using var reader = new BinaryReader(fs);

                                var patcher = new IpsPatcher(reader);
                                patcher.AddPatches(patches[index]);
                            }
                            break;

                        case ".pchtxt":
                            using (var fs = patchFile.OpenRead())
                            using (var reader = new StreamReader(fs))
                            {
                                var patcher = new IPSwitchPatcher(reader);

                                int index = GetIndex(patcher.BuildId);
                                if (index == -1)
                                {
                                    continue;
                                }

                                Logger.PrintInfo(LogClass.Loader, $"Found matching IPSwitch patch for bid={patcher.BuildId} - '{patchFile.Name}'");

                                patcher.AddPatches(patches[index]);
                            }
                            break;
                    }
                }
            }

            for (int i = 0; i < programs.Length; ++i)
            {
                patches[i].Apply(programs[i].Program, protectedOffset);
            }
        }
    }
}