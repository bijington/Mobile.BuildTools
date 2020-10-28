using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mobile.BuildTools.Build;
using Newtonsoft.Json.Linq;

namespace Mobile.BuildTools.Utils
{
    internal static class EnvironmentAnalyzer
    {
        private const string DefaultSecretPrefix = "BuildTools_";
        private const string LegacySecretPrefix = "Secret_";
        private const string DefaultManifestPrefix = "Manifest_";

        public static IDictionary<string, string> GatherEnvironmentVariables(IBuildConfiguration buildConfiguration = null, bool includeManifest = false)
        {
            var env = new Dictionary<string, string>();
            foreach(var key in Environment.GetEnvironmentVariables().Keys)
            {
                env.Add(key.ToString(), Environment.GetEnvironmentVariable(key.ToString()));
            }

            if (buildConfiguration is null)
                return env;

            var projectDirectory = buildConfiguration.ProjectDirectory;
            var solutionDirectory = buildConfiguration.SolutionDirectory;
            var configuration = buildConfiguration.BuildConfiguration;
            LoadSecrets(Path.Combine(projectDirectory, Constants.SecretsJsonFileName), ref env);
            LoadSecrets(Path.Combine(projectDirectory, string.Format(Constants.SecretsJsonConfigurationFileFormat, configuration)), ref env);
            LoadSecrets(Path.Combine(solutionDirectory, Constants.SecretsJsonFileName), ref env);
            LoadSecrets(Path.Combine(solutionDirectory, string.Format(Constants.SecretsJsonConfigurationFileFormat, configuration)), ref env);

            if (includeManifest)
            {
                LoadSecrets(Path.Combine(projectDirectory, Constants.ManifestJsonFileName), ref env);
                LoadSecrets(Path.Combine(solutionDirectory, Constants.ManifestJsonFileName), ref env);
            }

            if(buildConfiguration?.Configuration?.Environment != null)
            {
                var settings = buildConfiguration.Configuration.Environment;
                var defaultSettings = settings.Defaults ?? new Dictionary<string, string>();
                if(settings.Configuration != null && settings.Configuration.ContainsKey(configuration))
                {
                    foreach ((var key, var value) in settings.Configuration[configuration])
                        defaultSettings[key] = value;
                }

                UpdateVariables(defaultSettings, ref env);
            }

            return env;
        }

        internal static void UpdateVariables(IDictionary<string, string> settings, ref Dictionary<string, string> output)
        {
            if (settings is null || settings.Count < 1)
                return;

            foreach((var key, var value) in settings)
            {
                if (!output.ContainsKey(key))
                    output[key] = value;
            }
        }

        public static string LocateSolution(string searchDirectory)
        {
            var solutionFiles = Directory.GetFiles(searchDirectory, "*.sln");
            if (solutionFiles.Length > 0)
            {
                return searchDirectory;
            }
            else if (Directory.EnumerateDirectories(searchDirectory).Any(x => x == ".git"))
            {
                return searchDirectory;
            }
            else if (searchDirectory == Path.GetPathRoot(searchDirectory))
            {
                return searchDirectory;
            }

            return LocateSolution(Directory.GetParent(searchDirectory).FullName);
        }

        public static IEnumerable<string> GetManifestPrefixes(Platform platform, string knownPrefix)
        {
            var prefixes = new List<string>(GetSecretPrefixes(platform, forceIncludeDefault: true))
            {
                DefaultManifestPrefix
            };

            if(!string.IsNullOrEmpty(knownPrefix))
            {
                prefixes.Add(knownPrefix);
            }

            var platformPrefix = GetPlatformManifestPrefix(platform);
            if(!string.IsNullOrWhiteSpace(platformPrefix))
            {
                prefixes.Add(platformPrefix);
            }

            return prefixes;
        }

        private static string GetPlatformManifestPrefix(Platform platform)
        {
            return platform switch
            {
                Platform.Android => "DroidManifest_",
                Platform.iOS => "iOSManifest_",
                Platform.UWP => "UWPManifest_",
                Platform.macOS => "MacManifest_",
                Platform.Tizen => "TizenManifest_",
                _ => null,
            };
        }

        public static string[] GetPlatformSecretPrefix(Platform platform)
        {
            return platform switch
            {
                Platform.Android => new[] { "DroidSecret_" },
                Platform.iOS => new[] { "iOSSecret_" },
                Platform.UWP => new[] { "UWPSecret_" },
                Platform.macOS => new[] { "MacSecret_" },
                Platform.Tizen => new[] { "TizenSecret_" },
                _ => new[] { DefaultSecretPrefix, LegacySecretPrefix },
            };
        }

        public static IEnumerable<string> GetSecretPrefixes(Platform platform, bool forceIncludeDefault = false)
        {
            var prefixes = new List<string>(GetPlatformSecretPrefix(platform))
            {
                "SharedSecret_"
            };

            if(platform != Platform.Unsupported)
            {
                prefixes.Add("PlatformSecret_");
            }

            if(forceIncludeDefault && !prefixes.Contains(DefaultSecretPrefix))
            {
                prefixes.Add(DefaultSecretPrefix);
                prefixes.Add($"MB{DefaultManifestPrefix}");
            }

            return prefixes;
        }

        public static IEnumerable<string> GetSecretKeys(IEnumerable<string> prefixes)
        {
            var variables = GatherEnvironmentVariables();
            return variables.Keys.Where(k => prefixes.Any(p => k.StartsWith(p)));
        }

        public static IDictionary<string, string> GetSecrets(IBuildConfiguration build, string knownPrefix)
        {
            var prefixes = GetSecretPrefixes(build.Platform);
            if(!string.IsNullOrEmpty(knownPrefix))
            {
                prefixes = new List<string>(prefixes)
                {
                    knownPrefix
                };
            }
            var keys = GetSecretKeys(prefixes);
            var variables = GatherEnvironmentVariables().Where(p => keys.Any(k => k == p.Key));
            var output = new Dictionary<string, string>();
            foreach(var prefix in prefixes)
            {
                foreach(var pair in variables.Where(v => v.Key.StartsWith(prefix)))
                {
                    var key = Regex.Replace(pair.Key, prefix, "");
                    output.Add(key, pair.Value);
                }
            }

            if (build?.Configuration?.Environment != null)
            {
                var configuration = build.BuildConfiguration;
                var settings = build.Configuration.Environment;
                var defaultSettings = settings.Defaults ?? new Dictionary<string, string>();
                if (settings.Configuration != null && settings.Configuration.ContainsKey(configuration))
                {
                    foreach ((var key, var value) in settings.Configuration[configuration])
                        defaultSettings[key] = value;
                }

                UpdateVariables(defaultSettings, ref output);
            }

            return output;
        }

        public static bool IsInGitRepo(string projectPath)
        {
            var di = new DirectoryInfo(projectPath);
            if (di.EnumerateDirectories().Any(x => x.Name == ".git"))
                return true;

            if (IsRootPath(di))
                return false;

            return IsInGitRepo(di.Parent.FullName);
        }

        private static bool IsRootPath(DirectoryInfo directoryPath)
        {
            return directoryPath.Root.FullName == directoryPath.FullName ||
                directoryPath.FullName == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static void LoadSecrets(string path, ref Dictionary<string, string> env)
        {
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var secrets = JObject.Parse(json);
            foreach(var secret in secrets)
            {
                if (!env.ContainsKey(secret.Key))
                {
                    env.Add(secret.Key, secret.Value.ToString());
                }
            }
        }
    }
}
