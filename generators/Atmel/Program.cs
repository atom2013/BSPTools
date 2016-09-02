﻿/* Copyright (c) 2016 Sysprogs OU. All Rights Reserved.
   This software is licensed under the Sysprogs BSP Generator License.
   https://github.com/sysprogs/BSPTools/blob/master/LICENSE
*/

using BSPEngine;
using BSPGenerationTools;
using LinkerScriptGenerator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;


namespace Atmel_bsp_generator
{
    class Program
    {
        class SLabBSPBuilder : BSPBuilder
        {
            const uint FLASHBase = 0x00000000, SRAMBase = 0x20000000;

            public SLabBSPBuilder(BSPDirectories dirs)
                : base(dirs)
            {
                ShortName = "Atmel";
            }


            public override void GetMemoryBases(out uint flashBase, out uint ramBase)
            {
                flashBase = FLASHBase;
                ramBase = SRAMBase;
            }

            public override MemoryLayout GetMemoryLayout(MCUBuilder mcu, MCUFamilyBuilder family)
            {
                //No additional memory information available for this MCU. Build a basic memory layout from known RAM/FLASH sizes.
                MemoryLayout layout = new MemoryLayout();
                layout.Memories = new List<Memory>();
                layout.DeviceName = mcu.Name;
                string shName = mcu.Name;
                if (mcu.Name.StartsWith("AT"))
                    shName = mcu.Name.Substring(2, mcu.Name.Length - 2);

                string aFileName = family.BSP.Directories.InputDir + "\\Sam\\Utils\\Cmsis\\" + family.FamilyFilePrefix + "\\Include\\" + shName;

                if (family.FamilyFilePrefix.ToUpper().StartsWith("SAMG"))
                    aFileName = family.BSP.Directories.InputDir + "\\Sam\\Utils\\Cmsis\\samg\\" + family.FamilyFilePrefix + "\\Include\\" + shName;

                if (family.Definition.Name.ToUpper() == "SAM4C" || family.Definition.Name.ToUpper() == "SAM4CM" || family.Definition.Name.ToUpper() == "SAM4CP" || family.Definition.Name.ToUpper() == "SAM4CM32")
                    aFileName = aFileName + "_0.h";
                else
                    aFileName = aFileName + ".h";

                int RAMStart = -1;
                int FLASHStart = -1;
                foreach (var ln in File.ReadAllLines(aFileName))
                {
                    var m = Regex.Match(ln, @"(#define [IH]?RAM[C]?[0]?_ADDR[ \t]*.*0x)([0-9A-Fa-f]+[^uU]+)+.*");
                  
                    if (m.Success)
                    {
                        RAMStart = Convert.ToInt32(m.Groups[2].Value, 16);
                    }

                    m = Regex.Match(ln, @"(#define [I]?FLASH[0]?_ADDR[ \t]*.*0x)([0-9A-Fa-f]+[^u|U]+)+.*");
                    if (!m.Success)
                        m = Regex.Match(ln, @"(#define [I]?FLASH[0]?_CNC_ADDR[ \t]*.*0x)([0-9A-Fa-f]+[^u|U]+)+.*");
                    if (m.Success)
                    {
                        FLASHStart = Convert.ToInt32(m.Groups[2].Value, 16);
                    }
                    if (FLASHStart > -1 && RAMStart > -1)
                        break;
                }
                if (RAMStart == -1)
                    throw new Exception("no RAM Start");
                //    Console.WriteLine("RAMBase mcu {0} NO file :{1} ", mcu.Name, aFileName);
                if (FLASHStart == -1)
                    throw new Exception("no FLASH Start");
             //   Console.WriteLine("FLASHBase mcu {0} NO file :{1} ",mcu.Name , aFileName);
                layout.Memories.Insert(0, new Memory
                {
                    Name = "FLASH",
                    Access = MemoryAccess.Undefined,
                    Type = MemoryType.FLASH,
                    Start = (uint)FLASHStart,
                    Size = (uint)mcu.FlashSize
                });
                layout.Memories.Insert(0, new Memory
                {
                    Name = "SRAM",
                    Access = MemoryAccess.Undefined,
                    Type = MemoryType.RAM,
                    Start = (uint)RAMStart,
                    Size = (uint)mcu.RAMSize
                });

                return layout;
            }
        }


