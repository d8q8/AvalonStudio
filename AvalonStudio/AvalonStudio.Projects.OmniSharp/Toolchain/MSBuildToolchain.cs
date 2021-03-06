namespace AvalonStudio.Toolchains.MSBuild
{
    using AvalonStudio.CommandLineTools;
    using AvalonStudio.Extensibility.Projects;
    using AvalonStudio.GlobalSettings;
    using AvalonStudio.Platforms;
    using AvalonStudio.Projects;
    using AvalonStudio.Projects.OmniSharp;
    using AvalonStudio.Projects.OmniSharp.Toolchain;
    using AvalonStudio.Utils;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public static class IEnumerableExtensions
    {
        /// <summary>
        /// Flattens an object hierarchy.
        /// </summary>
        /// <param name="rootLevel">The root level in the hierarchy.</param>
        /// <param name="nextLevel">A function that returns the next level below a given item.</param>
        /// <returns><![CDATA[An IEnumerable<T> containing every item from every level in the hierarchy.]]></returns>
        public static IEnumerable<T> Flatten<T>(this IEnumerable<T> rootLevel, Func<T, IEnumerable<T>> nextLevel)
        {
            HashSet<T> accumulation = new HashSet<T>();

            flattenLevel<T>(accumulation, rootLevel, nextLevel);

            foreach (var item in rootLevel)
            {
                accumulation.Add(item);
            }

            return accumulation;
        }

        /// <summary>
        /// Recursive helper method that traverses a hierarchy, accumulating items along the way.
        /// </summary>
        /// <param name="accumulation">A collection in which to accumulate items.</param>
        /// <param name="currentLevel">The current level we are traversing.</param>
        /// <param name="nextLevel">A function that returns the next level below a given item.</param>
        private static void flattenLevel<T>(HashSet<T> accumulation, IEnumerable<T> currentLevel, Func<T, IEnumerable<T>> nextLevel)
        {
            foreach (T item in currentLevel)
            {
                flattenLevel<T>(accumulation, nextLevel(item), nextLevel);

                foreach (var child in currentLevel)
                {
                    accumulation.Add(child);
                }
            }
        }
    }

    public class MSBuildToolchain : IToolChain
    {
        public string BinDirectory => Path.Combine(Platform.ReposDirectory, "AvalonStudio.Languages.CSharp", "coreclr");

        public string Description => "Toolchain for MSBuild 15 (Dotnet Core Support)";

        public string Name => "MSBuild Toolchain";

        public Version Version => new Version(0, 0, 0);

        public void Activation()
        {
        }

        public void BeforeActivation()
        {
        }

        private static bool RequiresBuilding(IProject project)
        {
            bool requiresBuild = false;

            if (project.Executable != null)
            {
                if (!File.Exists(project.Executable))
                {
                    requiresBuild = true;
                }
                else
                {
                    var lastBuildDate = File.GetLastWriteTime(project.Executable);

                    if (File.GetLastWriteTime(project.Location) > lastBuildDate)
                    {
                        requiresBuild = true;
                    }
                    else
                    {
                        foreach (var dependency in project.References)
                        {
                            // TODO implement public API analysis to determine if we need to rebuild or not.

                            if (File.GetLastWriteTime(dependency.Executable) > lastBuildDate)
                            {
                                requiresBuild = true;
                                break;
                            }
                        }

                        if (!requiresBuild)
                        {
                            foreach (var file in project.SourceFiles)
                            {
                                if (File.GetLastWriteTime(file.Location) > lastBuildDate)
                                {
                                    requiresBuild = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // TODO unsure about this case, or if its possible?
                requiresBuild = true;
            }

            return requiresBuild;
        }

        private List<Task<(bool result, IProject project)>> QueueItems(List<IProject> toBuild, List<IProject> built, BuildRunner runner)
        {
            var tasks = new List<Task<(bool result, IProject project)>>();

            var toRemove = new List<IProject>();

            foreach (var item in toBuild)
            {
                bool canBuild = true;

                foreach (var dep in item.References.OfType<OmniSharpProject>())
                {
                    if (!built.Contains(dep))
                    {
                        canBuild = false;
                        break;
                    }
                }

                if (canBuild)
                {
                    toRemove.Add(item);
                    tasks.Add(runner.Queue(item));
                }
            }

            foreach (var item in toRemove)
            {
                toBuild.Remove(item);
            }

            return tasks;
        }

        public async Task<bool> Build(IConsole console, IProject project, string label = "", IEnumerable<string> definitions = null)
        {
            var builtProjects = new List<IProject>();

            var cancellationSouce = new CancellationTokenSource();

            IEnumerable<IProject> projects = new List<IProject> { project };

            projects = projects.Flatten(p => p.References);

            var toBuild = projects.ToList();

            using (var buildRunner = new BuildRunner())
            {
                console.WriteLine($"Creating: {Environment.ProcessorCount} build nodes.");
                await buildRunner.InitialiseAsync();

                var startTime = DateTime.Now;

                buildRunner.Start(cancellationSouce.Token, (node, proj) =>
                {
                    var netProject = proj as OmniSharpProject;

                    if (netProject.RestoreRequired)
                    {
                        var buildResult = netProject.Restore(console).GetAwaiter().GetResult();

                        if (!buildResult)
                        {
                            return false;
                        }

                        netProject.MarkRestored();
                    }

                    if (RequiresBuilding(proj))
                    {
                        var result = node.BuildProject(proj).GetAwaiter().GetResult();

                        console.WriteLine();
                        console.WriteLine($"[{proj.Name}] Node = {node.Id}");

                        console.Write(result.consoleOutput);

                        if (result.result)
                        {
                            foreach (var output in result.outputAssemblies)
                            {
                                console.WriteLine($"{proj.Name} -> {output}");
                            }
                        }

                        return result.result;
                    }

                    console.WriteLine($"[Skipped] {proj.Name} -> {proj.Executable}");

                    return true;
                });

                var buildTasks = QueueItems(toBuild, builtProjects, buildRunner);

                bool canContinue = true;

                while (true)
                {
                    var result = await Task.WhenAny(buildTasks);

                    canContinue = result.Result.result;

                    buildTasks.Remove(result);

                    builtProjects.Add(result.Result.project);

                    if (canContinue)
                    {
                        if (toBuild.Count > 0)
                        {
                            buildTasks = buildTasks.Concat(QueueItems(toBuild, builtProjects, buildRunner)).ToList();
                        }

                        if (toBuild.Count == 0 && buildTasks.Count == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                cancellationSouce.Cancel();

                var duration = DateTime.Now - startTime;

                if (canContinue)
                {
                    console.WriteLine($"Build Successful - {duration.ToString()}");
                    return true;
                }
                else
                {
                    console.WriteLine($"Build Failed - {duration.ToString()}");
                    return false;
                }
            }
        }

        public bool CanHandle(IProject project)
        {
            return project is OmniSharpProject;
        }

        public async Task Clean(IConsole console, IProject project)
        {
            await Task.Factory.StartNew(() =>
            {
                console.Write($"Cleaning Project: {project.Name}...");
                var exitCode = PlatformSupport.ExecuteShellCommand(DotNetCliService.Instance.DotNetPath, "clean /nologo", (s, e) => console.WriteLine(e.Data), (s, e) =>
                {
                    if (e.Data != null)
                    {
                        console.WriteLine();
                        console.WriteLine(e.Data);
                    }
                },
                false, project.CurrentDirectory, false);
            });

            console.WriteLine("Done.");
        }

        public IList<object> GetConfigurationPages(IProject project)
        {
            return new List<object>() { };
        }

        public IEnumerable<string> GetToolchainIncludes(ISourceFile file)
        {
            return new List<string>();
        }

        public Task<bool> InstallAsync(IConsole console, IProject project)
        {
            return Task.FromResult(true);
        }

        public void ProvisionSettings(IProject project)
        {
        }
    }
}