﻿using Newtonsoft.Json;
using OpenBound_Network_Object_Library.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using OpenBound_Network_Object_Library.FileManagement;
using System.Linq;

namespace OpenBound_Network_Object_Library.FileManagement
{
    public class GamePatcher
    {
        private static string BuildPatchPath(string outputPath, ApplicationManifest applicationManifest) =>
            @$"{outputPath}\{NetworkObjectParameters.GamePatchFilename}-{DateTime.UtcNow:dd-MM-yyyy}-{applicationManifest.ID}{NetworkObjectParameters.GamePatchExtension}";

        /// <summary>
        /// Extracts and verifies if the extracted file is correct. Returns true if the extracted file is correct.
        /// </summary>
        /// <param name="gameFolderPath"></param>
        /// <param name="patchPath"></param>
        /// <returns></returns>
        public static bool ApplyUpdatePatch(string gameFolderPath, string patchPath)
        {
            //Extracting zip files into the directory
            using (ZipArchive patch = ZipFile.OpenRead(patchPath))
                patch.ExtractToDirectory(gameFolderPath, overwriteFiles: true);

            //Reading Manifest
            string manifestOldPath = $@"{gameFolderPath}\{NetworkObjectParameters.ManifestFilename}{NetworkObjectParameters.ManifestExtension}";
            ApplicationManifest appManifest = ObjectWrapper.Deserialize<ApplicationManifest>(File.ReadAllText(manifestOldPath));

            //Moving Manifest
            string manifestNewPath = $@"{gameFolderPath}\{NetworkObjectParameters.ManifestFilename}-{appManifest.ID}{NetworkObjectParameters.ManifestExtension}";
            File.Move(manifestOldPath, manifestNewPath);

            //Files to be deleted
            foreach (string toBeDeletedFile in appManifest.CurrentVersionFileList.ToBeDeleted)
                File.Delete(toBeDeletedFile);

            //Verify game cache integrity
            return Manifest.VerifyMD5Checksum(gameFolderPath, appManifest);
        }

        public static ApplicationManifest GenerateUpdatePatch(string currentVersionFolderPath, string newVersionFolderPath, string outputPackagePath)
        {
            //Create ApplicationManifest given the new and the old game folder
            ApplicationManifest appManifest = Manifest.GenerateChecksumManifest(currentVersionFolderPath, newVersionFolderPath);

            string tmpAppManifestFilename = Path.GetTempFileName();

            using (ZipArchive zipArchive = ZipFile.Open(BuildPatchPath(outputPackagePath, appManifest), ZipArchiveMode.Update))
            {
                foreach (string filePath in appManifest.CurrentVersionFileList.ToBeDownloaded)
                {
                    zipArchive.CreateEntryFromFile($@"{newVersionFolderPath}\{filePath}", filePath, CompressionLevel.Optimal);
                }

                //Save the manifest file into the temporary folder, add it into the zip and delete it
                File.WriteAllText(tmpAppManifestFilename, ObjectWrapper.Serialize(appManifest, Formatting.Indented));
                zipArchive.CreateEntryFromFile(tmpAppManifestFilename, NetworkObjectParameters.ManifestFilename + NetworkObjectParameters.ManifestExtension);
                File.Delete(tmpAppManifestFilename);

                return appManifest;
            }
        }

        /// <summary>
        /// Merge two patchs into one zip file
        /// </summary>
        /// <param name="patch1"></param>
        /// <param name="patch2"></param>
        /// <param name="outputPackagePath"></param>
        /// <returns></returns>
        public static ApplicationManifest MergeUpdatePatch(string patch1, string patch2, string outputPackagePath)
        {
            using (ZipArchive zipArchive1 = ZipFile.Open(patch1, ZipArchiveMode.Read))
            using (ZipArchive zipArchive2 = ZipFile.Open(patch2, ZipArchiveMode.Read))
            {
                string tmpFolder = @$"{Path.GetTempPath()}{Guid.NewGuid()}";
                string manifestFilePath = $@"{tmpFolder}\{NetworkObjectParameters.ManifestFilename}{NetworkObjectParameters.ManifestExtension}";

                zipArchive1.ExtractToDirectory(tmpFolder, true);
                ApplicationManifest appManifest1 = ObjectWrapper.Deserialize<ApplicationManifest>(File.ReadAllText(manifestFilePath));

                zipArchive2.ExtractToDirectory(tmpFolder, true);
                ApplicationManifest appManifest2 = ObjectWrapper.Deserialize<ApplicationManifest>(File.ReadAllText(manifestFilePath));

                ApplicationManifest newManifest = new ApplicationManifest(appManifest1, appManifest2);
                File.WriteAllText(manifestFilePath, ObjectWrapper.Serialize(newManifest, Formatting.Indented));

                using (ZipArchive outputZipArchive = ZipFile.Open(BuildPatchPath(outputPackagePath, newManifest), ZipArchiveMode.Update))
                {
                    foreach (string filePath in newManifest.CurrentVersionFileList.ToBeDownloaded)
                        outputZipArchive.CreateEntryFromFile($@"{tmpFolder}\{filePath}", filePath, CompressionLevel.Optimal);

                    outputZipArchive.CreateEntryFromFile(manifestFilePath, $@"{NetworkObjectParameters.ManifestFilename}{NetworkObjectParameters.ManifestExtension}", CompressionLevel.Optimal);
                }

                Directory.Delete(tmpFolder);

                return newManifest;
            }
        }
    }
}