        static IEnumerable<StartupFileGenerator.InterruptVectorTable> ParseStartupFiles(string startupFileName, MCUFamilyBuilder fam)
        {
            string aStartNewName;
                string strPrefFam;
            if (fam.Definition.Name.StartsWith("AT"))
                strPrefFam = fam.Definition.Name.Substring(2, fam.Definition.Name.Length - 2);
            else
                strPrefFam = fam.Definition.Name;
            if(strPrefFam != "SAMG51")
                strPrefFam = strPrefFam.Substring(0, strPrefFam.Length - 1);

            if (strPrefFam == "SAM4cm3")
                strPrefFam = strPrefFam.Substring(0, strPrefFam.Length - 1);

            foreach (var fl in Directory.GetFiles(Path.GetDirectoryName(startupFileName), strPrefFam + "*.h", SearchOption.TopDirectoryOnly))
            {
                string aNameFl = fl.Substring(fl.LastIndexOf("\\")+1, fl.Length - fl.LastIndexOf("\\")-1);
                if (Path.GetFileNameWithoutExtension(aNameFl).ToUpper() == strPrefFam.ToUpper())
                    continue;
                if (aNameFl.EndsWith("_1.h"))
                    continue;
                if (aNameFl.EndsWith("_0.h"))
                    aNameFl = aNameFl.Replace("_0.h", ".h");

                 aStartNewName = "startup_" + aNameFl;

                List<StartupFileGenerator.InterruptVector[]> list = new List<StartupFileGenerator.InterruptVector[]>();
                list.Add(StartupFileGenerator.ParseInterruptVectors(fl,
                         @"^typedef struct _DeviceVectors.*",
                         @"} DeviceVectors;.*",
                         @"[ \t]+void\*[ ]+([\w]+).*",
                         @"([^ \t,]+)[,]?.*",
                         @"^[ \t]*[/{]+.*",
                         null,
                         1,
                         2));
                List<StartupFileGenerator.InterruptVector> vectors = new List<StartupFileGenerator.InterruptVector>(list[0]);
                list.RemoveAt(0);

                //Fix the vector names from comments
                for (int i = 0; i < vectors.Count; i++)
                {
                    if (vectors[i] == null)
                        continue;

                    if (i == 0)
                    {
                        vectors[i].Name = "_estack";
                        continue;
                    }
                    else if (i == 1)
                    {
                        vectors[i].Name = "Reset_Handler";
                        continue;
                    }
                    else if (vectors[i].Name.StartsWith("pvReserved"))
                    {
                        vectors[i] = null;
                        continue;
                    }
                    else
                    {
                        if (vectors[i] == null)
                            continue;

                        if (!vectors[i].Name.StartsWith("pfn"))
                            throw new Exception("no pfn Func Startup Header");

                        vectors[i].Name = vectors[i].Name.Substring(3, vectors[i].Name.Length - 3);
                    }

                }
                yield return new StartupFileGenerator.InterruptVectorTable
                {
                    FileName = Path.ChangeExtension(Path.GetFileName(aStartNewName), ".c"),
                    MatchPredicate = m =>(Path.GetFileNameWithoutExtension(aNameFl).ToUpper() == m.Name.ToUpper()),
                    Vectors = vectors.ToArray()
                };
            }
        }

        private static IEnumerable<MCUDefinitionWithPredicate> ParsePeripheralRegisters(string dir, MCUFamilyBuilder fam)
        {
            List<MCUDefinitionWithPredicate> RegistersPeriphs = new List<MCUDefinitionWithPredicate>();
            Dictionary<string, HardwareRegisterSet[]> periphs = PeripheralRegisterGenerator.GenerateFamilyPeripheralRegistersAtmel(dir +"\\"+ fam.Definition.FamilySubdirectory+ "\\Utils\\Include",fam.FamilyFilePrefix);

            foreach (var subfamily in periphs.Keys)
            {
                MCUDefinitionWithPredicate mcu_def = new MCUDefinitionWithPredicate { MCUName = subfamily, RegisterSets = periphs[subfamily], MatchPredicate = m => ( subfamily == m.Name), };
                RegistersPeriphs.Add(mcu_def);
            }
            return RegistersPeriphs;
        }

