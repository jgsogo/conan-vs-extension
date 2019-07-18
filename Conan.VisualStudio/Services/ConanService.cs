using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Conan.VisualStudio.Core;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;

namespace Conan.VisualStudio.Services
{
    internal class ConanService : IConanService
    {
        private readonly ISettingsService _settingsService;
        private readonly IErrorListService _errorListService;
        private readonly IVcProjectService _vcProjectService;

        public ConanService(ISettingsService settingsService, Core.IErrorListService errorListService, IVcProjectService vcProjectService)
        {
            _settingsService = settingsService;
            _errorListService = errorListService;
            _vcProjectService = vcProjectService;
        }

        private string GetPropsFilePath(VCConfiguration configuration)
        {
            string installPath = _vcProjectService.GetInstallationDirectory(_settingsService, configuration);
            string propFileName;
            if (_settingsService.GetConanGenerator() == ConanGeneratorType.visual_studio)
                propFileName = @"conanbuildinfo.props";
            else
                propFileName = @"conanbuildinfo_multi.props";
            return Path.Combine(installPath, propFileName);
        }


        private void IntegrateIntoConfiguration(VCConfiguration configuration)
        {
            string absPropFilePath = GetPropsFilePath(configuration);
            string relativePropFilePath = ConanPathHelper.GetRelativePath(configuration.project.ProjectDirectory, absPropFilePath);

            IVCCollection tools = (IVCCollection)configuration.Tools;
            if (tools != null)
            {
                VCLinkerTool ltool = (VCLinkerTool)tools.Item("VCLinkerTool");
                if (ltool != null)
                {
                    string deps = ltool.AdditionalDependencies;
                    ltool.AdditionalDependencies = deps.Replace("$(NOINHERIT)", "");
                }
            }

            foreach (VCPropertySheet sheet in configuration.PropertySheets)
            {
                if (ConanPathHelper.NormalizePath(sheet.PropertySheetFile) == ConanPathHelper.NormalizePath(absPropFilePath))
                {
                    string msg = $"[Conan.VisualStudio] Property sheet '{absPropFilePath}' already added to project {configuration.project.Name}";
                    Logger.Log(msg);
                    return;
                }
            }
            configuration.AddPropertySheet(relativePropFilePath);
            Logger.Log($"[Conan.VisualStudio] Property sheet '{absPropFilePath}' added to project {configuration.project.Name}");
            configuration.CollectIntelliSenseInfo();
        }

        public async Task IntegrateAsync(VCProject vcProject)
        {
            var projectDirectory = vcProject.ProjectDirectory;
            var conanfileDirectory = await ConanPathHelper.GetNearestConanfilePathAsync(projectDirectory);
            if (conanfileDirectory == null)
            {
                _errorListService.WriteError("unable to locate conanfile directory!");
                return;
            }

            if (_settingsService.GetConanInstallOnlyActiveConfiguration())
            {
                IntegrateIntoConfiguration(vcProject.ActiveConfiguration);
            }
            else
            {
                foreach (VCConfiguration configuration in vcProject.Configurations)
                {
                    IntegrateIntoConfiguration(configuration);
                }
            }
        }

        public async Task<bool> InstallAsync(VCProject vcProject)
        {
            var conanPath = _settingsService.GetConanExecutablePath();
            if (conanPath == null || conanPath == "")
            {
                _errorListService.WriteError(
                    "Conan executable path is not set and Conan executable wasn't found automatically. " +
                    "Please set it up in the Tools → Settings → Conan menu.");
                return false;
            }

            var project = await _vcProjectService.ExtractConanProjectAsync(vcProject, _settingsService);
            if (project == null)
            {
                _errorListService.WriteError("Unable to extract conan project!");
                return false;
            }
            var conan = new ConanRunner(_settingsService.LoadSettingFile(project), conanPath);

            return await InstallDependenciesAsync(conan, project);
        }
        private static void AppendLinesFunc(object packedParams)
        {
            var paramsTuple = (Tuple<StreamWriter, StreamReader>)packedParams;
            StreamWriter writer = paramsTuple.Item1;
            StreamReader reader = paramsTuple.Item2;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lock (writer)
                {
                    Logger.Log(line);
                    writer.WriteLine(line);
                }
            }
        }

        private async Task<bool> InstallDependenciesAsync(ConanRunner conan, ConanProject project)
        {
            foreach (var configuration in project.Configurations)
            {
                var installPath = configuration.InstallPath;
                await Task.Run(() => Directory.CreateDirectory(installPath));
                var logFilePath = Path.Combine(installPath, $"conan_{Guid.NewGuid().ToString()}.log");

                using (var logFile = File.Open(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var logStream = new StreamWriter(logFile))
                {
                    ConanGeneratorType generator = _settingsService.GetConanGenerator();
                    ConanBuildType build = _settingsService.GetConanBuild();
                    bool update = _settingsService.GetConanUpdate();

                    string message = $"[Conan.VisualStudio] Calling process '{process.FileName}' " +
                                     $"with arguments '{process.Arguments}'";
                    Logger.Log(message);
                    await logStream.WriteLineAsync(message);

                    try
                    {
                        using (Process exeProcess = Process.Start(process))
                        {
                            int exitCode = await exeProcess.WaitForExitAsync();

                            var tokenSource = new CancellationTokenSource();
                            var token = tokenSource.Token;

                            Task outputReader = Task.Factory.StartNew(AppendLinesFunc,
                                Tuple.Create(logStream, exeProcess.StandardOutput),
                                token, TaskCreationOptions.None, TaskScheduler.Default);
                            Task errorReader = Task.Factory.StartNew(AppendLinesFunc,
                                Tuple.Create(logStream, exeProcess.StandardError),
                                token, TaskCreationOptions.None, TaskScheduler.Default);

                            Task.WaitAll(outputReader, errorReader);
                            
                            if (exitCode != 0)
                            {
                                message = $"Conan has returned exit code '{exitCode}' " +
                                          $"while processing configuration '{configuration}'. " +
                                          $"Please check file '{logFilePath}' for details.";

                                Logger.Log(message);
                                await logStream.WriteLineAsync(message);
                                _errorListService.WriteError(message, logFilePath);
                                return false;
                            }
                            else
                            {
                                message = $"[Conan.VisualStudio] Conan has succsessfully " +
                                          $"installed configuration '{configuration}'";
                                Logger.Log(message);
                                await logStream.WriteLineAsync(message);
                                _errorListService.WriteMessage(message);
                            }
                        }
                    }
                    catch(System.ComponentModel.Win32Exception)
                    {
                        message = $"[Conan.VisualStudio] Error running '{process.FileName}'. Is it a valid path to conan.exe?";
                        Logger.Log(message);
                        await logStream.WriteLineAsync(message);
                        _errorListService.WriteError(message);
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
