using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class RepositoryTestDiscoveryTests
{
    [Fact]
    public void DiscoverDockerFreeTestProjects_DockerOnlySolutionDoesNotSelectAnyTests()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string solution = Path.Combine(directory.FullName, "Fixture.slnx");
            File.WriteAllText(solution, """<Solution><Project Path="Live.csproj" /></Solution>""");
            File.WriteAllText(Path.Combine(directory.FullName, "Live.csproj"), """
                <Project>
                  <PropertyGroup>
                    <IsTestProject>true</IsTestProject>
                    <RequiresDocker>true</RequiresDocker>
                  </PropertyGroup>
                </Project>
                """);

            Assert.Empty(RepositoryVerification.DiscoverDockerFreeTestProjects(solution));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DiscoverDockerFreeTestProjects_UsesSolutionMembershipAndExcludesDockerTestsAndNonTests()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string solution = Path.Combine(directory.FullName, "Fixture.slnx");
            File.WriteAllText(solution, """
                <Solution>
                  <Folder Name="/tests/">
                    <Project Path="tests/Core.Tests.csproj" />
                    <Project Path="tests\Hosting.Tests.csproj" />
                    <Project Path="tests/Integration.Tests.csproj" />
                    <Project Path="tests/Probe.csproj" />
                    <Project Path="tests/Disabled.Tests.csproj" />
                  </Folder>
                  <Project Path="tools/Tools.csproj" />
                </Solution>
                """);
            string core = WriteProject("tests", "Core.Tests", "<IsTestProject>true</IsTestProject>");
            string hosting = WriteProject("tests", "Hosting.Tests", "<IsTestProject> true </IsTestProject><RequiresDocker>False</RequiresDocker>");
            WriteProject("tests", "Integration.Tests", "<IsTestProject>true</IsTestProject><RequiresDocker> true </RequiresDocker>");
            WriteProject("tests", "Probe", "<OutputType>Exe</OutputType><RequiresDocker>true</RequiresDocker>");
            WriteProject("tests", "Disabled.Tests", "<IsTestProject>false</IsTestProject>");
            WriteProject("tests", "Unregistered.Tests", "<IsTestProject>true</IsTestProject>");
            WriteProject("tools", "Tools", "<OutputType>Exe</OutputType>");

            var selected = RepositoryVerification.DiscoverDockerFreeTestProjects(solution);

            Assert.Equal([core, hosting], selected.Select(Path.GetFullPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        string WriteProject(string folder, string name, string properties)
        {
            string path = Path.Combine(directory.CreateSubdirectory(folder).FullName, name + ".csproj");
            File.WriteAllText(path, $"<Project><PropertyGroup>{properties}</PropertyGroup></Project>");
            return path;
        }
    }
}