        static List<MCUBuilder> RemoveDuplicateMCU(ref List<MCUBuilder> rawmcu_list)
        {
            foreach (var amcu in rawmcu_list)
            {
                if (amcu.Name.StartsWith("AT"))
                    amcu.Name = amcu.Name.Substring(2, amcu.Name.Length - 2);
                var idx = amcu.Name.IndexOf("-");
                if (idx > 0)
                    amcu.Name = amcu.Name.Remove(idx);
            }
            return rawmcu_list;
        }
        static List<Framework> GenereteAddFrameWorks(BSPDirectories pBspDir, string pstrFile)
        {
            List<Framework> bleFrameworks = new List<Framework>();
            List<PropertyEntry> propFr = new List<PropertyEntry>();
            PropertyList pl = new PropertyList();

            List<string> a_SimpleFileConditions = new List<string>();
            foreach (var line in File.ReadAllLines(Path.Combine(pBspDir.RulesDir, pstrFile)))
            {
                string dir = line;
                string desc = "";
                string id = dir;

                int idx = line.IndexOf('|');
                if (idx > 0)
                {
                    dir = line.Substring(0, idx);
                    desc = line.Substring(idx + 1);
                    id = Path.GetFileName(dir);
                }
                dir= dir.ToLower();
                
                id = dir.Replace("\\", "_");
                string a_name = line.Substring(line.LastIndexOf("\\") + 1).ToLower(); ;


                dir = a_name;
                string aIncludeMask = "-sam[0-9g]?*;-uc*;*.h";
                a_SimpleFileConditions.Clear();
                if (a_name.ToLower() == "clock")
                {
                               a_SimpleFileConditions.Add(@"sam4l\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4l");
                               a_SimpleFileConditions.Add(@"sam3n\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam3n");
                               a_SimpleFileConditions.Add(@"sam3s\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam3s");
                               a_SimpleFileConditions.Add(@"sam3u\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam3u");
                               a_SimpleFileConditions.Add(@"sam3x\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam3x");
                               a_SimpleFileConditions.Add(@"sam4c\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4c");
                               a_SimpleFileConditions.Add(@"sam4cp\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4cp");
                               a_SimpleFileConditions.Add(@"sam4cm\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4cm");
                               a_SimpleFileConditions.Add(@"sam4e\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4e");
                               a_SimpleFileConditions.Add(@"sam4n\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4n");
                               a_SimpleFileConditions.Add(@"sam4s\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sam4s");
                               a_SimpleFileConditions.Add(@"same70\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == same70");
                               a_SimpleFileConditions.Add(@"sams70\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == sams70");
                               a_SimpleFileConditions.Add(@"samv70\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == samv70");
                               a_SimpleFileConditions.Add(@"samv71\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == samv71");
                               a_SimpleFileConditions.Add(@"samg\\sysclk.c: $$com.sysprogs.atmel.sam32._header_prefix$$ == samg51");

                               aIncludeMask = @"-*/*;*.h";
                }
    

                bleFrameworks.Add(new Framework
                {
                    Name = "Atmel Services " + dir,
                    ID = "com.sysprogs.arm.atmel.service." + dir,
                    ClassID = "com.sysprogs.arm.atmel.cl.services." + dir,
                    ProjectFolderName = "Services_" + dir,
                    DefaultEnabled = false,

                    CopyJobs = new CopyJob[]
                               {
                            new CopyJob
                            {
                                SourceFolder = @"$$BSPGEN:INPUT_DIR$$\common\services\"+dir,
                                TargetFolder = @"common\services\"+dir,
                                FilesToCopy = "-*example*.c;-*example*.h;-*unit_test*.*;-*doxygen*;-*mega*;-*avr*;-*sam0*;-*uc3*;*.c;*.h",
                                AutoIncludeMask =aIncludeMask,
                                ProjectInclusionMask = "*.c",
                                SimpleFileConditions = a_SimpleFileConditions.ToArray(),
            }
            },
                    
                });
            }
            return bleFrameworks;
        }

