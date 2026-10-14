using System.Diagnostics;
using System.Text;

namespace Brama.SourceGenerator.MSBuildTests;

/// <summary>The outcome of building a fixture project.</summary>
/// <param name="Succeeded">Whether the build exited zero.</param>
/// <param name="Output">Everything the build wrote, for asserting on Brama's own messages.</param>
public sealed record BuildResult(bool Succeeded, string Output);

/// <summary>
/// A throwaway project on disk that references Brama and its build step, so the Contracts behaviour
/// can be driven the way a real consumer drives it: by running <c>dotnet build</c>.
/// </summary>
/// <remarks>
/// The alternative — calling the analyzer in-process — would test the generator but not the thing
/// most likely to be wrong, which is the MSBuild target: whether it runs at the right time, finds
/// the generated file, writes to the right place, and fails the build when it should.
/// </remarks>
public sealed class ContractFixture : IDisposable
{
    private const int BuildTimeout = 120_000;

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private readonly string _directory;

    public ContractFixture()
    {
        _directory = Path.Combine(Path.GetTempPath(), "brama-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        File.WriteAllText(Path.Combine(_directory, "Fixture.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <LangVersion>latest</LangVersion>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="{RepositoryRoot}/src/Brama/Brama.csproj" />
                <ProjectReference Include="{RepositoryRoot}/src/Brama.SourceGenerator/Brama.SourceGenerator.csproj"
                                  OutputItemType="Analyzer"
                                  ReferenceOutputAssembly="false" />
              </ItemGroup>

              <Import Project="{RepositoryRoot}/src/Brama.SourceGenerator/build/Brama.Contracts.targets" />

            </Project>
            """);

        // An empty Directory.Build.props and .targets stop this fixture inheriting the repository's
        // own analyzer settings, which would fail the build for reasons unrelated to Contracts.
        File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(_directory, "Directory.Build.targets"), "<Project />");
    }

    public string ContractPath(string identity) =>
        Path.Combine(_directory, "contracts", identity + ".contract.txt");

    public bool ContractExists(string identity) => File.Exists(ContractPath(identity));

    public string ReadContract(string identity) => File.ReadAllText(ContractPath(identity));

    /// <summary>Writes the fixture's <c>Program.cs</c> around a configuration body.</summary>
    public void WriteProgram(string configuration, string types = "") =>
        File.WriteAllText(Path.Combine(_directory, "Program.cs"), $$"""
            using Brama;
            using Brama.Configuration;
            using Brama.Hosting;

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddBrama(api =>
            {
                api.ProblemDetails(p => p
                    .Map(ErrorKind.Validation, 400)
                    .Map(ErrorKind.NotFound, 404)
                    .Map(ErrorKind.Conflict, 409)
                    .Map(ErrorKind.Unauthorized, 401)
                    .Map(ErrorKind.Forbidden, 403)
                    .Map(ErrorKind.Problem, 400));

                {{configuration}}
            });

            var app = builder.Build();
            app.MapBrama();
            app.Run();

            {{types}}
            """);

    public BuildResult Build(params string[] extraArguments)
    {
        ArgumentNullException.ThrowIfNull(extraArguments);

        return RunBuild(extraArguments);
    }

    /// <summary>
    /// Builds the projects the fixture references, without building the fixture itself.
    /// </summary>
    /// <remarks>
    /// A design-time build never builds its project references; it only reads their reference
    /// assemblies, which an IDE has already produced. A test that drives a design-time build has to
    /// recreate that state first, or it depends on another test having built Brama before it ran.
    /// </remarks>
    public BuildResult BuildReferences()
    {
        BuildResult brama = RunBuild($"{RepositoryRoot}/src/Brama/Brama.csproj");

        return brama.Succeeded
            ? RunBuild($"{RepositoryRoot}/src/Brama.SourceGenerator/Brama.SourceGenerator.csproj")
            : brama;
    }

    private BuildResult RunBuild(params string[] arguments)
    {
        var output = new StringBuilder();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        // Without this, MSBuild leaves worker nodes running after the build to speed up the next
        // one. They inherit the redirected pipes, so reading output to end never completes even
        // though the build itself has finished.
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add("-nodeReuse:false");
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        // Both streams are drained concurrently. Reading one to completion before the other
        // deadlocks as soon as the build fills the pipe buffer of the stream not being read — which
        // a failing build, with its long diagnostic output, reliably does.
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(BuildTimeout))
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();

            throw new TimeoutException($"The fixture build did not finish within {BuildTimeout / 1000} seconds.");
        }

        output.Append(standardOutput.GetAwaiter().GetResult());
        output.Append(standardError.GetAwaiter().GetResult());

        bool succeeded = process.ExitCode == 0;
        process.Dispose();

        return new BuildResult(succeeded, output.ToString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brama.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (no Brama.slnx above the test binaries).");
    }
}
