using System.IO;
using Mobile.BuildTools.Build;
using Mobile.BuildTools.Generators;
using Mobile.BuildTools.Generators.Manifests;
using Mobile.BuildTools.Utils;
using Xamarin.Android.Tools;

namespace Mobile.BuildTools.Tasks
{
    public class TemplateManifestTask : BuildToolsTaskBase
    {
        public string[] ReferenceAssemblyPaths { get; set; }

        public string ManifestPath { get; set; }

        internal override void ExecuteInternal(IBuildConfiguration config)
        {
            if(string.IsNullOrEmpty(ManifestPath))
            {
                Log.LogWarning("No value was provided for the Manifest. Unable to process Manifest Tokens");
                return;
            }

            if(!File.Exists(ManifestPath))
            {
                Log.LogWarning($"Unable to process Manifest Tokens, no manifest was found at the path '{ManifestPath}'");
                return;
            }

            IGenerator generator = null;
            switch(config.Platform)
            {
                case Platform.iOS:
                    generator = new TemplatedPlistGenerator(config)
                    {
                        ManifestOutputPath = ManifestPath
                    };
                    break;
                case Platform.Android:
                    generator = new TemplatedAndroidAppManifestGenerator(config, ReferenceAssemblyPaths)
                    {
                        ManifestOutputPath = ManifestPath
                    };
                    break;
            }

            generator?.Execute();
        }
    }
}