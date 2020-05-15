using LibHac.Common;
using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.FsSystem.RomFs;
using LibHac.FsService;
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
        private const string RomfsStorageFile = "romfs.storage";
        private const string ExefsDir = "exefs";
        private const string ExefsPatchesDir = "exefs_patches";
        private const string NroPatchesDir = "nro_patches";

        public struct ModEntry
        {
            public readonly DirectoryInfo ModDir;
            public readonly DirectoryInfo Exefs;
            public readonly DirectoryInfo Romfs;
            public readonly FileInfo RomfsFile;

            public bool Enabled;

            public ModEntry(DirectoryInfo modDir, bool enabled)
            {
                ModDir = modDir;

                Exefs = new DirectoryInfo(Path.Combine(modDir.FullName, ExefsDir));
                Romfs = new DirectoryInfo(Path.Combine(modDir.FullName, RomfsDir));
                RomfsFile = new FileInfo(Path.Combine(modDir.FullName, RomfsStorageFile));

                Enabled = enabled;
            }

            public string ModName => ModDir.Name;
            public bool Empty => !(Exefs.Exists || RomfsFile.Exists || Romfs.Exists);

            public override string ToString() => $"[{(Exefs.Exists ? "E" : "")}{(RomfsFile.Exists ? "r" : "")}{(Romfs.Exists ? "R" : "")}] '{ModName}'";
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
                        case NroPatchesDir:
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
                            if (titleDir.Name.Length >= 16 && ulong.TryParse(titleDir.Name.Substring(0, 16), System.Globalization.NumberStyles.HexNumber, null, out ulong titleId))
                            {
                                foreach (var modDir in titleDir.EnumerateDirectories())
                                {
                                    var modEntry = new ModEntry(modDir, true);

                                    Logger.PrintInfo(LogClass.Application, $"Found Mod [{titleId:X16}] {modEntry}");

                                    if (modEntry.Empty)
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

        // TODO: Combine both romfs methods
        private int ApplyRomFsModStorages(IEnumerable<FileInfo> storageFiles, HashSet<string> fileSet, RomFsBuilder builder)
        {
            int modCount = 0;

            foreach (var storageFile in storageFiles)
            {
                var fs = new RomFsFileSystem(storageFile.OpenRead().AsStorage());
                foreach (var entry in fs.EnumerateEntries()
                                       .Where(f => f.Type == DirectoryEntryType.File)
                                       .OrderBy(f => f.FullPath, StringComparer.Ordinal))
                {
                    fs.OpenFile(out IFile file, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                    if (fileSet.Add(entry.FullPath))
                    {
                        builder.AddFile(entry.FullPath, file);
                    }
                    else
                    {
                        Logger.PrintWarning(LogClass.Loader, $"    Skipped duplicate file '{entry.FullPath}' from '{storageFile.Directory.Name}'");
                    }
                }

                modCount++;
            }

            return modCount;
        }

        private int ApplyRomFsModDirs(IEnumerable<DirectoryInfo> romfsDirs, HashSet<string> fileSet, RomFsBuilder builder)
        {
            int modCount = 0;

            foreach (var romfsDir in romfsDirs)
            {
                using LocalFileSystem fs = new LocalFileSystem(romfsDir.FullName);
                foreach (var entry in fs.EnumerateEntries()
                                       .Where(f => f.Type == DirectoryEntryType.File)
                                       .OrderBy(f => f.FullPath, StringComparer.Ordinal))
                {
                    fs.OpenFile(out IFile file, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                    if (fileSet.Add(entry.FullPath))
                    {
                        builder.AddFile(entry.FullPath, file);
                    }
                    else
                    {
                        Logger.PrintWarning(LogClass.Loader, $"    Skipped duplicate file '{entry.FullPath}' from '{romfsDir.Parent.Name}'");
                    }
                }

                modCount++;
            }

            return modCount;
        }

        internal IStorage ApplyRomFsMods(ulong titleId, IStorage baseStorage)
        {
            if (Mods.TryGetValue(titleId, out var titleMods))
            {
                var enabledMods = titleMods.Where(mod => mod.Enabled);

                var romfsContainers = enabledMods
                                      .Where(mod => mod.RomfsFile.Exists)
                                      .Select(mod => mod.RomfsFile);

                var romfsDirs = enabledMods
                                .Where(mod => !mod.RomfsFile.Exists && mod.Romfs.Exists)
                                .Select(mod => mod.Romfs);

                var fileSet = new HashSet<string>();
                var builder = new RomFsBuilder();

                int appliedCount = 0;

                Logger.PrintInfo(LogClass.Loader, "Collecting RomFs Containers...");
                appliedCount += ApplyRomFsModStorages(romfsContainers, fileSet, builder);

                Logger.PrintInfo(LogClass.Loader, "Collecting RomFs Dirs...");
                appliedCount += ApplyRomFsModDirs(romfsDirs, fileSet, builder);

                if (appliedCount == 0)
                {
                    Logger.PrintInfo(LogClass.Loader, "Using base RomFs");
                    return baseStorage;
                }

                Logger.PrintInfo(LogClass.Loader, $"Found {fileSet.Count} modded files over {appliedCount} mods. Processing base storage...");
                var baseRfs = new RomFsFileSystem(baseStorage);

                foreach (var entry in baseRfs.EnumerateEntries()
                                             .Where(f => f.Type == DirectoryEntryType.File && !fileSet.Contains(f.FullPath))
                                             .OrderBy(f => f.FullPath, StringComparer.Ordinal))
                {
                    baseRfs.OpenFile(out IFile file, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                    builder.AddFile(entry.FullPath, file);
                }

                Logger.PrintInfo(LogClass.Loader, "Building new RomFs...");
                IStorage newStorage = builder.Build();
                Logger.PrintInfo(LogClass.Loader, "Using modded RomFs");

                return newStorage;
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

            static string GetModName(DirectoryInfo exefsDir) => exefsDir.Name == ExefsDir ? exefsDir.Parent.Name : exefsDir.Name;

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

                                Logger.PrintInfo(LogClass.Loader, $"Found IPS patch '{GetModName(patchDir)}'/'{patchFile.Name}' bid={buildId}");

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

                                Logger.PrintInfo(LogClass.Loader, $"Found IPSwitch patch '{GetModName(patchDir)}'/'{patchFile.Name}' bid={patcher.BuildId}");

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