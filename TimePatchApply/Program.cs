using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kontract.Interfaces.FileSystem;
using Kontract.Interfaces.Managers;
using Kontract.Interfaces.Providers;
using Kontract.Models.Archive;
using Kontract.Models.Context;
using Kore.Factories;
using Kore.Managers;
using plugin_criware.Archives;
using plugin_nintendo.Archives;
using VCDiff.Decoders;
using VCDiff.Includes;

namespace TimePatchApply
{
    class Program
    {
        private const string Welcome_ =
            @"
##################################################
# This is the Time Travelers Patch Applier. This #
# tool applies a patch file containing delta-    #
# diffed files to a .cia or .3ds version of Time #
# Travelers and creates a LayeredFS-ready        #
# payload.                                       #
##################################################
";

        private static async Task Main(string[] args)
        {
            // Print welcome text
            Console.WriteLine(Welcome_);

            // Get path arguments
            GetPathArguments(args, out var gamePath, out var patchPath, out var outputPath);

            // Apply patch
            string layeredFsFolder = await ApplyPatch(gamePath, patchPath, outputPath);

            // Finish up
            if (layeredFsFolder != null)
            {
                Console.WriteLine();
                Console.WriteLine($"The LayeredFS-ready files can be found in \"{Path.GetFullPath(layeredFsFolder)}\".");
            }
        }

        private static void GetPathArguments(string[] args, out string gamePath, out string patchPath, out string outputPath)
        {
            // Get cia or 3ds game path
            Console.WriteLine("Enter the path to the .3ds or .cia of Time Travelers:");
            Console.Write("> ");

            gamePath = args.Length > 0 ? args[0] : Console.ReadLine();
            if (args.Length > 0)
                Console.WriteLine(args[0]);
            Console.WriteLine();

            // Get patch file
            Console.WriteLine("Enter the path to the patch file (.pat):");
            Console.Write("> ");

            patchPath = args.Length > 1 ? args[1] : Console.ReadLine();
            if (args.Length > 1)
                Console.WriteLine(args[1]);
            Console.WriteLine();

            // Get output path
            Console.WriteLine("Enter the directory, in which the LayeredFS structure will be created:");
            Console.Write("> ");

            outputPath = args.Length > 2 ? args[2] : Console.ReadLine();
            if (args.Length > 2)
                Console.WriteLine(args[2]);
            Console.WriteLine();
        }

        // TODO: Create CPK from scratch without extracting 2GB donor CPK first?
        private static async Task<string> ApplyPatch(string gamePath, string patchPath, string outputPath)
        {
            // Try to open patch file
            if (!TryLoadPatch(patchPath, out PatchFile patchFile))
                return null;

            // Try to open game
            var partitions = await LoadGamePartitions(gamePath);
            if (partitions == null)
                return null;

            // Create folder structure for LayeredFS
            var titleIdFolder = Path.Combine(outputPath, "000400000008C600");
            Directory.CreateDirectory(titleIdFolder);

            // Try to open GameData.cxi
            IArchiveFileInfo gameDataFile = partitions.FirstOrDefault(x => x.FilePath == "/GameData.cxi");
            if (gameDataFile == null)
            {
                Console.WriteLine($"Could not find GameData.cxi in \"{gamePath}\".");
                return null;
            }

            Stream gameDataFileStream = await gameDataFile.GetFileData();

            if (!TryLoadGameFiles(gameDataFileStream, gamePath, out IList<IArchiveFileInfo> gameFiles))
                return null;

            // Try to open tt1_ctr.cpk
            IArchiveFileInfo cpkArchiveFile = gameFiles.FirstOrDefault(x => x.FilePath == "/RomFs/tt1_ctr.cpk");
            if (cpkArchiveFile == null)
            {
                Console.WriteLine($"Could not find tt1_ctr.cpk in \"{gamePath}\".");
                return null;
            }

            Stream cpkArchiveFileStream = await cpkArchiveFile.GetFileData();

            if (!TryLoadCpkFiles(cpkArchiveFileStream, gamePath, out Cpk cpkState, out IList<IArchiveFileInfo> cpkFiles))
                return null;

            // Apply patches from patch file
            Console.Write("Apply patches to patch.cpk... ");

            var patchedCpkFiles = new List<IArchiveFileInfo>();
            foreach (IArchiveFileInfo cpkFile in cpkFiles)
            {
                if (!patchFile.HasPatch(cpkFile.FilePath.FullName))
                {
                    // Delete file, if no patch exists
                    cpkState.DeleteFile(cpkFile);
                    continue;
                }

                // Otherwise apply VCDiff patch
                var source = await cpkFile.GetFileData();
                var delta = patchFile.GetPatch(cpkFile.FilePath.FullName);
                var output = new MemoryStream();

                var coder = new VcDecoder(source, delta, output);
                var result = coder.Decode(out _);

                delta.Close();

                if (result != VCDiffResult.SUCCESS)
                {
                    Console.WriteLine($"An error occurred applying the patch to \"{cpkFile}\" ({result}).");
                    output.Close();

                    continue;
                }

                // Replace file in archive with patched file
                output.Position = 0;
                cpkFile.SetFileData(output);

                patchedCpkFiles.Add(cpkFile);
            }

            Console.WriteLine("Done");

            // Save patch.cpk
            Console.Write("Save changes to patch.cpk...");

            var patchCpkPath = Path.Combine(titleIdFolder, "romfs", "patch.cpk");
            Directory.CreateDirectory(Path.GetDirectoryName(patchCpkPath)!);

            using Stream patchCpkStream = File.Create(patchCpkPath);

            cpkState.Save(patchCpkStream, patchedCpkFiles);

            Console.WriteLine(" Done");

            // Apply code.bin patches
            Console.Write("Apply patches to code.bin...");

            if (patchFile.HasPatch(".code"))
            {
                IArchiveFileInfo codeFile = gameFiles.FirstOrDefault(x => x.FilePath == "/ExeFs/.code");
                if (codeFile != null)
                {
                    using Stream codeFileStream = await codeFile.GetFileData();

                    var delta = patchFile.GetPatch(".code");
                    var output = new MemoryStream();

                    var coder = new VcDecoder(codeFileStream, delta, output);
                    var result = coder.Decode(out _);

                    delta.Close();
                    codeFileStream.Close();

                    if (result == VCDiffResult.SUCCESS)
                    {
                        // Save .code to output
                        var codeOutput = File.OpenWrite(Path.Combine(titleIdFolder, "code.bin"));

                        output.Position = 0;
                        output.CopyTo(codeOutput);

                        output.Close();
                        codeOutput.Close();
                    }
                    else
                    {
                        Console.WriteLine("An error occurred applying the patch to \".code\".");
                        output.Close();
                    }
                }
            }

            Console.WriteLine(" Done");

            // Apply exheader.bin patches
            Console.Write("Apply patches to exheader.bin...");

            if (patchFile.HasPatch("exheader.bin"))
            {
                IArchiveFileInfo exHeaderFile = gameFiles.FirstOrDefault(x => x.FilePath == "/ExHeader.bin");
                if (exHeaderFile != null)
                {
                    using Stream exHeaderStream = await exHeaderFile.GetFileData();

                    var delta = patchFile.GetPatch("exheader.bin");
                    var output = new MemoryStream();

                    var coder = new VcDecoder(exHeaderStream, delta, output);
                    var result = coder.Decode(out _);

                    delta.Close();
                    exHeaderStream.Close();

                    if (result == VCDiffResult.SUCCESS)
                    {
                        // Save exheader.bin to output
                        var codeOutput = File.OpenWrite(Path.Combine(titleIdFolder, "exheader.bin"));

                        output.Position = 0;
                        output.CopyTo(codeOutput);

                        output.Close();
                        codeOutput.Close();
                    }
                    else
                    {
                        Console.WriteLine("An error occurred applying the patch to \"exheader.bin\".");
                        output.Close();
                    }
                }
            }

            Console.WriteLine(" Done");

            return titleIdFolder;
        }

