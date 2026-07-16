using System;
using System.IO;

namespace Automation
{
    internal sealed class HmiDevelopmentSource
    {
        public string SourceDirectory { get; set; }

        public string ProjectPath { get; set; }

        public string PlatformSourceRoot { get; set; }

        public string ValidationScriptPath { get; set; }

        public string CustomFunctionSourcePath { get; set; }
    }

    internal static class HmiDevelopmentSourceLocator
    {
        public const string SourceDirectoryEnvironmentVariable = "AUTOMATION_HMI_SOURCE_DIRECTORY";
        public const string ProjectPathEnvironmentVariable = "AUTOMATION_HMI_PROJECT_PATH";
        public const string PlatformSourceRootEnvironmentVariable = "AUTOMATION_PLATFORM_SOURCE_ROOT";
        public const string ValidationScriptEnvironmentVariable = "AUTOMATION_HMI_VALIDATION_SCRIPT";
        public const string CustomFunctionSourceEnvironmentVariable = "AUTOMATION_HMI_CUSTOM_FUNCTION_SOURCE";

        public static bool TryResolve(string startDirectory, out HmiDevelopmentSource source, out string error)
        {
            source = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                error = "HMI 源码定位起始目录为空。";
                return false;
            }

            DirectoryInfo directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
            }
            catch (Exception ex)
            {
                error = "HMI 源码定位起始目录无效：" + ex.Message;
                return false;
            }

            while (directory != null)
            {
                string directProjectPath = Path.Combine(directory.FullName, "MachineApp.csproj");
                string directSourceDirectory = Path.Combine(directory.FullName, "Hmi");
                if (File.Exists(directProjectPath) && Directory.Exists(directSourceDirectory))
                {
                    string platformSourceRoot = directory.FullName;
                    if (directory.Parent != null)
                    {
                        string siblingPlatformRoot = Path.Combine(directory.Parent.FullName, "Automation");
                        if (File.Exists(Path.Combine(siblingPlatformRoot, "Automation.csproj")))
                        {
                            platformSourceRoot = siblingPlatformRoot;
                        }
                    }
                    source = CreateSource(directSourceDirectory, directProjectPath, platformSourceRoot);
                    return true;
                }

                string platformProjectPath = Path.Combine(directory.FullName, "Automation.csproj");
                string platformHmiDirectory = Path.Combine(directory.FullName, "Hmi");
                if (File.Exists(platformProjectPath) && Directory.Exists(platformHmiDirectory))
                {
                    source = CreateSource(platformHmiDirectory, platformProjectPath, directory.FullName);
                    return true;
                }

                string deviceProjectDirectory = Path.Combine(directory.FullName, "DeviceProject");
                string deviceProjectPath = Path.Combine(deviceProjectDirectory, "MachineApp.csproj");
                string deviceSourceDirectory = Path.Combine(deviceProjectDirectory, "Hmi");
                if (File.Exists(deviceProjectPath) && Directory.Exists(deviceSourceDirectory))
                {
                    string platformSourceRoot = Path.Combine(directory.FullName, "Automation");
                    if (!File.Exists(Path.Combine(platformSourceRoot, "Automation.csproj")))
                    {
                        platformSourceRoot = directory.FullName;
                    }
                    source = CreateSource(deviceSourceDirectory, deviceProjectPath, platformSourceRoot);
                    return true;
                }

                directory = directory.Parent;
            }

            string deployedSourceDirectory = Path.Combine(
                Path.GetFullPath(startDirectory),
                "Hmi");
            if (Directory.Exists(deployedSourceDirectory))
            {
                source = CreateSource(deployedSourceDirectory, null, null);
                return true;
            }

            error = "未找到当前 HMI 工程源码目录。开发环境需包含 Automation.csproj/Hmi 或 MachineApp.csproj/Hmi，发布环境若开放源码编辑需在程序目录携带 Hmi。";
            return false;
        }

        private static HmiDevelopmentSource CreateSource(
            string sourceDirectory,
            string projectPath,
            string platformSourceRoot)
        {
            DirectoryInfo rootDirectory = null;
            if (!string.IsNullOrWhiteSpace(platformSourceRoot))
            {
                rootDirectory = new DirectoryInfo(platformSourceRoot);
            }
            else if (!string.IsNullOrWhiteSpace(projectPath))
            {
                rootDirectory = new DirectoryInfo(Path.GetDirectoryName(projectPath));
            }

            string validationScriptPath = null;
            platformSourceRoot = null;
            while (rootDirectory != null)
            {
                string candidateScriptPath = Path.Combine(
                    rootDirectory.FullName,
                    "Scripts",
                    "Invoke-AiValidation.ps1");
                string platformContractPath = Path.Combine(
                    rootDirectory.FullName,
                    "Automation.DeviceSdk",
                    "PlatformContracts.cs");
                if (File.Exists(candidateScriptPath) && File.Exists(platformContractPath))
                {
                    platformSourceRoot = rootDirectory.FullName;
                    validationScriptPath = candidateScriptPath;
                    break;
                }
                rootDirectory = rootDirectory.Parent;
            }

            string customFunctionSourcePath = Path.Combine(sourceDirectory, "CustomFunctions.cs");
            if (!File.Exists(customFunctionSourcePath))
            {
                customFunctionSourcePath = null;
            }

            return new HmiDevelopmentSource
            {
                SourceDirectory = Path.GetFullPath(sourceDirectory),
                ProjectPath = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFullPath(projectPath),
                PlatformSourceRoot = string.IsNullOrWhiteSpace(platformSourceRoot) ? null : Path.GetFullPath(platformSourceRoot),
                ValidationScriptPath = validationScriptPath,
                CustomFunctionSourcePath = customFunctionSourcePath
            };
        }
    }
}
