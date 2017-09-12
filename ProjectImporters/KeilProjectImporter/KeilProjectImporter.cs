﻿using BSPEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace KeilProjectImporter
{
    public class KeilProjectImporter : IExternalProjectImporter
    {
        public string Name => "Keil";

        public string ImportCommandText => "Import an existing Keil Project";

        public string ProjectFileFilter => "Keil Project Files|*.uvprojx";
        public string HelpText => null;
        public string HelpURL => null;

        public string UniqueID => "com.sysprogs.project_importers.keil";

        static string AdjustPath(string baseDir, string path)
        {
            var finalPath = Path.GetFullPath(Path.Combine(baseDir, path));
            //Try automatically replacing IAR-specific files with GCC-specific versions (this will only work if the directory structure stores them in 'IAR' and 'GCC' subdirectories respectively).
            if (finalPath.IndexOf("\\RVDS\\", StringComparison.InvariantCultureIgnoreCase) != -1)
            {
                var substitute = finalPath.ToLower().Replace("\\rvds\\", "\\gcc\\");
                if (File.Exists(substitute) || Directory.Exists(substitute)) 
                    finalPath = substitute;
            }
            if (finalPath.EndsWith(".lib", StringComparison.InvariantCultureIgnoreCase) && finalPath.IndexOf("_Keil", StringComparison.InvariantCultureIgnoreCase) != -1)
            {
                string dir = Path.GetDirectoryName(finalPath);
                string fn = Path.GetFileName(finalPath);

                var substitute = Path.Combine(dir, Path.ChangeExtension(fn.ToLower().Replace("_keil", "_gcc"), ".a"));
                if (File.Exists(substitute))
                    finalPath = substitute;
            }

            return finalPath;
        }

        public ImportedExternalProject ImportProject(ProjectImportParameters parameters, IProjectImportService service)
        {
            XmlDocument xml = new XmlDocument();
            xml.Load(parameters.ProjectFile);

            var target = xml.SelectSingleNode("Project/Targets/Target") as XmlElement;
            if (target == null)
                throw new Exception("Failed to locate the target node in " + parameters.ProjectFile);

            string deviceName = (target.SelectSingleNode("TargetOption/TargetCommonOption/Device") as XmlElement)?.InnerText;
            if (deviceName == null)
                throw new Exception("Failed to extract the device name from " + parameters.ProjectFile);

            if (deviceName.EndsWith("x"))
            {
                deviceName = deviceName.TrimEnd('x');
                deviceName = deviceName.Substring(0, deviceName.Length - 1);
            }

            string baseDir = Path.GetDirectoryName(parameters.ProjectFile);
            ImportedExternalProject.ConstructedVirtualDirectory rootDir = new ImportedExternalProject.ConstructedVirtualDirectory();

            foreach (var group in target.SelectNodes("Groups/Group").OfType<XmlElement>())
            {
                string virtualPath = group.SelectSingleNode("GroupName")?.InnerText;
                if (string.IsNullOrEmpty(virtualPath))
                    continue;

                var subdir = rootDir.ProvideSudirectory(virtualPath);
                foreach (var file in group.SelectNodes("Files/File").OfType<XmlElement>())
                {
                    string path = file.SelectSingleNode("FilePath")?.InnerText;
                    string type = file.SelectSingleNode("FileType")?.InnerText;
                    if (type == "2")
                    {
                        //This is an assembly file. Keil uses a different assembly syntax than GCC, so we cannot include this file into the project.
                        //The end user will need to include a GCC-specific replacement manually (unless this is the startup file, in which case VisualGDB
                        //automatically includes a GCC-compatible replacement).
                        continue;
                    }
                    if (string.IsNullOrEmpty(path))
                        continue;

                    subdir.AddFile(AdjustPath(baseDir, path), type == "5");
                }
            }

            List<string> macros = new List<string> { "$$com.sysprogs.bspoptions.primary_memory$$_layout" };
            List<string> includeDirs = new List<string>();

            var optionsNode = target.SelectSingleNode("TargetOption/TargetArmAds/Cads/VariousControls");
            if (optionsNode != null)
            {
                macros.AddRange((optionsNode.SelectSingleNode("Define")?.InnerText ?? "").Split(',').Select(m=>m.Trim()));
                includeDirs.AddRange((optionsNode.SelectSingleNode("IncludePath")?.InnerText ?? "")
                    .Split(';')
                    .Select(p => AdjustPath(baseDir, p.Trim())));
            }

            return new ImportedExternalProject
            {
                DeviceNameMask = new Regex(deviceName.Replace("x", ".*") + ".*"),
                OriginalProjectFile = parameters.ProjectFile,
                RootDirectory = rootDir,
                GNUTargetID = "arm-eabi",
                ReferencedFrameworks = new string[0],   //Unless this is explicitly specified, VisualGDB will try to reference the default frameworks (STM32 HAL) that will conflict with the STM32CubeMX-generated files.

                Configurations = new[]
                {
                    new ImportedExternalProject.ImportedConfiguration
                    {
                        Settings = new ImportedExternalProject.InvariantProjectBuildSettings
                        {
                            IncludeDirectories = includeDirs.ToArray(),
                            PreprocessorMacros = macros.ToArray()
                        }
                    }
                }
            };
        }
    }
}