        #region Patch File

        private static bool TryLoadPatch(string filePath, out PatchFile patch)
        {
            patch = null;

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Could not find patch file \"{filePath}\".");
                return false;
            }

            try
            {
                patch = PatchFile.Open(filePath);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not load patch file \"{filePath}\". Error: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Cpk Data

        private static bool TryLoadCpkFiles(Stream cpkData, string gamePath, out Cpk cpk, out IList<IArchiveFileInfo> files)
        {
            cpk = null;
            files = null;

            try
            {
                cpk = new Cpk();
                files = cpk.Load(cpkData);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not load tt1_ctr.cpk from \"{gamePath}\". Error: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Game Data

        private static bool TryLoadGameFiles(Stream gameData, string gamePath, out IList<IArchiveFileInfo> files)
        {
            files = null;

            try
            {
                var ncch = new NCCH();
                files = ncch.Load(gameData);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not load GameData.cxi from \"{gamePath}\". Error: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Game Card

        static async Task<IList<IArchiveFileInfo>> LoadGamePartitions(string gamePath)
        {
            // Check if file exists
            if (!File.Exists(gamePath))
            {
                Console.WriteLine($"The file \"{gamePath}\" does not exist.");
                return null;
            }

            // Check if the file can be opened as readable
            FileStream fileStream;
            try
            {
                fileStream = File.OpenRead(gamePath);
            }
            catch (Exception)
            {
                Console.WriteLine($"The file \"{gamePath}\" can not be opened. Is it open in another program?");
                return null;
            }

            bool isNcsd = await IsNcsd(gamePath);

            if (!TryLoadGamePartitions(fileStream, isNcsd, out IList<IArchiveFileInfo> files))
                return null;

            return files;
        }

        private static async Task<bool> IsNcsd(string gamePath)
        {
            IStreamManager streamManager = new StreamManager();

            using IFileSystem fileSystem = FileSystemFactory.CreatePhysicalFileSystem(streamManager);
            gamePath = (string)fileSystem.ConvertPathFromInternal(gamePath);

            ITemporaryStreamProvider temporaryStreamProvider = streamManager.CreateTemporaryStreamProvider();

            var ncsdPlugin = new NcsdPlugin();
            var identifyContext = new IdentifyContext(temporaryStreamProvider);

            bool isNcsd = await ncsdPlugin.IdentifyAsync(fileSystem, gamePath, identifyContext);

            streamManager.ReleaseAll();

            return isNcsd;
        }

        private static bool TryLoadGamePartitions(FileStream fileStream, bool isNcsd, out IList<IArchiveFileInfo> files)
        {
            files = null;

            try
            {
                if (isNcsd)
                {
                    var ncsd = new NCSD();
                    files = ncsd.Load(fileStream);

                    return true;
                }

                var cia = new CIA();
                files = cia.Load(fileStream);

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not load file \"{fileStream.Name}\". Error: {e.Message}");
                Console.WriteLine("Possible reasons could be that the file is not a .3ds or .cia, or is not decrypted.");

                throw e;
            }
        }

        #endregion
    }
}
