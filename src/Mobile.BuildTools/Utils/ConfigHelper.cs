﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if !NETCOREAPP
using Microsoft.Build.Framework;
using Mobile.BuildTools.Build;
#endif
using Mobile.BuildTools.Models;
using Mobile.BuildTools.Models.Secrets;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Mobile.BuildTools.Utils
{
    public static class ConfigHelper
    {
        private static readonly object lockObject = new object();

        public static bool Exists(string path) =>
            File.Exists(GetConfigFilePath(path));
#if !NETCOREAPP
        public static BuildToolsConfig GetConfig(ITaskItem item) =>
            GetConfig(item.ItemSpec);
#endif
        public static BuildToolsConfig GetConfig(string path)
        {
            var filePath = GetConfigFilePath(path);
            if(File.Exists(filePath))
            {
                SaveDefaultConfig(path);
            }

            var json = string.Empty;
            lock(lockObject)
            {
                json = File.ReadAllText(filePath);
            }

            return JsonConvert.DeserializeObject<BuildToolsConfig>(json, GetSerializerSettings());
        }

        public static void SaveConfig(BuildToolsConfig config, string path)
        {
            var filePath = GetConfigFilePath(path);
            var json = JsonConvert.SerializeObject(config, GetSerializerSettings());
            lock(lockObject)
            {
                File.WriteAllText(filePath, json);
            }
        }

        public static void SaveDefaultConfig(string path)
        {
            var config = new BuildToolsConfig
            {
                ArtifactCopy = new ArtifactCopy { Disable = false },
                AutomaticVersioning = new AutomaticVersioning
                {
#if !NETCOREAPP
                    Behavior = VersionBehavior.PreferBuildNumber,
                    Environment = VersionEnvironment.All,
#endif
                    VersionOffset = 0
                },
                Css = new XamarinCss
                {
                    Minify = false,
                    BundleScss = false
                },
                Images = new ImageResize
                {
                    ConditionalDirectories = null,
                    Directories = null
                },
                Manifests = new TemplatedManifest
                {
                    Disable = false,
                    Token = "$$",
                    MissingTokensAsErrors = false,
                    VariablePrefix = "Manifest_"
                },
                ReleaseNotes = new ReleaseNotesOptions
                {
                    Disable = false,
                    CharacterLimit = 250,
                    CreateInRoot = true,
                    FileName = "ReleaseNotes.txt",
                    MaxCommit = 10,
                    MaxDays = 7
                }
            };

#if !NETCOREAPP
            var imagesRootDir = Path.Combine(path, "Images");
            if (Directory.Exists(imagesRootDir))
            {
                if (Directory.GetDirectories(imagesRootDir).Length > 0)
                {
                    var allDirectories = Directory.GetDirectories(imagesRootDir).Select(x => Path.GetDirectoryName(x));
                    config.Images.Directories = allDirectories.Where(x => !conditionalDefaults.Any(d => x.Equals(d, StringComparison.InvariantCultureIgnoreCase)))
                        .Select(x => Path.Combine("Images", x)).ToList();
                    config.Images.ConditionalDirectories = allDirectories.Where(x => conditionalDefaults.Any(d => x.Equals(d, StringComparison.InvariantCultureIgnoreCase)))
                        .ToDictionary(x => x, x => (IEnumerable<string>)new[] { Path.Combine("Images", x) });
                }
                else
                {
                    config.Images.Directories = new List<string> { imagesRootDir };
                }
            }
#endif

            SaveConfig(config, path);

#if !DEBUG // Do not generate .gitignore for local debug builds
            var requiredContents = @"# Mobile.BuildTools
secrets.json
secrets.*.json
";
            var gitignoreFile = Path.Combine(path, ".gitignore");
            if (File.Exists(gitignoreFile))
            {
                if(!File.ReadAllText(gitignoreFile).Contains(Constants.SecretsJsonFileName))
                {
                    File.AppendAllText(gitignoreFile, $"\n\n{requiredContents}");
                }
            }
            else
            {
                File.WriteAllText(gitignoreFile, requiredContents);
            }
#endif
        }

#if !NETCOREAPP
        internal static SecretsConfig GetSecretsConfig(IBuildConfiguration buildConfiguration) =>
            GetSecretsConfig(buildConfiguration.ProjectName, buildConfiguration.ProjectDirectory, buildConfiguration.Configuration);
#endif
        public static SecretsConfig GetSecretsConfig(string projectName, string projectDir, BuildToolsConfig config)
        {
            var configPath = Path.Combine(projectDir, Constants.SecretsConfigFileName);
            if (File.Exists(configPath))
            {
                return JsonConvert.DeserializeObject<SecretsConfig>(configPath);
            }

            if (config.ProjectSecrets != null && config.ProjectSecrets.Any(x => x.Key == projectName))
                return config.ProjectSecrets.First(x => x.Key == projectName).Value;

            return null;
        }

        private static string GetConfigFilePath(string path)
        {
            if(Path.GetFileName(path) == Constants.BuildToolsConfigFileName)
            {
                return path;
            }

            return Path.Combine(path, Constants.BuildToolsConfigFileName);
        }

        public static JsonSerializerSettings GetSerializerSettings()
        {
            var serializer = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
            };
            serializer.Converters.Add(new StringEnumConverter());
            return serializer;
        }

        private static readonly string[] conditionalDefaults = new[]
        {
            "Debug",
            "Release",
            "Store",
            "Ad-Hoc",
            "MonoAndroid",
            "Xamarin.iOS"
        };
    }
}