        static List<Framework> GenereteAddFrameWorksDir(BSPDirectories pBspDir, string pstrFile="")
        {
            List<Framework> bleFrameworks = new List<Framework>();
            List<PropertyEntry> propFr = new List<PropertyEntry>();
            PropertyList pl = new PropertyList();

            List<string> a_SimpleFileConditions = new List<string>();
            string a_macname = "com.sysprogs.arm.atmel.drivers.";
            string strIdFr = "com.sysprogs.arm.atmel.drivers";
            string strClFr = "com.sysprogs.arm.atmel.cl.drivers";
            string nameFr = "Drivers";
            string asrcJob = @"$$BSPGEN:INPUT_DIR$$\sam\drivers";

            string astrTargetJob = @"drivers";
            string astrFileJob = "-*example*.c;-*example*.h;-*unit_test*.*;-*doxygen*;-*mega*;-*avr*;-*adc_enhanced_mode*;-*quick_start*;*.c;*.h";
            string aInclJob = "-sam[0-9g]?*;-uc*;*.h";


            string[] strPropFrim = Directory.GetDirectories(Path.Combine(pBspDir.InputDir, @"sam\drivers"));

            if(pstrFile!="")
            {
                strPropFrim = File.ReadAllLines(Path.Combine(pBspDir.RulesDir, pstrFile));
                a_macname = "com.sysprogs.arm.atmel.services.";
                strIdFr = "com.sysprogs.arm.atmel.services";
                strClFr = "com.sysprogs.arm.atmel.cl.services";
                nameFr = "Services";
                asrcJob = @"$$BSPGEN:INPUT_DIR$$\common\services";
                astrTargetJob = @"services";
            }
            foreach (var line in strPropFrim)// bspBuilder.Directories.RulesDir + @"\EmbFrameworks.txt"))
            {

                string a_name = line.Substring(line.LastIndexOf("\\") + 1).ToLower();
                if(a_name.Contains("gpio"))
                    aInclJob = "*.h";

                propFr.Add(new PropertyEntry.Boolean
                {
                    Name = a_name,
                    UniqueID = a_macname+a_name,
                    ValueForTrue = "1",
                    ValueForFalse = "0",
                });
                a_SimpleFileConditions.Add("^"+a_name + @"\\: $$"+ a_macname+ a_name + "$$ == 1");
            }
                List<PropertyGroup> pg = new List<PropertyGroup>();
                pg.Add( new PropertyGroup {Name = nameFr, Properties = propFr });
                pl.PropertyGroups =  pg;

            bleFrameworks.Add(new Framework
            {
                Name = nameFr + "Library",
                ID = strIdFr,
                ClassID = strClFr,
                ProjectFolderName = "Atmel_" + nameFr,
                DefaultEnabled = false,

                CopyJobs = new CopyJob[]
                       {
                            new CopyJob
                            {
                                SourceFolder = asrcJob ,
                                TargetFolder = astrTargetJob ,                                
                                FilesToCopy = astrFileJob  ,
                                SimpleFileConditions = a_SimpleFileConditions.ToArray(),
                               AutoIncludeMask = aInclJob,
        }
    },
              ConfigurableProperties = pl,

             });            

            return bleFrameworks;
        }
        static void CopyAddSourceFiles(string pStartDir)
        {
            string aSourceFile = pStartDir + @"\sam\utils\cmsis\samg\samg54\include\component\component_twihs.h";
            string aTargetFile = pStartDir + @"\sam\utils\cmsis\samg\samg55\include\component\twihs.h";
           
             if (!File.Exists(aSourceFile))
                    throw new Exception(aSourceFile + " Source file does not exist");

            if (!File.Exists(aTargetFile))
                File.Copy(aSourceFile,aTargetFile, true);
        }
        //----------------------------------------------
        static void Main(string[] args)
        {
            if (args.Length < 1)
                throw new Exception("Usage: EFM32.exe <Atmel SW package directory>");

            var bspBuilder = new SLabBSPBuilder(new BSPDirectories(args[0], @"..\..\Output", @"..\..\rules"));

            var devices = BSPGeneratorTools.ReadMCUDevicesFromCommaDelimitedCSVFile(bspBuilder.Directories.RulesDir + @"\McuAtmel.csv",
                "Device Name", "Flash (kBytes)", "SRAM (kBytes)", "CPU", true);
            RemoveDuplicateMCU(ref devices);

            List<MCUFamilyBuilder> allFamilies = new List<MCUFamilyBuilder>();
            foreach (var fn in Directory.GetFiles(bspBuilder.Directories.RulesDir + @"\Families", "*.xml"))
                allFamilies.Add(new MCUFamilyBuilder(bspBuilder, XmlTools.LoadObject<FamilyDefinition>(fn)));

            var rejects = BSPGeneratorTools.AssignMCUsToFamilies(devices, allFamilies);
            List<MCUFamily> familyDefinitions = new List<MCUFamily>();
            List<MCU> mcuDefinitions = new List<MCU>();
            List<EmbeddedFramework> frameworks = new List<EmbeddedFramework>();

            List<MCUFamilyBuilder.CopiedSample> exampleDirs = new List<MCUFamilyBuilder.CopiedSample>();

            CopyAddSourceFiles(bspBuilder.Directories.InputDir);

            bool noPeripheralRegisters = args.Contains("/noperiph");
            List<KeyValuePair<string, string>> macroToHeaderMap = new List<KeyValuePair<string, string>>();

            var commonPseudofamily = new MCUFamilyBuilder(bspBuilder, XmlTools.LoadObject<FamilyDefinition>(bspBuilder.Directories.RulesDir + @"\CommonFiles.xml"));

            //Embedded Frameworks
            var AddFrW = GenereteAddFrameWorks(bspBuilder.Directories, "ServicesFrimwork.txt");
            commonPseudofamily.Definition.AdditionalFrameworks = commonPseudofamily.Definition.AdditionalFrameworks.Concat(AddFrW).ToArray();
            AddFrW = GenereteAddFrameWorksDir(bspBuilder.Directories);
            commonPseudofamily.Definition.AdditionalFrameworks = commonPseudofamily.Definition.AdditionalFrameworks.Concat(AddFrW).ToArray();

           foreach (var fw in commonPseudofamily.GenerateFrameworkDefinitions())
                frameworks.Add(fw);

            var flags = new ToolFlags();
            List<string> projectFiles = new List<string>();
            commonPseudofamily.CopyFamilyFiles(ref flags, projectFiles);

            foreach (var sample in commonPseudofamily.CopySamples())
                exampleDirs.Add(sample);

            foreach (var fam in allFamilies)
            {
                var rejectedMCUs = fam.RemoveUnsupportedMCUs(true);
                if (rejectedMCUs.Length != 0)
                {
                    Console.WriteLine("Unsupported {0} MCUs:", fam.Definition.Name);
                    foreach (var mcu in rejectedMCUs)
                        Console.WriteLine("\t{0}", mcu.Name);
                }


                fam.AttachStartupFiles(ParseStartupFiles(fam.Definition.StartupFileDir, fam));

                var famObj = fam.GenerateFamilyObject(true);

                famObj.AdditionalSourceFiles = LoadedBSP.Combine(famObj.AdditionalSourceFiles, projectFiles.Where(f => !MCUFamilyBuilder.IsHeaderFile(f)).ToArray());
                famObj.AdditionalHeaderFiles = LoadedBSP.Combine(famObj.AdditionalHeaderFiles, projectFiles.Where(f => MCUFamilyBuilder.IsHeaderFile(f)).ToArray());

                famObj.AdditionalSystemVars = LoadedBSP.Combine(famObj.AdditionalSystemVars, commonPseudofamily.Definition.AdditionalSystemVars);
                famObj.CompilationFlags = famObj.CompilationFlags.Merge(flags);
                famObj.CompilationFlags.PreprocessorMacros = LoadedBSP.Combine(famObj.CompilationFlags.PreprocessorMacros, new string[] { "$$com.sysprogs.bspoptions.primary_memory$$_layout" });

                familyDefinitions.Add(famObj);
                fam.GenerateLinkerScripts(false);
                if (!noPeripheralRegisters)
                    fam.AttachPeripheralRegisters(ParsePeripheralRegisters(bspBuilder.Directories.OutputDir , fam));

                foreach (var mcu in fam.MCUs)
                    mcuDefinitions.Add(mcu.GenerateDefinition(fam, bspBuilder, !noPeripheralRegisters));

                foreach (var fw in fam.GenerateFrameworkDefinitions())
                    frameworks.Add(fw);

                foreach (var sample in fam.CopySamples())
                    exampleDirs.Add(sample);

            }

            BoardSupportPackage bsp = new BoardSupportPackage
            {
                PackageID = "com.sysprogs.arm.atmel.sam",
                PackageDescription = "Atmel Devices",
                GNUTargetID = "arm-eabi",
                GeneratedMakFileName = "atsam.mak",
                MCUFamilies = familyDefinitions.ToArray(),
                SupportedMCUs = mcuDefinitions.ToArray(),
                Frameworks = frameworks.ToArray(),
                Examples = exampleDirs.Where(s => !s.IsTestProjectSample).Select(s => s.RelativePath).ToArray(),
                TestExamples = exampleDirs.Where(s => s.IsTestProjectSample).Select(s => s.RelativePath).ToArray(),

                FileConditions = bspBuilder.MatchedFileConditions.ToArray(),
                PackageVersion = "1.0"
            };

            bspBuilder.Save(bsp, true);

        }
    }
}